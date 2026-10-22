// Tipaldi AI - base comune: JSON, percorsi, impostazioni, stato, eventi (console AI).
// C# 5: compilato dal csc di .NET Framework 4 incluso in Windows (nessun SDK da installare).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TipaldiAI
{
    public static class Json
    {
        static JavaScriptSerializer NewSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 256;
            return s;
        }

        public static string Write(object value) { return NewSerializer().Serialize(value); }

        public static object Read(string text) { return NewSerializer().DeserializeObject(text); }

        public static Dictionary<string, object> ReadObject(string text)
        {
            Dictionary<string, object> d = Read(text) as Dictionary<string, object>;
            return d ?? new Dictionary<string, object>();
        }

        public static Dictionary<string, object> ReadFile(string path)
        {
            try
            {
                if (File.Exists(path)) { return ReadObject(File.ReadAllText(path, Encoding.UTF8)); }
            }
            catch (Exception) { }
            return new Dictionary<string, object>();
        }

        public static void WriteFile(string path, object value, bool pretty)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string text = Write(value);
            if (pretty) { text = Indent(text); }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) { File.Delete(path); }
            File.Move(tmp, path);
        }

        public static string Str(IDictionary<string, object> d, string key, string def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) { return Convert.ToString(v, CultureInfo.InvariantCulture); }
            return def;
        }

        public static double Num(IDictionary<string, object> d, string key, double def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch (Exception) { }
            }
            return def;
        }

        public static bool Bool(IDictionary<string, object> d, string key, bool def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is bool) { return (bool)v; }
            return def;
        }

        public static Dictionary<string, object> Obj(IDictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) { Dictionary<string, object> o = v as Dictionary<string, object>; if (o != null) { return o; } }
            return new Dictionary<string, object>();
        }

        public static List<object> Arr(IDictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is IEnumerable && !(v is string)) { return ((IEnumerable)v).Cast<object>().ToList(); }
            return new List<object>();
        }

        /// <summary>Rientro leggibile per file modificabili a mano (impostazioni, config di Claude).</summary>
        public static string Indent(string json)
        {
            StringBuilder sb = new StringBuilder();
            int level = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[++i]); }
                    else if (c == '"') { inString = false; }
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[++i]); break; }
                        level++; sb.Append('\n').Append(' ', level * 2); break;
                    case '}':
                    case ']':
                        level--; sb.Append('\n').Append(' ', level * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', level * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    public static class Paths
    {
        static string root;

        /// <summary>%LOCALAPPDATA%\TipaldiAI (TIPALDI_HOME lo sostituisce nei test).</summary>
        public static string Root
        {
            get
            {
                if (root == null)
                {
                    string env = Environment.GetEnvironmentVariable("TIPALDI_HOME");
                    root = !string.IsNullOrEmpty(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TipaldiAI");
                    Directory.CreateDirectory(root);
                }
                return root;
            }
        }

        public static string Sub(string name)
        {
            string p = Path.Combine(Root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        public static string Activity { get { return Sub("activity"); } }
        public static string Reports { get { return Sub("reports"); } }
        public static string Logs { get { return Sub("logs"); } }
        public static string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }
        public static string StateFile { get { return Path.Combine(Root, "state.json"); } }
        public static string LearnedFile { get { return Path.Combine(Root, "learned.json"); } }
        public static string EventsFile { get { return Path.Combine(Root, "events.jsonl"); } }
        public static string IssuesFile { get { return Path.Combine(Root, "issues.json"); } }
        public static string JournalFile { get { return Path.Combine(Sub("organizer"), "journal.jsonl"); } }

        /// <summary>Cartella delle skill personali di Claude Code (TIPALDI_SKILLS la sostituisce nei test).</summary>
        public static string Skills
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("TIPALDI_SKILLS");
                string p = !string.IsNullOrEmpty(env) ? env : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string AppDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static string ExePath { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }

        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
    }

    /// <summary>Impostazioni dell'utente (settings.json, modificabile a mano).</summary>
    public class Settings
    {
        public bool TrackActivity = true;
        public bool TrackTitles = true;          // scelta dell'utente: anche i titoli delle finestre arrivano a Claude
        public bool AutoFixSafe = true;          // riparazioni sicure in automatico
        public bool ClaudeAutoResolve = true;    // problemi senza riparazione sicura: Claude Code (se installato) analizza
        public bool Overdrive = true;            // piano energetico prestazioni nei contesti pesanti
        public bool OrganizeDownloads = true;
        public bool OrganizeDesktop = true;
        public bool WeeklyReport = true;
        public bool Skills = true;
        public bool Hud = true;
        public int IdleSeconds = 180;

        public static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, object> d = Json.ReadFile(Paths.SettingsFile);
            s.TrackActivity = Json.Bool(d, "trackActivity", s.TrackActivity);
            s.TrackTitles = Json.Bool(d, "trackTitles", s.TrackTitles);
            s.AutoFixSafe = Json.Bool(d, "autoFixSafe", s.AutoFixSafe);
            s.ClaudeAutoResolve = Json.Bool(d, "claudeAutoResolve", s.ClaudeAutoResolve);
            s.Overdrive = Json.Bool(d, "overdrive", s.Overdrive);
            s.OrganizeDownloads = Json.Bool(d, "organizeDownloads", s.OrganizeDownloads);
            s.OrganizeDesktop = Json.Bool(d, "organizeDesktop", s.OrganizeDesktop);
            s.WeeklyReport = Json.Bool(d, "weeklyReport", s.WeeklyReport);
            s.Skills = Json.Bool(d, "skills", s.Skills);
            s.Hud = Json.Bool(d, "hud", s.Hud);
            s.IdleSeconds = (int)Json.Num(d, "idleSeconds", s.IdleSeconds);
            if (!File.Exists(Paths.SettingsFile)) { s.Save(); }
            return s;
        }

        public void Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["trackActivity"] = TrackActivity;
            d["trackTitles"] = TrackTitles;
            d["autoFixSafe"] = AutoFixSafe;
            d["claudeAutoResolve"] = ClaudeAutoResolve;
            d["overdrive"] = Overdrive;
            d["organizeDownloads"] = OrganizeDownloads;
            d["organizeDesktop"] = OrganizeDesktop;
            d["weeklyReport"] = WeeklyReport;
            d["skills"] = Skills;
            d["hud"] = Hud;
            d["idleSeconds"] = IdleSeconds;
            Json.WriteFile(Paths.SettingsFile, d, true);
        }
    }

    /// <summary>Stato interno (ultime esecuzioni dei protocolli, attese tra una riparazione e l'altra).</summary>
    public static class State
    {
        static readonly object Gate = new object();

        public static string Get(string key)
        {
            lock (Gate) { return Json.Str(Json.ReadFile(Paths.StateFile), key, null); }
        }

        public static void Set(string key, string value)
        {
            lock (Gate)
            {
                Dictionary<string, object> d = Json.ReadFile(Paths.StateFile);
                if (value == null) { d.Remove(key); } else { d[key] = value; }
                Json.WriteFile(Paths.StateFile, d, true);
            }
        }

        public static DateTime GetTime(string key)
        {
            string v = Get(key);
            DateTime t;
            if (v != null && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) { return t; }
            return DateTime.MinValue;
        }

        public static void SetTime(string key, DateTime t) { Set(key, t.ToString("o", CultureInfo.InvariantCulture)); }
    }

    /// <summary>Eventi mostrati nella console AI del desktop e usati nel report settimanale.</summary>
    public static class Events
    {
        static readonly object Gate = new object();

        public static void Add(string kind, string text)
        {
            Dictionary<string, object> e = new Dictionary<string, object>();
            e["t"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            e["kind"] = kind;
            e["text"] = text;
            lock (Gate)
            {
                try { File.AppendAllText(Paths.EventsFile, Json.Write(e) + "\n", new UTF8Encoding(false)); } catch (Exception) { }
            }
        }

        public static List<Dictionary<string, object>> Read(DateTime since, int max)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            string[] lines;
            lock (Gate)
            {
                try { lines = File.Exists(Paths.EventsFile) ? File.ReadAllLines(Paths.EventsFile, Encoding.UTF8) : new string[0]; }
                catch (Exception) { lines = new string[0]; }
            }
            for (int i = lines.Length - 1; i >= 0 && list.Count < max; i--)
            {
                if (lines[i].Length == 0) { continue; }
                Dictionary<string, object> e;
                try { e = Json.ReadObject(lines[i]); } catch (Exception) { continue; }
                DateTime t;
                if (DateTime.TryParse(Json.Str(e, "t", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t < since) { break; }
                list.Add(e);
            }
            list.Reverse();
            return list;
        }
    }

    public static class Fmt
    {
        public static readonly CultureInfo It = new CultureInfo("it-IT");

        public static string Duration(double seconds)
        {
            int total = (int)Math.Round(seconds / 60.0);
            if (total < 60) { return total + " min"; }
            return (total / 60) + " h " + (total % 60).ToString("00") + " min";
        }

        public static string Bytes(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return b.ToString(i == 0 ? "0" : "0.0", It) + " " + u[i];
        }

        public static string Slug(string s)
        {
            string norm = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder sb = new StringBuilder();
            foreach (char c in norm)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { continue; }
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') { sb.Append('-'); }
            }
            return sb.ToString().Trim('-');
        }

        public static string Html(string s)
        {
            if (s == null) { return ""; }
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
