// Tipaldi AI - report settimanale: cosa usi di più, quando lavori, cosa ha imparato l'AI, salute del PC.
// Produce report HTML (da aprire nel browser) e Markdown (per Claude e per le skill).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TipaldiAI
{
    public class WeeklyReport
    {
        public DateTime From;
        public DateTime To;
        public ActivityStats Stats;
        public ActivityStats Previous;
        public List<string> LearnedLines = new List<string>();
        public List<string> Suggestions = new List<string>();
        public List<string> SystemLines = new List<string>();
        public string HtmlPath;
        public string MarkdownPath;

        public static DateTime WeekStart(DateTime d)
        {
            int diff = ((int)d.DayOfWeek + 6) % 7;
            return d.Date.AddDays(-diff);
        }

        public static string WeekId(DateTime monday)
        {
            int week = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(monday.AddDays(3), CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return monday.AddDays(3).Year + "-W" + week.ToString("00");
        }

        /// <summary>Report della settimana che inizia il lunedì indicato.</summary>
        public static WeeklyReport Build(DateTime monday)
        {
            WeeklyReport r = new WeeklyReport();
            r.From = monday.Date;
            r.To = monday.Date.AddDays(7).AddSeconds(-1);
            List<Session> sessions = ActivityStore.Load(r.From, r.To);
            r.Stats = ActivityStats.From(sessions);
            r.Previous = ActivityStats.From(ActivityStore.Load(r.From.AddDays(-7), r.From.AddSeconds(-1)));
            r.BuildInsights(sessions);
            return r;
        }

        void BuildInsights(List<Session> sessions)
        {
            ActivityStats s = Stats;
            if (s.Total < 60) { LearnedLines.Add("Settimana senza attività registrata: nessuna osservazione."); return; }

            // orari tipici
            List<DateTime> starts = s.FirstOfDay.Values.OrderBy(x => x.TimeOfDay).ToList();
            List<DateTime> ends = s.LastOfDay.Values.OrderBy(x => x.TimeOfDay).ToList();
            if (starts.Count > 0)
            {
                LearnedLines.Add("Di solito inizi verso le " + starts[starts.Count / 2].ToString("HH:mm") + " e chiudi verso le " + ends[ends.Count / 2].ToString("HH:mm") + ".");
            }

            // fascia più intensa
            double[] byHour = new double[24];
            for (int d = 0; d < 7; d++) { for (int hh = 0; hh < 24; hh++) { byHour[hh] += s.Heat[d, hh]; } }
            int peak = 0;
            for (int hh = 1; hh < 23; hh++) { if (byHour[hh - 1] + byHour[hh] + byHour[hh + 1] > byHour[Math.Max(0, peak - 1)] + byHour[peak] + byHour[Math.Min(23, peak + 1)]) { peak = hh; } }
            LearnedLines.Add("La tua fascia più intensa è tra le " + Math.Max(0, peak - 1) + ":00 e le " + (Math.Min(23, peak + 1) + 1) + ":00.");

            // contesto principale e confronto
            KeyValuePair<string, double> main = s.ByContext.Where(x => x.Key != "system").OrderByDescending(x => x.Value).FirstOrDefault();
            if (main.Key != null)
            {
                double share = 100 * main.Value / s.Total;
                double prev;
                Previous.ByContext.TryGetValue(main.Key, out prev);
                string trend = prev > 0 ? " (" + (main.Value >= prev ? "+" : "") + Math.Round(100 * (main.Value - prev) / prev) + "% rispetto alla settimana prima)" : "";
                LearnedLines.Add("Il tuo lavoro principale è stato " + Contexts.Get(main.Key).Name + ": " + Fmt.Duration(main.Value) + ", il " + Math.Round(share) + "% del tempo attivo" + trend + ".");
            }

            // passaggi di contesto
            KeyValuePair<string, int> tr = s.Transitions.OrderByDescending(x => x.Value).FirstOrDefault();
            if (tr.Key != null && tr.Value >= 3)
            {
                string[] p = tr.Key.Split('>');
                LearnedLines.Add("Passi spesso da " + Contexts.Get(p[0]).Name + " a " + Contexts.Get(p[1]).Name + " (" + tr.Value + " volte).");
            }
            double hours = s.Total / 3600;
            if (hours > 0)
            {
                double perHour = s.Switches / hours;
                LearnedLines.Add("Cambi contesto in media " + perHour.ToString("0.0", Fmt.It) + " volte all'ora.");
                if (perHour > 8) { Suggestions.Add("Molti cambi di contesto: prova blocchi di 50 minuti su un solo lavoro, con notifiche in pausa."); }
            }

            // app nuove
            HashSet<string> before = new HashSet<string>(ActivityStats.From(ActivityStore.Load(From.AddDays(-28), From.AddSeconds(-1))).ByApp.Keys);
            List<string> fresh = s.ByApp.Where(x => !before.Contains(x.Key) && x.Value >= 900).OrderByDescending(x => x.Value).Select(x => x.Key).Take(5).ToList();
            if (fresh.Count > 0 && before.Count > 0) { LearnedLines.Add("App nuove nel tuo lavoro: " + string.Join(", ", fresh) + "."); }

            // apprendimento e insegnamenti
            Learned l = TipaldiAI.Learned.Load();
            foreach (Dictionary<string, object> i in l.Insights)
            {
                DateTime t;
                if (DateTime.TryParse(Json.Str(i, "t", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t >= From && t <= To) { LearnedLines.Add(Json.Str(i, "text", "")); }
            }

            // lavoro serale e weekend
            double late = 0, weekend = 0;
            foreach (Session x in sessions)
            {
                if (x.Start.Hour >= 22 || x.Start.Hour < 6) { late += x.Seconds; }
                if (x.Start.DayOfWeek == DayOfWeek.Saturday || x.Start.DayOfWeek == DayOfWeek.Sunday) { weekend += x.Seconds; }
            }
            if (late > 3 * 3600) { Suggestions.Add("Hai lavorato " + Fmt.Duration(late) + " dopo le 22: valuta di spostare i render lunghi di notte con il PC che lavora da solo."); }
            if (weekend > 4 * 3600) { LearnedLines.Add("Nel weekend hai lavorato " + Fmt.Duration(weekend) + "."); }

            // sistema
            List<Dictionary<string, object>> events = Events.Read(From, 2000).Where(e => { DateTime t; return DateTime.TryParse(Json.Str(e, "t", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t <= To; }).ToList();
            int fixes = events.Count(e => Json.Str(e, "kind", "") == "ok");
            int overdrive = events.Count(e => Json.Str(e, "text", "").StartsWith("Protocollo Overdrive attivo"));
            int moved = Organizer.MovedSince(From);
            if (fixes > 0) { SystemLines.Add(fixes + " interventi automatici o su tua richiesta completati."); }
            if (overdrive > 0) { SystemLines.Add("Protocollo Overdrive attivato " + overdrive + " volte nei lavori pesanti."); }
            if (moved > 0) { SystemLines.Add(moved + " file sistemati per contesto in Desktop e Download."); }
            HealthReport h = HealthReport.LoadLast();
            if (h.Time != DateTime.MinValue)
            {
                SystemLines.Add("Ultima diagnosi: stato " + h.Status + ", " + h.Issues.Count + " segnalazioni.");
                foreach (Issue i in h.Issues.Where(x => x.Severity != "info").Take(5)) { SystemLines.Add("Da guardare: " + i.Title + "."); }
                foreach (VolumeInfo v in h.Volumes.Where(x => x.FreePct < 15)) { Suggestions.Add("Disco " + v.Letter + " al " + Math.Round(100 - v.FreePct) + "%: sposta i render finiti e le cache su un disco esterno."); }
            }
            if (Suggestions.Count == 0) { Suggestions.Add("Nessun intervento suggerito: continua così."); }
        }

        public string Markdown()
        {
            StringBuilder md = new StringBuilder();
            ActivityStats s = Stats;
            md.AppendLine("# Tipaldi AI - report settimanale " + WeekId(From));
            md.AppendLine();
            md.AppendLine("Periodo: " + From.ToString("dddd d MMMM", Fmt.It) + " - " + To.ToString("dddd d MMMM yyyy", Fmt.It) + ". Tempo attivo: **" + Fmt.Duration(s.Total) + "**.");
            md.AppendLine();
            md.AppendLine("## Contesti di lavoro");
            foreach (KeyValuePair<string, double> kv in s.ByContext.OrderByDescending(x => x.Value)) { md.AppendLine("- " + Contexts.Get(kv.Key).Name + ": " + Fmt.Duration(kv.Value) + " (" + Math.Round(100 * kv.Value / Math.Max(1, s.Total)) + "%)"); }
            md.AppendLine();
            md.AppendLine("## App più usate");
            foreach (KeyValuePair<string, double> kv in s.Top(s.ByApp, 10)) { md.AppendLine("- " + kv.Key + " (" + Contexts.Get(s.AppContext[kv.Key]).Name + "): " + Fmt.Duration(kv.Value)); }
            md.AppendLine();
            if (s.Projects.Count > 0)
            {
                md.AppendLine("## Progetti e file ricorrenti");
                foreach (KeyValuePair<string, double> kv in s.Top(s.Projects, 10)) { md.AppendLine("- " + kv.Key + ": " + Fmt.Duration(kv.Value)); }
                md.AppendLine();
            }
            md.AppendLine("## Cosa ha imparato Tipaldi AI");
            foreach (string l in LearnedLines) { md.AppendLine("- " + l); }
            md.AppendLine();
            if (SystemLines.Count > 0)
            {
                md.AppendLine("## Sistema");
                foreach (string l in SystemLines) { md.AppendLine("- " + l); }
                md.AppendLine();
            }
            md.AppendLine("## Suggerimenti");
            foreach (string l in Suggestions) { md.AppendLine("- " + l); }
            return md.ToString();
        }

        public string Html()
        {
            ActivityStats s = Stats;
            StringBuilder h = new StringBuilder();
            h.Append("<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            h.Append("<title>Tipaldi AI - " + WeekId(From) + "</title><style>");
            h.Append(":root{--bg:#05080c;--panel:#0b131b;--line:#16303f;--cyan:#38d6ff;--gold:#e8b04b;--red:#ff4f5e;--text:#dce9f2;--dim:#6e8494}");
            h.Append("*{box-sizing:border-box}body{margin:0;background:radial-gradient(1200px 600px at 20% -10%,#0d2a3a 0,transparent 60%),var(--bg);color:var(--text);font:15px/1.5 'Segoe UÌ,system-ui,sans-serif;padding:32px 16px}");
            h.Append(".wrap{max-width:1080px;margin:0 auto}h1{font-weight:300;letter-spacing:.18em;text-transform:uppercase;margin:0;color:var(--cyan)}h2{font-weight:400;letter-spacing:.12em;text-transform:uppercase;font-size:13px;color:var(--gold);margin:0 0 14px}");
            h.Append(".sub{color:var(--dim);margin:6px 0 28px;letter-spacing:.06em}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:16px}");
            h.Append(".card{background:linear-gradient(180deg,#0c1720,#081017);border:1px solid var(--line);border-radius:16px;padding:20px;position:relative}.card:before{content:'';position:absolute;inset:0;border-radius:16px;pointer-events:none;box-shadow:inset 0 0 0 1px rgba(56,214,255,.06),0 0 40px rgba(56,214,255,.05)}");
            h.Append(".kpi{font-size:44px;font-weight:200;color:#fff}.kpi small{font-size:14px;color:var(--dim);margin-left:6px}.row{display:flex;align-items:center;gap:10px;margin:7px 0}.row .n{flex:0 0 132px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}");
            h.Append(".bar{flex:1;min-width:60px;height:8px;border-radius:5px;background:#0f2230;overflow:hidden}.bar i{display:block;height:100%;border-radius:5px}.row .v{flex:0 0 86px;text-align:right;color:var(--dim);font-variant-numeric:tabular-nums}");
            h.Append("ul{margin:0;padding-left:18px}li{margin:6px 0}.full{grid-column:1/-1}svg text{fill:var(--dim);font-size:10px}");
            h.Append("</style></head><body><div class=\"wrap\">");
            h.Append("<h1>Tipaldi AI</h1><div class=\"sub\">REPORT SETTIMANALE " + WeekId(From) + " &middot; " + Fmt.Html(From.ToString("d MMMM", Fmt.It) + " - " + To.ToString("d MMMM yyyy", Fmt.It)) + "</div>");
            h.Append("<div class=\"grid\">");
            h.Append("<div class=\"card\"><h2>Tempo attivo</h2><div class=\"kpi\">" + Math.Round(s.Total / 3600, 1).ToString(Fmt.It) + "<small>ore</small></div>");
            double prevTotal = Previous.Total;
            if (prevTotal > 0) { h.Append("<div class=\"sub\" style=\"margin:4px 0 0\">" + (s.Total >= prevTotal ? "+" : "") + Math.Round(100 * (s.Total - prevTotal) / prevTotal) + "% rispetto alla settimana prima</div>"); }
            h.Append("</div>");
            h.Append("<div class=\"card\"><h2>Contesti</h2>");
            double maxCtx = s.ByContext.Count > 0 ? s.ByContext.Values.Max() : 1;
            foreach (KeyValuePair<string, double> kv in s.ByContext.OrderByDescending(x => x.Value).Take(8))
            {
                ContextDef c = Contexts.Get(kv.Key);
                h.Append(Row(c.Name, kv.Value, maxCtx, c.Color));
            }
            h.Append("</div>");
            h.Append("<div class=\"card\"><h2>App più usate</h2>");
            double maxApp = s.ByApp.Count > 0 ? s.ByApp.Values.Max() : 1;
            foreach (KeyValuePair<string, double> kv in s.Top(s.ByApp, 8)) { h.Append(Row(kv.Key, kv.Value, maxApp, Contexts.Get(s.AppContext[kv.Key]).Color)); }
            h.Append("</div>");
            h.Append("<div class=\"card full\"><h2>Quando lavori</h2>" + HeatSvg() + "</div>");
            if (s.Projects.Count > 0)
            {
                h.Append("<div class=\"card\"><h2>Progetti e file</h2>");
                double maxP = s.Projects.Values.Max();
                foreach (KeyValuePair<string, double> kv in s.Top(s.Projects, 8)) { h.Append(Row(kv.Key, kv.Value, maxP, "#e8b04b")); }
                h.Append("</div>");
            }
            h.Append("<div class=\"card\"><h2>Cosa ho imparato</h2><ul>" + string.Concat(LearnedLines.Select(x => "<li>" + Fmt.Html(x) + "</li>")) + "</ul></div>");
            if (SystemLines.Count > 0) { h.Append("<div class=\"card\"><h2>Sistema</h2><ul>" + string.Concat(SystemLines.Select(x => "<li>" + Fmt.Html(x) + "</li>")) + "</ul></div>"); }
            h.Append("<div class=\"card\"><h2>Suggerimenti</h2><ul>" + string.Concat(Suggestions.Select(x => "<li>" + Fmt.Html(x) + "</li>")) + "</ul></div>");
            h.Append("</div></div></body></html>");
            return h.ToString();
        }

        static string Row(string name, double value, double max, string color)
        {
            double pct = max > 0 ? 100 * value / max : 0;
            return "<div class=\"row\"><span class=\"n\" title=\"" + Fmt.Html(name) + "\">" + Fmt.Html(name) + "</span><span class=\"bar\"><i style=\"width:" + pct.ToString("0.0", CultureInfo.InvariantCulture) + "%;background:" + color + "\"></i></span><span class=\"v\">" + Fmt.Duration(value) + "</span></div>";
        }

        string HeatSvg()
        {
            string[] days = { "Lun", "Mar", "Mer", "Gio", "Ven", "Sab", "Dom" };
            double max = 1;
            for (int d = 0; d < 7; d++) { for (int hh = 0; hh < 24; hh++) { max = Math.Max(max, Stats.Heat[d, hh]); } }
            StringBuilder sb = new StringBuilder("<svg viewBox=\"0 0 760 190\" width=\"100%\" role=\"img\" aria-label=\"Attività per giorno e ora\">");
            for (int d = 0; d < 7; d++)
            {
                sb.Append("<text x=\"0\" y=\"" + (d * 24 + 16) + "\">" + days[d] + "</text>");
                for (int hh = 0; hh < 24; hh++)
                {
                    double a = Stats.Heat[d, hh] / max;
                    sb.Append("<rect x=\"" + (36 + hh * 30) + "\" y=\"" + (d * 24) + "\" width=\"27\" height=\"20\" rx=\"4\" fill=\"#38d6ff\" fill-opacity=\"" + (a < 0.02 ? 0.05 : 0.12 + 0.88 * a).ToString("0.00", CultureInfo.InvariantCulture) + "\"/>");
                }
            }
            for (int hh = 0; hh < 24; hh += 3) { sb.Append("<text x=\"" + (40 + hh * 30) + "\" y=\"184\">" + hh + ":00</text>"); }
            sb.Append("</svg>");
            return sb.ToString();
        }

        public void Save()
        {
            string id = WeekId(From);
            HtmlPath = Path.Combine(Paths.Reports, "report-" + id + ".html");
            MarkdownPath = Path.Combine(Paths.Reports, "report-" + id + ".md");
            File.WriteAllText(HtmlPath, Html(), new UTF8Encoding(false));
            File.WriteAllText(MarkdownPath, Markdown(), new UTF8Encoding(false));
        }
    }
}
