// Tipaldi AI - trasforma ciò che ha imparato in skill per Claude.
// Le skill vanno in %USERPROFILE%\.claude\skills\tipaldi-*\SKILL.md (Claude Code le carica da sole)
// e in reports\skills\*.zip, pronte da caricare in Claude (Impostazioni > Funzionalità > Skill).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TipaldiAI
{
    public static class Skills
    {
        public const string Prefix = "tipaldi-";

        public static string SkillName(string id)
        {
            string n = Fmt.Slug(id);
            if (!n.StartsWith(Prefix, StringComparison.Ordinal)) { n = Prefix + n; }
            return n.Length > 64 ? n.Substring(0, 64).TrimEnd('-') : n;
        }

        /// <summary>Genera/aggiorna le skill dalle ultime 4 settimane. Restituisce i percorsi scritti.</summary>
        public static List<string> Generate()
        {
            DateTime to = DateTime.Now, from = to.AddDays(-28);
            List<Session> sessions = ActivityStore.Load(from, to);
            ActivityStats st = ActivityStats.From(sessions);
            List<string> written = new List<string>();
            if (st.Total < 1800) { return written; }

            written.Add(Write("profilo", "Profilo di lavoro dell'utente (contesti, programmi, orari, preferenze). Usala all'inizio di ogni richiesta sul suo lavoro al PC per adattare risposte e istruzioni.", Profile(st)));
            foreach (KeyValuePair<string, double> kv in st.ByContext.Where(x => x.Value >= 2 * 3600 && x.Key != "system" && x.Key != Contexts.Other).OrderByDescending(x => x.Value))
            {
                ContextDef c = Contexts.Get(kv.Key);
                List<string> apps = st.ByApp.Where(a => st.AppContext[a.Key] == kv.Key).OrderByDescending(a => a.Value).Take(6).Select(a => a.Key).ToList();
                string description = "Come lavora l'utente in " + c.Name + " (" + string.Join(", ", apps.Take(3)) + "): strumenti, progetti ricorrenti, orari. Usala quando chiede aiuto su " + c.Name.ToLowerInvariant() + " o su questi programmi.";
                written.Add(Write(c.Id, description, ContextBody(st, kv.Key, kv.Value, sessions)));
            }
            State.SetTime("lastSkills", DateTime.Now);
            Events.Add("ai", written.Count + " skill per Claude aggiornate con quello che ho imparato.");
            return written;
        }

        static string Header(string title)
        {
            return "# " + title + "\n\nGenerata da Tipaldi AI il " + DateTime.Now.ToString("d MMMM yyyy", Fmt.It) + " dall'uso reale del PC (ultime 4 settimane).\n\n";
        }

        static string Profile(ActivityStats st)
        {
            StringBuilder b = new StringBuilder(Header("Profilo di lavoro"));
            b.AppendLine("## Come rispondere");
            b.AppendLine("- In italiano, chiaro e al punto, con istruzioni precise e semplici (preferenza esplicita dell'utente).");
            b.AppendLine("- Per problemi del PC usa prima gli strumenti MCP di Tipaldi AI (`tipaldi-ai`): diagnosi, riparazioni, attività.");
            b.AppendLine();
            b.AppendLine("## Contesti di lavoro (ore nelle ultime 4 settimane)");
            foreach (KeyValuePair<string, double> kv in st.ByContext.Where(x => x.Key != "system").OrderByDescending(x => x.Value).Take(8)) { b.AppendLine("- " + Contexts.Get(kv.Key).Name + ": " + Math.Round(kv.Value / 3600, 1).ToString(Fmt.It) + " h"); }
            b.AppendLine();
            b.AppendLine("## Programmi principali");
            foreach (KeyValuePair<string, double> kv in st.Top(st.ByApp, 12)) { b.AppendLine("- " + kv.Key + " (" + Contexts.Get(st.AppContext[kv.Key]).Name + ")"); }
            b.AppendLine();
            b.AppendLine("## Ritmi");
            List<DateTime> starts = st.FirstOfDay.Values.OrderBy(x => x.TimeOfDay).ToList();
            List<DateTime> ends = st.LastOfDay.Values.OrderBy(x => x.TimeOfDay).ToList();
            if (starts.Count > 0) { b.AppendLine("- Inizio tipico " + starts[starts.Count / 2].ToString("HH:mm") + ", fine tipica " + ends[ends.Count / 2].ToString("HH:mm") + "."); }
            b.AppendLine("- Giorni attivi: " + st.ByDay.Count + " su 28.");
            Learned l = Learned.Load();
            List<string> taught = l.UserApps.Select(x => x.Key + " -> " + Contexts.Get(x.Value).Name).ToList();
            if (taught.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Indicazioni date dall'utente");
                foreach (string t in taught) { b.AppendLine("- " + t); }
            }
            return b.ToString();
        }

        static string ContextBody(ActivityStats st, string ctx, double seconds, List<Session> sessions)
        {
            ContextDef c = Contexts.Get(ctx);
            StringBuilder b = new StringBuilder(Header(c.Name));
            b.AppendLine("Tempo dedicato: " + Fmt.Duration(seconds) + " in 4 settimane.");
            b.AppendLine();
            b.AppendLine("## Strumenti");
            foreach (KeyValuePair<string, double> kv in st.ByApp.Where(a => st.AppContext[a.Key] == ctx).OrderByDescending(a => a.Value).Take(8))
            {
                string exe;
                st.AppExe.TryGetValue(kv.Key, out exe);
                b.AppendLine("- " + kv.Key + ": " + Fmt.Duration(kv.Value) + (exe != null ? " (`" + exe + "`)" : ""));
            }
            Dictionary<string, double> projects;
            if (st.ProjectsByContext.TryGetValue(ctx, out projects) && projects.Count > 0)
            {
                b.AppendLine();
                b.AppendLine("## Progetti e file ricorrenti");
                foreach (KeyValuePair<string, double> kv in projects.OrderByDescending(x => x.Value).Take(10)) { b.AppendLine("- " + kv.Key + " (" + Fmt.Duration(kv.Value) + ")"); }
            }
            double[] hours = new double[24];
            foreach (Session s in sessions.Where(x => x.Ctx == ctx)) { hours[s.Start.Hour] += s.Seconds; }
            int peak = Array.IndexOf(hours, hours.Max());
            b.AppendLine();
            b.AppendLine("## Quando");
            b.AppendLine("- Fascia più frequente: " + peak + ":00 - " + (peak + 1) + ":00.");
            List<string> next = st.Transitions.Where(t => t.Key.StartsWith(ctx + ">", StringComparison.Ordinal)).OrderByDescending(t => t.Value).Take(3).Select(t => Contexts.Get(t.Key.Split('>')[1]).Name + " (" + t.Value + ")").ToList();
            if (next.Count > 0) { b.AppendLine("- Dopo questo lavoro passa di solito a: " + string.Join(", ", next) + "."); }
            b.AppendLine();
            b.AppendLine("## Come aiutare");
            b.AppendLine("- Dai per scontato che usi questi programmi: riferisci menu e comandi a loro.");
            b.AppendLine("- Istruzioni numerate e brevi; se servono valori (codec, risoluzioni, impostazioni), indicali esatti.");
            if (c.Heavy) { b.AppendLine("- Lavoro pesante per il PC: Tipaldi AI attiva da solo il protocollo Overdrive (piano energetico prestazioni). Se il PC rallenta, controlla con lo strumento `system_status`."); }
            return b.ToString();
        }

        /// <summary>Scrive una skill (anche da Claude via MCP). Solo nomi tipaldi-*: non tocca altre skill.</summary>
        public static string Write(string id, string description, string body)
        {
            string name = SkillName(id);
            description = Regex.Replace(description ?? "", @"[\r\n]+", " ").Trim();
            if (description.Length > 1024) { description = description.Substring(0, 1024); }
            if (description.Length == 0) { throw new ArgumentException("Serve una descrizione."); }
            string dir = Path.Combine(Paths.Skills, name);
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "SKILL.md");
            string text = "---\nname: " + name + "\ndescription: " + description.Replace(":", " -") + "\n---\n\n" + body.Trim() + "\n";
            File.WriteAllText(file, text, new UTF8Encoding(false));

            // archivio per claude.ai: cartella <nome>/SKILL.md dentro lo zip
            string zipDir = Path.Combine(Paths.Reports, "skills");
            Directory.CreateDirectory(zipDir);
            string zip = Path.Combine(zipDir, name + ".zip");
            if (File.Exists(zip)) { File.Delete(zip); }
            using (ZipArchive z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                ZipArchiveEntry e = z.CreateEntry(name + "/SKILL.md");
                using (StreamWriter w = new StreamWriter(e.Open(), new UTF8Encoding(false))) { w.Write(text); }
            }
            return file;
        }

        public static List<Dictionary<string, object>> List()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (string dir in Directory.GetDirectories(Paths.Skills, Prefix + "*"))
            {
                string file = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(file)) { continue; }
                string text = File.ReadAllText(file, Encoding.UTF8);
                Match m = Regex.Match(text, @"^description:\s*(.+)$", RegexOptions.Multiline);
                list.Add(new Dictionary<string, object> { { "nome", Path.GetFileName(dir) }, { "descrizione", m.Success ? m.Groups[1].Value.Trim() : "" }, { "file", file }, { "aggiornata", File.GetLastWriteTime(file).ToString("s", CultureInfo.InvariantCulture) } });
            }
            return list;
        }
    }
}
