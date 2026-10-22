// Tipaldi AI - server MCP (Model Context Protocol) su stdio: Claude Desktop / Claude Code avviano
// "TipaldiAI.exe --mcp" e possono leggere diagnosi e attività, avviare riparazioni (con le regole di
// sicurezza di Fixes), riordinare file, scrivere report e skill. Un messaggio JSON-RPC per riga.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TipaldiAI
{
    public class McpServer
    {
        const string Version = "1.0.0";
        readonly TextReader input;
        readonly TextWriter output;

        public McpServer(TextReader input, TextWriter output)
        {
            this.input = input;
            this.output = output;
        }

        public static int RunStdio()
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Stream stdout = Console.OpenStandardOutput();
            StreamWriter w = new StreamWriter(stdout, new UTF8Encoding(false));
            w.AutoFlush = true;
            w.NewLine = "\n";
            StreamReader r = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            new McpServer(r, w).Run();
            return 0;
        }

        public void Run()
        {
            string line;
            while ((line = input.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) { continue; }
                string reply = Handle(line);
                if (reply != null) { output.WriteLine(reply); output.Flush(); }
            }
        }

        public string Handle(string line)
        {
            Dictionary<string, object> msg;
            try { msg = Json.ReadObject(line); }
            catch (Exception) { return Error(null, -32700, "JSON non valido"); }
            object id;
            bool hasId = msg.TryGetValue("id", out id);
            string method = Json.Str(msg, "method", "");
            Dictionary<string, object> prms = Json.Obj(msg, "params");
            if (!hasId) { return null; }   // notifica (es. notifications/initialized): nessuna risposta
            try
            {
                switch (method)
                {
                    case "initialize": return Result(id, Initialize(prms));
                    case "ping": return Result(id, new Dictionary<string, object>());
                    case "tools/list": return Result(id, new Dictionary<string, object> { { "tools", Tools() } });
                    case "tools/call": return Result(id, Call(Json.Str(prms, "name", ""), Json.Obj(prms, "arguments")));
                    case "resources/list": return Result(id, new Dictionary<string, object> { { "resources", new object[0] } });
                    case "prompts/list": return Result(id, new Dictionary<string, object> { { "prompts", new object[0] } });
                    default: return Error(id, -32601, "Metodo non supportato: " + method);
                }
            }
            catch (Exception ex) { return Error(id, -32603, ex.Message); }
        }

        static string Result(object id, object result)
        {
            return Json.Write(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } });
        }

        static string Error(object id, int code, string message)
        {
            return Json.Write(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } } });
        }

        static Dictionary<string, object> Initialize(Dictionary<string, object> prms)
        {
            string version = Json.Str(prms, "protocolVersion", "2025-06-18");
            return new Dictionary<string, object>
            {
                { "protocolVersion", version },
                { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object> { { "listChanged", false } } } } },
                { "serverInfo", new Dictionary<string, object> { { "name", "tipaldi-ai" }, { "version", Version } } },
                { "instructions", "Tipaldi AI controlla il PC Windows dell'utente: stato del sistema, salute dei dischi, integrità, attività (app, finestre, contesti di lavoro), report settimanali e skill. " +
                    "Rispondi in italiano, breve e con istruzioni precise. Le riparazioni sicure partono subito; quelle che richiedono amministratore o che possono far perdere dati chiedono conferma all'utente con una finestra sul suo desktop: spiega sempre il motivo nel campo reason." }
            };
        }

        // ------------------------------------------------------------------ strumenti

        static Dictionary<string, object> Tool(string name, string description, Dictionary<string, object> props, params string[] required)
        {
            Dictionary<string, object> schema = new Dictionary<string, object> { { "type", "object" }, { "properties", props ?? new Dictionary<string, object>() } };
            if (required.Length > 0) { schema["required"] = required; }
            return new Dictionary<string, object> { { "name", name }, { "description", description }, { "inputSchema", schema } };
        }

        static Dictionary<string, object> P(string type, string description)
        {
            return new Dictionary<string, object> { { "type", type }, { "description", description } };
        }

        static Dictionary<string, object> Props(params object[] kv)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) { d[(string)kv[i]] = kv[i + 1]; }
            return d;
        }

        public static List<Dictionary<string, object>> Tools()
        {
            return new List<Dictionary<string, object>>
            {
                Tool("system_status", "Stato attuale del PC: ultima diagnosi (problemi, dischi fisici, volumi), memoria, contesto di lavoro attuale ed eventi recenti della console di Tipaldi AI.", null),
                Tool("run_health_check", "Esegue subito una diagnosi. deep=true aggiunge registro eventi di disco e file system, antivirus, integrità di Windows, riavvii in sospeso (circa 10-30 secondi).",
                    Props("deep", P("boolean", "Diagnosi completa (default true)"))),
                Tool("list_fixes", "Elenco delle riparazioni disponibili, con quali sono sicure (partono subito) e quali chiedono conferma o amministratore.", null),
                Tool("apply_fix", "Esegue una riparazione per id (vedi list_fixes o il campo riparazione dei problemi). Le non sicure mostrano una richiesta di conferma all'utente; le amministrative anche l'UAC.",
                    Props("fix_id", P("string", "Id della riparazione"), "argument", P("string", "Argomento (es. PID per close-hung-app)"), "reason", P("string", "Perché serve, mostrato all'utente")), "fix_id", "reason"),
                Tool("activity_summary", "Riepilogo di cosa ha fatto l'utente negli ultimi N giorni: ore per contesto, app, progetti/file (dai titoli delle finestre), giorni e orari, passaggi tra contesti.",
                    Props("days", P("integer", "Giorni da considerare (default 7, max 90)"))),
                Tool("activity_timeline", "Sessioni di un giorno (ora inizio/fine, app, titolo finestra, contesto). Utile per capire su cosa ha lavorato.",
                    Props("date", P("string", "Data AAAA-MM-GG (default oggi)"), "limit", P("integer", "Massimo sessioni (default 200)"))),
                Tool("weekly_report", "Genera il report settimanale (HTML + Markdown) e restituisce il Markdown e i percorsi.",
                    Props("week_offset", P("integer", "0 = settimana in corso, -1 = scorsa (default -1)"))),
                Tool("contexts", "Contesti di lavoro riconosciuti, app associate e cosa Tipaldi AI ha imparato.", null),
                Tool("set_app_context", "Insegna a Tipaldi AI a quale contesto appartiene un'app (nome processo senza .exe).",
                    Props("app", P("string", "Nome del processo, es. resolve"), "context_id", P("string", "Id del contesto (vedi contexts)")), "app", "context_id"),
                Tool("organize_preview", "Mostra cosa verrebbe spostato riordinando Desktop o Download per contesto (nessuna modifica).",
                    Props("folder", P("string", "desktop oppure downloads"))),
                Tool("organize_apply", "Riordina Desktop o Download per contesto (solo file vecchi, non in uso; annullabile con organize_undo).",
                    Props("folder", P("string", "desktop oppure downloads")), "folder"),
                Tool("organize_undo", "Annulla l'ultimo riordino (o un lotto preciso).", Props("batch", P("string", "Lotto da annullare (opzionale)"))),
                Tool("skills_list", "Skill di Claude generate da Tipaldi AI (cartella ~/.claude/skills/tipaldi-*).", null),
                Tool("skills_generate", "Rigenera le skill dal comportamento delle ultime 4 settimane.", null),
                Tool("skill_write", "Scrive o migliora una skill tipaldi-* con il testo fornito (per trasformare ciò che hai capito dell'utente in istruzioni durature).",
                    Props("name", P("string", "Nome breve (diventa tipaldi-<nome>)"), "description", P("string", "Quando usarla (max 1024 caratteri)"), "body", P("string", "Contenuto Markdown della skill")), "name", "description", "body"),
            };
        }

        static Dictionary<string, object> Text(object payload, bool isError)
        {
            string text = payload as string ?? Json.Indent(Json.Write(payload));
            return new Dictionary<string, object>
            {
                { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } },
                { "isError", isError }
            };
        }

        public static Dictionary<string, object> Call(string name, Dictionary<string, object> a)
        {
            switch (name)
            {
                case "system_status":
                {
                    HealthReport h = HealthReport.LoadLast();
                    if (h.Time == DateTime.MinValue) { h = Health.Run(false); h.Save(); }
                    Dictionary<string, object> d = h.ToDict();
                    List<Session> today = ActivityStore.Load(DateTime.Today, DateTime.Now);
                    Session last = today.LastOrDefault();
                    d["contesto_attuale"] = last != null && (DateTime.Now - last.End).TotalMinutes < 20 ? Contexts.Get(last.Ctx).Name : "nessuna attività recente";
                    d["eventi_recenti"] = Events.Read(DateTime.Now.AddDays(-2), 25);
                    return Text(d, false);
                }
                case "run_health_check":
                {
                    HealthReport h = Health.Run(Json.Bool(a, "deep", true));
                    h.Save();
                    return Text(h.ToDict(), false);
                }
                case "list_fixes":
                    return Text(Fixes.All.Select(f => new Dictionary<string, object> { { "id", f.Id }, { "titolo", f.Title }, { "descrizione", f.Description }, { "sicura", f.Safe }, { "amministratore", f.Admin } }).ToList(), false);
                case "apply_fix":
                {
                    FixResult r = Fixes.Execute(Json.Str(a, "fix_id", ""), Json.Str(a, "argument", null), "mcp", Json.Str(a, "reason", ""));
                    return Text(new Dictionary<string, object> { { "ok", r.Ok }, { "esito", r.Message } }, !r.Ok);
                }
                case "activity_summary":
                {
                    int days = Math.Max(1, Math.Min(90, (int)Json.Num(a, "days", 7)));
                    ActivityStats st = ActivityStats.From(ActivityStore.Load(DateTime.Today.AddDays(1 - days), DateTime.Now));
                    Dictionary<string, object> d = st.ToDict(15);
                    d["periodo_giorni"] = days;
                    return Text(d, false);
                }
                case "activity_timeline":
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(Json.Str(a, "date", ""), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) { day = DateTime.Today; }
                    int limit = Math.Max(1, Math.Min(1000, (int)Json.Num(a, "limit", 200)));
                    List<Session> s = ActivityStore.Load(day, day.AddDays(1).AddSeconds(-1));
                    return Text(s.Skip(Math.Max(0, s.Count - limit)).Select(x => new Dictionary<string, object> {
                        { "inizio", x.Start.ToString("HH:mm:ss") }, { "fine", x.End.ToString("HH:mm:ss") }, { "app", x.App }, { "titolo", x.Title }, { "contesto", Contexts.Get(x.Ctx).Name } }).ToList(), false);
                }
                case "weekly_report":
                {
                    int off = (int)Json.Num(a, "week_offset", -1);
                    WeeklyReport r = WeeklyReport.Build(WeeklyReport.WeekStart(DateTime.Today).AddDays(7 * off));
                    r.Save();
                    return Text(r.Markdown() + "\n\n(HTML: " + r.HtmlPath + ")", false);
                }
                case "contexts":
                {
                    Learned l = Learned.Load();
                    return Text(new Dictionary<string, object>
                    {
                        { "contesti", Contexts.All.Select(c => new Dictionary<string, object> { { "id", c.Id }, { "nome", c.Name }, { "app", c.Apps }, { "estensioni", c.Extensions } }).ToList() },
                        { "imparate", l.Apps },
                        { "insegnate", l.UserApps },
                        { "scoperte_recenti", l.Insights.Skip(Math.Max(0, l.Insights.Count - 20)).ToList() }
                    }, false);
                }
                case "set_app_context":
                {
                    string ctx = Json.Str(a, "context_id", "");
                    if (!Contexts.All.Any(c => c.Id == ctx)) { return Text("Contesto sconosciuto: " + ctx, true); }
                    Learned l = Learned.Load();
                    l.Teach(Json.Str(a, "app", "").Replace(".exe", ""), ctx);
                    return Text("Fatto: " + Json.Str(a, "app", "") + " -> " + Contexts.Get(ctx).Name + ".", false);
                }
                case "organize_preview":
                {
                    string target = Json.Str(a, "folder", "downloads");
                    List<MovePlan> plan = Organizer.Plan(target, target == "desktop" ? TimeSpan.FromHours(24) : TimeSpan.FromHours(2));
                    return Text(plan.Select(p => new Dictionary<string, object> { { "file", Path.GetFileName(p.From) }, { "verso", p.Context } }).ToList(), false);
                }
                case "organize_apply":
                {
                    string target = Json.Str(a, "folder", "");
                    if (target != "desktop" && target != "downloads") { return Text("folder deve essere desktop o downloads.", true); }
                    return Text(Organizer.Apply(target, target == "desktop" ? TimeSpan.FromHours(24) : TimeSpan.FromHours(2)), false);
                }
                case "organize_undo":
                    return Text(Organizer.Undo(Json.Str(a, "batch", null)), false);
                case "skills_list":
                    return Text(Skills.List(), false);
                case "skills_generate":
                    return Text(Skills.Generate(), false);
                case "skill_write":
                {
                    string file = Skills.Write(Json.Str(a, "name", ""), Json.Str(a, "description", ""), Json.Str(a, "body", ""));
                    Events.Add("ai", "Claude ha aggiornato la skill " + Path.GetFileName(Path.GetDirectoryName(file)) + ".");
                    return Text("Skill scritta: " + file, false);
                }
                default:
                    return Text("Strumento sconosciuto: " + name, true);
            }
        }
    }
}
