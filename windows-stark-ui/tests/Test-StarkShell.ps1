<#
    Tipaldi AI - verifica dell'integrazione con la shell reale di Windows (Esplora risorse).

    Studia la gerarchia delle finestre del desktop prima e dopo l'avvio di Tipaldi AI e controlla che il desktop
    futuristico sia davvero integrato: dentro il livello sfondo (WorkerW) dietro alle icone, fuori dalla barra
    delle applicazioni, con i clic sul desktop inoltrati ai suoi pulsanti e il riaggancio dopo un riavvio di
    Esplora risorse. Stampa una cattura reale dello schermo (base64, tra =====BEGIN SHELL===== e =====END SHELL=====).

    Uso:  powershell -ExecutionPolicy Bypass -File tests\Test-StarkShell.ps1 [-Exe agent\bin\TipaldiAI.exe]
#>
param([string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'agent\bin\TipaldiAI.exe'))
$ErrorActionPreference = 'Stop'
$script:Fails = 0
function Check([bool]$Ok, [string]$Name, [string]$Detail = '') {
    if ($Ok) { Write-Host "  ok   $Name" } else { Write-Host "  FAIL $Name $Detail"; $script:Fails++ }
}

Add-Type -AssemblyName System.Drawing, WindowsBase, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ShellProbe
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint p, ref bool v, uint f);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static IntPtr LayerAfterView()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, x) => {
            if (FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero && Cls(h) == "WorkerW") { found = FindWindowEx(IntPtr.Zero, h, "WorkerW", null); return false; }
            return true; }, IntPtr.Zero);
        return found;
    }

    public static void Send052C(int w, int l)
    {
        IntPtr r;
        SendMessageTimeout(FindWindow("Progman", null), 0x052C, new IntPtr(w), new IntPtr(l), 2, 1000, out r);
    }

    public static bool Spi(uint action) { bool v = false; SystemParametersInfo(action, 0, ref v, 0); return v; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static string Cls(IntPtr h) { StringBuilder sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }
    public static string Txt(IntPtr h) { StringBuilder sb = new StringBuilder(128); GetWindowText(h, sb, 128); return sb.ToString(); }
    public static int Pid(IntPtr h) { int p; GetWindowThreadProcessId(h, out p); return p; }
    public static long Style(IntPtr h) { return GetWindowLongPtr(h, -16).ToInt64(); }
    public static long ExStyle(IntPtr h) { return GetWindowLongPtr(h, -20).ToInt64(); }
    public static IntPtr Owner(IntPtr h) { return GetWindow(h, 4); }   // GW_OWNER

    public static List<IntPtr> TopLevel()
    {
        List<IntPtr> l = new List<IntPtr>();
        EnumWindows((h, x) => { l.Add(h); return true; }, IntPtr.Zero);
        return l;
    }

    public static List<IntPtr> Children(IntPtr p)
    {
        List<IntPtr> l = new List<IntPtr>();
        EnumChildWindows(p, (h, x) => { if (GetParent(h) == p) { l.Add(h); } return true; }, IntPtr.Zero);
        return l;
    }

    public static string Describe(IntPtr h)
    {
        RECT r; GetWindowRect(h, out r);
        return string.Format("0x{0:X} {1} \"{2}\" pid={3} {4} [{5},{6} {7}x{8}]", h.ToInt64(), Cls(h), Txt(h), Pid(h),
            IsWindowVisible(h) ? "visibile" : "nascosta", r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    public static IntPtr At(int x, int y) { POINT p; p.X = x; p.Y = y; return WindowFromPoint(p); }

    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(0x2, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x4, 0, 0, 0, IntPtr.Zero);
    }
}
'@

function Show-ShellTree([string]$Title) {
    Write-Host "--- finestre del desktop: $Title"
    $n = 0
    foreach ($h in [ShellProbe]::TopLevel()) {
        $c = [ShellProbe]::Cls($h)
        if ($c -notin 'Progman', 'WorkerW') { continue }
        $n++
        Write-Host ("  [{0}] {1}" -f $n, [ShellProbe]::Describe($h))
        foreach ($k in [ShellProbe]::Children($h)) {
            Write-Host ('        +- ' + [ShellProbe]::Describe($k))
            foreach ($g in [ShellProbe]::Children($k)) { Write-Host ('           +- ' + [ShellProbe]::Describe($g)) }
        }
    }
}

function Wait-Until([scriptblock]$Cond, [int]$Seconds) {
    $end = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $end) { if (& $Cond) { return $true }; Start-Sleep -Milliseconds 500 }
    [bool](& $Cond)
}

