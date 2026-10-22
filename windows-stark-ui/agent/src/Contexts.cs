// Tipaldi AI - contesti di lavoro: riconosce cosa stai facendo dall'app e dal titolo della finestra,
// impara a quale contesto appartengono le app nuove guardando quando le usi.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TipaldiAI
{
    public class ContextDef
    {
        public string Id;
        public string Name;
        public string Color;
        public string Glyph;            // icona del contesto (cartella icons\<Glyph>.ico)
        public string[] Apps;           // nomi di processo (minuscolo, senza .exe); '*' finale = prefisso
        public string[] Keywords;       // parole nel titolo (per browser e app generiche)
        public string[] Extensions;     // estensioni dei file (per l'ordinamento automatico)
        public bool Heavy;              // contesto pesante: attiva il protocollo Overdrive

        public ContextDef(string id, string name, string color, bool heavy, string apps, string keywords, string ext)
        {
            Id = id; Name = name; Color = color; Glyph = id; Heavy = heavy;
            Apps = Split(apps); Keywords = Split(keywords); Extensions = Split(ext);
        }

        static string[] Split(string s)
        {
            return string.IsNullOrEmpty(s) ? new string[0] : s.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()).ToArray();
        }

        public bool MatchesApp(string app)
        {
            foreach (string a in Apps)
            {
                if (a.EndsWith("*") ? app.StartsWith(a.TrimEnd('*'), StringComparison.Ordinal) : app == a) { return true; }
            }
            return false;
        }

        public bool MatchesTitle(string titleLower)
        {
            foreach (string k in Keywords) { if (titleLower.Contains(k)) { return true; } }
            return false;
        }
    }

    public static class Contexts
    {
        public const string Other = "altro";
        public const string Web = "web";

        public static readonly ContextDef[] All =
        {
            new ContextDef("video", "Montaggio video", "#38D6FF", true,
                "resolve|adobe premiere pro|premiere*|vegas*|kdenlive|avid*|mediacomposer|shotcut|olivevideo|capcut|handbrake|mediaencoder|adobe media encoder",
                "davinci resolve|premiere pro|frame.io",
                "mp4|mov|mxf|braw|r3d|ari|mkv|avi|m4v|prproj|drp|drt|edl|fcpxml"),
            new ContextDef("vfx", "VFX e compositing", "#B678FF", true,
                "nuke*|fusion*|afterfx|houdini*|hindie|happrentice|natron|mocha*|syntheyes*|silhouette*|pftrack*",
                "after effects|nuke|houdini",
                "nk|aep|comp|exr|dpx|hip|hipnc|setup"),
            new ContextDef("3d", "3D", "#FF8A3D", true,
                "blender|maya|3dsmax|cinema 4d|c4d|unrealeditor|unity|unity hub|substance*|zbrushcore|zbrush|marmoset*|rhino|sketchup|fusion360",
                "blender|sketchfab",
                "blend|fbx|obj|abc|usd|usda|usdz|c4d|ma|mb|max|glb|gltf"),
            new ContextDef("print", "Stampa 3D", "#E8B04B", false,
                "orca-slicer|orcaslicer|flashprint*|cura|ultimaker-cura|prusa-slicer|prusaslicer|bambu-studio|bambustudio|superslicer|lychee*|chitubox*",
                "printables|thingiverse|makerworld|flashforge|orcaslicer|klipper|mainsail|fluidd",
                "stl|3mf|gcode|gx|step|stp|amf"),
            new ContextDef("design", "Grafica", "#FF4F8B", false,
                "photoshop|illustrator|indesign|figma|krita|gimp*|affinity*|canva|lightroom*|captureone*|photopea",
                "figma|canva|photopea|behance|dribbble|pinterest",
                "psd|psb|ai|indd|svg|png|jpg|jpeg|webp|tif|tiff|cr2|cr3|nef|arw|dng|heic|gif"),
            new ContextDef("audio", "Audio", "#2EC4B6", false,
                "audition|reaper|ableton*|fl64|fl|protools|cubase*|studioone|audacity|izotope*",
                "soundcloud|freesound|epidemic sound|artlist",
                "wav|mp3|flac|aif|aiff|m4a|ogg|aac"),
            new ContextDef("dev", "Sviluppo", "#4FE3A1", false,
                "code|devenv|idea64|pycharm64|rider64|webstorm64|windowsterminal|wt|powershell|pwsh|cmd|mintty|sublime_text|notepad++|githubdesktop|docker desktop|cursor|windsurf",
                "github|gitlab|stack overflow|stackoverflow|localhost|npm|pypi|visual studio",
                "py|js|ts|tsx|cs|cpp|c|h|java|go|rs|ps1|psm1|sh|bat|json|yaml|yml|toml|html|css|ipynb|sql"),
            new ContextDef("ai", "AI", "#9AE6FF", false,
                "claude|chatgpt|ollama|lm studio|comfyui|pinokio",
                "claude|chatgpt|gemini|perplexity|higgsfield|midjourney|comfyui|hugging face|huggingface|runway|krea|kling|copilot",
                "safetensors|ckpt|gguf"),
            new ContextDef("comm", "Comunicazione", "#7FB2FF", false,
                "outlook|olk|thunderbird|teams|ms-teams|slack|discord|whatsapp|telegram|zoom|signal|skype|mailspring",
                "gmail|outlook|whatsapp|teams|meet.google|slack|posta|inbox|telegram",
                "eml|msg"),
            new ContextDef("office", "Documenti e ufficio", "#C9D6DF", false,
                "winword|excel|powerpnt|onenote|acrobat|acrord32|sumatrapdf|notepad|wordpad|libreoffice*|soffice*|notion|obsidian",
                "google docs|documenti google|fogli google|google sheets|presentazioni google|notion|dropbox|drive.google|onedrive|fattur|preventiv",
                "doc|docx|xls|xlsx|ppt|pptx|pdf|txt|odt|ods|csv|rtf|md|pages|numbers|key"),
            new ContextDef("fun", "Svago", "#FFD166", false,
                "spotify|vlc|steam|epicgameslauncher|battle.net|netflix|primevideo|disney*|mpc-hc*|potplayer*",
                "youtube|netflix|twitch|prime video|disney+|spotify|raiplay|mediaset",
                ""),
            new ContextDef("system", "Sistema", "#8899AA", false,
                "explorer|taskmgr|systemsettings|control|mmc|regedit|applicationframehost|searchhost|searchapp|shellexperiencehost|startmenuexperiencehost|tipaldiai|lockapp",
                "",
                ""),
            new ContextDef(Web, "Web", "#6E8494", false,
                "chrome|msedge|firefox|brave|opera|vivaldi|arc|iexplore",
                "",
                "html|htm|url"),
            new ContextDef(Other, "Altro", "#56697A", false, "", "", ""),
        };

        static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "iexplore" };

        public static ContextDef Get(string id)
        {
            foreach (ContextDef c in All) { if (c.Id == id) { return c; } }
            return All[All.Length - 1];
        }

        public static bool IsBrowser(string app) { return Browsers.Contains(app); }

        /// <summary>Contesto di una sessione. Ordine: scelta dell'utente, regole note, titolo (browser), apprendimento.</summary>
        public static string Classify(string app, string title, Learned learned)
        {
            app = (app ?? "").ToLowerInvariant();
            string t = (title ?? "").ToLowerInvariant();
            string forced;
            if (learned != null && learned.UserApps.TryGetValue(app, out forced)) { return forced; }
            bool browser = IsBrowser(app);
            if (!browser)
            {
                foreach (ContextDef c in All) { if (c.MatchesApp(app)) { return c.Id; } }
            }
            if (t.Length > 0)
            {
                foreach (ContextDef c in All)
                {
                    if (c.Id == "system" || c.Id == Web || c.Id == Other) { continue; }
                    if (c.MatchesTitle(t)) { return c.Id; }
                }
            }
            if (browser) { return Web; }
            if (learned != null && learned.Apps.TryGetValue(app, out forced)) { return forced; }
            return Other;
        }

        /// <summary>Contesto di un file dalla sua estensione (per l'ordinamento automatico). Null se sconosciuta.</summary>
        public static ContextDef ForExtension(string ext)
        {
            ext = (ext ?? "").TrimStart('.').ToLowerInvariant();
            if (ext.Length == 0) { return null; }
            foreach (ContextDef c in All) { if (c.Extensions.Contains(ext)) { return c; } }
            return null;
        }
    }

    /// <summary>Quello che Tipaldi AI ha imparato (learned.json): app assegnate ai contesti e scoperte.</summary>
    public class Learned
    {
        public Dictionary<string, string> Apps = new Dictionary<string, string>();       // imparate dall'uso
        public Dictionary<string, string> UserApps = new Dictionary<string, string>();   // insegnate dall'utente (anche via Claude)
        public List<Dictionary<string, object>> Insights = new List<Dictionary<string, object>>();

        public static Learned Load()
        {
            Learned l = new Learned();
            Dictionary<string, object> d = Json.ReadFile(Paths.LearnedFile);
            foreach (KeyValuePair<string, object> kv in Json.Obj(d, "apps")) { l.Apps[kv.Key] = Convert.ToString(kv.Value); }
            foreach (KeyValuePair<string, object> kv in Json.Obj(d, "userApps")) { l.UserApps[kv.Key] = Convert.ToString(kv.Value); }
            foreach (object o in Json.Arr(d, "insights")) { Dictionary<string, object> i = o as Dictionary<string, object>; if (i != null) { l.Insights.Add(i); } }
            return l;
        }

        public void Save()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["apps"] = Apps;
            d["userApps"] = UserApps;
            d["insights"] = Insights.Skip(Math.Max(0, Insights.Count - 400)).ToList();
            Json.WriteFile(Paths.LearnedFile, d, true);
        }

        public void AddInsight(string kind, string text)
        {
            Dictionary<string, object> i = new Dictionary<string, object>();
            i["t"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            i["kind"] = kind;
            i["text"] = text;
            Insights.Add(i);
        }

        public void Teach(string app, string contextId)
        {
            app = app.ToLowerInvariant();
            UserApps[app] = contextId;
            AddInsight("teach", "Mi hai insegnato che " + app + " fa parte di \"" + Contexts.Get(contextId).Name + "\".");
            Save();
        }

        /// <summary>
        /// Le app "Altro" usate almeno 10 minuti vengono collegate al contesto con cui si alternano di più
        /// (sessioni a meno di 10 minuti di distanza), se quel contesto pesa almeno il 60%.
        /// </summary>
        public List<string> LearnFrom(List<Session> sessions)
        {
            List<string> news = new List<string>();
            Dictionary<string, double> unknownTime = new Dictionary<string, double>();
            foreach (Session s in sessions)
            {
                if (s.Ctx != Contexts.Other || UserApps.ContainsKey(s.App) || Apps.ContainsKey(s.App)) { continue; }
                double v;
                unknownTime.TryGetValue(s.App, out v);
                unknownTime[s.App] = v + s.Seconds;
            }
            foreach (KeyValuePair<string, double> kv in unknownTime.Where(x => x.Value >= 600))
            {
                Dictionary<string, double> near = new Dictionary<string, double>();
                foreach (Session s in sessions.Where(x => x.App == kv.Key))
                {
                    foreach (Session o in sessions)
                    {
                        if (o.Ctx == Contexts.Other || o.Ctx == "system" || o.Ctx == Contexts.Web) { continue; }
                        if (o.Start > s.End.AddMinutes(10) || o.End < s.Start.AddMinutes(-10)) { continue; }
                        double v;
                        near.TryGetValue(o.Ctx, out v);
                        near[o.Ctx] = v + o.Seconds;
                    }
                }
                double total = near.Values.Sum();
                if (total <= 0) { continue; }
                KeyValuePair<string, double> best = near.OrderByDescending(x => x.Value).First();
                if (best.Value / total < 0.6) { continue; }
                Apps[kv.Key] = best.Key;
                string text = "Ho collegato " + kv.Key + " al contesto \"" + Contexts.Get(best.Key).Name + "\": la usi insieme a quel lavoro (" + Math.Round(100 * best.Value / total) + "% delle volte).";
                AddInsight("learn", text);
                news.Add(text);
            }
            if (news.Count > 0) { Save(); }
            return news;
        }
    }
}
