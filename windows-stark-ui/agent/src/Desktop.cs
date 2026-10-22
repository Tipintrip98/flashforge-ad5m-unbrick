// Tipaldi AI - desktop futuristico (WPF). Una finestra sotto tutte le altre che prende il posto del desktop:
// reattore animato con telemetria, contesto di lavoro, avvio rapido, spazio di lavoro ordinato per contesto,
// integrità di sistema con riparazioni, console AI. Disegnato a 1920x1080 e scalato su qualsiasi schermo.
#if !NOWPF
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using IOPath = System.IO.Path;

namespace TipaldiAI
{
    public static class DesktopApp
    {
        public static int Run(Agent agent, EventWaitHandle stop)
        {
            Application app = new Application();
            // chiusura ordinata richiesta da "TipaldiAI.exe --stop" (installer, ripristino)
            Thread waiter = new Thread(() => { stop.WaitOne(); app.Dispatcher.BeginInvoke(new Action(app.Shutdown)); }) { IsBackground = true };
            waiter.Start();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            HudWindow hud = new HudWindow(agent, false);
            if (agent.Settings.Hud) { hud.Show(); }

            Forms.NotifyIcon tray = new Forms.NotifyIcon();
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Paths.ExePath); } catch (Exception) { tray.Icon = System.Drawing.SystemIcons.Application; }
            tray.Text = "Tipaldi AI";
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Mostra / nascondi il desktop Tipaldi AI", null, (s, e) => { if (hud.IsVisible) { hud.Hide(); } else { hud.Show(); hud.SendToBottom(); } });
            menu.Items.Add("Scansione completa", null, (s, e) => hud.FullScan());
            menu.Items.Add("Report settimanale", null, (s, e) => hud.OpenReport());
            menu.Items.Add("Cartella dati", null, (s, e) => Process.Start(new ProcessStartInfo(Paths.Root) { UseShellExecute = true }));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Esci", null, (s, e) => { tray.Visible = false; app.Shutdown(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => { hud.Show(); hud.SendToBottom(); };
            tray.Visible = true;
            app.Run();
            tray.Dispose();
            return 0;
        }

        /// <summary>Immagine del desktop con dati dimostrativi (per i test su Windows).</summary>
        public static int Screenshot(string png)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIPALDI_HOME")))
            {
                string home = IOPath.Combine(IOPath.GetTempPath(), "tipaldi-demo");
                if (System.IO.Directory.Exists(home)) { System.IO.Directory.Delete(home, true); }
                Environment.SetEnvironmentVariable("TIPALDI_HOME", home);
                Environment.SetEnvironmentVariable("TIPALDI_SKILLS", IOPath.Combine(home, "skills"));
            }
            HudWindow.DesktopOverride = SelfTest.SeedDemo();
            Agent agent = new Agent();
            agent.RunHealth(true);
            Application app = new Application();
            HudWindow hud = new HudWindow(agent, true);
            hud.Show();
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            int code = 1;
            t.Tick += (s, e) =>
            {
                t.Stop();
                try { hud.Save(png); code = 0; } catch (Exception ex) { Console.Error.WriteLine(ex); }
                app.Shutdown();
            };
            t.Start();
            app.Run();
            return code;
        }
    }

    static class HudXaml
    {
        public static string Markup
        {
            get
            {
                using (System.IO.Stream st = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("TipaldiAI.Hud.xaml"))
                using (System.IO.StreamReader r = new System.IO.StreamReader(st, System.Text.Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
        }
    }

    public class HudWindow : Window
    {
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attr; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }

        const double W = 1920, H = 1080, CX = 960, CY = 450;
        static readonly Color Cyan = (Color)ColorConverter.ConvertFromString("#38D6FF");
        static readonly Color Gold = (Color)ColorConverter.ConvertFromString("#E8B04B");
        static readonly Color Red = (Color)ColorConverter.ConvertFromString("#FF4F5E");
        static readonly Color Dim = (Color)ColorConverter.ConvertFromString("#6E8494");

        readonly Agent agent;
        readonly bool demo;
        readonly Canvas root;
        readonly Dictionary<string, FrameworkElement> named = new Dictionary<string, FrameworkElement>();
        readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer typer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(22) };
        long lastIdle, lastTotal;
        long lastRx, lastTx;
        DateTime lastNet;
        int refreshCountdown;
        string typingTarget = "";
        TextBlock typingBlock;
        string lastEventKey = "";
        public static string DesktopOverride;

        public HudWindow(Agent agent, bool demo)
        {
            this.agent = agent;
            this.demo = demo;
            Title = "Tipaldi AI";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#05080C"));
            if (demo)
            {
                Left = 0; Top = 0; Width = W; Height = H;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
            else
            {
                Rect wa = SystemParameters.WorkArea;
                Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = wa.Height;
            }

            Resources["Display"] = LoadFont("./#Rajdhani, Segoe UI Light");
            Resources["DisplayBold"] = LoadFont("./#Rajdhani SemiBold, Segoe UI Semibold");
            Resources["Mono"] = LoadFont("./#Share Tech Mono, Consolas");

            root = (Canvas)XamlReader.Parse(HudXaml.Markup);
            Collect(root);
            Viewbox vb = new Viewbox { Stretch = Stretch.Uniform, Child = root };
            Grid host = new Grid();
            host.Background = new RadialGradientBrush((Color)ColorConverter.ConvertFromString("#0C2333"), (Color)ColorConverter.ConvertFromString("#04070A")) { Center = new Point(0.5, 0.42), GradientOrigin = new Point(0.5, 0.42), RadiusX = 0.75, RadiusY = 0.9 };
            host.Children.Add(vb);
            Content = host;

            foreach (string n in new[] { "PanelLeft", "PanelWork", "PanelRight", "PanelActions", "PanelConsole" }) { AddBrackets(named[n] as Border); }
            Click("BtnScan", FullScan);
            Click("BtnReport", OpenReport);
            Click("BtnOrganize", OrganizeNow);
            Click("BtnUndo", () => RunBackground("Annullo l'ultimo riordino...", () => Organizer.Undo(null)));
            Click("BtnSkills", () => RunBackground("Aggiorno le skill di Claude...", () => Skills.Generate()));

            agent.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshData));
            clock.Tick += (s, e) => Tick();
            typer.Tick += (s, e) => TypeStep();
            Loaded += (s, e) =>
            {
                Tick();
                RefreshData();
                clock.Start();
                if (!demo) { Boot(); }
            };
            SourceInitialized += (s, e) => AttachToDesktop();
            Activated += (s, e) => SendToBottom();
        }

        static FontFamily LoadFont(string family)
        {
            string dir = IOPath.Combine(Paths.AppDir, "fonts") + IOPath.DirectorySeparatorChar;
            try { return new FontFamily(new Uri(dir), family); } catch (Exception) { return new FontFamily(family.Split(',').Last().Trim()); }
        }

        void Collect(DependencyObject d)
        {
            FrameworkElement fe = d as FrameworkElement;
            if (fe != null && !string.IsNullOrEmpty(fe.Name)) { named[fe.Name] = fe; }
            foreach (object c in LogicalTreeHelper.GetChildren(d)) { DependencyObject dc = c as DependencyObject; if (dc != null) { Collect(dc); } }
        }

        T Get<T>(string name) where T : class { FrameworkElement e; return named.TryGetValue(name, out e) ? e as T : null; }

        void Click(string name, Action a)
        {
            Button b = Get<Button>(name);
            if (b != null) { b.Click += (s, e) => a(); }
        }

        // ------------------------------------------------------------------ finestra sul desktop

        void AttachToDesktop()
        {
            if (demo) { return; }
            IntPtr h = new WindowInteropHelper(this).Handle;
            // proprietaria = finestra del desktop: resta visibile con Win+D e non compare in Alt+Tab
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero) { SetWindowLongPtr64(h, -8, progman); }
            long ex = GetWindowLongPtr64(h, -20).ToInt64();
            SetWindowLongPtr64(h, -20, new IntPtr((ex | 0x80) & ~0x40000L));   // WS_EX_TOOLWINDOW, senza WS_EX_APPWINDOW
            SendToBottom();
        }

        public void SendToBottom()
        {
            if (demo) { return; }
            IntPtr h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) { SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }   // HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
        }

        void Boot()
        {
            int i = 0;
            foreach (string n in new[] { "TopBar", "PanelLeft", "Reactor", "PanelRight", "PanelWork", "PanelConsole", "PanelActions" })
            {
                FrameworkElement e = Get<FrameworkElement>(n);
                if (e == null) { continue; }
                e.Opacity = 0;
                TimeSpan begin = TimeSpan.FromMilliseconds(180 * i++);
                DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                e.BeginAnimation(OpacityProperty, fade);
                if (e.RenderTransform == null || e.RenderTransform == Transform.Identity)
                {
                    // il reattore ha già la sua scala: si anima solo la dissolvenza
                    TranslateTransform tt = new TranslateTransform(0, 24);
                    e.RenderTransform = tt;
                    DoubleAnimation slide = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(700)) { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                    tt.BeginAnimation(TranslateTransform.YProperty, slide);
                }
            }
        }

        // ------------------------------------------------------------------ ogni secondo: orologio e telemetria

        void Tick()
        {
            DateTime now = demo ? DateTime.Today.AddHours(10).AddMinutes(42) : DateTime.Now;
            Set("ClockText", now.ToString("HH:mm"));
            Set("SecondsText", now.ToString("ss"));
            Set("DateText", now.ToString("dddd d MMMM yyyy", Fmt.It).ToUpper(Fmt.It));
            int hour = now.Hour;
            Set("GreetText", (hour < 13 ? "Buongiorno" : hour < 18 ? "Buon pomeriggio" : "Buonasera") + ", " + UserName() + ".");

            double cpu = demo ? 23 : Cpu();
            double total;
            double ram = Health.MemoryLoad(out total);
            if (demo) { ram = 61; }
            SetArc("CpuArc", 236, cpu / 100.0);
            SetArc("RamArc", 210, ram / 100.0);
            Set("CpuValue", Math.Round(cpu) + "%");
            Set("RamValue", Math.Round(ram) + "%");
            Set("RamSub", total > 0 ? total.ToString("0", Fmt.It) + " GB TOTALI" : "");
            Set("NetValue", Net());
            System.IO.DriveInfo sys = new System.IO.DriveInfo(IOPath.GetPathRoot(Environment.SystemDirectory));
            double free = sys.IsReady ? 100.0 * sys.AvailableFreeSpace / sys.TotalSize : 0;
            Set("DiskValue", sys.Name.TrimEnd('\\') + "  " + Math.Round(free) + "% LIBERO  ·  " + Fmt.Bytes(sys.AvailableFreeSpace));

            string ctx = agent.CurrentContext;
            if (demo) { ctx = "video"; }
            UpdateContext(ctx);
            if (--refreshCountdown <= 0) { refreshCountdown = 10; RefreshData(); }
        }

        static string UserName()
        {
            string n = Environment.UserName;
            return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n;
        }

        double Cpu()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) { return 0; }
            long total = kernel + user;
            double v = 0;
            if (lastTotal > 0 && total > lastTotal) { v = 100.0 * (1 - (double)(idle - lastIdle) / (total - lastTotal)); }
            lastIdle = idle; lastTotal = total;
            return Math.Max(0, Math.Min(100, v));
        }

        string Net()
        {
            if (demo) { return "↓ 2,4 MB/s   ↑ 310 KB/s"; }
            long rx = 0, tx = 0;
            try
            {
                foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) { continue; }
                    IPv4InterfaceStatistics st = n.GetIPv4Statistics();
                    rx += st.BytesReceived; tx += st.BytesSent;
                }
            }
            catch (Exception) { }
            DateTime now = DateTime.Now;
            string text = "↓ 0 B/s   ↑ 0 B/s";
            double sec = (now - lastNet).TotalSeconds;
            if (lastNet != DateTime.MinValue && sec > 0) { text = "↓ " + Fmt.Bytes(Math.Max(0, rx - lastRx) / sec) + "/s   ↑ " + Fmt.Bytes(Math.Max(0, tx - lastTx) / sec) + "/s"; }
            lastRx = rx; lastTx = tx; lastNet = now;
            return text;
        }

        void SetArc(string name, double r, double fraction)
        {
            Path p = Get<Path>(name);
            if (p == null) { return; }
            fraction = Math.Max(0.002, Math.Min(0.999, fraction));
            double start = -220, sweep = 260 * fraction;   // arco aperto in basso, come un indicatore HUD
            Point a = OnCircle(r, start), b = OnCircle(r, start + sweep);
            PathFigure f = new PathFigure { StartPoint = a, IsClosed = false };
            f.Segments.Add(new ArcSegment(b, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
            PathGeometry g = new PathGeometry();
            g.Figures.Add(f);
            p.Data = g;
        }

        static Point OnCircle(double r, double deg)
        {
            double rad = deg * Math.PI / 180;
            return new Point(CX + r * Math.Cos(rad), CY + r * Math.Sin(rad));
        }

        void Set(string name, string text)
        {
            TextBlock t = Get<TextBlock>(name);
            if (t != null && t.Text != text) { t.Text = text; }
        }

        // ------------------------------------------------------------------ dati (ogni 10 s o quando l'agente avvisa)

        string shownContext;

        void UpdateContext(string ctx)
        {
            if (ctx == shownContext) { return; }
            shownContext = ctx;
            ContextDef c = ctx != null ? Contexts.Get(ctx) : null;
            Set("CtxName", c != null ? c.Name.ToUpper(Fmt.It) : "IN ATTESA");
            TextBlock name = Get<TextBlock>("CtxName");
            if (name != null)
            {
                Color col = c != null ? (Color)ColorConverter.ConvertFromString(c.Color) : Dim;
                name.Foreground = new SolidColorBrush(col);
                name.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = col, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.7 };
            }
            Set("OverdriveText", c != null && c.Heavy && agent.Settings.Overdrive ? "OVERDRIVE PRONTO" : "OVERDRIVE IN ATTESA");
            FillQuickLaunch(ctx);
        }

        void RefreshData()
        {
            DateTime today = demo ? DateTime.Today.AddDays(-7) : DateTime.Today;
            ActivityStats st = ActivityStats.From(ActivityStore.Load(today, today.AddDays(1)));
            Set("CtxTime", "OGGI  " + Fmt.Duration(st.Total).ToUpper(Fmt.It) + " DI LAVORO");
            FillBars(st);
            FillHealth(agent.LastHealth);
            FillConsole();
            FillWorkspace();
            bool linked = false;
            try { linked = ClaudeLink.IsRegisteredDesktop() || demo; } catch (Exception) { }
            Set("ClaudeText", linked ? "CLAUDE · MCP COLLEGATO" : "CLAUDE · NON COLLEGATO");
            Ellipse dot = Get<Ellipse>("ClaudeDot");
            if (dot != null) { dot.Fill = new SolidColorBrush(linked ? Cyan : Gold); }
            bool overdrive = State.Get("overdriveOriginal") != null;
            if (overdrive) { Set("OverdriveText", "OVERDRIVE ATTIVO"); }
        }

        void FillBars(ActivityStats st)
        {
            StackPanel p = Get<StackPanel>("CtxBars");
            if (p == null) { return; }
            p.Children.Clear();
            double max = st.ByContext.Count > 0 ? st.ByContext.Values.Max() : 1;
            foreach (KeyValuePair<string, double> kv in st.ByContext.Where(x => x.Key != "system").OrderByDescending(x => x.Value).Take(5))
            {
                ContextDef c = Contexts.Get(kv.Key);
                Color col = (Color)ColorConverter.ConvertFromString(c.Color);
                Grid row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.Children.Add(Label(c.Name, 17, Colors.White, 0.85));
                Border track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(40, 56, 214, 255)), VerticalAlignment = VerticalAlignment.Center };
                Border bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(col), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(4, 190 * kv.Value / max) };
                bar.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = col, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 };
                Grid cell = new Grid { VerticalAlignment = VerticalAlignment.Center };
                cell.Children.Add(track);
                cell.Children.Add(bar);
                Grid.SetColumn(cell, 1);
                row.Children.Add(cell);
                TextBlock v = Label(Fmt.Duration(kv.Value), 15, Dim, 1);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                v.FontFamily = (FontFamily)Resources["Mono"];
                Grid.SetColumn(v, 2);
                row.Children.Add(v);
                p.Children.Add(row);
            }
            if (p.Children.Count == 0) { p.Children.Add(Label("Sto imparando come lavori: i contesti compaiono qui.", 16, Dim, 1)); }
        }

        TextBlock Label(string text, double size, Color color, double opacity)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = new SolidColorBrush(color), Opacity = opacity, FontFamily = (FontFamily)Resources["Display"], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        }

        void FillQuickLaunch(string ctx)
        {
            WrapPanel p = Get<WrapPanel>("QuickPanel");
            if (p == null) { return; }
            p.Children.Clear();
            ActivityStats st = ActivityStats.From(ActivityStore.Load(DateTime.Today.AddDays(-28), DateTime.Now));
            IEnumerable<KeyValuePair<string, double>> apps = st.ByApp.Where(a => st.AppExe.ContainsKey(a.Key) && st.AppContext[a.Key] != "system");
            List<KeyValuePair<string, double>> ordered = apps.OrderByDescending(a => (ctx != null && st.AppContext[a.Key] == ctx ? 1e9 : 0) + a.Value).Take(6).ToList();
            foreach (KeyValuePair<string, double> a in ordered)
            {
                string exe = st.AppExe[a.Key];
                p.Children.Add(Tile(exe, a.Key, Contexts.Get(st.AppContext[a.Key]).Color, () => Launch(exe), 64));
            }
            if (p.Children.Count == 0) { p.Children.Add(Label("Le app che usi di più compariranno qui.", 16, Dim, 1)); }
        }

        FrameworkElement Tile(string path, string caption, string color, Action open, double size)
        {
            Color col = (Color)ColorConverter.ConvertFromString(color);
            StackPanel sp = new StackPanel { Width = size + 18, Margin = new Thickness(4, 4, 4, 6) };
            Grid frame = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
            Path hex = new Path
            {
                Data = Geometry.Parse("M 50,2 L 92,26 L 92,74 L 50,98 L 8,74 L 8,26 Z"),
                Stretch = Stretch.Fill,
                Stroke = new SolidColorBrush(Color.FromArgb(160, col.R, col.G, col.B)),
                StrokeThickness = 1.4,
                Fill = new LinearGradientBrush(Color.FromArgb(70, col.R, col.G, col.B), Color.FromArgb(12, col.R, col.G, col.B), 90)
            };
            frame.Children.Add(hex);
            ImageSource icon = IconOf(path);
            if (icon != null) { frame.Children.Add(new Image { Source = icon, Width = size * 0.5, Height = size * 0.5 }); }
            sp.Children.Add(frame);
            TextBlock t = Label(caption, 13, Colors.White, 0.8);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            t.MaxWidth = size + 16;
            t.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(t);
            Button b = new Button { Content = sp, Style = (Style)root.Resources["TileButton"], ToolTip = path };
            b.Click += (s, e) => open();
            return b;
        }

        ImageSource IconOf(string path)
        {
            if (string.IsNullOrEmpty(path) || (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))) { return null; }
            SHFILEINFO info = new SHFILEINFO();
            IntPtr r = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x100 | 0x0);   // SHGFI_ICON | SHGFI_LARGEICON
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) { return null; }
            try
            {
                BitmapSource bs = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            finally { DestroyIcon(info.hIcon); }
        }

        static void Launch(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Events.Add("warn", "Non riesco ad aprire " + IOPath.GetFileName(path) + ": " + ex.Message); }
        }

        // ------------------------------------------------------------------ spazio di lavoro (desktop ordinato per contesto)

        void FillWorkspace()
        {
            WrapPanel p = Get<WrapPanel>("WorkPanel");
            if (p == null) { return; }
            p.Children.Clear();
            List<string> items = new List<string>();
            foreach (string dir in new[] { DesktopOverride ?? Organizer.Desktop, demo ? null : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
            {
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) { continue; }
                foreach (string f in System.IO.Directory.GetFileSystemEntries(dir))
                {
                    System.IO.FileAttributes a = System.IO.File.GetAttributes(f);
                    if ((a & (System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System)) != 0) { continue; }
                    items.Add(f);
                }
            }
            var grouped = items.Select(f => new { Path = f, Ctx = ContextOfItem(f) })
                .OrderBy(x => x.Ctx == null ? "zz" : x.Ctx.Name).ThenBy(x => IOPath.GetFileName(x.Path)).Take(18).ToList();
            foreach (var g in grouped)
            {
                string name = IOPath.GetFileNameWithoutExtension(g.Path);
                string color = g.Ctx != null ? g.Ctx.Color : "#8899AA";
                string path = g.Path;
                p.Children.Add(Tile(path, name, color, () => Launch(path), 52));
            }
            if (p.Children.Count == 0) { p.Children.Add(Label("Desktop pulito. I file nuovi vengono sistemati per contesto.", 16, Dim, 1)); }
            Set("WorkCount", items.Count + " ELEMENTI");
        }

        static ContextDef ContextOfItem(string path)
        {
            if (System.IO.Directory.Exists(path))
            {
                ContextDef byName = Contexts.All.FirstOrDefault(c => c.Name.Equals(IOPath.GetFileName(path), StringComparison.OrdinalIgnoreCase));
                return byName;
            }
            string ext = IOPath.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk")
            {
                string target = ShortcutTarget(path);
                if (target != null) { return Contexts.Get(Contexts.Classify(IOPath.GetFileNameWithoutExtension(target), "", null)); }
                return null;
            }
            return Contexts.ForExtension(ext);
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                return (string)sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ integrità

        void FillHealth(HealthReport h)
        {
            if (h == null || h.Time == DateTime.MinValue) { Set("StatusWord", "ANALISI..."); return; }
            string status = h.Status;
            Set("StatusWord", status);
            TextBlock sw = Get<TextBlock>("StatusWord");
            if (sw != null)
            {
                Color col = status == "CRITICO" ? Red : status == "ATTENZIONE" ? Gold : Cyan;
                sw.Foreground = new SolidColorBrush(col);
                sw.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = col, BlurRadius = 26, ShadowDepth = 0, Opacity = 0.75 };
            }
            Set("StatusTime", "ULTIMA DIAGNOSI " + h.Time.ToString("HH:mm", Fmt.It) + (h.Deep ? "  ·  COMPLETA" : "  ·  RAPIDA"));

            StackPanel disks = Get<StackPanel>("DisksPanel");
            if (disks != null)
            {
                disks.Children.Clear();
                foreach (DiskInfo d in h.Disks.Take(3))
                {
                    Color col = d.Health == "Integro" ? Cyan : d.Health == "Avviso" ? Gold : d.Health == "Guasto" ? Red : Dim;
                    StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(col), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
                    TextBlock t = Label((d.Media + " " + d.Bus).Trim() + "  " + Math.Round(d.SizeGb) + " GB  ·  " + d.Health.ToUpper(Fmt.It), 15, Colors.White, 0.85);
                    t.FontFamily = (FontFamily)Resources["Mono"];
                    t.MaxWidth = 380;
                    row.Children.Add(t);
                    disks.Children.Add(row);
                }
                foreach (VolumeInfo v in h.Volumes.Take(3))
                {
                    Color col = v.FreePct < 10 ? Red : v.FreePct < 20 ? Gold : Cyan;
                    Grid row = new Grid { Margin = new Thickness(0, 6, 0, 2) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    row.Children.Add(Label(v.Letter, 16, Colors.White, 0.9));
                    Grid cell = new Grid { VerticalAlignment = VerticalAlignment.Center };
                    cell.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(40, 56, 214, 255)) });
                    cell.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(col), HorizontalAlignment = HorizontalAlignment.Left, Width = Math.Max(3, 2.2 * (100 - v.FreePct)) });
                    Grid.SetColumn(cell, 1);
                    row.Children.Add(cell);
                    TextBlock free = Label(Math.Round(v.FreeGb) + " GB LIBERI", 14, Dim, 1);
                    free.HorizontalAlignment = HorizontalAlignment.Right;
                    free.FontFamily = (FontFamily)Resources["Mono"];
                    Grid.SetColumn(free, 2);
                    row.Children.Add(free);
                    disks.Children.Add(row);
                }
            }

            StackPanel issues = Get<StackPanel>("IssuesPanel");
            if (issues == null) { return; }
            issues.Children.Clear();
            foreach (Issue i in h.Issues.Take(4))
            {
                Color col = i.Severity == "crit" ? Red : i.Severity == "warn" ? Gold : Cyan;
                Grid row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new Border { Background = new SolidColorBrush(col), CornerRadius = new CornerRadius(3) });
                StackPanel txt = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
                TextBlock title = Label(i.Title, 17, Colors.White, 0.95);
                txt.Children.Add(title);
                TextBlock detail = Label(i.Detail, 13, Dim, 1);
                detail.TextWrapping = TextWrapping.Wrap;
                detail.MaxHeight = 36;
                txt.Children.Add(detail);
                Grid.SetColumn(txt, 1);
                row.Children.Add(txt);
                Issue issue = i;
                FixDef f = i.FixId != null ? Fixes.Get(i.FixId) : null;
                Button b = new Button { Content = f != null ? "RISOLVI" : "CLAUDE", Style = (Style)root.Resources["HudButtonSmall"], VerticalAlignment = VerticalAlignment.Center };
                b.Click += (s, e) => { if (f != null) { RunFix(issue, f); } else { AskClaude(issue); } };
                Grid.SetColumn(b, 2);
                row.Children.Add(b);
                issues.Children.Add(row);
            }
            if (h.Issues.Count == 0) { issues.Children.Add(Label("Nessun problema rilevato. Tutti i sistemi nominali.", 17, Cyan, 0.9)); }
        }

        void RunFix(Issue issue, FixDef f)
        {
            if (!f.Safe)
            {
                MessageBoxResult ok = MessageBox.Show(f.Title + "\n\n" + f.Description + (f.Admin ? "\n\nServirà l'autorizzazione di amministratore." : "") + "\n\nProcedo?", "Tipaldi AI", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ok != MessageBoxResult.Yes) { return; }
            }
            RunBackground(f.Title + " in corso...", () =>
            {
                Fixes.Execute(f.Id, issue.FixArg, "ui", null);
                agent.RunHealth(false);
            });
        }

        void AskClaude(Issue issue)
        {
            if (ClaudeLink.FindClaudeCli() != null)
            {
                RunBackground("Claude sta analizzando: " + issue.Title + "...", () =>
                {
                    string answer = ClaudeLink.AutoResolve(new List<Issue> { issue });
                    Events.Add("ai", answer != null ? "Claude: " + answer.Replace("\r", " ").Replace("\n", " ") : "Claude non ha risposto. Riprova da Claude Desktop.");
                });
                return;
            }
            string prompt = "Tipaldi, controlla il sistema: " + issue.Title + ". Usa gli strumenti tipaldi-ai e dimmi cosa fare.";
            try { Clipboard.SetText(prompt); } catch (Exception) { }
            try { Process.Start(new ProcessStartInfo("claude:") { UseShellExecute = true }); } catch (Exception) { }
            Events.Add("ai", "Richiesta copiata: incollala in Claude Desktop (Ctrl+V). Claude userà gli strumenti di Tipaldi AI.");
            FillConsole();
        }

        void RunBackground(string message, Action work)
        {
            Events.Add("info", message);
            FillConsole();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { work(); } catch (Exception ex) { Events.Add("warn", ex.Message); }
                Dispatcher.BeginInvoke(new Action(RefreshData));
            });
        }

        public void FullScan()
        {
            RunBackground("Scansione completa avviata: dischi, file system, antivirus, integrità...", () => agent.RunHealth(true));
        }

        public void OpenReport()
        {
            RunBackground("Preparo il report settimanale...", () =>
            {
                WeeklyReport r = WeeklyReport.Build(WeeklyReport.WeekStart(DateTime.Today).AddDays(-7));
                if (r.Stats.Total < 600) { r = WeeklyReport.Build(WeeklyReport.WeekStart(DateTime.Today)); }
                r.Save();
                Process.Start(new ProcessStartInfo(r.HtmlPath) { UseShellExecute = true });
            });
        }

        void OrganizeNow()
        {
            List<MovePlan> plan = Organizer.Plan("downloads", TimeSpan.FromHours(2)).Concat(Organizer.Plan("desktop", TimeSpan.FromHours(24))).ToList();
            if (plan.Count == 0) { Events.Add("info", "Niente da riordinare: Desktop e Download sono già in ordine."); FillConsole(); return; }
            string summary = string.Join("\n", plan.GroupBy(p => p.Context).Select(g => "• " + g.Key + ": " + g.Count() + " file"));
            if (MessageBox.Show("Sistemo " + plan.Count + " file per contesto:\n\n" + summary + "\n\nPuoi annullare con ANNULLA ORDINE.", "Tipaldi AI", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { return; }
            RunBackground("Protocollo ordine avviato...", () =>
            {
                Organizer.Apply("downloads", TimeSpan.FromHours(2));
                Organizer.Apply("desktop", TimeSpan.FromHours(24));
            });
        }

        // ------------------------------------------------------------------ console AI

        void FillConsole()
        {
            StackPanel p = Get<StackPanel>("ConsoleLines");
            if (p == null) { return; }
            List<Dictionary<string, object>> ev = Events.Read(DateTime.Now.AddDays(-7), 6);
            string key = ev.Count > 0 ? Json.Str(ev[ev.Count - 1], "t", "") + Json.Str(ev[ev.Count - 1], "text", "") : "";
            bool fresh = key != lastEventKey && lastEventKey != "" && !demo;
            lastEventKey = key;
            p.Children.Clear();
            for (int i = 0; i < ev.Count; i++)
            {
                string kind = Json.Str(ev[i], "kind", "info");
                string t = Json.Str(ev[i], "t", "");
                string time = t.Length >= 16 ? t.Substring(11, 5) : "";
                Color col = kind == "warn" ? Gold : kind == "ok" ? Cyan : kind == "ai" ? (Color)ColorConverter.ConvertFromString("#9AE6FF") : Dim;
                TextBlock line = new TextBlock { FontFamily = (FontFamily)Resources["Mono"], FontSize = 15, Margin = new Thickness(0, 2, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis };
                line.Inlines.Add(new System.Windows.Documents.Run(time + "  ") { Foreground = new SolidColorBrush(Dim) });
                line.Inlines.Add(new System.Windows.Documents.Run(kind == "ai" ? "AI › " : kind == "ok" ? "OK › " : kind == "warn" ? "!! › " : "   › ") { Foreground = new SolidColorBrush(col) });
                System.Windows.Documents.Run body = new System.Windows.Documents.Run(Json.Str(ev[i], "text", "")) { Foreground = new SolidColorBrush(Color.FromArgb(230, 220, 233, 242)) };
                line.Inlines.Add(body);
                p.Children.Add(line);
                if (i == ev.Count - 1 && fresh)
                {
                    typingTarget = body.Text;
                    body.Text = "";
                    typingBlock = line;
                    typer.Start();
                }
            }
        }

        void TypeStep()
        {
            if (typingBlock == null) { typer.Stop(); return; }
            System.Windows.Documents.Run body = typingBlock.Inlines.LastInline as System.Windows.Documents.Run;
            if (body == null || body.Text.Length >= typingTarget.Length) { typer.Stop(); return; }
            body.Text = typingTarget.Substring(0, Math.Min(typingTarget.Length, body.Text.Length + 2));
        }

        // ------------------------------------------------------------------ dettagli grafici

        void AddBrackets(Border panel)
        {
            if (panel == null) { return; }
            double x = Canvas.GetLeft(panel), y = Canvas.GetTop(panel), w = panel.Width, h = panel.Height, l = 26;
            string[] corners =
            {
                string.Format(CultureInfo.InvariantCulture, "M {0},{1} L {0},{2} L {3},{2}", x - 6, y - 6 + l, y - 6, x - 6 + l),
                string.Format(CultureInfo.InvariantCulture, "M {0},{1} L {2},{1} L {2},{3}", x + w + 6 - l, y - 6, x + w + 6, y - 6 + l),
                string.Format(CultureInfo.InvariantCulture, "M {0},{1} L {0},{2} L {3},{2}", x - 6, y + h + 6 - l, y + h + 6, x - 6 + l),
                string.Format(CultureInfo.InvariantCulture, "M {0},{1} L {2},{1} L {2},{3}", x + w + 6 - l, y + h + 6, x + w + 6, y + h + 6 - l),
            };
            foreach (string c in corners)
            {
                root.Children.Add(new Path { Data = Geometry.Parse(c), Stroke = new SolidColorBrush(Color.FromArgb(200, Gold.R, Gold.G, Gold.B)), StrokeThickness = 2, IsHitTestVisible = false });
            }
        }

        public void Save(string png)
        {
            UpdateLayout();
            root.Measure(new Size(W, H));
            root.Arrange(new Rect(0, 0, W, H));
            RenderTargetBitmap bmp = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(((Grid)Content).Background, null, new Rect(0, 0, W, H));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, W, H));
            }
            bmp.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (System.IO.FileStream fs = System.IO.File.Create(png)) { enc.Save(fs); }
        }
    }
}
#endif