function Save-Screen([string]$Label) {
    $b = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object Drawing.Bitmap $b.Width, $b.Height
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $codec = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
    $ep = New-Object Drawing.Imaging.EncoderParameters 1
    $ep.Param[0] = New-Object Drawing.Imaging.EncoderParameter ([Drawing.Imaging.Encoder]::Quality), 80L
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, $codec, $ep)
    Write-Host "=====BEGIN SHELL $Label====="
    Write-Host ([Convert]::ToBase64String($ms.ToArray()))
    Write-Host "=====END SHELL $Label====="
}
Add-Type -AssemblyName System.Windows.Forms

Write-Host "== Shell di Windows: $([Environment]::OSVersion.VersionString)"
# 1) shell attiva (sui runner Esplora risorse non sempre parte da solo)
if (-not (Wait-Until { [ShellProbe]::FindWindow('Progman', [NullString]::Value) -ne [IntPtr]::Zero } 2)) {
    Write-Host 'Esplora risorse non attivo: lo avvio come shell.'
    Start-Process explorer.exe
}
$shellReady = { [ShellProbe]::FindWindow('Progman', [NullString]::Value) -ne [IntPtr]::Zero -and [ShellProbe]::FindWindow('Shell_TrayWnd', [NullString]::Value) -ne [IntPtr]::Zero }
$hasShell = Wait-Until $shellReady 20
if (-not $hasShell) {
    # explorer.exe diventa la shell (desktop + barra) solo se Winlogon lo indica come shell; altrimenti apre una cartella
    $wl = 'Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
    $hklm = (Get-ItemProperty -Path "HKLM:\$wl" -Name Shell -ErrorAction SilentlyContinue).Shell
    $hkcu = (Get-ItemProperty -Path "HKCU:\$wl" -Name Shell -ErrorAction SilentlyContinue).Shell
    Write-Host "Shell di Winlogon: HKLM='$hklm' HKCU='$hkcu'"
    foreach ($h in [ShellProbe]::TopLevel()) { if ([ShellProbe]::Pid($h) -in @(Get-Process explorer -ErrorAction SilentlyContinue).Id -and [ShellProbe]::IsWindowVisible($h)) { Write-Host ('  finestra di explorer: ' + [ShellProbe]::Describe($h)) } }
    if ($env:GITHUB_ACTIONS -and $hklm -notmatch '^explorer(\.exe)?$') {
        Write-Host 'Runner usa e getta: imposto explorer.exe come shell dell''utente e lo riavvio.'
        New-Item -Path "HKCU:\$wl" -Force | Out-Null
        Set-ItemProperty -Path "HKCU:\$wl" -Name Shell -Value 'explorer.exe'
    }
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    Start-Process explorer.exe
    $hasShell = Wait-Until $shellReady 40
}
if (-not $hasShell) {
    # sessione senza desktop interattivo (es. servizio nella sessione 0): il chiamante puo' riprovare nella sessione utente
    Write-Host ("Nessun desktop di Esplora risorse in questa sessione (sessione {0})." -f (Get-Process -Id $PID).SessionId)
    exit 2
}
Check $hasShell 'Desktop e barra delle applicazioni di Esplora risorse presenti'
Start-Sleep -Seconds 3
# le finestre del runner (console dell'agente GitHub) coprono il desktop: ridotte a icona per la prova
foreach ($h in [ShellProbe]::TopLevel()) {
    if ([ShellProbe]::IsWindowVisible($h) -and [ShellProbe]::Cls($h) -in 'ConsoleWindowClass', 'CabinetWClass') { [void][ShellProbe]::ShowWindow($h, 6) }
}
Show-ShellTree 'prima di Tipaldi AI'

