// Tipaldi AI - riparazioni. Regola scelta dall'utente:
//   - "sicure" (reversibili, senza amministratore): Tipaldi AI le esegue da solo;
//   - tutte le altre (SFC, DISM, CHKDSK, chiudere app, svuotare il cestino): solo con conferma esplicita
//     dell'utente, mostrata sul desktop. Vale anche quando la richiesta arriva da Claude via MCP.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TipaldiAI
{
    public class FixResult
    {
        public bool Ok;
        public string Message;
        public FixResult(bool ok, string message) { Ok = ok; Message = message; }
    }

    public class FixDef
    {
        public string Id;
        public string Title;
        public string Description;
        public bool Safe;
        public bool Admin;
        public bool Manual;     // sicura, ma solo su richiesta (apre finestre): mai in automatico
        public Func<string, FixResult> Run;
    }

    public static class Fixes
    {
        /// <summary>Chiede conferma all'utente (impostata da agente/MCP: finestra di dialogo sul desktop).</summary>
        public static Func<string, bool> Confirm = DefaultConfirm;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        static bool DefaultConfirm(string text)
        {
            if (!Paths.IsWindows) { return false; }
            // MB_YESNO | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND
            return MessageBox(IntPtr.Zero, text, "Tipaldi AI - conferma", 0x4u | 0x20u | 0x40000u | 0x10000u) == 6;
        }

        public static readonly List<FixDef> All = new List<FixDef>
        {
            new FixDef { Id = "clean-temp", Title = "Pulizia file temporanei", Safe = true,
                Description = "Elimina i file temporanei dell'utente più vecchi di 3 giorni (quelli in uso vengono saltati).", Run = a => CleanTemp() },
            new FixDef { Id = "restart-explorer", Title = "Riavvio di Esplora risorse", Safe = true,
                Description = "Riavvia barra delle applicazioni e desktop (le finestre di Esplora file si chiudono).", Run = a => RestartExplorer() },
            new FixDef { Id = "rebuild-icon-cache", Title = "Ricostruzione cache delle icone", Safe = true,
                Description = "Risolve icone sbagliate o vuote: cancella la cache delle icone dell'utente e riavvia Esplora risorse.", Run = a => RebuildIconCache() },
            new FixDef { Id = "clear-thumbnails", Title = "Pulizia anteprime", Safe = true,
                Description = "Cancella la cache delle anteprime di Esplora file (si ricrea da sola).", Run = a => ClearThumbnails() },
            new FixDef { Id = "flush-dns", Title = "Pulizia cache DNS", Safe = true,
                Description = "Risolve siti che non si aprono dopo un cambio di rete.", Run = a => RunHidden("ipconfig.exe", "/flushdns", 30) },
            new FixDef { Id = "open-security", Title = "Apri Sicurezza di Windows", Safe = true, Manual = true,
                Description = "Apre Sicurezza di Windows per aggiornare o riattivare la protezione.", Run = a => OpenUri("windowsdefender:") },
            new FixDef { Id = "empty-recycle-bin", Title = "Svuota il cestino", Safe = false,
                Description = "Libera spazio. I file nel cestino non saranno più recuperabili.", Run = a => EmptyRecycleBin() },
            new FixDef { Id = "close-hung-app", Title = "Chiudi l'app bloccata", Safe = false,
                Description = "Chiude un'app che non risponde (argomento: PID). Il lavoro non salvato va perso.", Run = CloseApp },
            new FixDef { Id = "sfc-scan", Title = "Verifica e riparazione file di Windows (SFC)", Safe = false, Admin = true,
                Description = "sfc /scannow: controlla e ripara i file di sistema. Circa 10-15 minuti.", Run = a => Elevated("sfc-scan", "sfc.exe /scannow", "lastSfc") },
            new FixDef { Id = "dism-restore", Title = "Riparazione immagine di Windows (DISM)", Safe = false, Admin = true,
                Description = "DISM /RestoreHealth: ripara l'archivio componenti usato da SFC. 10-30 minuti, usa Windows Update.", Run = a => Elevated("dism-restore", "DISM.exe /Online /Cleanup-Image /RestoreHealth", "lastDism") },
            new FixDef { Id = "chkdsk-scan", Title = "Controllo del disco (CHKDSK online)", Safe = false, Admin = true,
                Description = "chkdsk C: /scan: controlla il file system senza bloccare il PC e senza modificarlo.", Run = a => Elevated("chkdsk-scan", "chkdsk.exe " + SystemDrive() + " /scan", "lastChkdsk") },
            new FixDef { Id = "optimize-drives", Title = "Ottimizzazione unità", Safe = false, Admin = true,
                Description = "defrag /C /O: TRIM sugli SSD, deframmentazione sugli HDD.", Run = a => Elevated("optimize-drives", "defrag.exe /C /O", "lastOptimize") },
        };

        public static FixDef Get(string id) { return All.FirstOrDefault(f => f.Id == id); }

        /// <summary>
        /// Esegue una riparazione rispettando la regola di sicurezza. origin: "auto" (agente), "ui" (clic dell'utente),
        /// "mcp" (richiesta di Claude). Le non sicure da "auto" non partono mai; da "mcp" solo dopo la conferma dell'utente.
        /// </summary>
        public static FixResult Execute(string id, string arg, string origin, string reason)
        {
            FixDef f = Get(id);
            if (f == null) { return new FixResult(false, "Riparazione sconosciuta: " + id); }
            if (!f.Safe)
            {
                if (origin == "auto") { return new FixResult(false, "\"" + f.Title + "\" richiede la tua conferma: non viene eseguita in automatico."); }
                if (origin != "ui")
                {
                    string ask = "Claude chiede di eseguire:\n\n" + f.Title + "\n" + f.Description +
                        (string.IsNullOrEmpty(reason) ? "" : "\n\nMotivo: " + reason) +
                        (f.Admin ? "\n\nServirà anche l'autorizzazione di amministratore." : "") + "\n\nConsenti?";
                    if (!Confirm(ask))
                    {
                        Events.Add("info", "Hai rifiutato: " + f.Title + ".");
                        return new FixResult(false, "L'utente ha rifiutato la riparazione \"" + f.Title + "\".");
                    }
                }
            }
            if (f.Manual && origin == "auto") { return new FixResult(false, "\"" + f.Title + "\" parte solo su tua richiesta."); }
            FixResult r;
            try { r = f.Run(arg); }
            catch (Exception ex) { r = new FixResult(false, ex.Message); }
            string who = origin == "mcp" ? "Claude" : origin == "ui" ? "Su tua richiesta" : "Protocollo automatico";
            Events.Add(r.Ok ? "ok" : "warn", who + ": " + f.Title + " - " + r.Message);
            return r;
        }

        // ------------------------------------------------------------------ riparazioni

        static IEnumerable<string> TempDirs()
        {
            HashSet<string> dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string d in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp") })
            {
                try { if (Directory.Exists(d)) { dirs.Add(Path.GetFullPath(d).TrimEnd('\\', '/')); } } catch (Exception) { }
            }
            return dirs;
        }

        static IEnumerable<FileInfo> OldFiles(string dir, DateTime before)
        {
            Stack<DirectoryInfo> stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(dir));
            while (stack.Count > 0)
            {
                DirectoryInfo cur = stack.Pop();
                FileSystemInfo[] items;
                try { items = cur.GetFileSystemInfos(); } catch (Exception) { continue; }
                foreach (FileSystemInfo i in items)
                {
                    if ((i.Attributes & FileAttributes.ReparsePoint) != 0) { continue; }
                    DirectoryInfo di = i as DirectoryInfo;
                    if (di != null) { stack.Push(di); continue; }
                    FileInfo fi = (FileInfo)i;
                    if (fi.LastWriteTime < before && fi.LastAccessTime < before) { yield return fi; }
                }
            }
        }

        public static double TempSize(TimeSpan olderThan)
        {
            double total = 0;
            DateTime before = DateTime.Now - olderThan;
            foreach (string d in TempDirs()) { foreach (FileInfo f in OldFiles(d, before)) { try { total += f.Length; } catch (Exception) { } } }
            return total;
        }

        static FixResult CleanTemp()
        {
            double freed = 0;
            int files = 0, skipped = 0;
            DateTime before = DateTime.Now.AddDays(-3);
            foreach (string d in TempDirs())
            {
                foreach (FileInfo f in OldFiles(d, before).ToList())
                {
                    try { long len = f.Length; f.Delete(); freed += len; files++; }
                    catch (Exception) { skipped++; }
                }
                // cartelle rimaste vuote
                try
                {
                    foreach (string sub in Directory.GetDirectories(d, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    {
                        try { if (!Directory.EnumerateFileSystemEntries(sub).Any() && Directory.GetLastWriteTime(sub) < before) { Directory.Delete(sub); } } catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
            State.SetTime("lastCleanup", DateTime.Now);
            return new FixResult(true, Fmt.Bytes(freed) + " liberati (" + files + " file" + (skipped > 0 ? ", " + skipped + " in uso saltati" : "") + ").");
        }

        public static FixResult RestartExplorer()
        {
            foreach (Process p in Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { } }
            Thread.Sleep(2000);
            if (Process.GetProcessesByName("explorer").Length == 0) { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")); }
            return new FixResult(true, "Esplora risorse riavviato.");
        }

        static FixResult RebuildIconCache()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            foreach (Process p in Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { } }
            Thread.Sleep(1500);
            int n = 0;
            foreach (string pattern in new[] { "iconcache_*.db" })
            {
                try { foreach (string f in Directory.GetFiles(dir, pattern)) { try { File.Delete(f); n++; } catch (Exception) { } } } catch (Exception) { }
            }
            try { string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IconCache.db"); if (File.Exists(legacy)) { File.Delete(legacy); n++; } } catch (Exception) { }
            if (Process.GetProcessesByName("explorer").Length == 0) { Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")); }
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);   // SHCNE_ASSOCCHANGED
            return new FixResult(true, n + " file di cache icone rimossi, Esplora risorse riavviato.");
        }

        static FixResult ClearThumbnails()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
            int n = 0, busy = 0;
            try { foreach (string f in Directory.GetFiles(dir, "thumbcache_*.db")) { try { File.Delete(f); n++; } catch (Exception) { busy++; } } } catch (Exception) { }
            return new FixResult(true, n + " file di anteprime rimossi" + (busy > 0 ? " (" + busy + " in uso: si puliranno al prossimo riavvio)" : "") + ".");
        }

        static FixResult EmptyRecycleBin()
        {
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);   // senza conferma, progresso o suono
            return new FixResult(hr == 0 || hr == unchecked((int)0x8000FFFF), "Cestino svuotato.");
        }

        static FixResult CloseApp(string arg)
        {
            int pid;
            if (!int.TryParse(arg, out pid)) { return new FixResult(false, "Serve il PID dell'app."); }
            using (Process p = Process.GetProcessById(pid))
            {
                string name = p.ProcessName;
                p.Kill();
                p.WaitForExit(5000);
                return new FixResult(true, name + " chiusa.");
            }
        }

        static FixResult OpenUri(string uri)
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return new FixResult(true, "Aperto.");
        }

        static string SystemDrive() { return Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'); }

        public static FixResult RunHidden(string exe, string args, int timeoutSec)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch (Exception) { } return new FixResult(false, "Tempo scaduto."); }
                return new FixResult(p.ExitCode == 0, p.ExitCode == 0 ? "Completato." : "Codice " + p.ExitCode + ": " + Last(output, 200));
            }
        }

        static string Last(string s, int n) { s = (s ?? "").Trim(); return s.Length > n ? s.Substring(s.Length - n) : s; }

        /// <summary>
        /// Comando da amministratore: Windows mostra la richiesta UAC. L'output va in logs\&lt;id&gt;-data.txt
        /// (il processo elevato è dello stesso utente e può scrivere nella sua cartella).
        /// </summary>
        static FixResult Elevated(string id, string command, string stateKey)
        {
            string log = Path.Combine(Paths.Logs, id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
            string script = "/c " + command + " > \"" + log + "\" 2>&1";
            ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), script) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Minimized };
            Process p;
            try { p = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception) { return new FixResult(false, "Autorizzazione di amministratore negata."); }
            using (p)
            {
                p.WaitForExit(60 * 60 * 1000);
                if (stateKey != null) { State.SetTime(stateKey, DateTime.Now); }
                string output = "";
                try { output = ReadAnyEncoding(log); } catch (Exception) { }
                string summary = Summarize(id, output);
                return new FixResult(p.ExitCode == 0 || summary.Length > 0, (summary.Length > 0 ? summary : "Codice di uscita " + p.ExitCode + ".") + " Dettagli: " + log);
            }
        }

        static string ReadAnyEncoding(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            // sfc scrive in UTF-16 senza BOM: se metà dei byte sono zero, è UTF-16
            int zeros = b.Count(x => x == 0);
            return zeros > b.Length / 3 ? Encoding.Unicode.GetString(b) : Encoding.GetEncoding(850).GetString(b);
        }

        static string Summarize(string id, string output)
        {
            string o = (output ?? "").Replace("\0", "").ToLowerInvariant();
            if (id == "sfc-scan")
            {
                if (o.Contains("did not find any integrity violations") || o.Contains("non ha rilevato violazioni")) { return "Nessuna violazione di integrità."; }
                if (o.Contains("successfully repaired") || o.Contains("riparati correttamente")) { return "File danneggiati trovati e riparati."; }
                if (o.Contains("unable to fix") || o.Contains("non è stato in grado") || o.Contains("non è stato in grado")) { return "File danneggiati che SFC non riesce a riparare: serve DISM (dism-restore), poi di nuovo SFC."; }
            }
            if (id == "dism-restore" && (o.Contains("completed successfully") || o.Contains("completata"))) { return "Immagine di Windows riparata."; }
            if (id == "chkdsk-scan")
            {
                if (o.Contains("found no problems") || o.Contains("non ha trovato problemi") || o.Contains("nessun problema")) { return "Nessun problema nel file system."; }
                if (o.Contains("found problems") || o.Contains("rilevato problemi")) { return "Problemi nel file system: va pianificata una riparazione al riavvio (chkdsk /f)."; }
            }
            return "";
        }
    }
}
