// Tipaldi AI - autotest: lavora in una cartella temporanea (TIPALDI_HOME/TIPALDI_SKILLS), non tocca i dati veri.
// Sulle parti che dipendono da Windows (diagnosi, piano energetico, conferme) gira solo su Windows.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipaldiAI
{
    public static class SelfTest
    {
        static int pass, fail;
        static readonly List<string> Failures = new List<string>();

        static void Check(bool ok, string name, string detail)
        {
            if (ok) { pass++; Console.WriteLine("  ok   " + name); }
            else { fail++; Failures.Add(name + " " + detail); Console.WriteLine("  FAIL " + name + " " + detail); }
        }

        static void Check(bool ok, string name) { Check(ok, name, ""); }

        public static int Run(string dir)
        {
            dir = Path.GetFullPath(dir);
            if (Directory.Exists(dir)) { Directory.Delete(dir, true); }
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("TIPALDI_HOME", Path.Combine(dir, "home"));
            Environment.SetEnvironmentVariable("TIPALDI_SKILLS", Path.Combine(dir, "skills"));
            Console.WriteLine("Tipaldi AI - autotest in " + dir);
            Step("Contesti", TestContexts);
            Step("Attività e apprendimento", TestActivity);
            Step("Report settimanale", () => TestReport(dir));
            Step("Skill per Claude", TestSkills);
            Step("Ordine automatico", () => TestOrganizer(dir));
            Step("Server MCP", TestMcp);
            Step("Configurazione di Claude Desktop", () => TestClaudeConfig(dir));
            Step("Regole di sicurezza delle riparazioni", TestFixPolicy);
            if (Paths.IsWindows) { Step("Diagnosi su Windows", TestHealth); }
            Console.WriteLine();
            Console.WriteLine(pass + " ok, " + fail + " falliti");
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), pass + " ok, " + fail + " falliti\n" + string.Join("\n", Failures), new UTF8Encoding(false));
            return fail == 0 ? 0 : 1;
        }

        static void Step(string title, Action a)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + title + " ===");
            try { a(); }
            catch (Exception ex) { Check(false, title + " senza eccezioni", ex.ToString()); }
        }

        static void TestContexts()
        {
            Learned l = new Learned();
            Check(Contexts.Classify("Resolve", "progetto.drp - DaVinci Resolve", l) == "video", "DaVinci Resolve -> Montaggio video");
            Check(Contexts.Classify("chrome", "Lo spot finale - YouTube - Google Chrome", l) == "fun", "YouTube nel browser -> Svago");
            Check(Contexts.Classify("msedge", "Posta in arrivo - Gmail", l) == "comm", "Gmail nel browser -> Comunicazione");
            Check(Contexts.Classify("chrome", "Claude", l) == "ai", "claude.ai nel browser -> AI");
            Check(Contexts.Classify("firefox", "Notizie del giorno", l) == Contexts.Web, "Browser generico -> Web");
            Check(Contexts.Classify("orca-slicer", "benchy.3mf - OrcaSlicer", l) == "print", "OrcaSlicer -> Stampa 3D");
            Check(Contexts.Classify("strumentoxyz", "", l) == Contexts.Other, "App sconosciuta -> Altro");
            l.UserApps["strumentoxyz"] = "vfx";
            Check(Contexts.Classify("strumentoxyz", "", l) == "vfx", "App insegnata dall'utente -> contesto scelto");
            Check(Organizer.CategoryOf("spot.mov") == "Montaggio video" && Organizer.CategoryOf("benchy.stl") == "Stampa 3D" && Organizer.CategoryOf("setup.exe") == "Installer" && Organizer.CategoryOf("x.unknownext") == null, "Categorie dei file per estensione");
        }

        /// <summary>Due settimane finte: montaggio al mattino, 3D al pomeriggio, uno strumento sconosciuto insieme al montaggio.</summary>
        static void TestActivity()
        {
            DateTime monday = WeeklyReport.WeekStart(DateTime.Today).AddDays(-14);
            for (int d = 0; d < 14; d++)
            {
                DateTime day = monday.AddDays(d);
                if (day.DayOfWeek == DayOfWeek.Sunday) { continue; }
                Add(day.AddHours(9), 90, "resolve", "Spot Barilla v" + (d % 3 + 1) + ".drp - DaVinci Resolve", "video");
                Add(day.AddHours(10.6), 20, "colorlab", "Grade - ColorLab", Contexts.Other);
                Add(day.AddHours(11), 60, "resolve", "Spot Barilla v2.drp - DaVinci Resolve", "video");
                Add(day.AddHours(14), 120, "blender", "scena_auto.blend - Blender", "3d");
                Add(day.AddHours(16.2), 30, "chrome", "Posta in arrivo - Gmail - Google Chrome", "comm");
                Add(day.AddHours(17), 40, "code", "agent.cs - tipaldi - Visual Studio Code", "dev");
            }
            List<Session> all = ActivityStore.Load(monday, monday.AddDays(14));
            Check(all.Count >= 70, "Sessioni salvate e rilette", "(" + all.Count + ")");
            ActivityStats st = ActivityStats.From(all);
            Check(st.ByContext["video"] > st.ByContext["3d"], "Montaggio video è il contesto principale");
            Check(st.Projects.ContainsKey("Spot Barilla v2.drp"), "Progetto riconosciuto dal titolo della finestra");
            Check(st.Switches > 20, "Cambi di contesto contati", "(" + st.Switches + ")");
            Learned l = Learned.Load();
            List<string> news = l.LearnFrom(all);
            Check(l.Apps.ContainsKey("colorlab") && l.Apps["colorlab"] == "video", "Imparato: ColorLab va con il montaggio", string.Join(" | ", news));
            Check(Learned.Load().Insights.Count > 0, "Scoperta salvata in learned.json");

            ActivityTracker t = new ActivityTracker(new Settings(), l);
            Check(t.Current == null, "Tracker pronto");
        }

        static void Add(DateTime start, int minutes, string app, string title, string ctx)
        {
            Add(start, minutes, app, title, ctx, null);
        }

        static void Add(DateTime start, int minutes, string app, string title, string ctx, string exe)
        {
            ActivityStore.Append(new Session { Start = start, End = start.AddMinutes(minutes), App = app, Title = title, Ctx = ctx, Exe = exe });
        }

        /// <summary>
        /// Dati dimostrativi per lo screenshot del desktop: due settimane di lavoro, oggi in corso, eventi della
        /// console e una scrivania d'esempio. Restituisce la cartella da mostrare come desktop.
        /// </summary>
        public static string SeedDemo()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys = Environment.SystemDirectory;
            for (int d = 0; d < 14; d++)
            {
                DateTime day = DateTime.Today.AddDays(-d);
                if (day.DayOfWeek == DayOfWeek.Sunday) { continue; }
                Add(day.AddHours(9), 95, "resolve", "Spot Barilla v2.drp - DaVinci Resolve", "video", Path.Combine(win, "explorer.exe"));
                Add(day.AddHours(10.7), 25, "afterfx", "titoli_finali.aep - Adobe After Effects", "vfx", Path.Combine(sys, "mspaint.exe"));
                Add(day.AddHours(14), 110, "blender", "scena_auto.blend - Blender", "3d", Path.Combine(sys, "notepad.exe"));
                Add(day.AddHours(16), 35, "orca-slicer", "supporto_camera.3mf - OrcaSlicer", "print", Path.Combine(sys, "mstsc.exe"));
                Add(day.AddHours(16.7), 30, "chrome", "Posta in arrivo - Gmail - Google Chrome", "comm", Path.Combine(sys, "cmd.exe"));
                Add(day.AddHours(17.3), 40, "claude", "Claude", "ai", Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe"));
            }
            Events.Add("ai", "Tipaldi AI online. Protocolli attivi: diagnosi, riparazioni sicure, Overdrive, ordine, apprendimento.");
            Events.Add("ok", "Protocollo automatico: Pulizia file temporanei - 1,8 GB liberati (2.413 file).");
            Events.Add("ai", "Ho collegato colorlab al contesto \"Montaggio video\": la usi insieme a quel lavoro (92% delle volte).");
            Events.Add("ok", "Protocollo ordine: 14 file in Download sistemati per contesto.");
            Events.Add("ai", "Protocollo Overdrive attivo: piano energetico Prestazioni elevate per Montaggio video.");
            Events.Add("ai", "5 skill per Claude aggiornate con quello che ho imparato.");

            string desk = Path.Combine(Paths.Root, "desktop-demo");
            Directory.CreateDirectory(desk);
            foreach (string n in new[] { "Spot Barilla v2.drp", "Montaggio finale.mov", "titoli_finali.aep", "scena_auto.blend", "supporto_camera.3mf", "Preventivo Barilla.pdf", "Moodboard.psd", "voiceover.wav", "tipaldi-agent.ps1" })
            {
                File.WriteAllText(Path.Combine(desk, n), "demo");
            }
            Directory.CreateDirectory(Path.Combine(desk, "Montaggio video"));
            return desk;
        }

        static void TestReport(string dir)
        {
            DateTime lastMonday = WeeklyReport.WeekStart(DateTime.Today).AddDays(-7);
            WeeklyReport r = WeeklyReport.Build(lastMonday);
            r.Save();
            string md = File.ReadAllText(r.MarkdownPath);
            string html = File.ReadAllText(r.HtmlPath);
            Check(r.Stats.Total > 10 * 3600, "Ore della settimana", "(" + Math.Round(r.Stats.Total / 3600, 1) + ")");
            Check(md.Contains("## Cosa ha imparato Tipaldi AI") && md.Contains("Montaggio video"), "Report Markdown con contesti e apprendimento");
            Check(r.LearnedLines.Any(x => x.Contains("lavoro principale")), "Osservazione sul lavoro principale");
            Check(r.LearnedLines.Any(x => x.Contains("Di solito inizi")), "Osservazione sugli orari");
            Check(html.StartsWith("<!doctype html>") && html.Contains("<svg") && html.Contains("Quando lavori"), "Report HTML con grafico orari");
            File.Copy(r.HtmlPath, Path.Combine(dir, "report-esempio.html"), true);
            File.Copy(r.MarkdownPath, Path.Combine(dir, "report-esempio.md"), true);
            Check(WeeklyReport.WeekId(new DateTime(2026, 10, 19)) == "2026-W43", "Numero della settimana ISO");
        }

        static void TestSkills()
        {
            List<string> files = Skills.Generate();
            Check(files.Count >= 3, "Skill generate (profilo + contesti principali)", "(" + files.Count + ")");
            foreach (string f in files)
            {
                string text = File.ReadAllText(f);
                string name = Path.GetFileName(Path.GetDirectoryName(f));
                Match m = Regex.Match(text, "^---\\nname: ([a-z0-9-]{1,64})\\ndescription: (.{1,1024})\\n---\\n", RegexOptions.None);
                Check(m.Success && m.Groups[1].Value == name, "Intestazione valida: " + name);
            }
            Check(Skills.List().Count == files.Count, "Elenco skill");
            Check(File.Exists(Path.Combine(Paths.Reports, "skills", "tipaldi-profilo.zip")), "Zip pronto per claude.ai");
            Check(Skills.SkillName("../../Evil Name") == "tipaldi-evil-name", "Nome skill ripulito (niente percorsi)");
        }

        static void TestOrganizer(string dir)
        {
            string folder = Path.Combine(dir, "scrivania");
            Directory.CreateDirectory(folder);
            string[] names = { "spot.mov", "benchy.stl", "setup.exe", "note.docx", "misterioso.xyz", "collegamento.lnk" };
            foreach (string n in names)
            {
                string p = Path.Combine(folder, n);
                File.WriteAllText(p, "x");
                File.SetCreationTime(p, DateTime.Now.AddDays(-3));
                File.SetLastWriteTime(p, DateTime.Now.AddDays(-3));
            }
            string fresh = Path.Combine(folder, "nuovo.mov");
            File.WriteAllText(fresh, "x");
            List<MovePlan> plan = Organizer.Plan(folder, TimeSpan.FromHours(24));
            Check(plan.Count == 4, "Piano: 4 file da sistemare", "(" + string.Join(", ", plan.Select(p => Path.GetFileName(p.From))) + ")");
            Check(!plan.Any(p => p.From.EndsWith(".lnk") || p.From.EndsWith(".xyz") || p.From == fresh), "Collegamenti, sconosciuti e file recenti restano dove sono");
            Dictionary<string, object> res = Organizer.Apply(folder, TimeSpan.FromHours(24));
            Check(Convert.ToInt32(res["spostati"]) == 4 && File.Exists(Path.Combine(folder, "Stampa 3D", "benchy.stl")), "File spostati nelle cartelle di contesto");
            Dictionary<string, object> undo = Organizer.Undo(null);
            Check(Convert.ToInt32(undo["ripristinati"]) == 4 && File.Exists(Path.Combine(folder, "benchy.stl")), "Annulla: file rimessi al loro posto");
        }

        static void TestMcp()
        {
            StringWriter output = new StringWriter();
            string input = string.Join("\n", new[]
            {
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}",
                "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}",
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
                "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"activity_summary\",\"arguments\":{\"days\":30}}}",
                "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"set_app_context\",\"arguments\":{\"app\":\"colorlab.exe\",\"context_id\":\"vfx\"}}}",
                "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"skill_write\",\"arguments\":{\"name\":\"color\",\"description\":\"Correzione colore dell'utente\",\"body\":\"# Colore\\nUsa ColorLab.\"}}}",
                "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"metodo/inesistente\"}",
                "non json",
                "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"weekly_report\",\"arguments\":{\"week_offset\":-1}}}",
            });
            new McpServer(new StringReader(input), output).Run();
            string[] lines = output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Check(lines.Length == 8, "Una risposta per richiesta (nessuna per le notifiche)", "(" + lines.Length + ")");
            Dictionary<string, object> init = Json.ReadObject(lines[0]);
            Check(Json.Str(Json.Obj(Json.Obj(init, "result"), "serverInfo"), "name", "") == "tipaldi-ai" && Json.Str(Json.Obj(init, "result"), "protocolVersion", "") == "2025-06-18", "initialize");
            int tools = Json.Arr(Json.Obj(Json.ReadObject(lines[1]), "result"), "tools").Count;
            Check(tools == McpServer.Tools().Count && tools >= 15, "tools/list", "(" + tools + ")");
            Check(lines[2].Contains("Montaggio video") && lines[2].Contains("\"isError\":false"), "activity_summary");
            Check(Learned.Load().UserApps.ContainsKey("colorlab") && Learned.Load().UserApps["colorlab"] == "vfx", "set_app_context insegna il contesto");
            Check(File.Exists(Path.Combine(Paths.Skills, "tipaldi-color", "SKILL.md")), "skill_write scrive solo skill tipaldi-*");
            Check(lines[5].Contains("-32601"), "Metodo sconosciuto -> errore -32601");
            Check(lines[6].Contains("-32700"), "JSON non valido -> errore -32700");
            Check(lines[7].Contains("report settimanale"), "weekly_report via MCP");
            foreach (string l in lines) { Json.ReadObject(l); }
            Check(true, "Risposte JSON valide, una per riga");
        }

        static void TestClaudeConfig(string dir)
        {
            string cfg = Path.Combine(dir, "claude_desktop_config.json");
            File.WriteAllText(cfg, "{\"preferences\":{\"theme\":\"dark\"},\"mcpServers\":{\"altro\":{\"command\":\"x.exe\"}}}");
            ClaudeLink.RegisterAt(cfg, @"C:\Users\davide\AppData\Local\TipaldiAI\app\TipaldiAI.exe");
            Dictionary<string, object> d = Json.ReadFile(cfg);
            Dictionary<string, object> servers = Json.Obj(d, "mcpServers");
            Check(servers.ContainsKey("altro") && servers.ContainsKey("tipaldi-ai") && Json.Obj(d, "preferences").ContainsKey("theme"), "Registrazione: altre impostazioni intatte");
            Check(Json.Str(Json.Obj(servers, "tipaldi-ai"), "command", "").EndsWith("TipaldiAI.exe"), "Comando del server MCP");
            ClaudeLink.UnregisterAt(cfg);
            servers = Json.Obj(Json.ReadFile(cfg), "mcpServers");
            Check(servers.ContainsKey("altro") && !servers.ContainsKey("tipaldi-ai"), "Rimozione: tolta solo la voce tipaldi-ai");
            string only = Path.Combine(dir, "solo", "claude_desktop_config.json");
            ClaudeLink.RegisterAt(only, "x.exe");
            ClaudeLink.UnregisterAt(only);
            Check(!File.Exists(only) && !Directory.Exists(Path.GetDirectoryName(only)), "Configurazione creata da Tipaldi AI: rimossa del tutto");
        }

        static void TestFixPolicy()
        {
            Func<string, bool> saved = Fixes.Confirm;
            int asked = 0;
            Fixes.Confirm = text => { asked++; return false; };
            try
            {
                FixResult auto = Fixes.Execute("empty-recycle-bin", null, "auto", null);
                Check(!auto.Ok && asked == 0, "Non sicura in automatico: mai eseguita, nessuna domanda");
                FixResult mcp = Fixes.Execute("sfc-scan", null, "mcp", "test");
                Check(!mcp.Ok && asked == 1 && mcp.Message.Contains("rifiutato"), "Non sicura da Claude: chiede conferma e rispetta il no");
                Check(Fixes.All.Where(f => f.Admin).All(f => !f.Safe), "Nessuna riparazione da amministratore è marcata sicura");
                Check(!Fixes.Execute("inesistente", null, "ui", null).Ok, "Riparazione sconosciuta rifiutata");
            }
            finally { Fixes.Confirm = saved; }
        }

        static void TestHealth()
        {
            HealthReport h = Health.Run(true);
            Check(h.Volumes.Count > 0, "Volumi letti", "(" + h.Volumes.Count + ")");
            Check(h.Disks.Count > 0, "Dischi fisici e stato di salute", string.Join(" | ", h.Notes));
            Check(h.MemoryLoad > 0, "Memoria in uso", "(" + h.MemoryLoad + "%)");
            Console.WriteLine("  stato: " + h.Status + ", problemi: " + string.Join(" | ", h.Issues.Select(i => i.Severity + " " + i.Title)));
            Console.WriteLine("  dischi: " + string.Join(" | ", h.Disks.Select(d => d.Name + " " + d.Media + " " + d.Health)));
            foreach (string n in h.Notes) { Console.WriteLine("  nota: " + n); }
            h.Save();
            Check(HealthReport.LoadLast().Issues.Count == h.Issues.Count, "Diagnosi salvata e riletta");
            FixResult dns = Fixes.Execute("flush-dns", null, "auto", null);
            Check(dns.Ok, "Riparazione sicura eseguita in automatico (flush DNS)", dns.Message);
        }
    }
}
