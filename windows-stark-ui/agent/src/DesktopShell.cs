// Tipaldi AI - integrazione con la shell del desktop di Windows 10.
//
// Come è fatto il desktop (verificato sulla gerarchia reale delle finestre, vedi docs/RICERCA.md):
//   Progman ("Program Manager")            finestra del desktop di explorer.exe
//     SHELLDLL_DefView                     vista del desktop (menu col tasto destro, trascinamento)
//       SysListView32 "FolderView"         le icone
// Il messaggio non documentato 0x052C inviato a Progman fa creare a Esplora risorse un livello "WorkerW"
// dietro alle icone, quello che Windows usa per l'animazione del cambio sfondo:
//   WorkerW  ->  SHELLDLL_DefView  ->  SysListView32      (icone, sopra)
//   WorkerW                                               (livello sfondo: qui va il desktop Tipaldi AI)
//   Progman
// Su Windows 11 24H2 la vista e il livello sfondo sono invece figli di Progman.
//
// Il desktop Tipaldi AI diventa una finestra figlia del livello sfondo: è lo sfondo, non una finestra.
// Non compare nella barra delle applicazioni né in Alt+Tab, resta con Win+D, le icone e il menu del
// desktop di Windows restano sopra e funzionano. Nessuna DLL in explorer.exe: solo messaggi e SetParent.
// I clic sul desktop arrivano a Esplora risorse; quelli che cadono su un pulsante del HUD (e non su
// un'icona) vengono inoltrati al pulsante.
#if !NOWPF
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TipaldiAI
{
    /// <summary>Diario dell'integrazione col desktop (logs\desktop.log): agganci, riavvii di Esplora risorse, clic.</summary>
    public static class DesktopLog
    {
        static readonly object Gate = new object();

        public static void Write(string text)
        {
            lock (Gate)
            {
                try
                {
                    string f = System.IO.Path.Combine(Paths.Logs, "desktop.log");
                    if (System.IO.File.Exists(f) && new System.IO.FileInfo(f).Length > 256 * 1024) { System.IO.File.Delete(f); }
                    System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine);
                }
                catch (Exception) { }
            }
        }
    }

    public static class DesktopShell
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int idx);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT pts, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref RECT r, int flags);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GWL_HWNDPARENT = -8;
        const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L, WS_CLIPSIBLINGS = 0x04000000L, WS_CLIPCHILDREN = 0x02000000L;
        const long WS_EX_TOOLWINDOW = 0x80L, WS_EX_APPWINDOW = 0x40000L, WS_EX_NOACTIVATE = 0x08000000L;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;

        public static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Livello sfondo dietro alle icone (WorkerW), creandolo se serve. IntPtr.Zero se la shell non c'è.</summary>
        static IntPtr askedProgman;

        public static IntPtr FindWallpaperLayer()
        {
            IntPtr progman = FindWindow("Progman", null);
            // senza la vista del desktop (Esplora risorse non è la shell, o non ha ancora creato il desktop)
            // non si manda nulla: il desktop di Windows non va toccato finché non esiste davvero
            if (progman == IntPtr.Zero || FindDesktopView() == IntPtr.Zero) { return IntPtr.Zero; }
            IntPtr layer = LayerAfterView();
            if (layer == IntPtr.Zero && progman != askedProgman)
            {
                // una sola richiesta per ogni avvio di Esplora risorse. Misurato su Windows 10 (build 20348, effetti
                // visivi spenti): solo wParam 0xD / lParam 1 crea il livello; (0,0) e (0xD,0) non fanno nulla.
                // Nessuna delle due cambia "Mostra icone del desktop". (0,0) resta come ripiego per build più vecchie.
                askedProgman = progman;
                IntPtr res;
                SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0x2, 1000, out res);
                layer = LayerAfterView();
                if (layer == IntPtr.Zero)
                {
                    SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x2, 1000, out res);
                    layer = LayerAfterView();
                }
            }
            if (layer == IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                layer = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);   // Windows 11 24H2
            }
            return layer;
        }

        static IntPtr LayerAfterView()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                if (FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero && ClassOf(h) == "WorkerW")
                {
                    found = FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Vista del desktop (SHELLDLL_DefView), ovunque si trovi.</summary>
        public static IntPtr FindDesktopView()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr view = progman != IntPtr.Zero ? FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) : IntPtr.Zero;
            if (view != IntPtr.Zero) { return view; }
            EnumWindows((h, l) =>
            {
                IntPtr v = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (v != IntPtr.Zero && ClassOf(h) == "WorkerW") { view = v; return false; }
                return true;
            }, IntPtr.Zero);
            return view;
        }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        /// <summary>
        /// Larghezza in pixel delle colonne occupate dalle icone vere del desktop (0 se nascoste), così il HUD
        /// non ci finisce sotto. Solo messaggi senza puntatori (nessuna memoria scritta in explorer.exe):
        /// LVM_GETITEMCOUNT e LVM_GETITEMSPACING.
        /// </summary>
        public static int IconColumnsWidth(RECT area)
        {
            IntPtr view = FindDesktopView();
            IntPtr list = view != IntPtr.Zero ? FindWindowEx(view, IntPtr.Zero, "SysListView32", null) : IntPtr.Zero;
            if (list == IntPtr.Zero || !IsWindowVisible(list)) { return 0; }
            int count = SendMessage(list, 0x1004, IntPtr.Zero, IntPtr.Zero).ToInt32();   // LVM_GETITEMCOUNT
            if (count <= 0) { return 0; }
            int spacing = SendMessage(list, 0x1033, IntPtr.Zero, IntPtr.Zero).ToInt32();  // LVM_GETITEMSPACING (icone grandi)
            int cx = spacing & 0xFFFF, cy = (spacing >> 16) & 0xFFFF;
            if (cx <= 0 || cy <= 0) { return 0; }
            int rows = Math.Max(1, (area.Bottom - area.Top) / cy);
            int cols = (count + rows - 1) / rows;
            return Math.Min((area.Right - area.Left) / 3, cols * cx + 8);
        }

        /// <summary>Area di lavoro del monitor principale in pixel fisici (schermo meno barra delle applicazioni).</summary>
        public static RECT WorkArea()
        {
            RECT r = new RECT();
            SystemParametersInfo(0x0030, 0, ref r, 0);   // SPI_GETWORKAREA
            return r;
        }

        /// <summary>Mette la finestra nel livello sfondo, sulla zona indicata (coordinate schermo in pixel).</summary>
        public static bool Embed(IntPtr h, IntPtr layer, RECT screen)
        {
            if (h == IntPtr.Zero || layer == IntPtr.Zero || !IsWindow(layer)) { return false; }
            SetWindowLongPtr64(h, GWL_HWNDPARENT, IntPtr.Zero);
            long style = GetWindowLongPtr64(h, GWL_STYLE).ToInt64();
            SetWindowLongPtr64(h, GWL_STYLE, new IntPtr((style & ~WS_POPUP) | WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN));
            long ex = GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW));
            if (SetParent(h, layer) == IntPtr.Zero && GetParent(h) != layer) { return false; }
            // il livello copre tutti i monitor e parte dall'angolo dello schermo virtuale
            MapWindowPoints(IntPtr.Zero, layer, ref screen, 2);
            SetWindowPos(h, IntPtr.Zero, screen.Left, screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            return GetParent(h) == layer;
        }

        /// <summary>Ripiego senza livello sfondo: finestra di proprietà del desktop, sotto tutte le altre.</summary>
        public static void AttachBelow(IntPtr h)
        {
            if (h == IntPtr.Zero) { return; }
            long style = GetWindowLongPtr64(h, GWL_STYLE).ToInt64();
            if ((style & WS_CHILD) != 0)
            {
                // era nel livello sfondo (es. Esplora risorse chiuso): torna finestra principale
                SetParent(h, IntPtr.Zero);
                SetWindowLongPtr64(h, GWL_STYLE, new IntPtr((style & ~WS_CHILD) | WS_POPUP));
                RECT wa = WorkArea();
                SetWindowPos(h, IntPtr.Zero, wa.Left, wa.Top, wa.Right - wa.Left, wa.Bottom - wa.Top, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero) { SetWindowLongPtr64(h, GWL_HWNDPARENT, progman); }
            long ex = GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
            SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);   // HWND_BOTTOM
        }

        public static bool IsAlive(IntPtr h) { return h != IntPtr.Zero && IsWindow(h); }

        public static bool IsShown(IntPtr h) { return IsAlive(h) && IsWindowVisible(h); }

        public static IntPtr ParentOf(IntPtr h) { return GetParent(h); }

        /// <summary>True se la finestra fa parte del desktop di Esplora risorse (vista, icone, livelli).</summary>
        public static bool IsDesktopSurface(IntPtr h)
        {
            for (int i = 0; i < 4 && h != IntPtr.Zero; i++)
            {
                string c = ClassOf(h);
                if (c == "SHELLDLL_DefView" || c == "Progman" || c == "WorkerW") { return true; }
                if (c != "SysListView32") { return false; }
                h = GetParent(h);
            }
            return false;
        }
    }

    /// <summary>
    /// Inoltra al desktop Tipaldi AI i clic fatti sul desktop di Windows. Un hook del mouse di basso livello
    /// legge soltanto: non blocca né modifica nessun clic, Esplora risorse li riceve sempre tutti.
    /// </summary>
    public sealed class DesktopInput : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

        const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;

        readonly HwndSource host;
        readonly HookProc proc;   // riferimento tenuto vivo: il delegato non deve essere raccolto dal GC
        IntPtr hook;
        POINT down;
        bool pressed;
        ButtonBase hover;
        readonly DispatcherTimer hoverTimer;
        POINT lastMove;
        bool moved;
        int downs;

        public int Presses { get { return downs; } }

        public DesktopInput(HwndSource host)
        {
            this.host = host;
            proc = Callback;
            hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Input, (s, e) => UpdateHover(), host.Dispatcher);
            hoverTimer.Start();
        }

        public bool Active { get { return hook != IntPtr.Zero; } }

        IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            // il più breve possibile: Windows scollega gli hook lenti
            if (code >= 0)
            {
                MSLLHOOKSTRUCT m = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(MSLLHOOKSTRUCT));
                int msg = w.ToInt32();
                if (msg == WM_MOUSEMOVE) { lastMove = m.pt; moved = true; }
                else if (msg == WM_LBUTTONDOWN) { down = m.pt; pressed = true; downs++; }
                else if (msg == WM_LBUTTONUP && pressed)
                {
                    pressed = false;
                    POINT up = m.pt;
                    if (Math.Abs(up.X - down.X) <= 4 && Math.Abs(up.Y - down.Y) <= 4)
                    {
                        host.Dispatcher.BeginInvoke(new Action(() => Click(up)), DispatcherPriority.Input);
                    }
                }
            }
            return CallNextHookEx(hook, code, w, l);
        }

        ButtonBase ButtonAt(POINT p) { string why; return ButtonAt(p, out why); }

        ButtonBase ButtonAt(POINT p, out string why)
        {
            why = "";
            if (host.IsDisposed || host.RootVisual == null) { why = "finestra chiusa"; return null; }
            if (!DesktopShell.IsDesktopSurface(WindowFromPoint(p))) { why = "non sul desktop"; return null; }
            UIElement top = host.RootVisual as UIElement;
            if (top == null) { why = "radice assente"; return null; }
            Point local;
            try { local = top.PointFromScreen(new Point(p.X, p.Y)); } catch (InvalidOperationException ex) { why = ex.Message; return null; }
            HitTestResult hit = VisualTreeHelper.HitTest(top, local);
            DependencyObject d = hit != null ? hit.VisualHit : null;
            why = string.Format("punto HUD {0:0},{1:0}, colpito {2}", local.X, local.Y, d != null ? d.GetType().Name : "niente");
            while (d != null && !(d is ButtonBase)) { d = VisualTreeHelper.GetParent(d); }
            ButtonBase b = d as ButtonBase;
            if (b != null && !(b.IsEnabled && b.IsVisible)) { why += ", pulsante non attivo"; return null; }
            return b;
        }

        static bool OnDesktopIcon(POINT p)
        {
            // le icone vincono sempre: se sotto il cursore c'è un'icona del desktop, il clic è suo
            try
            {
                AutomationElement e = AutomationElement.FromPoint(new Point(p.X, p.Y));
                return e != null && e.Current.ControlType == ControlType.ListItem;
            }
            catch (Exception) { return false; }
        }

        void Click(POINT p)
        {
            IntPtr under = WindowFromPoint(p);
            string why;
            ButtonBase b = ButtonAt(p, out why);
            bool icon = b != null && OnDesktopIcon(p);
            DesktopLog.Write(string.Format("clic {0},{1} su {2} ({3}): pulsante {4}{5}", p.X, p.Y, DesktopShell.ClassOf(under), why,
                b != null ? (b.Name != "" ? b.Name : "senza nome") : "nessuno", icon ? " (coperto da un'icona: lasciato al desktop)" : ""));
            if (b == null || icon) { return; }
            AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement(b);
            IInvokeProvider inv = peer != null ? peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider : null;
            if (inv != null) { inv.Invoke(); }
            else { b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b)); }
        }

        void UpdateHover()
        {
            if (!moved) { return; }
            moved = false;
            ButtonBase b = ButtonAt(lastMove);
            if (b == hover) { return; }
            if (hover != null) { hover.ClearValue(UIElement.OpacityProperty); }
            hover = b;
            if (hover != null) { hover.Opacity = 0.72; }
        }

        public void Dispose()
        {
            hoverTimer.Stop();
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
    }
    /// <summary>
    /// Apre file, cartelle e app tramite Esplora risorse (la vista del desktop, IShellFolderViewDual).
    /// Il clic sul desktop lo riceve Esplora risorse: se è lui ad aprire, la nuova finestra va in primo piano
    /// come per un doppio clic su un'icona. Aperta da Tipaldi AI resterebbe dietro (regole di primo piano).
    /// </summary>
    public static class ShellLaunch
    {
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IShellWindows
        {
            int Count { get; }
            [return: MarshalAs(UnmanagedType.IDispatch)] object Item([MarshalAs(UnmanagedType.Struct)] object index);
            [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
            int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
            int RegisterPending(int threadId, [MarshalAs(UnmanagedType.Struct)] ref object loc, [MarshalAs(UnmanagedType.Struct)] ref object locRoot, int swClass);
            void Revoke(int cookie);
            void OnNavigate(int cookie, [MarshalAs(UnmanagedType.Struct)] ref object loc);
            void OnActivated(int cookie, bool active);
            [return: MarshalAs(UnmanagedType.IDispatch)] object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object loc, [MarshalAs(UnmanagedType.Struct)] ref object locRoot, int swClass, out int hwnd, int options);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid sid, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellBrowser
        {
            void GetWindow(out IntPtr h);
            void ContextSensitiveHelp(bool enter);
            void InsertMenusSB(IntPtr menu, IntPtr widths);
            void SetMenuSB(IntPtr menu, IntPtr shared, IntPtr active);
            void RemoveMenusSB(IntPtr menu);
            void SetStatusTextSB(IntPtr text);
            void EnableModelessSB(bool enable);
            void TranslateAcceleratorSB(IntPtr msg, ushort id);
            void BrowseObject(IntPtr pidl, uint flags);
            void GetViewStateStream(uint mode, out IntPtr stream);
            void GetControlWindow(uint id, out IntPtr h);
            void SendControlMsg(uint id, uint msg, IntPtr w, IntPtr l, out IntPtr ret);
            void QueryActiveShellView(out IShellView view);
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellView
        {
            void GetWindow(out IntPtr h);
            void ContextSensitiveHelp(bool enter);
            void TranslateAccelerator(IntPtr msg);
            void EnableModeless(bool enable);
            void UIActivate(uint state);
            void Refresh();
            void CreateViewWindow(IntPtr previous, IntPtr settings, IntPtr browser, IntPtr rect, out IntPtr h);
            void DestroyViewWindow();
            void GetCurrentInfo(IntPtr settings);
            void AddPropertySheetPages(uint reserved, IntPtr add, IntPtr l);
            void SaveViewState();
            void SelectItem(IntPtr pidl, uint flags);
            [PreserveSig] int GetItemObject(uint item, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        }

        /// <summary>Apre con Esplora risorse; se la shell non risponde, apre direttamente.</summary>
        public static void Open(string file, string args)
        {
            Exception error = null;
            bool done = false;
            System.Threading.Thread t = new System.Threading.Thread(() =>
            {
                try { done = ViaExplorer(file, args ?? ""); } catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(5000) || !done)
            {
                if (error != null) { Events.Add("info", "Apertura diretta (shell non disponibile: " + error.Message + ")."); }
                Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
            }
        }

        static bool ViaExplorer(string file, string args)
        {
            Type t = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));   // ShellWindows
            IShellWindows windows = (IShellWindows)Activator.CreateInstance(t);
            object loc = 0;   // CSIDL_DESKTOP
            object root = null;   // VT_EMPTY
            int hwnd;
            object browserApp = windows.FindWindowSW(ref loc, ref root, 8, out hwnd, 1);   // SWC_DESKTOP, SWFO_NEEDDISPATCH
            if (browserApp == null) { return false; }
            Guid sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");   // SID_STopLevelBrowser
            Guid iidBrowser = typeof(IShellBrowser).GUID;
            object o;
            if (((IServiceProvider)browserApp).QueryService(ref sid, ref iidBrowser, out o) != 0 || o == null) { return false; }
            IShellView view;
            ((IShellBrowser)o).QueryActiveShellView(out view);
            Guid iidDispatch = new Guid("00020400-0000-0000-C000-000000000046");
            object folderView;
            if (view == null || view.GetItemObject(0, ref iidDispatch, out folderView) != 0 || folderView == null) { return false; }   // SVGIO_BACKGROUND
            object shell = folderView.GetType().InvokeMember("Application", System.Reflection.BindingFlags.GetProperty, null, folderView, null);
            string dir = "";
            try { dir = System.IO.Directory.Exists(file) ? file : (System.IO.Path.GetDirectoryName(file) ?? ""); } catch (Exception) { }   // es. "claude:"
            shell.GetType().InvokeMember("ShellExecute", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { file, args, dir, "", 1 });
            return true;
        }
    }
}
#endif
