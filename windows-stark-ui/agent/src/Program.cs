// Tipaldi AI - punto d'ingresso.
//   TipaldiAI.exe                     agente + desktop futuristico + icona nell'area di notifica
//   TipaldiAI.exe --mcp               server MCP per Claude (avviato da Claude Desktop / Claude Code)
//   TipaldiAI.exe --register          collega Tipaldi AI a Claude Desktop e Claude Code
//   TipaldiAI.exe --unregister        scollega
//   TipaldiAI.exe --stop              chiude l'agente in esecuzione in modo ordinato
//   TipaldiAI.exe --report            report della settimana scorsa (lo apre nel browser)
//   TipaldiAI.exe --health            diagnosi completa in JSON
//   TipaldiAI.exe --selftest DIR      autotest (CI)
//   TipaldiAI.exe --hud-screenshot F  salva un'immagine del desktop (CI)
//   TipaldiAI.exe --play FILE.wav     suona un file (suono di avvio all'accesso, senza finestre)
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

[assembly: AssemblyTitle("Tipaldi AI")]
[assembly: AssemblyDescription("Desktop futuristico con AI, diagnosi e ottimizzazione automatica per Windows 10")]
[assembly: AssemblyProduct("Tipaldi AI")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace TipaldiAI
{
    public static class Program
    {
        public const string StopEventName = "Local\\TipaldiAI-Stop";

        [STAThread]
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string arg = args.Length > 1 ? args[1] : null;
            try
            {
                switch (mode)
                {
                    case "--mcp":
                        return McpServer.RunStdio();
                    case "--selftest":
                        return SelfTest.Run(arg ?? Path.Combine(Path.GetTempPath(), "tipaldi-selftest"));
                    case "--register":
                    {
                        foreach (string c in ClaudeLink.RegisterDesktop(Paths.ExePath)) { Console.WriteLine("Claude Desktop: " + c); }
                        string cli = ClaudeLink.RegisterCode(Paths.ExePath);
                        Console.WriteLine(cli != null ? "Claude Code: " + cli : "Claude Code non trovato (facoltativo).");
                        Events.Add("ai", "Collegamento con Claude attivo (server MCP tipaldi-ai).");
                        return 0;
                    }
                    case "--stop":
                    {
                        EventWaitHandle stop;
                        if (EventWaitHandle.TryOpenExisting(StopEventName, out stop)) { using (stop) { stop.Set(); } }
                        return 0;
                    }
                    case "--unregister":
                        ClaudeLink.UnregisterDesktop();
                        ClaudeLink.UnregisterCode();
                        return 0;
                    case "--report":
                    {
                        WeeklyReport r = WeeklyReport.Build(WeeklyReport.WeekStart(DateTime.Today).AddDays(arg == "corrente" ? 0 : -7));
                        r.Save();
                        Console.WriteLine(r.HtmlPath);
                        if (Paths.IsWindows && arg != "--no-open") { Process.Start(new ProcessStartInfo(r.HtmlPath) { UseShellExecute = true }); }
                        return 0;
                    }
                    case "--health":
                    {
                        HealthReport h = Health.Run(true);
                        h.Save();
                        Console.WriteLine(Json.Indent(Json.Write(h.ToDict())));
                        return 0;
                    }
                    case "--play":
                        if (arg != null && File.Exists(arg)) { using (System.Media.SoundPlayer sp = new System.Media.SoundPlayer(arg)) { sp.PlaySync(); } }
                        return 0;
                    case "--hud-screenshot":
#if NOWPF
                        Console.Error.WriteLine("Desktop non disponibile in questa build.");
                        return 2;
#else
                        return DesktopApp.Screenshot(arg);
#endif
                    default:
                        return RunAgent();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                try { File.AppendAllText(Path.Combine(Paths.Logs, "errori.log"), DateTime.Now.ToString("s") + " " + ex + "\n"); } catch (Exception) { }
                return 1;
            }
        }

        static int RunAgent()
        {
            bool created;
            using (Mutex single = new Mutex(true, "Local\\TipaldiAI-Agent", out created))
            {
                if (!created) { return 0; }   // già in esecuzione
                Agent agent = new Agent();
                agent.Start();
                using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName))
                {
#if NOWPF
                    stop.WaitOne();
#else
                    DesktopApp.Run(agent, stop);
#endif
                }
                agent.Stop();
                return 0;
            }
        }
    }
}
