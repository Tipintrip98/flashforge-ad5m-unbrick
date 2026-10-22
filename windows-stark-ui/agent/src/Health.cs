// Tipaldi AI - diagnosi: dischi (salute e spazio), errori di disco e file system nel registro eventi,
// integrità di Windows, protezione antivirus, memoria, app bloccate, riavvii in sospeso.
// Solo letture: nessuna di queste verifiche modifica il sistema.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TipaldiAI
{
    public class Issue
    {
        public string Id;          // stabile: serve per non ripetere riparazioni e segnalazioni
        public string Severity;    // info | warn | crit
        public string Category;    // disco | integrita | sicurezza | prestazioni | app | sistema
        public string Title;
        public string Detail;
        public string FixId;       // riparazione proposta (può essere null)
        public string FixArg;

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["id"] = Id; d["gravita"] = Severity; d["categoria"] = Category; d["titolo"] = Title; d["dettaglio"] = Detail;
            if (FixId != null)
            {
                FixDef f = Fixes.Get(FixId);
                d["riparazione"] = FixId;
                if (FixArg != null) { d["argomento"] = FixArg; }
                if (f != null) { d["riparazione_sicura"] = f.Safe; d["serve_amministratore"] = f.Admin; }
            }
            return d;
        }

        public static Issue FromDict(Dictionary<string, object> d)
        {
            Issue i = new Issue();
            i.Id = Json.Str(d, "id", ""); i.Severity = Json.Str(d, "gravita", "info"); i.Category = Json.Str(d, "categoria", "");
            i.Title = Json.Str(d, "titolo", ""); i.Detail = Json.Str(d, "dettaglio", ""); i.FixId = Json.Str(d, "riparazione", null);
            i.FixArg = Json.Str(d, "argomento", null);
            return i;
        }
    }

    public class DiskInfo
    {
        public string Name;
        public string Media;       // SSD | HDD | ?
        public string Health;      // Integro | Avviso | Guasto | ?
        public string Bus;
        public double SizeGb;

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> { { "nome", Name }, { "tipo", Media }, { "salute", Health }, { "bus", Bus }, { "gb", Math.Round(SizeGb) } };
        }
    }

    public class VolumeInfo
    {
        public string Letter;
        public string Label;
        public double TotalGb;
        public double FreeGb;
        public double FreePct { get { return TotalGb > 0 ? 100 * FreeGb / TotalGb : 100; } }

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object> { { "unita", Letter }, { "etichetta", Label }, { "totale_gb", Math.Round(TotalGb, 1) }, { "libero_gb", Math.Round(FreeGb, 1) }, { "libero_pct", Math.Round(FreePct) } };
        }
    }

    public class HealthReport
    {
        public DateTime Time = DateTime.Now;
        public bool Deep;
        public List<Issue> Issues = new List<Issue>();
        public List<DiskInfo> Disks = new List<DiskInfo>();
        public List<VolumeInfo> Volumes = new List<VolumeInfo>();
        public double MemoryLoad;
        public TimeSpan Uptime;
        public List<string> Notes = new List<string>();

        public string Status
        {
            get
            {
                if (Issues.Any(i => i.Severity == "crit")) { return "CRITICO"; }
                if (Issues.Any(i => i.Severity == "warn")) { return "ATTENZIONE"; }
                return "OTTIMALE";
            }
        }

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["ora"] = Time.ToString("s", CultureInfo.InvariantCulture);
            d["stato"] = Status;
            d["completa"] = Deep;
            d["problemi"] = Issues.Select(i => i.ToDict()).ToList();
            d["dischi_fisici"] = Disks.Select(x => x.ToDict()).ToList();
            d["volumi"] = Volumes.Select(x => x.ToDict()).ToList();
            d["memoria_in_uso_pct"] = Math.Round(MemoryLoad);
            d["acceso_da_ore"] = Math.Round(Uptime.TotalHours, 1);
            d["note"] = Notes;
            return d;
        }

        public void Save()
        {
            Json.WriteFile(Paths.IssuesFile, ToDict(), true);
        }

        public static HealthReport LoadLast()
        {
            Dictionary<string, object> d = Json.ReadFile(Paths.IssuesFile);
            HealthReport r = new HealthReport();
            DateTime t;
            if (DateTime.TryParse(Json.Str(d, "ora", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) { r.Time = t; } else { r.Time = DateTime.MinValue; }
            r.Deep = Json.Bool(d, "completa", false);
            foreach (object o in Json.Arr(d, "problemi")) { Dictionary<string, object> i = o as Dictionary<string, object>; if (i != null) { r.Issues.Add(Issue.FromDict(i)); } }
            foreach (object o in Json.Arr(d, "dischi_fisici"))
            {
                Dictionary<string, object> x = o as Dictionary<string, object>;
                if (x == null) { continue; }
                r.Disks.Add(new DiskInfo { Name = Json.Str(x, "nome", ""), Media = Json.Str(x, "tipo", "?"), Health = Json.Str(x, "salute", "?"), Bus = Json.Str(x, "bus", ""), SizeGb = Json.Num(x, "gb", 0) });
            }
            foreach (object o in Json.Arr(d, "volumi"))
            {
                Dictionary<string, object> x = o as Dictionary<string, object>;
                if (x == null) { continue; }
                r.Volumes.Add(new VolumeInfo { Letter = Json.Str(x, "unita", ""), Label = Json.Str(x, "etichetta", ""), TotalGb = Json.Num(x, "totale_gb", 0), FreeGb = Json.Num(x, "libero_gb", 0) });
            }
            r.MemoryLoad = Json.Num(d, "memoria_in_uso_pct", 0);
            return r;
        }
    }

    public static class Health
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad; public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile; public ulong ullTotalVirtual; public ulong ullAvailVirtual; public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

        public static double MemoryLoad(out double totalGb)
        {
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            totalGb = 0;
            if (!GlobalMemoryStatusEx(ref m)) { return 0; }
            totalGb = m.ullTotalPhys / 1073741824.0;
            return m.dwMemoryLoad;
        }

        /// <summary>Verifica rapida (pochi secondi). deep = anche registro eventi, antivirus, integrità.</summary>
        public static HealthReport Run(bool deep)
        {
            HealthReport r = new HealthReport();
            r.Deep = deep;
            Safe(r, "volumi", () => CheckVolumes(r));
            Safe(r, "dischi", () => CheckPhysicalDisks(r));
            Safe(r, "memoria", () => CheckMemory(r));
            Safe(r, "app bloccate", () => CheckHungApps(r));
            Safe(r, "Esplora risorse", () => CheckExplorer(r));
            Safe(r, "file temporanei", () => CheckTemp(r));
            Safe(r, "tempo di attività", () => CheckUptime(r));
            if (deep)
            {
                Safe(r, "eventi disco", () => CheckDiskEvents(r));
                Safe(r, "previsione guasti", () => CheckSmart(r));
                Safe(r, "riavvio in sospeso", () => CheckPendingReboot(r));
                Safe(r, "antivirus", () => CheckDefender(r));
                Safe(r, "integrità", () => CheckIntegrityAge(r));
                Safe(r, "cestino", () => CheckRecycleBin(r));
            }
            r.Issues = r.Issues.OrderBy(i => i.Severity == "crit" ? 0 : i.Severity == "warn" ? 1 : 2).ToList();
            return r;
        }

        static void Safe(HealthReport r, string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { r.Notes.Add("Verifica " + name + " non disponibile: " + ex.Message); }
        }

        static void AddIssue(HealthReport r, string id, string sev, string cat, string title, string detail, string fix, string arg)
        {
            r.Issues.Add(new Issue { Id = id, Severity = sev, Category = cat, Title = title, Detail = detail, FixId = fix, FixArg = arg });
        }

        static void CheckVolumes(HealthReport r)
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) { continue; }
                VolumeInfo v = new VolumeInfo { Letter = d.Name.TrimEnd('\\'), Label = d.VolumeLabel, TotalGb = d.TotalSize / 1073741824.0, FreeGb = d.AvailableFreeSpace / 1073741824.0 };
                r.Volumes.Add(v);
                bool system = string.Equals(d.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase);
                if (v.FreePct < 5 || (system && v.FreeGb < 5))
                {
                    AddIssue(r, "spazio-" + v.Letter, "crit", "disco", "Disco " + v.Letter + " quasi pieno", Math.Round(v.FreeGb, 1) + " GB liberi su " + Math.Round(v.TotalGb) + " GB (" + Math.Round(v.FreePct) + "%).", system ? "clean-temp" : null, null);
                }
                else if (v.FreePct < 10)
                {
                    AddIssue(r, "spazio-" + v.Letter, "warn", "disco", "Poco spazio su " + v.Letter, Math.Round(v.FreeGb, 1) + " GB liberi (" + Math.Round(v.FreePct) + "%).", system ? "clean-temp" : null, null);
                }
            }
        }

        static void CheckPhysicalDisks(HealthReport r)
        {
            ManagementScope scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName, MediaType, HealthStatus, BusType, Size FROM MSFT_PhysicalDisk")))
            {
                foreach (ManagementObject o in s.Get())
                {
                    DiskInfo d = new DiskInfo();
                    d.Name = Convert.ToString(o["FriendlyName"]);
                    int media = Convert.ToInt32(o["MediaType"] ?? 0);
                    d.Media = media == 4 ? "SSD" : media == 3 ? "HDD" : media == 5 ? "SCM" : "?";
                    int health = Convert.ToInt32(o["HealthStatus"] ?? 5);
                    d.Health = health == 0 ? "Integro" : health == 1 ? "Avviso" : health == 2 ? "Guasto" : "?";
                    int bus = Convert.ToInt32(o["BusType"] ?? 0);
                    d.Bus = bus == 17 ? "NVMe" : bus == 11 ? "SATA" : bus == 7 ? "USB" : bus == 8 ? "RAID" : bus == 10 ? "SAS" : "";
                    d.SizeGb = Convert.ToDouble(o["Size"] ?? 0) / 1e9;
                    r.Disks.Add(d);
                    if (health == 1) { AddIssue(r, "salute-" + Fmt.Slug(d.Name), "warn", "disco", "Disco " + d.Name + ": salute in avviso", "Windows segnala un degrado del disco. Fai subito un backup dei dati importanti.", null, null); }
                    if (health == 2) { AddIssue(r, "salute-" + Fmt.Slug(d.Name), "crit", "disco", "Disco " + d.Name + ": guasto segnalato", "Windows considera il disco guasto. Copia subito i dati importanti su un altro disco.", null, null); }
                }
            }
        }

        static void CheckSmart(HealthReport r)
        {
            // previsione guasti S.M.A.R.T.: leggibile solo con privilegi di amministratore
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        if (Convert.ToBoolean(o["PredictFailure"]))
                        {
                            AddIssue(r, "smart-" + Fmt.Slug(Convert.ToString(o["InstanceName"])), "crit", "disco", "S.M.A.R.T.: guasto del disco previsto", Convert.ToString(o["InstanceName"]), null, null);
                        }
                    }
                }
            }
            catch (ManagementException) { r.Notes.Add("Previsione guasti S.M.A.R.T. leggibile solo con la scansione completa da amministratore."); }
        }

        static void CheckDiskEvents(HealthReport r)
        {
            // eventi di sistema degli ultimi 7 giorni che indicano problemi a disco o file system
            Dictionary<string, int[]> wanted = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "disk", new[] { 7, 11, 51, 153, 154 } },
                { "Ntfs", new[] { 55, 98, 137, 140 } },
                { "volmgr", new[] { 161, 162 } },
                { "stornvme", new[] { 11, 129 } },
                { "storahci", new[] { 129 } },
                { "Microsoft-Windows-Kernel-Power", new[] { 41 } },
                { "EventLog", new[] { 6008 } },
                { "Microsoft-Windows-WHEA-Logger", new[] { 1, 17, 18, 19, 47 } },
            };
            DateTime since = DateTime.Now.AddDays(-7);
            Dictionary<string, int> counts = new Dictionary<string, int>();
            Dictionary<string, string> sample = new Dictionary<string, string>();
            using (EventLog log = new EventLog("System"))
            {
                int n = log.Entries.Count;
                for (int i = n - 1; i >= 0 && i >= n - 20000; i--)
                {
                    EventLogEntry e;
                    try { e = log.Entries[i]; } catch (Exception) { continue; }
                    if (e.TimeGenerated < since) { break; }
                    int[] ids;
                    if (!wanted.TryGetValue(e.Source, out ids)) { continue; }
                    int id = (int)(e.InstanceId & 0xFFFF);
                    if (!ids.Contains(id)) { continue; }
                    string key = e.Source + ":" + id;
                    int c;
                    counts.TryGetValue(key, out c);
                    counts[key] = c + 1;
                    if (!sample.ContainsKey(key)) { sample[key] = e.TimeGenerated.ToString("g", Fmt.It); }
                }
            }
            foreach (KeyValuePair<string, int> kv in counts)
            {
                string src = kv.Key.Split(':')[0], id = kv.Key.Split(':')[1];
                string detail = kv.Value + " eventi negli ultimi 7 giorni (ultimo: " + sample[kv.Key] + "). Fonte " + src + ", ID " + id + ".";
                if (src.Equals("disk", StringComparison.OrdinalIgnoreCase) && (id == "7" || id == "11" || id == "51"))
                {
                    AddIssue(r, "evt-" + kv.Key, "crit", "disco", id == "7" ? "Settori danneggiati rilevati sul disco" : "Errori di lettura/scrittura del disco", detail + " Consigliati backup e controllo del disco.", "chkdsk-scan", null);
                }
                else if (src.Equals("Ntfs", StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(r, "evt-" + kv.Key, "warn", "integrita", "File system da controllare", detail, "chkdsk-scan", null);
                }
                else if (src.Contains("Kernel-Power") || src == "EventLog")
                {
                    AddIssue(r, "evt-" + kv.Key, "warn", "sistema", "Spegnimenti improvvisi", detail + " Possibili cause: blocchi, alimentazione, surriscaldamento.", null, null);
                }
                else if (src.Contains("WHEA"))
                {
                    AddIssue(r, "evt-" + kv.Key, "warn", "sistema", "Errori hardware segnalati da Windows", detail, null, null);
                }
                else
                {
                    AddIssue(r, "evt-" + kv.Key, "warn", "disco", "Il controller del disco ha segnalato ritardi", detail, null, null);
                }
            }
        }

        static void CheckMemory(HealthReport r)
        {
            double total;
            r.MemoryLoad = MemoryLoad(out total);
            if (r.MemoryLoad >= 92)
            {
                string top = string.Join(", ", Process.GetProcesses().OrderByDescending(p => { try { return p.WorkingSet64; } catch (Exception) { return 0L; } }).Take(3).Select(p => { try { return p.ProcessName + " " + Fmt.Bytes(p.WorkingSet64); } catch (Exception) { return "?"; } }));
                AddIssue(r, "memoria", "warn", "prestazioni", "Memoria quasi esaurita (" + Math.Round(r.MemoryLoad) + "%)", "Le app che usano più memoria: " + top + ".", null, null);
            }
        }

        static void CheckHungApps(HealthReport r)
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || p.Responding) { continue; }
                    if ((DateTime.Now - p.StartTime).TotalSeconds < 30) { continue; }
                    AddIssue(r, "bloccata-" + p.ProcessName.ToLowerInvariant(), "warn", "app", p.ProcessName + " non risponde", "La finestra \"" + p.MainWindowTitle + "\" non risponde. Chiuderla fa perdere il lavoro non salvato.", "close-hung-app", p.Id.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
        }

        static void CheckExplorer(HealthReport r)
        {
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                AddIssue(r, "explorer", "crit", "sistema", "Esplora risorse non è in esecuzione", "Barra delle applicazioni e desktop non sono disponibili.", "restart-explorer", null);
            }
        }

        static void CheckTemp(HealthReport r)
        {
            double size = Fixes.TempSize(TimeSpan.FromDays(3));
            if (size > 2.0 * 1073741824)
            {
                AddIssue(r, "temp", "info", "prestazioni", "File temporanei vecchi: " + Fmt.Bytes(size), "File temporanei più vecchi di 3 giorni che si possono eliminare senza rischi.", "clean-temp", null);
            }
        }

        static void CheckUptime(HealthReport r)
        {
            r.Uptime = TimeSpan.FromMilliseconds(GetTickCount64());
            if (r.Uptime.TotalDays > 14)
            {
                AddIssue(r, "uptime", "info", "prestazioni", "PC acceso da " + Math.Round(r.Uptime.TotalDays) + " giorni", "Un riavvio libera memoria e completa gli aggiornamenti in sospeso.", null, null);
            }
        }

        static void CheckPendingReboot(HealthReport r)
        {
            bool pending = false;
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")) { pending |= k != null; }
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")) { pending |= k != null; }
            if (pending) { AddIssue(r, "riavvio", "info", "integrita", "Riavvio richiesto da Windows Update", "Alcuni aggiornamenti saranno completati al prossimo riavvio.", null, null); }
        }

        static void CheckDefender(HealthReport r)
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT RealTimeProtectionEnabled, AntivirusSignatureAge, AntivirusEnabled FROM MSFT_MpComputerStatus"))
            {
                foreach (ManagementObject o in s.Get())
                {
                    bool rt = Convert.ToBoolean(o["RealTimeProtectionEnabled"]);
                    bool av = Convert.ToBoolean(o["AntivirusEnabled"]);
                    int age = Convert.ToInt32(o["AntivirusSignatureAge"]);
                    if (av && !rt) { AddIssue(r, "defender-rt", "crit", "sicurezza", "Protezione in tempo reale disattivata", "Microsoft Defender non sta controllando i file in tempo reale.", "open-security", null); }
                    if (av && age > 7) { AddIssue(r, "defender-firme", "warn", "sicurezza", "Definizioni antivirus vecchie di " + age + " giorni", "Aggiorna Microsoft Defender da Sicurezza di Windows.", "open-security", null); }
                }
            }
        }

        static void CheckIntegrityAge(HealthReport r)
        {
            DateTime last = State.GetTime("lastSfc");
            double days = last == DateTime.MinValue ? 999 : (DateTime.Now - last).TotalDays;
            if (days > 30)
            {
                AddIssue(r, "integrita", "info", "integrita", last == DateTime.MinValue ? "Integrità dei file di Windows mai verificata" : "Integrità dei file di Windows verificata " + Math.Round(days) + " giorni fa", "Il controllo (SFC) richiede l'autorizzazione di amministratore e circa 10 minuti.", "sfc-scan", null);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);

        static void CheckRecycleBin(HealthReport r)
        {
            SHQUERYRBINFO q = new SHQUERYRBINFO();
            q.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
            if (SHQueryRecycleBin(null, ref q) == 0 && q.i64Size > 5L * 1073741824)
            {
                AddIssue(r, "cestino", "info", "disco", "Cestino da " + Fmt.Bytes(q.i64Size), q.i64NumItems + " elementi. Svuotarlo libera spazio ma i file non si possono più recuperare.", "empty-recycle-bin", null);
            }
        }
    }
}
