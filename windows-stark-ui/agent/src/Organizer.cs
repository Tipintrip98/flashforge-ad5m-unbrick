// Tipaldi AI - ordine automatico di Desktop e Download per contesto di lavoro.
// Regole prudenti: solo file (mai cartelle), non nascosti/di sistema, non collegamenti, non in uso,
// non modificati da almeno 24 ore (Desktop) o 2 ore (Download). Ogni spostamento va nel registro e si annulla.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TipaldiAI
{
    public class MovePlan
    {
        public string From;
        public string To;
        public string Context;
    }

    public static class Organizer
    {
        static readonly string[] Skip = { ".lnk", ".url", ".ini", ".tmp", ".crdownload", ".part", ".partial", ".download", ".opdownload" };
        static readonly string[] Installers = { "exe", "msi", "msix", "msixbundle", "appx", "appinstaller" };
        static readonly string[] Archives = { "zip", "rar", "7z", "tar", "gz", "tgz", "bz2", "xz", "iso" };

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

        public static string Downloads
        {
            get
            {
                if (Paths.IsWindows)
                {
                    IntPtr p;
                    if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                    {
                        string s = Marshal.PtrToStringUni(p);
                        Marshal.FreeCoTaskMem(p);
                        return s;
                    }
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        public static string Desktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }

        public static string FolderFor(string target)
        {
            if (target == "desktop") { return Desktop; }
            if (target == "downloads") { return Downloads; }
            return target;   // percorso esplicito (usato dai test)
        }

        /// <summary>Nome della cartella di destinazione per un file, o null se resta dov'è.</summary>
        public static string CategoryOf(string file)
        {
            string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
            if (Installers.Contains(ext)) { return "Installer"; }
            if (Archives.Contains(ext)) { return "Archivi"; }
            ContextDef c = Contexts.ForExtension(ext);
            if (c == null || c.Id == Contexts.Web || c.Id == Contexts.Other) { return null; }
            return c.Name;
        }

        public static List<MovePlan> Plan(string target, TimeSpan minAge)
        {
            List<MovePlan> plan = new List<MovePlan>();
            string dir = FolderFor(target);
            if (!Directory.Exists(dir)) { return plan; }
            DateTime before = DateTime.Now - minAge;
            foreach (string f in Directory.GetFiles(dir))
            {
                FileInfo fi = new FileInfo(f);
                if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) { continue; }
                if (Skip.Contains(fi.Extension.ToLowerInvariant())) { continue; }
                if (fi.LastWriteTime > before || fi.CreationTime > before) { continue; }
                string cat = CategoryOf(f);
                if (cat == null) { continue; }
                string destDir = Path.Combine(dir, cat);
                plan.Add(new MovePlan { From = f, To = Unique(Path.Combine(destDir, fi.Name)), Context = cat });
            }
            return plan;
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) { return path; }
            string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string p = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(p)) { return p; }
            }
        }

        static bool InUse(string file)
        {
            try { using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return false; }
            catch (Exception) { return true; }
        }

        public static Dictionary<string, object> Apply(string target, TimeSpan minAge)
        {
            List<MovePlan> plan = Plan(target, minAge);
            string batch = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + target;
            int moved = 0, busy = 0;
            List<string> lines = new List<string>();
            foreach (MovePlan m in plan)
            {
                if (InUse(m.From)) { busy++; continue; }
                try
                {
                    string destDir = Path.GetDirectoryName(m.To);
                    bool created = !Directory.Exists(destDir);
                    Directory.CreateDirectory(destDir);
                    if (created) { Icons.ApplyFolderIcon(destDir, m.Context); }
                    File.Move(m.From, m.To);
                    moved++;
                    Dictionary<string, object> j = new Dictionary<string, object> { { "batch", batch }, { "from", m.From }, { "to", m.To }, { "t", DateTime.Now.ToString("s", CultureInfo.InvariantCulture) } };
                    lines.Add(Json.Write(j));
                }
                catch (Exception) { busy++; }
            }
            if (lines.Count > 0) { File.AppendAllText(Paths.JournalFile, string.Join("\n", lines) + "\n", new UTF8Encoding(false)); }
            if (moved > 0)
            {
                Events.Add("ok", "Protocollo ordine: " + moved + " file in " + (target == "desktop" ? "Desktop" : target == "downloads" ? "Download" : target) + " sistemati per contesto." + (busy > 0 ? " " + busy + " in uso lasciati al loro posto." : ""));
            }
            return new Dictionary<string, object> { { "spostati", moved }, { "saltati", busy }, { "lotto", batch } };
        }

        /// <summary>Annulla l'ultimo riordino (o un lotto preciso): rimette i file dov'erano.</summary>
        public static Dictionary<string, object> Undo(string batch)
        {
            if (!File.Exists(Paths.JournalFile)) { return new Dictionary<string, object> { { "ripristinati", 0 } }; }
            List<Dictionary<string, object>> all = File.ReadAllLines(Paths.JournalFile, Encoding.UTF8).Where(l => l.Trim().Length > 0).Select(l => Json.ReadObject(l)).ToList();
            if (all.Count == 0) { return new Dictionary<string, object> { { "ripristinati", 0 } }; }
            if (string.IsNullOrEmpty(batch)) { batch = Json.Str(all[all.Count - 1], "batch", ""); }
            int back = 0, failed = 0;
            List<Dictionary<string, object>> keep = new List<Dictionary<string, object>>();
            foreach (Dictionary<string, object> j in all)
            {
                if (Json.Str(j, "batch", "") != batch) { keep.Add(j); continue; }
                string from = Json.Str(j, "from", ""), to = Json.Str(j, "to", "");
                try
                {
                    if (File.Exists(to) && !File.Exists(from)) { File.Move(to, from); back++; }
                    else { failed++; }
                }
                catch (Exception) { failed++; keep.Add(j); }
            }
            File.WriteAllText(Paths.JournalFile, string.Join("\n", keep.Select(k => Json.Write(k))) + (keep.Count > 0 ? "\n" : ""), new UTF8Encoding(false));
            Events.Add("info", "Riordino annullato: " + back + " file rimessi al loro posto.");
            return new Dictionary<string, object> { { "lotto", batch }, { "ripristinati", back }, { "non_ripristinabili", failed } };
        }

        public static int MovedSince(DateTime since)
        {
            if (!File.Exists(Paths.JournalFile)) { return 0; }
            int n = 0;
            foreach (string l in File.ReadAllLines(Paths.JournalFile, Encoding.UTF8))
            {
                if (l.Trim().Length == 0) { continue; }
                DateTime t;
                if (DateTime.TryParse(Json.Str(Json.ReadObject(l), "t", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t >= since) { n++; }
            }
            return n;
        }
    }

    /// <summary>Protocolli di ottimizzazione automatica.</summary>
    public class Optimizer
    {
        const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        DateTime heavySince = DateTime.MinValue;
        DateTime lightSince = DateTime.MinValue;

        /// <summary>
        /// Protocollo Overdrive: dopo 2 minuti in un contesto pesante (montaggio, VFX, 3D) passa al piano
        /// "Prestazioni elevate"; 10 minuti dopo averlo lasciato rimette il piano di prima.
        /// </summary>
        public void Tick(string ctx, Settings settings)
        {
            bool heavy = ctx != null && Contexts.Get(ctx).Heavy;
            DateTime now = DateTime.Now;
            string saved = State.Get("overdriveOriginal");
            if (heavy)
            {
                lightSince = DateTime.MinValue;
                if (heavySince == DateTime.MinValue) { heavySince = now; }
                if (settings.Overdrive && saved == null && (now - heavySince).TotalMinutes >= 2)
                {
                    string current = ActiveScheme();
                    if (current != null && !current.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase) && SetScheme(HighPerformance))
                    {
                        State.Set("overdriveOriginal", current);
                        Events.Add("ai", "Protocollo Overdrive attivo: piano energetico Prestazioni elevate per " + Contexts.Get(ctx).Name + ".");
                    }
                }
            }
            else
            {
                heavySince = DateTime.MinValue;
                if (lightSince == DateTime.MinValue) { lightSince = now; }
                if (saved != null && ((now - lightSince).TotalMinutes >= 10 || !settings.Overdrive)) { Release(); }
            }
        }

        public static void Release()
        {
            string saved = State.Get("overdriveOriginal");
            if (saved == null) { return; }
            if (SetScheme(saved)) { Events.Add("info", "Protocollo Overdrive concluso: piano energetico originale ripristinato."); }
            State.Set("overdriveOriginal", null);
        }

        static string ActiveScheme()
        {
            FixResult r;
            string output = Run("powercfg.exe", "/getactivescheme", out r);
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(output ?? "", "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value : null;
        }

        static bool SetScheme(string guid)
        {
            FixResult r;
            Run("powercfg.exe", "/setactive " + guid, out r);
            return r.Ok;
        }

        static string Run(string exe, string args, out FixResult result)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    result = new FixResult(p.ExitCode == 0, o);
                    return o;
                }
            }
            catch (Exception ex) { result = new FixResult(false, ex.Message); return null; }
        }
    }
}
