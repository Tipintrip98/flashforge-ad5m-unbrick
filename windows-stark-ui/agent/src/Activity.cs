// Tipaldi AI - attività: quale app è in primo piano, con quale finestra, per quanto tempo.
// Tutto resta sul PC (activity\AAAA-MM-GG.jsonl); Claude lo legge tramite il server MCP locale.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TipaldiAI
{
    public class Session
    {
        public DateTime Start;
        public DateTime End;
        public string App;      // nome del processo, minuscolo
        public string Title;
        public string Ctx;
        public string Exe;

        public double Seconds { get { return Math.Max(0, (End - Start).TotalSeconds); } }

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["s"] = Start.ToString("s", CultureInfo.InvariantCulture);
            d["e"] = End.ToString("s", CultureInfo.InvariantCulture);
            d["app"] = App;
            d["title"] = Title ?? "";
            d["ctx"] = Ctx;
            if (!string.IsNullOrEmpty(Exe)) { d["exe"] = Exe; }
            return d;
        }

        public static Session FromDict(Dictionary<string, object> d)
        {
            Session s = new Session();
            s.Start = DateTime.Parse(Json.Str(d, "s", ""), CultureInfo.InvariantCulture);
            s.End = DateTime.Parse(Json.Str(d, "e", ""), CultureInfo.InvariantCulture);
            s.App = Json.Str(d, "app", "");
            s.Title = Json.Str(d, "title", "");
            s.Ctx = Json.Str(d, "ctx", Contexts.Other);
            s.Exe = Json.Str(d, "exe", null);
            return s;
        }
    }

    public static class ActivityStore
    {
        static readonly object Gate = new object();

        public static void Append(Session s)
        {
            if (s.Seconds < 2) { return; }
            string file = Path.Combine(Paths.Activity, s.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
            lock (Gate) { File.AppendAllText(file, Json.Write(s.ToDict()) + "\n", new UTF8Encoding(false)); }
        }

        public static List<Session> Load(DateTime from, DateTime to)
        {
            List<Session> list = new List<Session>();
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1))
            {
                string file = Path.Combine(Paths.Activity, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");
                if (!File.Exists(file)) { continue; }
                string[] lines;
                lock (Gate) { lines = File.ReadAllLines(file, Encoding.UTF8); }
                foreach (string line in lines)
                {
                    if (line.Trim().Length == 0) { continue; }
                    try
                    {
                        Session s = Session.FromDict(Json.ReadObject(line));
                        if (s.End >= from && s.Start <= to) { list.Add(s); }
                    }
                    catch (Exception) { }
                }
            }
            return list.OrderBy(s => s.Start).ToList();
        }
    }

    /// <summary>Campiona la finestra in primo piano ogni pochi secondi e scrive le sessioni.</summary>
    public class ActivityTracker
    {
        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

        static readonly string[] PrivateMarkers = { "inprivate", "incognito", "navigazione anonima", "private browsing", "finestra privata" };

        readonly Settings settings;
        readonly Learned learned;
        Session current;

        public ActivityTracker(Settings settings, Learned learned)
        {
            this.settings = settings;
            this.learned = learned;
        }

        public Session Current { get { return current; } }

        public static double IdleSeconds()
        {
            LASTINPUTINFO li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) { return 0; }
            return ((uint)Environment.TickCount - li.dwTime) / 1000.0;
        }

        /// <summary>Un campione: chiamato ogni 5 secondi dall'agente.</summary>
        public void Sample()
        {
            if (!settings.TrackActivity) { Flush(); return; }
            DateTime now = DateTime.Now;
            string app = null, title = null, exe = null;
            if (IdleSeconds() < settings.IdleSeconds)
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    try
                    {
                        using (Process p = Process.GetProcessById((int)pid))
                        {
                            app = p.ProcessName.ToLowerInvariant();
                            try { exe = p.MainModule.FileName; } catch (Exception) { }
                        }
                    }
                    catch (Exception) { }
                    StringBuilder sb = new StringBuilder(512);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    title = sb.ToString();
                }
            }
            if (app == "lockapp") { app = null; }   // schermo bloccato = assenza
            if (app != null) { title = CleanTitle(title); }

            if (current != null && (app == null || app != current.App || title != current.Title || (now - current.Start).TotalMinutes >= 15))
            {
                current.End = now;
                Flush();
            }
            if (app != null && current == null)
            {
                current = new Session();
                current.Start = now;
                current.End = now;
                current.App = app;
                current.Title = title;
                current.Exe = exe;
                current.Ctx = Contexts.Classify(app, title, learned);
            }
            else if (current != null)
            {
                current.End = now;
            }
        }

        string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) { return ""; }
            string low = title.ToLowerInvariant();
            foreach (string m in PrivateMarkers) { if (low.Contains(m)) { return "(finestra privata)"; } }
            if (!settings.TrackTitles) { return ""; }
            return title.Length > 300 ? title.Substring(0, 300) : title;
        }

        public void Flush()
        {
            if (current == null) { return; }
            ActivityStore.Append(current);
            current = null;
        }
    }

    /// <summary>Statistiche su un insieme di sessioni (per desktop, report, skill e Claude).</summary>
    public class ActivityStats
    {
        public double Total;
        public Dictionary<string, double> ByApp = new Dictionary<string, double>();
        public Dictionary<string, double> ByContext = new Dictionary<string, double>();
        public Dictionary<string, string> AppExe = new Dictionary<string, string>();
        public Dictionary<string, string> AppContext = new Dictionary<string, string>();
        public Dictionary<string, double> Projects = new Dictionary<string, double>();
        public Dictionary<string, Dictionary<string, double>> ProjectsByContext = new Dictionary<string, Dictionary<string, double>>();
        public double[,] Heat = new double[7, 24];   // giorno (0 = lunedì) x ora
        public Dictionary<DateTime, double> ByDay = new Dictionary<DateTime, double>();
        public Dictionary<DateTime, DateTime> FirstOfDay = new Dictionary<DateTime, DateTime>();
        public Dictionary<DateTime, DateTime> LastOfDay = new Dictionary<DateTime, DateTime>();
        public Dictionary<string, int> Transitions = new Dictionary<string, int>();
        public int Switches;

        static readonly string[] GenericTitles = { "nuova scheda", "new tab", "(finestra privata)", "program manager", "task switching", "cambio attività", "start", "ricerca", "search" };

        public static ActivityStats From(List<Session> sessions)
        {
            ActivityStats st = new ActivityStats();
            string lastCtx = null;
            foreach (Session s in sessions)
            {
                double sec = s.Seconds;
                if (sec <= 0) { continue; }
                st.Total += sec;
                Add(st.ByApp, s.App, sec);
                Add(st.ByContext, s.Ctx, sec);
                if (!string.IsNullOrEmpty(s.Exe)) { st.AppExe[s.App] = s.Exe; }
                st.AppContext[s.App] = s.Ctx;
                DateTime day = s.Start.Date;
                Add(st.ByDay, day, sec);
                if (!st.FirstOfDay.ContainsKey(day) || s.Start < st.FirstOfDay[day]) { st.FirstOfDay[day] = s.Start; }
                if (!st.LastOfDay.ContainsKey(day) || s.End > st.LastOfDay[day]) { st.LastOfDay[day] = s.End; }
                int dow = ((int)s.Start.DayOfWeek + 6) % 7;
                st.Heat[dow, s.Start.Hour] += sec;
                string project = ProjectOf(s);
                if (project != null)
                {
                    Add(st.Projects, project, sec);
                    if (!st.ProjectsByContext.ContainsKey(s.Ctx)) { st.ProjectsByContext[s.Ctx] = new Dictionary<string, double>(); }
                    Add(st.ProjectsByContext[s.Ctx], project, sec);
                }
                if (s.Ctx != "system")
                {
                    if (lastCtx != null && lastCtx != s.Ctx)
                    {
                        st.Switches++;
                        string key = lastCtx + ">" + s.Ctx;
                        int n;
                        st.Transitions.TryGetValue(key, out n);
                        st.Transitions[key] = n + 1;
                    }
                    lastCtx = s.Ctx;
                }
            }
            return st;
        }

        static void Add<T>(Dictionary<T, double> d, T key, double v)
        {
            double cur;
            d.TryGetValue(key, out cur);
            d[key] = cur + v;
        }

        /// <summary>Nome del progetto/file dal titolo: "progetto.drp - DaVinci Resolve" -> "progetto.drp".</summary>
        public static string ProjectOf(Session s)
        {
            if (string.IsNullOrEmpty(s.Title) || s.Ctx == "system") { return null; }
            string[] parts = Regex.Split(s.Title, @"\s+[-–—|]\s+").Select(p => p.Trim().TrimStart('*', '●').Trim()).Where(p => p.Length > 0).ToArray();
            if (parts.Length < 2) { return null; }
            string first = parts[0];
            string low = first.ToLowerInvariant();
            if (low.Length < 2 || low.Length > 120 || GenericTitles.Contains(low)) { return null; }
            if (low.Contains(s.App)) { return null; }
            return first;
        }

        public IEnumerable<KeyValuePair<string, double>> Top(Dictionary<string, double> d, int n)
        {
            return d.OrderByDescending(x => x.Value).Take(n);
        }

        public Dictionary<string, object> ToDict(int top)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["totale_ore"] = Math.Round(Total / 3600, 2);
            d["contesti"] = ByContext.OrderByDescending(x => x.Value).Select(x => new Dictionary<string, object> { { "contesto", Contexts.Get(x.Key).Name }, { "id", x.Key }, { "ore", Math.Round(x.Value / 3600, 2) } }).ToList();
            d["app"] = Top(ByApp, top).Select(x => new Dictionary<string, object> { { "app", x.Key }, { "contesto", Contexts.Get(AppContext.ContainsKey(x.Key) ? AppContext[x.Key] : Contexts.Other).Name }, { "ore", Math.Round(x.Value / 3600, 2) } }).ToList();
            d["progetti"] = Top(Projects, top).Select(x => new Dictionary<string, object> { { "progetto", x.Key }, { "ore", Math.Round(x.Value / 3600, 2) } }).ToList();
            d["giorni"] = ByDay.OrderBy(x => x.Key).Select(x => new Dictionary<string, object> {
                { "giorno", x.Key.ToString("yyyy-MM-dd ddd", Fmt.It) },
                { "ore", Math.Round(x.Value / 3600, 2) },
                { "inizio", FirstOfDay[x.Key].ToString("HH:mm") },
                { "fine", LastOfDay[x.Key].ToString("HH:mm") } }).ToList();
            d["cambi_di_contesto"] = Switches;
            d["passaggi_frequenti"] = Transitions.OrderByDescending(x => x.Value).Take(5).Select(x => {
                string[] p = x.Key.Split('>');
                return Contexts.Get(p[0]).Name + " -> " + Contexts.Get(p[1]).Name + " (" + x.Value + " volte)";
            }).ToList();
            return d;
        }
    }
}
