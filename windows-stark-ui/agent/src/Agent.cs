// Tipaldi AI - l'agente in background: campiona l'attività, esegue i protocolli automatici
// (diagnosi, riparazioni sicure, Overdrive, pulizia, ordine, apprendimento, report e skill).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TipaldiAI
{
    public class Agent
    {
        public readonly Settings Settings;
        public readonly Learned Learned;
        public readonly ActivityTracker Tracker;
        readonly Optimizer optimizer = new Optimizer();
        readonly object gate = new object();
        Timer fast, slow;
        int ticks;
        bool busy;
        public HealthReport LastHealth;

        /// <summary>Notifica al desktop che ci sono dati nuovi (diagnosi, eventi).</summary>
        public event Action Changed;

        public Agent()
        {
            Settings = Settings.Load();
            Learned = Learned.Load();
            Tracker = new ActivityTracker(Settings, Learned);
            LastHealth = HealthReport.LoadLast();
        }

        public string CurrentContext { get { Session s = Tracker.Current; return s != null ? s.Ctx : null; } }

        public void Start()
        {
            Events.Add("ai", "Tipaldi AI online. Protocolli attivi: diagnosi, riparazioni sicure, Overdrive, ordine, apprendimento.");
            fast = new Timer(_ => Guard(() => Tracker.Sample()), null, 1000, 5000);
            slow = new Timer(_ => Background(Tick), null, 20000, 60000);
        }

        public void Stop()
        {
            if (fast != null) { fast.Dispose(); }
            if (slow != null) { slow.Dispose(); }
            Guard(() => Tracker.Flush());
            Guard(Optimizer.Release);
        }

        static void Guard(Action a)
        {
            try { a(); } catch (Exception ex) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Paths.Logs, "agent.log"), DateTime.Now.ToString("s") + " " + ex + "\n"); } catch (Exception) { } }
        }

        void Background(Action a)
        {
            lock (gate) { if (busy) { return; } busy = true; }
            try { Guard(a); }
            finally { lock (gate) { busy = false; } }
        }

        void Tick()
        {
            ticks++;
            optimizer.Tick(CurrentContext, Settings);
            if (ticks % 10 == 1) { RunHealth(ticks % 60 == 1); }
            if (ticks % 30 == 5) { Learn(); }
            if (ActivityTracker.IdleSeconds() > 600) { DailyProtocols(); }
            if (ticks % 30 == 2) { WeeklyProtocols(); }
        }

        /// <summary>Diagnosi + riparazioni sicure automatiche + passaggio a Claude di ciò che resta.</summary>
        public HealthReport RunHealth(bool deep)
        {
            HealthReport previous = LastHealth;
            HealthReport h = Health.Run(deep);
            HashSet<string> known = new HashSet<string>(previous != null ? previous.Issues.Select(i => i.Id) : new string[0]);
            foreach (Issue i in h.Issues.Where(x => !known.Contains(x.Id) && x.Severity != "info")) { Events.Add("warn", "Rilevato: " + i.Title + "."); }

            List<Issue> forClaude = new List<Issue>();
            foreach (Issue i in h.Issues)
            {
                FixDef f = i.FixId != null ? Fixes.Get(i.FixId) : null;
                if (f != null && f.Safe && !f.Manual && Settings.AutoFixSafe)
                {
                    string key = "fix:" + i.Id;
                    if ((DateTime.Now - State.GetTime(key)).TotalHours < 6) { continue; }
                    State.SetTime(key, DateTime.Now);
                    Fixes.Execute(f.Id, i.FixArg, "auto", null);
                }
                else if (i.Severity != "info")
                {
                    string key = "claude:" + i.Id;
                    if ((DateTime.Now - State.GetTime(key)).TotalHours < 24) { continue; }
                    State.SetTime(key, DateTime.Now);
                    forClaude.Add(i);
                }
            }
            if (forClaude.Count > 0) { Escalate(forClaude); }
            if (!deep && previous != null && previous.Deep && (DateTime.Now - previous.Time).TotalHours < 2)
            {
                // una diagnosi rapida non cancella le segnalazioni approfondite recenti (eventi disco, antivirus...)
                foreach (Issue i in previous.Issues.Where(x => !h.Issues.Any(y => y.Id == x.Id) && (x.Id.StartsWith("evt-") || x.Id.StartsWith("defender") || x.Id == "integrita" || x.Id == "riavvio" || x.Id == "cestino"))) { h.Issues.Add(i); }
                h.Deep = true;
            }
            h.Save();
            LastHealth = h;
            Notify();
            return h;
        }

        void Escalate(List<Issue> issues)
        {
            if (!Settings.ClaudeAutoResolve || ClaudeLink.FindClaudeCli() == null)
            {
                Events.Add("ai", "Da valutare con Claude: " + string.Join(", ", issues.Select(i => i.Title)) + ". Apri Claude e scrivi: Tipaldi, controlla il sistema.");
                return;
            }
            Events.Add("ai", "Passo a Claude l'analisi di: " + string.Join(", ", issues.Select(i => i.Title)) + ".");
            ThreadPool.QueueUserWorkItem(_ => Guard(() =>
            {
                string answer = ClaudeLink.AutoResolve(issues);
                Events.Add("ai", answer != null ? "Claude: " + answer.Replace("\r", " ").Replace("\n", " ") : "Claude non ha risposto: riproverò più tardi.");
                Notify();
            }));
        }

        void Learn()
        {
            foreach (string n in Learned.LearnFrom(ActivityStore.Load(DateTime.Today.AddDays(-14), DateTime.Now))) { Events.Add("ai", n); }
        }

        void DailyProtocols()
        {
            if ((DateTime.Now - State.GetTime("lastCleanup")).TotalHours >= 24 && Settings.AutoFixSafe)
            {
                Fixes.Execute("clean-temp", null, "auto", null);
            }
            if ((DateTime.Now - State.GetTime("lastOrganize")).TotalHours >= 24)
            {
                State.SetTime("lastOrganize", DateTime.Now);
                if (Settings.OrganizeDownloads) { Organizer.Apply("downloads", TimeSpan.FromHours(2)); }
                if (Settings.OrganizeDesktop) { Organizer.Apply("desktop", TimeSpan.FromHours(24)); }
            }
        }

        public void WeeklyProtocols()
        {
            DateTime monday = WeeklyReport.WeekStart(DateTime.Today);
            string lastWeek = WeeklyReport.WeekId(monday.AddDays(-7));
            if (!Settings.WeeklyReport || State.Get("lastReportWeek") == lastWeek || DateTime.Now < monday.AddHours(8)) { return; }
            State.Set("lastReportWeek", lastWeek);
            Learn();
            WeeklyReport r = WeeklyReport.Build(monday.AddDays(-7));
            if (r.Stats.Total < 600) { return; }
            r.Save();
            Events.Add("ai", "Report settimanale " + lastWeek + " pronto: " + Fmt.Duration(r.Stats.Total) + " di lavoro analizzate.");
            State.Set("lastReportPath", r.HtmlPath);
            if (Settings.Skills) { Skills.Generate(); }
            Notify();
        }

        void Notify()
        {
            Action h = Changed;
            if (h != null) { try { h(); } catch (Exception) { } }
        }
    }
}