# 1b) studio del messaggio 0x052C: quale variante crea il livello sfondo e cosa cambia nel desktop
$adv0 = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
Write-Host ("Effetti visivi: animazioni client {0}, animazioni finestre {1}, animazione menu {2}" -f [ShellProbe]::Spi(0x1042), [ShellProbe]::Spi(0x0048), [ShellProbe]::Spi(0x1002))
function Restart-Shell {
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    [void](Wait-Until { [ShellProbe]::FindWindowEx([ShellProbe]::FindWindow('Progman', [NullString]::Value), [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value) -ne [IntPtr]::Zero } 30)
    Start-Sleep -Seconds 3
    foreach ($h in [ShellProbe]::TopLevel()) { if ([ShellProbe]::IsWindowVisible($h) -and [ShellProbe]::Cls($h) -eq 'CabinetWClass') { [void][ShellProbe]::ShowWindow($h, 6) } }
}
foreach ($v in @(@(0, 0), @(0xD, 0), @(0xD, 1))) {
    Restart-Shell
    $hide0 = (Get-ItemProperty -Path $adv0 -Name HideIcons -ErrorAction SilentlyContinue).HideIcons
    [ShellProbe]::Send052C($v[0], $v[1])
    Start-Sleep -Milliseconds 800
    $layer = [ShellProbe]::LayerAfterView()
    $hide1 = (Get-ItemProperty -Path $adv0 -Name HideIcons -ErrorAction SilentlyContinue).HideIcons
    $lv = [ShellProbe]::FindWindowEx([ShellProbe]::FindWindowEx([ShellProbe]::FindWindow('Progman', [NullString]::Value), [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value), [IntPtr]::Zero, 'SysListView32', [NullString]::Value)
    if ($lv -eq [IntPtr]::Zero) { foreach ($h in [ShellProbe]::TopLevel()) { $dv = [ShellProbe]::FindWindowEx($h, [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value); if ($dv -ne [IntPtr]::Zero) { $lv = [ShellProbe]::FindWindowEx($dv, [IntPtr]::Zero, 'SysListView32', [NullString]::Value); break } } }
    Write-Host ("STUDIO 0x052C wParam=0x{0:X} lParam={1}: livello sfondo {2}, HideIcons {3}->{4}, icone visibili {5}" -f $v[0], $v[1],
        $(if ($layer -ne [IntPtr]::Zero) { 'CREATO ' + [ShellProbe]::Describe($layer) } else { 'non creato' }), $hide0, $hide1, ($lv -ne [IntPtr]::Zero -and [ShellProbe]::IsWindowVisible($lv)))
    Show-ShellTree ("dopo 0x052C(0x{0:X},{1})" -f $v[0], $v[1])
}
Restart-Shell

# 2) Tipaldi AI con dati in una cartella di prova e protocolli automatici spenti
$home2 = Join-Path $env:TEMP 'tipaldi-shell-test'
if (Test-Path $home2) { Remove-Item $home2 -Recurse -Force }
New-Item -ItemType Directory -Path $home2 -Force | Out-Null
$env:TIPALDI_HOME = $home2
$env:TIPALDI_SKILLS = Join-Path $home2 'skills'
@{ trackActivity = $false; autoFixSafe = $false; claudeAutoResolve = $false; overdrive = $false; organizeDownloads = $false; organizeDesktop = $false; weeklyReport = $false; hud = $true } |
    ConvertTo-Json | Set-Content -Path (Join-Path $home2 'settings.json') -Encoding UTF8
$stateFile = Join-Path $home2 'desktop.json'
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$hideBefore = (Get-ItemProperty -Path $adv -Name HideIcons -ErrorAction SilentlyContinue).HideIcons
function Get-IconsShown {
    $v = [ShellProbe]::FindWindowEx([ShellProbe]::FindWindow('Progman', [NullString]::Value), [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value)
    if ($v -eq [IntPtr]::Zero) {
        foreach ($h in [ShellProbe]::TopLevel()) { $x = [ShellProbe]::FindWindowEx($h, [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value); if ($x -ne [IntPtr]::Zero) { $v = $x; break } }
    }
    $lv = [ShellProbe]::FindWindowEx($v, [IntPtr]::Zero, 'SysListView32', [NullString]::Value)
    $lv -ne [IntPtr]::Zero -and [ShellProbe]::IsWindowVisible($lv)
}
$iconsBefore = Get-IconsShown
Write-Host "Icone del desktop visibili prima: $iconsBefore (HideIcons=$hideBefore)"
$agent = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
try {
    $ready = Wait-Until { Test-Path $stateFile } 40
    Check $ready 'Tipaldi AI ha agganciato il desktop (desktop.json scritto)'
    if (-not $ready) { exit 1 }
    Start-Sleep -Seconds 4   # animazione di avvio
    $st = Get-Content $stateFile -Raw | ConvertFrom-Json
    Write-Host ("desktop.json: modalita' {0}, padre {1} (0x{2:X}), inoltro clic {3}" -f $st.mode, $st.parentClass, [long]$st.parent, $st.inputForwarding)
    Show-ShellTree 'con Tipaldi AI'

    $hud = [IntPtr][long]$st.hwnd
    $parent = [IntPtr][long]$st.parent
    Check ($st.mode -eq 'sfondo') "Modalita' sfondo (non finestra)" $st.mode
    Check ($st.parentClass -eq 'WorkerW') 'Padre: livello WorkerW di Esplora risorse' $st.parentClass
    Check ((([ShellProbe]::Style($hud)) -band 0x40000000) -ne 0) 'Finestra figlia (WS_CHILD), non una finestra principale'
    Check ([ShellProbe]::Pid($parent) -eq (Get-Process explorer | Select-Object -First 1).Id -or [ShellProbe]::Cls($parent) -eq 'WorkerW') 'Il livello appartiene a explorer.exe'
    $viewInParent = [ShellProbe]::FindWindowEx($parent, [IntPtr]::Zero, 'SHELLDLL_DefView', [NullString]::Value)
    Check ($viewInParent -eq [IntPtr]::Zero) 'Livello sfondo distinto da quello delle icone (SHELLDLL_DefView)'

    # barra delle applicazioni / Alt+Tab: nessuna finestra principale visibile di Tipaldi AI senza WS_EX_TOOLWINDOW
    $taskbarLike = @([ShellProbe]::TopLevel() | Where-Object {
        [ShellProbe]::Pid($_) -eq $agent.Id -and [ShellProbe]::IsWindowVisible($_) -and
        (([ShellProbe]::ExStyle($_) -band 0x80) -eq 0) -and [ShellProbe]::Owner($_) -eq [IntPtr]::Zero
    })
    foreach ($w in $taskbarLike) { Write-Host ('  finestra in barra: ' + [ShellProbe]::Describe($w)) }
    Check ($taskbarLike.Count -eq 0) 'Nessuna finestra di Tipaldi AI nella barra delle applicazioni o in Alt+Tab'

    # le icone del desktop restano sopra: al centro dello schermo Windows vede il desktop, non Tipaldi AI
    $b = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $top = [ShellProbe]::At([int]($b.Left + $b.Width / 2), [int]($b.Top + $b.Height / 2))
    Write-Host ('  sotto il cursore al centro: ' + [ShellProbe]::Describe($top))
    Check ([ShellProbe]::Cls($top) -in 'SysListView32', 'SHELLDLL_DefView', 'WorkerW', 'Progman') 'Le icone e il menu del desktop di Windows restano sopra al HUD'

    Save-Screen 'con Tipaldi AI'
    $hideAfter = (Get-ItemProperty -Path $adv -Name HideIcons -ErrorAction SilentlyContinue).HideIcons
    Check ($hideAfter -eq $hideBefore) 'Impostazione "Mostra icone del desktop" non toccata' "(HideIcons $hideBefore -> $hideAfter)"
    Check ((Get-IconsShown) -eq $iconsBefore) 'Icone del desktop visibili come prima'

    # 3) clic sul desktop -> pulsante del HUD (scelto dove non c'e' un'icona)
    $events = Join-Path $home2 'events.jsonl'
    $targets = [ordered]@{ BtnSkills = 'Aggiorno le skill di Claude'; BtnUndo = "Annullo l'ultimo riordino"; BtnScan = 'Scansione completa avviata' }
    $clicked = $null
    foreach ($name in $targets.Keys) {
        $pt = $st.buttons.$name
        if (-not $pt) { continue }
        $el = [Windows.Automation.AutomationElement]::FromPoint((New-Object Windows.Point $pt[0], $pt[1]))
        if ($el -and $el.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem) { Write-Host "  $name coperto da un'icona del desktop: provo un altro pulsante"; continue }
        Write-Host ("  clic sul desktop in {0},{1} (sopra {2}, sotto c'e' {3})" -f $pt[0], $pt[1], [ShellProbe]::Cls([ShellProbe]::At($pt[0], $pt[1])), $name)
        [ShellProbe]::Click([int]$pt[0], [int]$pt[1])
        $want = $targets[$name]
        $clicked = Wait-Until { (Test-Path $events) -and ((Get-Content $events -Raw -Encoding UTF8) -match [regex]::Escape($want)) } 10
        Check $clicked "Clic sul desktop inoltrato al pulsante $name"
        break
    }
    if ($null -eq $clicked) { Check $false 'Pulsante del HUD libero da icone per la prova del clic' }

    # 4) Esplora risorse riavviato: il livello sfondo viene ricreato e Tipaldi AI si riaggancia
    Write-Host 'Riavvio di Esplora risorse...'
    $oldParent = $parent
    Get-Process explorer | Stop-Process -Force
    Start-Sleep -Seconds 2
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    $back = Wait-Until {
        try {
            $s2 = Get-Content $stateFile -Raw | ConvertFrom-Json
            $p2 = [IntPtr][long]$s2.parent
            $s2.mode -eq 'sfondo' -and $p2 -ne $oldParent -and [ShellProbe]::IsWindow($p2) -and [ShellProbe]::IsWindow([IntPtr][long]$s2.hwnd)
        } catch { $false }
    } 60
    Check $back 'Riagganciato al nuovo desktop dopo il riavvio di Esplora risorse'
    Check (-not $agent.HasExited) 'Tipaldi AI ancora in esecuzione dopo il riavvio di Esplora risorse'
    Start-Sleep -Seconds 3
    Show-ShellTree 'dopo il riavvio di Esplora risorse'
    Save-Screen 'dopo il riavvio'
}
finally {
    $p = Start-Process -FilePath $Exe -ArgumentList '--stop' -PassThru -Wait
    if (-not $agent.WaitForExit(15000)) { $agent | Stop-Process -Force }
    Check $agent.HasExited 'Tipaldi AI chiuso con --stop'
    $err = Join-Path $home2 'logs\errori.log'
    if (Test-Path $err) { Write-Host '--- errori.log'; Get-Content $err | Write-Host }
    Write-Host '--- console di Tipaldi AI'
    if (Test-Path (Join-Path $home2 'events.jsonl')) { Get-Content (Join-Path $home2 'events.jsonl') -Encoding UTF8 | Select-Object -Last 15 | Write-Host }
}
Write-Host ''
if ($script:Fails) { Write-Host "$($script:Fails) controlli falliti"; exit 1 }
Write-Host 'Integrazione con la shell verificata.'
exit 0
