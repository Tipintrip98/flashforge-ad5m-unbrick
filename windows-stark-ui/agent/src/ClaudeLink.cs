// Tipaldi AI - collegamento a Claude:
//  - registra il server MCP in Claude Desktop (claude_desktop_config.json) e in Claude Code (claude mcp add);
//  - se Claude Code è installato, gli passa i problemi senza riparazione sicura: Claude li analizza con gli
//    strumenti MCP e applica ciò che serve (le azioni non sicure chiedono comunque conferma all'utente).
//    Usa l'abbonamento Claude già collegato a Claude Code: nessuna chiave API, nessun costo a consumo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TipaldiAI
{
    public static class ClaudeLink
    {
        public const string ServerName = "tipaldi-ai";

        /// <summary>File di configurazione di Claude Desktop (installazione classica e da Microsoft Store).</summary>
        public static List<string> DesktopConfigs(bool includeMissing)
        {
            List<string> list = new List<string>();
            string roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            if (includeMissing || Directory.Exists(roaming)) { list.Add(Path.Combine(roaming, "claude_desktop_config.json")); }
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            try
            {
                foreach (string pkg in Directory.GetDirectories(packages, "Claude_*"))
                {
                    string dir = Path.Combine(pkg, @"LocalCache\Roaming\Claude");
                    if (Directory.Exists(dir)) { list.Add(Path.Combine(dir, "claude_desktop_config.json")); }
                }
            }
            catch (Exception) { }
            return list;
        }

        public static List<string> RegisterDesktop(string exe)
        {
            List<string> done = new List<string>();
            foreach (string cfg in DesktopConfigs(true))
            {
                string backup = Path.Combine(Paths.Sub("backup"), "claude_desktop_config-" + Math.Abs(cfg.ToLowerInvariant().GetHashCode()) + ".json");
                if (File.Exists(cfg) && !File.Exists(backup)) { File.Copy(cfg, backup); }
                RegisterAt(cfg, exe);
                done.Add(cfg);
            }
            return done;
        }

        /// <summary>Aggiunge (o aggiorna) solo la voce tipaldi-ai: il resto della configurazione resta com'è.</summary>
        public static void RegisterAt(string cfg, string exe)
        {
            Dictionary<string, object> d = Json.ReadFile(cfg);
            Dictionary<string, object> servers = Json.Obj(d, "mcpServers");
            servers[ServerName] = new Dictionary<string, object> { { "command", exe }, { "args", new[] { "--mcp" } } };
            d["mcpServers"] = servers;
            Json.WriteFile(cfg, d, true);
        }

        public static void UnregisterAt(string cfg)
        {
            if (!File.Exists(cfg)) { return; }
            Dictionary<string, object> d = Json.ReadFile(cfg);
            Dictionary<string, object> servers = Json.Obj(d, "mcpServers");
            if (!servers.Remove(ServerName)) { return; }
            if (servers.Count == 0) { d.Remove("mcpServers"); } else { d["mcpServers"] = servers; }
            if (d.Count == 0)
            {
                // la configurazione conteneva solo Tipaldi AI (l'avevamo creata noi): niente file vuoti lasciati in giro
                File.Delete(cfg);
                string dir = Path.GetDirectoryName(cfg);
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) { Directory.Delete(dir); } } catch (Exception) { }
                return;
            }
            Json.WriteFile(cfg, d, true);
        }

        public static void UnregisterDesktop()
        {
            foreach (string cfg in DesktopConfigs(false)) { UnregisterAt(cfg); }
        }

        public static bool IsRegisteredDesktop()
        {
            return DesktopConfigs(false).Any(cfg => Json.Obj(Json.ReadFile(cfg), "mcpServers").ContainsKey(ServerName));
        }

        /// <summary>Percorso della CLI di Claude Code, se installata.</summary>
        public static string FindClaudeCli()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates =
            {
                Path.Combine(home, @".local\bin\claude.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\claude.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\claude\claude.exe"),
            };
            foreach (string c in candidates) { if (File.Exists(c)) { return c; } }
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                foreach (string name in new[] { "claude.exe", "claude.cmd" })
                {
                    try { string p = Path.Combine(dir.Trim(), name); if (File.Exists(p)) { return p; } } catch (Exception) { }
                }
            }
            return null;
        }

        static string Run(string cli, string args, int timeoutSec, out int code)
        {
            ProcessStartInfo psi = new ProcessStartInfo(cli, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Paths.Root
            };
            if (cli.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                psi.Arguments = "/c \"\"" + cli + "\" " + args + "\"";
            }
            using (Process p = Process.Start(psi))
            {
                System.Threading.Tasks.Task<string> err = p.StandardError.ReadToEndAsync();
                System.Threading.Tasks.Task<string> outp = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch (Exception) { } code = -1; return "Tempo scaduto."; }
                code = p.ExitCode;
                return (outp.Result + "\n" + err.Result).Trim();
            }
        }

        public static string RegisterCode(string exe)
        {
            string cli = FindClaudeCli();
            if (cli == null) { return null; }
            int code;
            Run(cli, "mcp remove --scope user " + ServerName, 60, out code);
            Run(cli, "mcp add --scope user " + ServerName + " -- \"" + exe + "\" --mcp", 60, out code);
            return code == 0 ? cli : null;
        }

        public static void UnregisterCode()
        {
            string cli = FindClaudeCli();
            if (cli == null) { return; }
            int code;
            Run(cli, "mcp remove --scope user " + ServerName, 60, out code);
        }

        /// <summary>
        /// Chiede a Claude Code (in background) di occuparsi dei problemi indicati. Restituisce la risposta
        /// di Claude o null se Claude Code non è disponibile.
        /// </summary>
        public static string AutoResolve(List<Issue> issues)
        {
            string cli = FindClaudeCli();
            if (cli == null || issues.Count == 0) { return null; }
            string list = string.Join("; ", issues.Select(i => i.Title + " (" + i.Id + ")"));
            // niente virgolette doppie nel testo: viene passato come un unico argomento
            string prompt = ("Sei il modulo di riparazione di Tipaldi AI sul PC Windows dell'utente. Tipaldi AI ha rilevato: " + list +
                ". Usa gli strumenti MCP tipaldi-ai: leggi system_status, approfondisci con run_health_check se serve, applica con apply_fix le riparazioni utili spiegando il motivo nel campo reason " +
                "(quelle non sicure chiederanno conferma all'utente). Non usare altri strumenti. Alla fine scrivi in italiano, in massimo 5 righe, cosa hai trovato, cosa hai fatto e cosa resta da fare all'utente.").Replace("\"", "'");
            int code;
            string answer = Run(cli, "-p \"" + prompt + "\" --allowedTools mcp__" + ServerName + " --output-format text", 600, out code);
            if (code != 0) { return null; }
            return answer;
        }
    }
}
