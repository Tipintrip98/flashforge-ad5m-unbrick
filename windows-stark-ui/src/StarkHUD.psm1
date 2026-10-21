#Requires -Version 5.1
<#
    Stark HUD - motore di installazione / ripristino.

    Principi di sicurezza:
      * Scrive solo in HKCU (utente corrente) e nelle cartelle dell'utente.
      * Nessun servizio, driver, file di sistema o processo di sistema modificato.
      * Ogni valore di registro e ogni file viene salvato PRIMA della modifica
        (%LOCALAPPDATA%\StarkHUD\state) e 'Restore-StarkHUD' riporta tutto com'era.
      * Le uniche operazioni con privilegi di amministratore (punto di ripristino,
        tema del menu contestuale Nilesoft) passano da un prompt UAC esplicito.
#>

$script:PackRoot   = Split-Path -Parent $PSScriptRoot
$script:Version    = '1.0.0'
$script:Product    = 'Stark HUD'
$script:SchemeKey  = 'StarkHUD'
# fuori da Windows (test) si usa la cartella temporanea
$script:InstallRoot = Join-Path $(if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [IO.Path]::GetTempPath() }) 'StarkHUD'
$script:StateDir   = Join-Path $script:InstallRoot 'state'
$script:StateFile  = Join-Path $script:StateDir 'backup.json'
$script:LogDir     = Join-Path $script:InstallRoot 'logs'
$script:LogFile    = $null
$script:Logger     = $null
$script:State      = $null
$script:WingetFlags = $null

# Colori del tema
$script:AccentHex  = '#0096C7'   # colore di sistema Windows (evidenziazioni, Start)
$script:GoldHex    = '#F2B544'

$script:TranslucentPfn = '28017CharlesMilette.TranslucentTB_v826wp6bftszj'
$script:TaskbarXUrl    = 'https://github.com/ChrisAnd1998/TaskbarX/releases/download/1.7.8.0/TaskbarX_1.7.8.0_x64.zip'
$script:TaskbarXSha256 = 'ab192d20ed1ff6a88fef856b1a0dcf997e5022437c09a686c28356b43dc5a3e2'

# Ruoli dei cursori nell'ordine usato da HKCU\Control Panel\Cursors\Schemes
$script:CursorRoles = [ordered]@{
    Arrow = 'arrow.cur'; Help = 'help.cur'; AppStarting = 'working.ani'; Wait = 'busy.ani'
    Crosshair = 'precision.cur'; IBeam = 'text.cur'; NWPen = 'handwriting.cur'; No = 'unavailable.cur'
    SizeNS = 'vert.cur'; SizeWE = 'horz.cur'; SizeNWSE = 'dgn1.cur'; SizeNESW = 'dgn2.cur'
    SizeAll = 'move.cur'; UpArrow = 'alternate.cur'; Hand = 'link.cur'; Pin = 'pin.cur'; Person = 'person.cur'
}

# Eventi sonori: chiave HKCU\AppEvents\Schemes\Apps\<App>\<Evento> -> file
$script:SoundEvents = @(
    @{ App = '.Default'; Event = '.Default';              File = 'default.wav' }
    @{ App = '.Default'; Event = 'Notification.Default';  File = 'notify.wav' }
    @{ App = '.Default'; Event = 'Notification.Reminder'; File = 'reminder.wav' }
    @{ App = '.Default'; Event = 'MailBeep';              File = 'notify.wav' }
    @{ App = '.Default'; Event = 'SystemAsterisk';        File = 'asterisk.wav' }
    @{ App = '.Default'; Event = 'SystemExclamation';     File = 'exclamation.wav' }
    @{ App = '.Default'; Event = 'SystemHand';            File = 'error.wav' }
    @{ App = '.Default'; Event = 'DeviceConnect';         File = 'connect.wav' }
    @{ App = '.Default'; Event = 'DeviceDisconnect';      File = 'disconnect.wav' }
    @{ App = '.Default'; Event = 'WindowsLogon';          File = 'startup.wav' }
    @{ App = '.Default'; Event = 'WindowsLogoff';         File = 'logoff.wav' }
    @{ App = '.Default'; Event = 'WindowsUnlock';         File = 'unlock.wav' }
    @{ App = 'Explorer'; Event = 'EmptyRecycleBin';       File = 'recycle.wav' }
)

$script:Fonts = [ordered]@{
    'Rajdhani-Light.ttf'        = 'Rajdhani Light (TrueType)'
    'Rajdhani-Regular.ttf'      = 'Rajdhani (TrueType)'
    'Rajdhani-Medium.ttf'       = 'Rajdhani Medium (TrueType)'
    'Rajdhani-SemiBold.ttf'     = 'Rajdhani SemiBold (TrueType)'
    'ShareTechMono-Regular.ttf' = 'Share Tech Mono (TrueType)'
}

# =====================================================================================
#  Log e progresso
# =====================================================================================

function Set-StarkLogger {
    <# Registra una callback: { param($Message, $Level, $Percent) } usata dalla GUI. #>
    param([scriptblock]$Callback)
    $script:Logger = $Callback
}

function Write-StarkLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('info', 'ok', 'warn', 'error', 'step')][string]$Level = 'info',
        [int]$Percent = -1
    )
    if (-not $script:LogFile) {
        New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null
        $script:LogFile = Join-Path $script:LogDir ('starkhud-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
    }
    $line = '{0:HH:mm:ss} [{1}] {2}' -f (Get-Date), $Level.ToUpper(), $Message
    Add-Content -Path $script:LogFile -Value $line -Encoding UTF8
    if ($script:Logger) { & $script:Logger $Message $Level $Percent }
    else {
        $color = @{ info = 'Gray'; ok = 'Green'; warn = 'Yellow'; error = 'Red'; step = 'Cyan' }[$Level]
        Write-Host $line -ForegroundColor $color
    }
}

# =====================================================================================
#  Funzioni pure (testabili anche fuori da Windows)
# =====================================================================================

function ConvertFrom-StarkHex {
    param([Parameter(Mandatory)][string]$Hex)
    $h = $Hex.TrimStart('#')
    if ($h.Length -ne 6) { throw "Colore non valido: $Hex" }
    [pscustomobject]@{
        R = [Convert]::ToByte($h.Substring(0, 2), 16)
        G = [Convert]::ToByte($h.Substring(2, 2), 16)
        B = [Convert]::ToByte($h.Substring(4, 2), 16)
    }
}

function ConvertTo-StarkDword {
    <# Converte un uint32 nel valore Int32 che il registro (REG_DWORD) si aspetta. #>
    param([Parameter(Mandatory)][uint32]$Value)
    [BitConverter]::ToInt32([BitConverter]::GetBytes($Value), 0)
}

function Get-StarkArgb {
    param([Parameter(Mandatory)][string]$Hex, [byte]$Alpha = 0xC4)
    $c = ConvertFrom-StarkHex $Hex
    [uint32](([uint32]$Alpha -shl 24) -bor ([uint32]$c.R -shl 16) -bor ([uint32]$c.G -shl 8) -bor [uint32]$c.B)
}

function Get-StarkAbgr {
    param([Parameter(Mandatory)][string]$Hex, [byte]$Alpha = 0xFF)
    $c = ConvertFrom-StarkHex $Hex
    [uint32](([uint32]$Alpha -shl 24) -bor ([uint32]$c.B -shl 16) -bor ([uint32]$c.G -shl 8) -bor [uint32]$c.R)
}

function Get-StarkAccentPalette {
    <#
        Tavolozza accento di Windows 10 (32 byte = 8 colori RGBA):
        3 tinte piu' chiare, il colore, 3 tinte piu' scure, colore complementare (oro Stark).
    #>
    param([Parameter(Mandatory)][string]$Hex, [string]$ComplementHex = $script:GoldHex)
    $c = ConvertFrom-StarkHex $Hex
    $mix = {
        param($v, [double]$t)
        if ($t -ge 0) { [byte][math]::Round($v + (255 - $v) * $t) } else { [byte][math]::Round($v * (1 + $t)) }
    }
    $bytes = New-Object byte[] 32
    $i = 0
    foreach ($t in 0.60, 0.40, 0.20, 0.0, -0.22, -0.42, -0.62) {
        $bytes[$i] = & $mix $c.R $t; $bytes[$i + 1] = & $mix $c.G $t; $bytes[$i + 2] = & $mix $c.B $t; $bytes[$i + 3] = 0
        $i += 4
    }
    $k = ConvertFrom-StarkHex $ComplementHex
    $bytes[28] = $k.R; $bytes[29] = $k.G; $bytes[30] = $k.B; $bytes[31] = 0
    , $bytes
}

function Select-StarkWallpaper {
    <# Sceglie lo sfondo in base al rapporto d'aspetto dello schermo. #>
    param([int]$Width, [int]$Height)
    if ($Height -gt 0 -and ($Width / $Height) -ge 2.1) { 'StarkHUD-Desktop-Ultrawide.jpg' } else { 'StarkHUD-Desktop-4K.jpg' }
}

function New-StarkThemeContent {
    <# Genera il file .theme di Windows con percorsi assoluti. #>
    param(
        [Parameter(Mandatory)][string]$WallpaperPath,
        [string]$CursorDir,
        [string]$SoundDir,
        [string]$AccentHex = $script:AccentHex
    )
    $argb = '0X{0:X8}' -f (Get-StarkArgb -Hex $AccentHex -Alpha 0xC4)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('; Stark HUD - tema generato da StarkUI.ps1 (fan made, non ufficiale)')
    [void]$sb.AppendLine('[Theme]')
    [void]$sb.AppendLine('DisplayName=Stark HUD')
    [void]$sb.AppendLine('ThemeId={7B0A8F12-5C3E-4D7A-9E21-53A7C0DE5A01}')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('[Control Panel\Desktop]')
    [void]$sb.AppendLine("Wallpaper=$WallpaperPath")
    [void]$sb.AppendLine('TileWallpaper=0')
    [void]$sb.AppendLine('WallpaperStyle=10')
    [void]$sb.AppendLine('Pattern=')
    [void]$sb.AppendLine('')
    if ($CursorDir) {
        [void]$sb.AppendLine('[Control Panel\Cursors]')
        foreach ($role in $script:CursorRoles.Keys) {
            [void]$sb.AppendLine(('{0}={1}' -f $role, [IO.Path]::Combine($CursorDir, $script:CursorRoles[$role])))
        }
        [void]$sb.AppendLine(('Link={0}' -f [IO.Path]::Combine($CursorDir, 'link.cur')))
        [void]$sb.AppendLine('DefaultValue=Stark HUD')
        [void]$sb.AppendLine('')
    }
    [void]$sb.AppendLine('[VisualStyles]')
    [void]$sb.AppendLine('Path=%SystemRoot%\resources\themes\Aero\Aero.msstyles')
    [void]$sb.AppendLine('ColorStyle=NormalColor')
    [void]$sb.AppendLine('Size=NormalSize')
    [void]$sb.AppendLine('AutoColorization=0')
    [void]$sb.AppendLine("ColorizationColor=$argb")
    [void]$sb.AppendLine('SystemMode=Dark')
    [void]$sb.AppendLine('AppMode=Dark')
    [void]$sb.AppendLine('VisualStyleVersion=10')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('[boot]')
    [void]$sb.AppendLine('SCRNSAVE.EXE=')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('[MasterThemeSelector]')
    [void]$sb.AppendLine('MTSM=RJSPBS')
    if ($SoundDir) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('[Sounds]')
        [void]$sb.AppendLine('SchemeName=Stark HUD')
        foreach ($ev in $script:SoundEvents) {
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine(('[AppEvents\Schemes\Apps\{0}\{1}]' -f $ev.App, $ev.Event))
            [void]$sb.AppendLine(('DefaultValue={0}' -f [IO.Path]::Combine($SoundDir, $ev.File)))
        }
    }
    $sb.ToString()
}

function Get-StarkRainmeterLayout {
    <#
        Layout Rainmeter in pixel fisici: orologio a sinistra, tre schede in basso.
        Rainmeter non scala le skin con i DPI, quindi la scala viene passata alle skin
        (variabile Scale) e le posizioni vengono calcolate qui.
    #>
    param([int]$ScreenWidth = 1920, [int]$ScreenHeight = 1080, [double]$Scale = 1.0)
    $s = $Scale
    $left = [int][math]::Round($ScreenWidth * 0.07)
    $cardsY = [int][math]::Round($ScreenHeight - (40 + 40 + 176) * $s)
    $clockY = [int][math]::Round($ScreenHeight * 0.27)
    $rows = @(
        @{ Name = 'Clock';   X = $left; Y = $clockY }
        @{ Name = 'Weather'; X = $left; Y = $cardsY }
        @{ Name = 'Stats';   X = [int]($left + (220 + 20) * $s); Y = $cardsY }
        @{ Name = 'Control'; X = [int]($left + (220 + 20 + 320 + 20) * $s); Y = $cardsY }
    )
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("; Stark HUD - layout generato per ${ScreenWidth}x${ScreenHeight}, scala $s")
    foreach ($r in $rows) {
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine("[StarkHUD\$($r.Name)]")
        [void]$sb.AppendLine('Active=1')
        [void]$sb.AppendLine("WindowX=$($r.X)")
        [void]$sb.AppendLine("WindowY=$($r.Y)")
        [void]$sb.AppendLine('ClickThrough=0')
        [void]$sb.AppendLine('Draggable=1')
        [void]$sb.AppendLine('SnapEdges=1')
        [void]$sb.AppendLine('KeepOnScreen=1')
        [void]$sb.AppendLine('AlwaysOnTop=-2')
        [void]$sb.AppendLine('FadeDuration=250')
    }
    $sb.ToString()
}

function Set-StarkJsonScalar {
    <#
        Imposta una chiave scalare di primo livello in un testo JSON senza riserializzarlo
        (preserva formattazione e chiavi che PowerShell 5.1 gestirebbe male).
    #>
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Json,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)]$Value
    )
    if ($Value -is [bool]) { $lit = if ($Value) { 'true' } else { 'false' } }
    elseif ($Value -is [int] -or $Value -is [double]) { $lit = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $Value) }
    else { $lit = '"' + ([string]$Value).Replace('\', '\\').Replace('"', '\"') + '"' }

    $pattern = '("' + [regex]::Escape($Key) + '"\s*:\s*)("(?:[^"\\]|\\.)*"|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null)'
    $rx = New-Object System.Text.RegularExpressions.Regex($pattern)
    if ($rx.IsMatch($Json)) {
        return $rx.Replace($Json, { param($m) $m.Groups[1].Value + $lit }, 1)
    }
    $trim = $Json.Trim()
    if (-not $trim -or $trim -eq '{}') { return "{`r`n  `"$Key`": $lit`r`n}" }
    $idx = $Json.IndexOf('{')
    $Json.Substring(0, $idx + 1) + "`r`n  `"$Key`": $lit," + $Json.Substring($idx + 1)
}

function Get-StarkTweaks {
    param([string]$Path = (Join-Path $script:PackRoot 'config\tweaks.json'))
    $raw = Get-Content -Path $Path -Raw -Encoding UTF8
    (ConvertFrom-Json $raw).tweaks
}

function Get-StarkPreset {
    <# Opzioni predefinite: Default (consigliato), Minimal (solo nativo), Full (tutto). #>
    param([ValidateSet('Default', 'Minimal', 'Full')][string]$Name = 'Default')
    $tweaks = @(Get-StarkTweaks | Where-Object { $_.default } | ForEach-Object { $_.id })
    $o = @{
        Theme = $true; Cursors = $true; Sounds = $true; Fonts = $true; LockScreen = $true; StartupSound = $true
        Rainmeter = $true; Lively = $true; LivelyAudio = $false; FlowLauncher = $true; TranslucentTB = $true
        TaskbarX = $false; Windhawk = $false; Nilesoft = $false
        Tweaks = $tweaks; RestorePoint = $true; RestartExplorer = $true; City = ''; Clock24 = $true
    }
    if ($Name -eq 'Minimal') {
        foreach ($k in 'Rainmeter', 'Lively', 'FlowLauncher', 'TranslucentTB') { $o[$k] = $false }
    }
    if ($Name -eq 'Full') {
        foreach ($k in 'TaskbarX', 'Nilesoft', 'LivelyAudio') { $o[$k] = $true }
    }
    $o
}

# =====================================================================================
#  Ambiente
# =====================================================================================

function Initialize-StarkNative {
    if ('StarkHUD.Native' -as [type]) { return }
    Add-Type -Namespace StarkHUD -Name Native -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
[DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
public static extern int AddFontResource(string lpFileName);
[DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
public static extern bool RemoveFontResource(string lpFileName);
[DllImport("user32.dll", SetLastError = true)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
[DllImport("user32.dll")]
public static extern IntPtr GetDC(IntPtr hWnd);
[DllImport("user32.dll")]
public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
[DllImport("gdi32.dll")]
public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
[DllImport("dwmapi.dll")]
public static extern int DwmGetColorizationColor(out uint color, out bool opaque);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr FindWindowEx(IntPtr hParent, IntPtr hAfter, string lpClassName, string lpWindowName);
[DllImport("user32.dll")]
public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")]
public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
'@
}

function ConvertTo-StarkUInt32 {
    param([Parameter(Mandatory)]$Value)
    [BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$Value), 0)
}

function Get-StarkDwmColor {
    Initialize-StarkNative
    $c = [uint32]0; $opaque = $false
    if ([StarkHUD.Native]::DwmGetColorizationColor([ref]$c, [ref]$opaque) -eq 0) { $c } else { $null }
}

function Get-StarkFileLocker {
    <# Processi che tengono aperto un file, chiesti al Gestione riavvio di Windows (solo lettura). #>
    param([Parameter(Mandatory)][string]$Path)
    try {
        if (-not ('StarkHUD.FileLock' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace StarkHUD {
public static class FileLock {
    [StructLayout(LayoutKind.Sequential)]
    struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RM_PROCESS_INFO {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);
    [DllImport("rstrtmgr.dll")]
    static extern int RmEndSession(uint pSessionHandle);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, IntPtr rgApplications, uint nServices, string[] rgsServiceNames);
    [DllImport("rstrtmgr.dll")]
    static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);
    public static string[] Lockers(string path) {
        List<string> result = new List<string>();
        uint handle;
        if (RmStartSession(out handle, 0, new StringBuilder(64)) != 0) { return result.ToArray(); }
        try {
            if (RmRegisterResources(handle, 1, new string[] { path }, 0, IntPtr.Zero, 0, null) != 0) { return result.ToArray(); }
            uint needed = 0, count = 0, reasons = 0;
            RM_PROCESS_INFO[] info = null;
            int rc = 234;
            for (int attempt = 0; rc == 234 && attempt < 3; attempt++) {
                info = needed > 0 ? new RM_PROCESS_INFO[needed] : null;
                count = needed;
                rc = RmGetList(handle, out needed, ref count, info, ref reasons);
            }
            if (rc == 0 && info != null) {
                for (int i = 0; i < count; i++) {
                    string svc = info[i].strServiceShortName;
                    result.Add(info[i].strAppName + " (pid " + info[i].Process.dwProcessId + (String.IsNullOrEmpty(svc) ? "" : ", servizio " + svc) + ")");
                }
            }
        } finally { RmEndSession(handle); }
        return result.ToArray();
    }
}
}
'@
        }
        @([StarkHUD.FileLock]::Lockers($Path))
    } catch { @() }
}

function Get-StarkDesktopView {
    <# Finestra che contiene le icone del desktop (sotto Progman o sotto un WorkerW). #>
    Initialize-StarkNative
    $view = [StarkHUD.Native]::FindWindowEx([StarkHUD.Native]::FindWindow('Progman', $null), [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
    $worker = [IntPtr]::Zero
    while ($view -eq [IntPtr]::Zero) {
        $worker = [StarkHUD.Native]::FindWindowEx([IntPtr]::Zero, $worker, 'WorkerW', $null)
        if ($worker -eq [IntPtr]::Zero) { break }
        $view = [StarkHUD.Native]::FindWindowEx($worker, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
    }
    $view
}

function Get-StarkDesktopIconsVisible {
    $view = Get-StarkDesktopView
    if ($view -eq [IntPtr]::Zero) { return $null }
    $list = [StarkHUD.Native]::FindWindowEx($view, [IntPtr]::Zero, 'SysListView32', 'FolderView')
    if ($list -eq [IntPtr]::Zero) { return $null }
    [StarkHUD.Native]::IsWindowVisible($list)
}

function Set-StarkDesktopIcons {
    <#
        Mostra/nasconde le icone come il comando "Visualizza > Mostra icone del desktop".
        Esplora risorse salva questo stato per conto suo: scrivere solo HideIcons nel registro non basta.
    #>
    param([Parameter(Mandatory)][bool]$Visible)
    $current = Get-StarkDesktopIconsVisible
    if ($null -eq $current) { return $false }
    if ($current -ne $Visible) {
        [void][StarkHUD.Native]::SendMessage((Get-StarkDesktopView), 0x0111, [IntPtr]0x7402, [IntPtr]::Zero)  # WM_COMMAND
        Start-Sleep -Milliseconds 500
    }
    $true
}

function Send-StarkSettingChange {
    param([string]$Area = 'ImmersiveColorSet')
    Initialize-StarkNative
    $result = [UIntPtr]::Zero
    [void][StarkHUD.Native]::SendMessageTimeout([IntPtr]0xFFFF, 0x001A, [UIntPtr]::Zero, $Area, 0x0002, 3000, [ref]$result)
}

function Send-StarkFontChange {
    Initialize-StarkNative
    $result = [UIntPtr]::Zero
    [void][StarkHUD.Native]::SendMessageTimeout([IntPtr]0xFFFF, 0x001D, [UIntPtr]::Zero, [IntPtr]::Zero, 0x0002, 3000, [ref]$result)  # WM_FONTCHANGE
}

function Get-StarkDisplay {
    <# Risoluzione fisica dello schermo principale e scala DPI. #>
    Initialize-StarkNative
    $dc = [StarkHUD.Native]::GetDC([IntPtr]::Zero)
    try {
        $w = [StarkHUD.Native]::GetDeviceCaps($dc, 118)  # DESKTOPHORZRES
        $h = [StarkHUD.Native]::GetDeviceCaps($dc, 117)  # DESKTOPVERTRES
    } finally { [void][StarkHUD.Native]::ReleaseDC([IntPtr]::Zero, $dc) }
    $dpi = 96
    try {
        $v = (Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name AppliedDPI -ErrorAction Stop).AppliedDPI
        if ($v) { $dpi = [int]$v }
    } catch { }
    if ($w -le 0 -or $h -le 0) { $w = 1920; $h = 1080 }
    [pscustomobject]@{ Width = $w; Height = $h; Scale = [math]::Round($dpi / 96, 2) }
}

function Test-StarkEnvironment {
    $os = [Environment]::OSVersion.Version
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    [pscustomobject]@{
        Build      = $os.Build
        IsWin10    = ($os.Major -eq 10 -and $os.Build -lt 22000)
        IsWin11    = ($os.Major -eq 10 -and $os.Build -ge 22000)
        PSDesktop  = ($PSVersionTable.PSEdition -ne 'Core')
        Is64Bit    = [Environment]::Is64BitProcess
        IsAdmin    = $isAdmin
        HasWinget  = [bool](Get-Command winget.exe -ErrorAction SilentlyContinue)
    }
}

# =====================================================================================
#  Stato e backup
# =====================================================================================

function Get-StarkState {
    if ($script:State) { return $script:State }
    if (Test-Path $script:StateFile) {
        $obj = Get-Content $script:StateFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $script:State = @{
            version     = $obj.version
            created     = $obj.created
            registry    = [System.Collections.ArrayList]@($obj.registry | Where-Object { $_ })
            keysCreated = [System.Collections.ArrayList]@($obj.keysCreated | Where-Object { $_ })
            files       = [System.Collections.ArrayList]@($obj.files | Where-Object { $_ })
            apps        = [System.Collections.ArrayList]@($obj.apps | Where-Object { $_ })
            extra       = @{}
        }
        if ($obj.extra) { foreach ($p in $obj.extra.PSObject.Properties) { $script:State.extra[$p.Name] = $p.Value } }
    } else {
        $script:State = @{
            version = 1; created = (Get-Date).ToString('s')
            registry = New-Object System.Collections.ArrayList
            keysCreated = New-Object System.Collections.ArrayList
            files = New-Object System.Collections.ArrayList
            apps = New-Object System.Collections.ArrayList
            extra = @{}
        }
    }
    $script:State
}

function Save-StarkState {
    $s = Get-StarkState
    New-Item -ItemType Directory -Path $script:StateDir -Force | Out-Null
    $s | ConvertTo-Json -Depth 8 | Set-Content -Path $script:StateFile -Encoding UTF8
}

function Get-StarkHkcuSubKey {
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -notmatch '^HKCU:\\(.+)$') { throw "Stark HUD scrive solo in HKCU: $Path" }
    $Matches[1]
}

function Backup-StarkRegValue {
    <#
        Salva il valore attuale (una sola volta). Con -ObserveOnly il valore serve solo per verificare
        il ripristino: lo riallinea Windows da solo e riscriverlo noi lo impedirebbe.
    #>
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyString()][string]$Name, [switch]$ObserveOnly)
    $s = Get-StarkState
    foreach ($e in $s.registry) { if ($e.path -eq $Path -and $e.name -eq $Name) { return } }
    $entry = [ordered]@{ path = $Path; name = $Name; existed = $false; kind = $null; value = $null; observe = [bool]$ObserveOnly }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey((Get-StarkHkcuSubKey $Path), $false)
    if ($key) {
        try {
            if ($key.GetValueNames() -contains $Name) {
                $kind = $key.GetValueKind($Name)
                $val = $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $entry.existed = $true
                $entry.kind = [string]$kind
                $entry.value = switch ($kind) {
                    'Binary' { [Convert]::ToBase64String([byte[]]$val) }
                    'MultiString' { , @($val) }
                    default { $val }
                }
            }
        } finally { $key.Close() }
    }
    [void]$s.registry.Add([pscustomobject]$entry)
}

function Register-StarkCreatedKey {
    param([Parameter(Mandatory)][string]$Path)
    $s = Get-StarkState
    # risale fino al primo antenato esistente: quello e' il ramo da eliminare al ripristino
    $sub = Get-StarkHkcuSubKey $Path
    $parts = $sub.Split('\')
    $firstMissing = $null
    for ($i = 1; $i -le $parts.Count; $i++) {
        $p = ($parts[0..($i - 1)] -join '\')
        $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($p, $false)
        if ($k) { $k.Close() } else { $firstMissing = "HKCU:\$p"; break }
    }
    if ($firstMissing -and -not ($s.keysCreated -contains $firstMissing)) { [void]$s.keysCreated.Add($firstMissing) }
}

function ConvertTo-StarkRegData {
    <# Converte un valore nel tipo .NET richiesto da RegistryKey.SetValue. La virgola evita che gli array vengano srotolati. #>
    param($Value, [Parameter(Mandatory)][string]$Type)
    switch ($Type) {
        'DWord' { if ([int64]$Value -gt [int32]::MaxValue) { return ConvertTo-StarkDword ([uint32]$Value) } else { return [int32]$Value } }
        'QWord' { return [int64]$Value }
        'Binary' { return , ([byte[]]@($Value)) }
        'MultiString' { return , ([string[]]@($Value)) }
        default { return [string]$Value }
    }
}

function Set-StarkRegValue {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        [Parameter(Mandatory)]$Value,
        [ValidateSet('String', 'ExpandString', 'DWord', 'QWord', 'Binary', 'MultiString')][string]$Type = 'String'
    )
    Backup-StarkRegValue -Path $Path -Name $Name
    Register-StarkCreatedKey -Path $Path
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey((Get-StarkHkcuSubKey $Path))
    try {
        $kind = [Microsoft.Win32.RegistryValueKind]$Type
        $data = ConvertTo-StarkRegData -Value $Value -Type $Type
        $key.SetValue($Name, $data, $kind)
    } finally { $key.Close() }
}

function Backup-StarkFile {
    <# Salva una copia di un file (o registra che non esisteva) prima di sovrascriverlo. #>
    param([Parameter(Mandatory)][string]$Target)
    $s = Get-StarkState
    foreach ($e in $s.files) { if ($e.target -eq $Target) { return } }
    $entry = [ordered]@{ target = $Target; backup = $null; isDir = $false }
    if (Test-Path -LiteralPath $Target) {
        $item = Get-Item -LiteralPath $Target -Force
        $dest = Join-Path $script:StateDir ('files\{0}-{1}' -f ([guid]::NewGuid().ToString('N').Substring(0, 8)), $item.Name)
        New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
        if ($item.PSIsContainer) {
            Copy-Item -LiteralPath $Target -Destination $dest -Recurse -Force
            $entry.isDir = $true
        } else {
            Copy-Item -LiteralPath $Target -Destination $dest -Force
        }
        $entry.backup = $dest
    }
    [void]$s.files.Add([pscustomobject]$entry)
}

function Set-StarkExtra {
    param([Parameter(Mandatory)][string]$Key, $Value, [switch]$IfMissing)
    $s = Get-StarkState
    if ($IfMissing -and $s.extra.ContainsKey($Key)) { return }
    $s.extra[$Key] = $Value
}

function Get-StarkExtra {
    param([Parameter(Mandatory)][string]$Key)
    $s = Get-StarkState
    if ($s.extra.ContainsKey($Key)) { $s.extra[$Key] } else { $null }
}

# =====================================================================================
#  Utility di sistema
# =====================================================================================

function Invoke-StarkElevated {
    <# Esegue uno script con privilegi di amministratore tramite prompt UAC esplicito. #>
    param([Parameter(Mandatory)][string]$Script, [string]$Reason = 'operazione amministrativa')
    $tmp = Join-Path $env:TEMP ('starkhud-elevated-{0}.ps1' -f [guid]::NewGuid().ToString('N'))
    Set-Content -Path $tmp -Value $Script -Encoding UTF8
    try {
        Write-StarkLog "Richiesta autorizzazione amministratore: $Reason" 'info'
        $p = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
            -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$tmp`"") `
            -Verb RunAs -Wait -PassThru -WindowStyle Hidden
        return $p.ExitCode
    } catch {
        Write-StarkLog "Autorizzazione negata o non disponibile ($Reason): operazione saltata." 'warn'
        return -1
    } finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

function New-StarkShortcut {
    param([string]$Path, [string]$Target, [string]$Arguments = '', [string]$WorkingDir = '', [int]$WindowStyle = 1, [string]$Description = '')
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($Path)
    $lnk.TargetPath = $Target
    $lnk.Arguments = $Arguments
    if ($WorkingDir) { $lnk.WorkingDirectory = $WorkingDir }
    $lnk.WindowStyle = $WindowStyle
    $lnk.Description = $Description
    $lnk.Save()
}

function Restart-StarkExplorer {
    Write-StarkLog 'Riavvio di Esplora risorse per applicare barra e colori...' 'info'
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
}

function Find-StarkExe {
    param([string[]]$Candidates)
    foreach ($c in $Candidates) {
        $p = [Environment]::ExpandEnvironmentVariables($c)
        if (Test-Path -LiteralPath $p) { return $p }
    }
    $null
}

function Get-StarkWingetFlags {
    <# --disable-interactivity esiste solo da winget 1.4: sulle versioni vecchie viene omesso. #>
    if ($null -eq $script:WingetFlags) {
        $script:WingetFlags = @()
        $v = (& winget.exe --version 2>$null | Out-String).Trim()
        if ($v -match 'v?(\d+)\.(\d+)' -and ([int]$Matches[1] -gt 1 -or [int]$Matches[2] -ge 4)) { $script:WingetFlags = @('--disable-interactivity') }
    }
    $script:WingetFlags
}

function Join-StarkArguments {
    <# Riga di comando per Start-Process: virgolette solo dove servono. #>
    param([string[]]$Arguments)
    ($Arguments | Where-Object { $_ -ne $null -and $_ -ne '' } | ForEach-Object {
            if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
        }) -join ' '
}

function Invoke-StarkWinget {
    <#
        Esegue winget con un limite di tempo: un installer bloccato non deve mai bloccare Stark HUD.
        Restituisce codice di uscita, testo prodotto e se e' scaduto il tempo.
    #>
    param([Parameter(Mandatory)][string[]]$Arguments, [int]$TimeoutSec = 900)
    $all = @($Arguments) + @(Get-StarkWingetFlags)
    $log = Join-Path $env:TEMP ('starkhud-winget-{0}' -f [guid]::NewGuid().ToString('N'))
    $p = Start-Process -FilePath 'winget.exe' -ArgumentList (Join-StarkArguments $all) -NoNewWindow -PassThru `
        -RedirectStandardOutput "$log.out" -RedirectStandardError "$log.err"
    $null = $p.Handle  # senza questo PowerShell 5.1 perde il codice di uscita
    $timedOut = -not $p.WaitForExit($TimeoutSec * 1000)
    if ($timedOut) { try { $p.Kill() } catch { } }
    $text = ''
    foreach ($f in "$log.out", "$log.err") {
        if (Test-Path -LiteralPath $f) { $text += [IO.File]::ReadAllText($f); Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue }
    }
    [pscustomobject]@{ ExitCode = $(if ($timedOut) { -1 } else { $p.ExitCode }); Output = $text; TimedOut = $timedOut }
}

function Test-StarkWingetPackage {
    param([Parameter(Mandatory)][string]$Id)
    $r = Invoke-StarkWinget -Arguments @('list', '--id', $Id, '-e', '--accept-source-agreements') -TimeoutSec 180
    ($r.ExitCode -eq 0) -and ($r.Output -match [regex]::Escape($Id))
}

# Processi da chiudere prima di aggiornare i file di un'app o di disinstallarla
$script:AppProcesses = @{
    'Rainmeter.Rainmeter'           = @('Rainmeter')
    'rocksdanister.LivelyWallpaper' = @('Lively*')
    'Flow-Launcher.Flow-Launcher'   = @('Flow.Launcher')
    'CharlesMilette.TranslucentTB'  = @('TranslucentTB')
    'RamenSoftware.Windhawk'        = @('windhawk')
}

function Stop-StarkAppProcesses {
    param([Parameter(Mandatory)][string]$Id)
    if (-not $script:AppProcesses.ContainsKey($Id)) { return }
    $procs = @(foreach ($n in $script:AppProcesses[$Id]) { Get-Process -Name $n -ErrorAction SilentlyContinue })
    if ($procs.Count) {
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

function Install-StarkApp {
    <# Installa un'app con winget (solo se manca) e ricorda che l'ha installata Stark HUD. #>
    param([Parameter(Mandatory)][string]$Id, [Parameter(Mandatory)][string]$Name)
    if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) {
        Write-StarkLog "winget non trovato: installa 'Programma di installazione app' dal Microsoft Store, poi riprova ($Name)." 'warn'
        return $false
    }
    if (Test-StarkWingetPackage -Id $Id) {
        Write-StarkLog "$Name e' gia' installato." 'ok'
        return $true
    }
    Write-StarkLog "Installazione $Name (winget $Id)... potrebbe comparire un prompt UAC." 'info'
    $r = Invoke-StarkWinget -Arguments @('install', '--id', $Id, '-e', '--silent', '--accept-package-agreements', '--accept-source-agreements') -TimeoutSec 900
    if ($r.TimedOut) { Write-StarkLog "winget non ha finito entro 15 minuti per ${Name}: installazione interrotta." 'warn' }
    if ($r.ExitCode -eq 0 -or (Test-StarkWingetPackage -Id $Id)) {
        $s = Get-StarkState
        if (-not ($s.apps -contains $Id)) { [void]$s.apps.Add($Id) }
        Save-StarkState
        Write-StarkLog "$Name installato." 'ok'
        return $true
    }
    $tail = (($r.Output -split "`r?`n") | Where-Object { $_.Trim() -and $_ -notmatch '^[\s\-\\|/]+$' } | Select-Object -Last 3) -join ' / '
    Write-StarkLog ("Installazione di $Name non riuscita (codice 0x{0:X8}): {1}" -f $r.ExitCode, $tail) 'error'
    $false
}

# =====================================================================================
#  Moduli del tema
# =====================================================================================

function Copy-StarkAssets {
    $dst = Join-Path $script:InstallRoot 'theme'
    foreach ($d in 'wallpapers', 'cursors', 'sounds', 'fonts') {
        $target = Join-Path $dst $d
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item -Path (Join-Path $script:PackRoot "theme\$d\*") -Destination $target -Recurse -Force
    }
    $dst
}

function Install-StarkFonts {
    param([string]$AssetRoot)
    Initialize-StarkNative
    $fontDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
    New-Item -ItemType Directory -Path $fontDir -Force | Out-Null
    foreach ($file in $script:Fonts.Keys) {
        $src = Join-Path $AssetRoot "fonts\$file"
        $dst = Join-Path $fontDir $file
        Backup-StarkFile -Target $dst
        if (-not (Test-Path -LiteralPath $dst)) { Copy-Item -LiteralPath $src -Destination $dst -Force }
        Set-StarkRegValue -Path 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts' -Name $script:Fonts[$file] -Value $dst -Type String
        [void][StarkHUD.Native]::AddFontResource($dst)
    }
    Send-StarkFontChange
    Write-StarkLog 'Font Rajdhani e Share Tech Mono installati (solo per il tuo utente).' 'ok'
}

function Set-StarkCursors {
    param([string]$CursorDir)
    Initialize-StarkNative
    $base = 'HKCU:\Control Panel\Cursors'
    $paths = foreach ($role in $script:CursorRoles.Keys) {
        $p = Join-Path $CursorDir $script:CursorRoles[$role]
        Set-StarkRegValue -Path $base -Name $role -Value $p -Type ExpandString
        $p
    }
    Set-StarkRegValue -Path $base -Name '' -Value 'Stark HUD' -Type String
    Set-StarkRegValue -Path $base -Name 'Scheme Source' -Value 1 -Type DWord
    Set-StarkRegValue -Path "$base\Schemes" -Name 'Stark HUD' -Value ($paths -join ',') -Type ExpandString
    [void][StarkHUD.Native]::SystemParametersInfo(0x0057, 0, $null, 0x03)  # SPI_SETCURSORS
    Write-StarkLog 'Cursori Stark HUD attivi (17 puntatori, animati per attesa e caricamento).' 'ok'
}

function Set-StarkSounds {
    param([string]$SoundDir)
    $root = 'HKCU:\AppEvents\Schemes'
    Set-StarkRegValue -Path "$root\Names\$script:SchemeKey" -Name '' -Value 'Stark HUD' -Type String
    foreach ($ev in $script:SoundEvents) {
        $file = Join-Path $SoundDir $ev.File
        $evKey = "$root\Apps\$($ev.App)\$($ev.Event)"
        Set-StarkRegValue -Path "$evKey\$script:SchemeKey" -Name '' -Value $file -Type ExpandString
        Set-StarkRegValue -Path "$evKey\.Current" -Name '' -Value $file -Type ExpandString
    }
    Set-StarkRegValue -Path $root -Name '' -Value $script:SchemeKey -Type String
    Write-StarkLog 'Schema suoni Stark HUD attivo (notifiche "vetro", errori morbidi, cestino "dissolvenza").' 'ok'
}

function Set-StarkStartupSound {
    param([string]$SoundDir)
    $startup = [Environment]::GetFolderPath('Startup')
    $lnk = Join-Path $startup 'Stark HUD - suono di avvio.lnk'
    Backup-StarkFile -Target $lnk
    $wav = (Join-Path $SoundDir 'startup.wav').Replace("'", "''")
    $cmd = "-NoProfile -NonInteractive -WindowStyle Hidden -Command `"(New-Object Media.SoundPlayer '$wav').PlaySync()`""
    New-StarkShortcut -Path $lnk -Target "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Arguments $cmd -WindowStyle 7 -Description 'Stark HUD power-up'
    Write-StarkLog 'Suono "power-up" del reattore all''accesso attivato.' 'ok'
}

function Set-StarkWallpaper {
    param([Parameter(Mandatory)][string]$Path)
    Initialize-StarkNative
    foreach ($n in 'Wallpaper', 'WallpaperStyle', 'TileWallpaper') { Backup-StarkRegValue -Path 'HKCU:\Control Panel\Desktop' -Name $n }
    Set-StarkRegValue -Path 'HKCU:\Control Panel\Desktop' -Name 'WallpaperStyle' -Value '10' -Type String
    Set-StarkRegValue -Path 'HKCU:\Control Panel\Desktop' -Name 'TileWallpaper' -Value '0' -Type String
    [void][StarkHUD.Native]::SystemParametersInfo(0x0014, 0, $Path, 0x03)  # SPI_SETDESKWALLPAPER
}

function Save-StarkPreviousWallpaper {
    <# Copia dello sfondo attuale: serve al ripristino se il file originale non esiste piu'. #>
    if (Get-StarkExtra 'previousWallpaper') { return }
    New-Item -ItemType Directory -Path $script:StateDir -Force | Out-Null
    $transcoded = Join-Path $env:APPDATA 'Microsoft\Windows\Themes\TranscodedWallpaper'
    if (Test-Path -LiteralPath $transcoded) {
        $wp = Join-Path $script:StateDir 'previous-wallpaper.jpg'
        Copy-Item -LiteralPath $transcoded -Destination $wp -Force
        Set-StarkExtra 'previousWallpaper' $wp
    }
}

function Install-StarkTheme {
    param([string]$AssetRoot, [hashtable]$Options)
    $display = Get-StarkDisplay
    $wall = Join-Path $AssetRoot ('wallpapers\' + (Select-StarkWallpaper -Width $display.Width -Height $display.Height))
    Save-StarkPreviousWallpaper

    # Il colore di bordi e barre del titolo appartiene a DWM (il compositore grafico): non lo tocchiamo.
    # Windows sorveglia la chiave Accent e lo riallinea da solo (verificato sui runner Windows):
    # ne salviamo i valori solo per controllare il ripristino. Il salvataggio va fatto prima di
    # cambiare l'accento: Windows riallinea DWM in pochi istanti e il backup leggerebbe gia' il ciano.
    $dwm = 'HKCU:\Software\Microsoft\Windows\DWM'
    foreach ($n in 'AccentColor', 'ColorizationColor', 'ColorizationAfterglow') { Backup-StarkRegValue -Path $dwm -Name $n -ObserveOnly }

    # Modalita' scura + trasparenze (valori salvati per il ripristino)
    $pers = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    Set-StarkRegValue -Path $pers -Name 'AppsUseLightTheme' -Value 0 -Type DWord
    Set-StarkRegValue -Path $pers -Name 'SystemUsesLightTheme' -Value 0 -Type DWord
    Set-StarkRegValue -Path $pers -Name 'EnableTransparency' -Value 1 -Type DWord
    Set-StarkRegValue -Path $pers -Name 'ColorPrevalence' -Value 0 -Type DWord
    Set-StarkRegValue -Path 'HKCU:\Control Panel\Desktop' -Name 'AutoColorization' -Value 0 -Type DWord

    # Colore accento ciano + tavolozza (la 8a tinta e' l'oro Stark)
    $accent = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
    Set-StarkRegValue -Path $accent -Name 'AccentPalette' -Value (Get-StarkAccentPalette -Hex $script:AccentHex) -Type Binary
    Set-StarkRegValue -Path $accent -Name 'AccentColorMenu' -Value (Get-StarkAbgr -Hex $script:AccentHex) -Type DWord
    Set-StarkRegValue -Path $accent -Name 'StartColorMenu' -Value (Get-StarkAbgr -Hex '#00546F') -Type DWord
    Set-StarkRegValue -Path $dwm -Name 'ColorPrevalence' -Value 0 -Type DWord

    # File .theme "Stark HUD": compare in Impostazioni > Temi per riselezionarlo in futuro.
    # Non viene applicato con un doppio clic simulato: Windows lo applicherebbe in modo asincrono,
    # anche dopo un eventuale ripristino. Tutto viene applicato qui sotto in modo diretto e sincrono.
    $themeDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Themes'
    New-Item -ItemType Directory -Path $themeDir -Force | Out-Null
    $themePath = Join-Path $themeDir 'StarkHUD.theme'
    Backup-StarkFile -Target $themePath
    $content = New-StarkThemeContent -WallpaperPath $wall `
        -CursorDir $(if ($Options.Cursors) { Join-Path $AssetRoot 'cursors' }) `
        -SoundDir $(if ($Options.Sounds) { Join-Path $AssetRoot 'sounds' })
    Set-Content -Path $themePath -Value $content -Encoding Unicode
    Save-StarkState
    Set-StarkWallpaper -Path $wall
    Send-StarkSettingChange
    Write-StarkLog "Tema scuro, accento ciano reattore e sfondo $([IO.Path]::GetFileName($wall)) applicati." 'ok'
}

function Set-StarkLockScreen {
    param([Parameter(Mandatory)][string]$ImagePath)
    try {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
        $null = [Windows.System.UserProfile.LockScreen,Windows.System.UserProfile,ContentType=WindowsRuntime]
        $methods = [System.WindowsRuntimeSystemExtensions].GetMethods()
        $asTaskOp = $methods | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        $asTaskAct = $methods | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and -not $_.IsGenericMethod } | Select-Object -First 1
        $await = {
            param($op, [Type]$type)
            $t = $asTaskOp.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(-1) | Out-Null; $t.Result
        }
        $awaitAction = { param($a) $t = $asTaskAct.Invoke($null, @($a)); $t.Wait(-1) | Out-Null }

        # Windows spegne Spotlight da solo quando si imposta un'immagine: salva prima i valori originali
        $cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
        Backup-StarkRegValue -Path $cdm -Name 'RotatingLockScreenEnabled'
        Backup-StarkRegValue -Path $cdm -Name 'RotatingLockScreenOverlayEnabled'
        Save-StarkState

        # copia dell'immagine di blocco attuale (una sola volta)
        if (-not (Get-StarkExtra 'previousLockScreen')) {
            try {
                $stream = [Windows.System.UserProfile.LockScreen]::GetImageStream()
                if ($stream) {
                    $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
                    $prev = Join-Path $script:StateDir 'previous-lockscreen.jpg'
                    New-Item -ItemType Directory -Path $script:StateDir -Force | Out-Null
                    $fs = [IO.File]::Create($prev)
                    try { $net.CopyTo($fs) } finally { $fs.Dispose(); $net.Dispose() }
                    Set-StarkExtra 'previousLockScreen' $prev
                }
            } catch { Write-StarkLog 'Immagine di blocco attuale non salvabile (verra'' lasciata quella Stark HUD al ripristino).' 'warn' }
        }
        $file = & $await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
        & $awaitAction ([Windows.System.UserProfile.LockScreen]::SetImageFileAsync($file))
        Write-StarkLog 'Schermata di blocco e di accesso con il reattore Stark HUD.' 'ok'
    } catch {
        Write-StarkLog "Schermata di blocco non impostata: $($_.Exception.Message)" 'warn'
    }
}

# =====================================================================================
#  Componenti (app open source che girano accanto a Windows, senza iniezioni)
# =====================================================================================

function Install-StarkRainmeter {
    param([hashtable]$Options, [string]$AssetRoot)
    if (-not (Install-StarkApp -Id 'Rainmeter.Rainmeter' -Name 'Rainmeter')) { return }
    $exe = Find-StarkExe @('%ProgramFiles%\Rainmeter\Rainmeter.exe', '%ProgramFiles(x86)%\Rainmeter\Rainmeter.exe')
    if (-not $exe) { Write-StarkLog 'Rainmeter.exe non trovato dopo l''installazione.' 'error'; return }

    $skins = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter\Skins\StarkHUD'
    Backup-StarkFile -Target $skins
    Backup-StarkFile -Target (Join-Path $env:APPDATA 'Rainmeter\Rainmeter.ini')
    New-Item -ItemType Directory -Path $skins -Force | Out-Null
    Copy-Item -Path (Join-Path $script:PackRoot 'components\rainmeter\StarkHUD\*') -Destination $skins -Recurse -Force
    $fontDst = Join-Path $skins '@Resources\Fonts'
    New-Item -ItemType Directory -Path $fontDst -Force | Out-Null
    Copy-Item -Path (Join-Path $AssetRoot 'fonts\*.ttf') -Destination $fontDst -Force

    # scala HiDPI, citta' meteo e formato ora
    $display = Get-StarkDisplay
    $varsPath = Join-Path $skins '@Resources\Variables.inc'
    $vars = Get-Content $varsPath -Raw
    $scale = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $display.Scale)
    $city = ($Options.City -replace '[^\p{L}\p{N} \-,]', '').Trim() -replace ' ', '+'
    $timeFmt = if ($Options.Clock24) { '%H:%M' } else { '%I:%M' }
    $vars = $vars -replace '(?m)^Scale=.*$', "Scale=$scale" -replace '(?m)^City=.*$', "City=$city" -replace '(?m)^TimeFormat=.*$', "TimeFormat=$timeFmt"
    if ($city -match '[^\x00-\x7F]') {
        [IO.File]::WriteAllText($varsPath, $vars, [Text.Encoding]::Unicode)  # Rainmeter legge UTF-16 per i caratteri accentati
    } else {
        [IO.File]::WriteAllText($varsPath, $vars, [Text.Encoding]::ASCII)
    }

    $layoutDir = Join-Path $env:APPDATA 'Rainmeter\Layouts\StarkHUD'
    Backup-StarkFile -Target $layoutDir
    New-Item -ItemType Directory -Path $layoutDir -Force | Out-Null
    $layout = Get-StarkRainmeterLayout -ScreenWidth $display.Width -ScreenHeight $display.Height -Scale $display.Scale
    [IO.File]::WriteAllText((Join-Path $layoutDir 'Rainmeter.ini'), $layout, [Text.Encoding]::Unicode)
    Save-StarkState

    if (-not (Get-Process Rainmeter -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $exe
        Start-Sleep -Seconds 5
    }
    Start-Process -FilePath $exe -ArgumentList '!LoadLayout StarkHUD'
    Write-StarkLog "Widget HUD Rainmeter attivi: orologio, meteo, telemetria, control center (scala $scale)." 'ok'
}

function Get-StarkLivelyLibraries {
    $libs = New-Object System.Collections.Generic.List[string]
    $settings = Join-Path $env:LOCALAPPDATA 'Lively Wallpaper\Settings.json'
    $custom = $null
    if (Test-Path -LiteralPath $settings) {
        try { $custom = (Get-Content $settings -Raw -Encoding UTF8 | ConvertFrom-Json).WallpaperDir } catch { }
    }
    if ($custom) { $libs.Add((Join-Path $custom 'wallpapers')) }
    else { $libs.Add((Join-Path $env:LOCALAPPDATA 'Lively Wallpaper\Library\wallpapers')) }
    $store = Join-Path $env:LOCALAPPDATA 'Packages\12030rocksdanister.LivelyWallpaper_97hta09mmv6hy\LocalCache\Local\Lively Wallpaper\Library\wallpapers'
    if (Test-Path -LiteralPath (Split-Path $store)) { $libs.Add($store) }
    $libs
}

function Install-StarkLively {
    param([hashtable]$Options, [string]$AssetRoot)
    if (-not (Install-StarkApp -Id 'rocksdanister.LivelyWallpaper' -Name 'Lively Wallpaper')) { return }
    $exe = Find-StarkExe @('%ProgramFiles%\Lively Wallpaper\Lively.exe', '%LOCALAPPDATA%\Programs\Lively Wallpaper\Lively.exe')
    $src = Join-Path $script:PackRoot 'components\lively\StarkReactor'
    $first = $null
    foreach ($lib in Get-StarkLivelyLibraries) {
        $dst = Join-Path $lib 'StarkReactor'
        Backup-StarkFile -Target $dst
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
        $fonts = Join-Path $dst 'fonts'
        New-Item -ItemType Directory -Path $fonts -Force | Out-Null
        foreach ($f in 'Rajdhani-Light.ttf', 'Rajdhani-Medium.ttf', 'Rajdhani-SemiBold.ttf', 'ShareTechMono-Regular.ttf') {
            Copy-Item -LiteralPath (Join-Path $AssetRoot "fonts\$f") -Destination $fonts -Force
        }
        # Orologio grande nello sfondo solo se non ci pensa gia' Rainmeter
        $propsPath = Join-Path $dst 'LivelyProperties.json'
        $props = Get-Content $propsPath -Raw -Encoding UTF8
        $clock = if ($Options.Rainmeter) { 'false' } else { 'true' }
        $props = [regex]::Replace($props, '("showClock"[^}]*"value"\s*:\s*)(true|false)', "`${1}$clock")
        $props = [regex]::Replace($props, '("clock24"[^}]*"value"\s*:\s*)(true|false)', "`${1}$(if ($Options.Clock24) { 'true' } else { 'false' })")
        [IO.File]::WriteAllText($propsPath, $props, (New-Object Text.UTF8Encoding($false)))
        if ($Options.LivelyAudio) {
            $infoPath = Join-Path $dst 'LivelyInfo.json'
            $info = (Get-Content $infoPath -Raw -Encoding UTF8) -replace '"Arguments":\s*"', '"Arguments": "--audio '
            [IO.File]::WriteAllText($infoPath, $info, (New-Object Text.UTF8Encoding($false)))
        }
        if (-not $first) { $first = $dst }
    }
    Save-StarkState
    if (-not $exe) { Write-StarkLog 'Lively.exe non trovato: apri Lively e scegli "Stark HUD - Arc Reactor" dalla libreria.' 'warn'; return }
    if (-not (Get-Process Lively -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $exe
        Start-Sleep -Seconds 8
    }
    Start-Process -FilePath $exe -ArgumentList (Join-StarkArguments @('setwp', '--file', $first))
    Write-StarkLog 'Sfondo animato "Arc Reactor" attivo (anelli su GPU, pausa automatica con app a schermo intero).' 'ok'
}

function Install-StarkFlowLauncher {
    param([hashtable]$Options)
    if (-not (Install-StarkApp -Id 'Flow-Launcher.Flow-Launcher' -Name 'Flow Launcher')) { return }
    $root = Join-Path $env:APPDATA 'FlowLauncher'
    # Flow tiene aperti tema e impostazioni (e le riscrive in chiusura): va fermato prima di toccarli
    Stop-StarkAppProcesses -Id 'Flow-Launcher.Flow-Launcher'
    $themeDst = Join-Path $root 'Themes\StarkHUD.xaml'
    Backup-StarkFile -Target $themeDst
    New-Item -ItemType Directory -Path (Split-Path $themeDst) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $script:PackRoot 'components\flowlauncher\StarkHUD.xaml') -Destination $themeDst -Force

    $settings = Join-Path $root 'Settings\Settings.json'
    Backup-StarkFile -Target $settings
    New-Item -ItemType Directory -Path (Split-Path $settings) -Force | Out-Null
    $json = if (Test-Path -LiteralPath $settings) { Get-Content $settings -Raw -Encoding UTF8 } else { '{}' }
    $json = Set-StarkJsonScalar -Json $json -Key 'Theme' -Value 'StarkHUD'
    $json = Set-StarkJsonScalar -Json $json -Key 'QueryBoxFont' -Value 'Rajdhani'
    $json = Set-StarkJsonScalar -Json $json -Key 'QueryBoxFontWeight' -Value 'Medium'
    $json = Set-StarkJsonScalar -Json $json -Key 'QueryBoxFontSize' -Value 22
    $json = Set-StarkJsonScalar -Json $json -Key 'WindowSize' -Value 660
    $json = Set-StarkJsonScalar -Json $json -Key 'UseClock' -Value $true
    $json = Set-StarkJsonScalar -Json $json -Key 'UseDate' -Value $true
    $json = Set-StarkJsonScalar -Json $json -Key 'UseAnimation' -Value $true
    [IO.File]::WriteAllText($settings, $json, (New-Object Text.UTF8Encoding($false)))
    Save-StarkState

    $exe = Find-StarkExe @('%LOCALAPPDATA%\FlowLauncher\Flow.Launcher.exe')
    if ($exe) { Start-Process -FilePath $exe }
    Write-StarkLog 'Ricerca stile Spotlight pronta: premi Alt+Spazio (Flow Launcher, tema Stark HUD).' 'ok'
}

function Install-StarkTranslucentTB {
    if (-not (Install-StarkApp -Id 'CharlesMilette.TranslucentTB' -Name 'TranslucentTB')) { return }
    $pkgRoot = Join-Path $env:LOCALAPPDATA "Packages\$script:TranslucentPfn"
    $cfgDir = Join-Path $pkgRoot 'RoamingState'
    $pkg = Get-AppxPackage -Name '*CharlesMilette.TranslucentTB*' -ErrorAction SilentlyContinue | Select-Object -First 1
    $appId = $null
    if ($pkg) {
        try { $appId = @((Get-AppxPackageManifest $pkg).Package.Applications.Application)[0].Id } catch { }
    }
    if (-not (Test-Path -LiteralPath $pkgRoot) -and $pkg -and $appId) {
        Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!$appId"
        Start-Sleep -Seconds 4
    }
    New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
    $cfg = Join-Path $cfgDir 'settings.json'
    Backup-StarkFile -Target $cfg
    $text = Get-Content (Join-Path $script:PackRoot 'components\translucenttb\settings.json') -Raw -Encoding UTF8
    [IO.File]::WriteAllText($cfg, $text, (New-Object Text.UTF8Encoding($true)))
    Save-StarkState
    if ($pkg -and $appId -and -not (Get-Process TranslucentTB -ErrorAction SilentlyContinue)) {
        Start-Process "shell:AppsFolder\$($pkg.PackageFamilyName)!$appId"
    }
    Write-StarkLog 'Barra delle applicazioni trasparente sul desktop, acrilico grafite con le finestre aperte.' 'ok'
}

function Install-StarkTaskbarX {
    $dir = Join-Path $script:InstallRoot 'apps\TaskbarX'
    $exe = Join-Path $dir 'TaskbarX.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        $zip = Join-Path $env:TEMP 'StarkHUD-TaskbarX.zip'
        Write-StarkLog 'Download TaskbarX 1.7.8 (icone centrate stile dock)...' 'info'
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $script:TaskbarXUrl -OutFile $zip -UseBasicParsing
        } catch { Write-StarkLog "Download TaskbarX non riuscito: $($_.Exception.Message)" 'error'; return }
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $script:TaskbarXSha256) {
            Remove-Item -LiteralPath $zip -Force
            Write-StarkLog "TaskbarX: hash SHA256 inatteso ($hash). File scartato per sicurezza." 'error'
            return
        }
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Expand-Archive -LiteralPath $zip -DestinationPath $dir -Force
        Remove-Item -LiteralPath $zip -Force
    }
    $tbxArgs = '-tbs=0 -as=cubiceaseinout -asp=300 -lr=400'
    $lnk = Join-Path ([Environment]::GetFolderPath('Startup')) 'Stark HUD - TaskbarX.lnk'
    Backup-StarkFile -Target $lnk
    New-StarkShortcut -Path $lnk -Target $exe -Arguments $tbxArgs -WorkingDir $dir -Description 'Stark HUD: icone centrate'
    Set-StarkExtra 'taskbarxDir' $dir
    Save-StarkState
    Start-Process -FilePath $exe -ArgumentList $tbxArgs -WorkingDirectory $dir
    Write-StarkLog 'Icone della barra centrate con animazione fluida (TaskbarX, nessuna iniezione in explorer).' 'ok'
}

function Install-StarkWindhawk {
    if (-not (Install-StarkApp -Id 'RamenSoftware.Windhawk' -Name 'Windhawk')) { return }
    Write-StarkLog 'Windhawk installato. Mod consigliate per Windows 10 (installale dall''app): taskbar-clock-customization, dark-menus, custom-menu-height. Vedi docs\RICERCA.md.' 'ok'
}

function Install-StarkNilesoft {
    if (-not (Install-StarkApp -Id 'Nilesoft.Shell' -Name 'Nilesoft Shell')) { return }
    $nssDir = Join-Path $env:ProgramFiles 'Nilesoft Shell'
    if (-not (Test-Path -LiteralPath $nssDir)) { Write-StarkLog 'Cartella di Nilesoft Shell non trovata.' 'error'; return }
    $src = Join-Path $script:PackRoot 'components\nilesoft\theme.nss'
    $backup = Join-Path $script:StateDir 'nilesoft-theme.nss.bak'
    New-Item -ItemType Directory -Path $script:StateDir -Force | Out-Null
    $elevated = @"
`$ErrorActionPreference = 'Stop'
`$target = Join-Path '$nssDir' 'imports\theme.nss'
if ((Test-Path -LiteralPath `$target) -and -not (Test-Path -LiteralPath '$backup')) { Copy-Item -LiteralPath `$target -Destination '$backup' -Force }
Copy-Item -LiteralPath '$src' -Destination `$target -Force
(Get-Item -LiteralPath (Join-Path '$nssDir' 'shell.nss')).LastWriteTime = Get-Date
"@
    if ((Invoke-StarkElevated -Script $elevated -Reason 'tema del menu contestuale') -eq 0) {
        Set-StarkExtra 'nilesoftBackup' $backup
        Save-StarkState
        Write-StarkLog 'Menu contestuale in stile Stark HUD (Nilesoft Shell, acrilico scuro).' 'ok'
    }
}

# =====================================================================================
#  Ottimizzazioni
# =====================================================================================

function Invoke-StarkTweaks {
    param([string[]]$Ids)
    $all = Get-StarkTweaks
    foreach ($t in $all) {
        if ($Ids -notcontains $t.id) { continue }
        $type = if ($t.PSObject.Properties['type']) { $t.type } else { 'registry' }
        try {
            switch ($type) {
                'registry' {
                    foreach ($r in $t.registry) { Set-StarkRegValue -Path $r.path -Name $r.name -Value $r.value -Type $r.type }
                }
                'power' {
                    $current = (& powercfg.exe /getactivescheme) -join ' '
                    if ($current -match '([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})') {
                        Set-StarkExtra 'powerScheme' $Matches[1] -IfMissing
                    }
                    & powercfg.exe /setactive $t.scheme 2>$null
                    if ($LASTEXITCODE -ne 0) { throw 'piano non disponibile su questo PC (comune sui portatili Modern Standby)' }
                }
                'desktop-icons' {
                    $visible = Get-StarkDesktopIconsVisible
                    if ($null -eq $visible) { throw 'desktop non trovato (Esplora risorse non e'' in esecuzione)' }
                    Set-StarkExtra 'desktopIconsVisible' $visible -IfMissing
                    [void](Set-StarkDesktopIcons -Visible (-not [bool]$t.hide))
                }
                'cleanup' {
                    $limit = (Get-Date).AddDays(-[int]$t.olderThanDays)
                    $files = Get-ChildItem -LiteralPath $env:TEMP -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt $limit }
                    $freed = 0L
                    foreach ($f in $files) {
                        try { $len = $f.Length; Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop; $freed += $len } catch { }
                    }
                    Write-StarkLog ('File temporanei: liberati {0:N0} MB.' -f ($freed / 1MB)) 'info'
                }
            }
            Write-StarkLog "Ottimizzazione: $($t.title)" 'ok'
        } catch {
            Write-StarkLog "Ottimizzazione '$($t.title)' saltata: $($_.Exception.Message)" 'warn'
        }
        Save-StarkState
    }
}

function New-StarkRestorePoint {
    $elevated = @'
try {
    Checkpoint-Computer -Description 'Stark HUD - prima dell''installazione' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
    exit 0
} catch { exit 2 }
'@
    $code = Invoke-StarkElevated -Script $elevated -Reason 'punto di ripristino di sistema'
    switch ($code) {
        0 { Write-StarkLog 'Punto di ripristino di sistema creato.' 'ok' }
        2 { Write-StarkLog 'Punto di ripristino non creato (Protezione sistema disattivata o gia'' creato nelle ultime 24 ore). Il backup Stark HUD resta comunque attivo.' 'warn' }
        default { }
    }
}

# =====================================================================================
#  Installazione / ripristino
# =====================================================================================

function Install-StarkHUD {
    [CmdletBinding()]
    param([hashtable]$Options = (Get-StarkPreset 'Default'))
    $env0 = Test-StarkEnvironment
    Write-StarkLog "Stark HUD $script:Version - avvio installazione" 'step' 0
    if (-not $env0.PSDesktop) { throw 'Serve Windows PowerShell 5.1 (avvia Install.cmd).' }
    if ($env0.IsWin11) { Write-StarkLog 'Windows 11 rilevato: il pack e'' pensato per Windows 10, alcune parti potrebbero differire.' 'warn' }
    elseif (-not $env0.IsWin10) { throw 'Sistema operativo non supportato (serve Windows 10).' }

    $null = Get-StarkState
    Save-StarkState
    $steps = New-Object System.Collections.Generic.List[object]
    if ($Options.RestorePoint) { $steps.Add(@{ T = 'Punto di ripristino'; A = { New-StarkRestorePoint } }) }
    $steps.Add(@{ T = 'Copia risorse'; A = { $script:AssetRoot = Copy-StarkAssets } })
    if ($Options.Fonts) { $steps.Add(@{ T = 'Font'; A = { Install-StarkFonts -AssetRoot $script:AssetRoot } }) }
    if ($Options.Theme) { $steps.Add(@{ T = 'Tema, colori e sfondo'; A = { Install-StarkTheme -AssetRoot $script:AssetRoot -Options $Options } }) }
    if ($Options.Cursors) { $steps.Add(@{ T = 'Cursori'; A = { Set-StarkCursors -CursorDir (Join-Path $script:AssetRoot 'cursors') } }) }
    if ($Options.Sounds) { $steps.Add(@{ T = 'Suoni'; A = { Set-StarkSounds -SoundDir (Join-Path $script:AssetRoot 'sounds') } }) }
    if ($Options.StartupSound) { $steps.Add(@{ T = 'Suono di avvio'; A = { Set-StarkStartupSound -SoundDir (Join-Path $script:AssetRoot 'sounds') } }) }
    if ($Options.LockScreen) { $steps.Add(@{ T = 'Schermata di blocco'; A = { Set-StarkLockScreen -ImagePath (Join-Path $script:AssetRoot 'wallpapers\StarkHUD-Lock-4K.jpg') } }) }
    if ($Options.Tweaks -and @($Options.Tweaks).Count) { $steps.Add(@{ T = 'Ottimizzazioni'; A = { Invoke-StarkTweaks -Ids @($Options.Tweaks) } }) }
    if ($Options.TranslucentTB) { $steps.Add(@{ T = 'Barra trasparente'; A = { Install-StarkTranslucentTB } }) }
    if ($Options.TaskbarX) { $steps.Add(@{ T = 'Icone centrate'; A = { Install-StarkTaskbarX } }) }
    if ($Options.Rainmeter) { $steps.Add(@{ T = 'Widget HUD'; A = { Install-StarkRainmeter -Options $Options -AssetRoot $script:AssetRoot } }) }
    if ($Options.Lively) { $steps.Add(@{ T = 'Sfondo animato'; A = { Install-StarkLively -Options $Options -AssetRoot $script:AssetRoot } }) }
    if ($Options.FlowLauncher) { $steps.Add(@{ T = 'Ricerca Spotlight'; A = { Install-StarkFlowLauncher -Options $Options } }) }
    if ($Options.Windhawk) { $steps.Add(@{ T = 'Windhawk'; A = { Install-StarkWindhawk } }) }
    if ($Options.Nilesoft) { $steps.Add(@{ T = 'Menu contestuale'; A = { Install-StarkNilesoft } }) }
    if ($Options.RestartExplorer) { $steps.Add(@{ T = 'Applicazione finale'; A = { Restart-StarkExplorer } }) }

    $errors = 0
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $pct = [int](($i / [math]::Max(1, $steps.Count)) * 100)
        Write-StarkLog "[$($i + 1)/$($steps.Count)] $($steps[$i].T)" 'step' $pct
        try { & $steps[$i].A }
        catch {
            $errors++
            Write-StarkLog "$($steps[$i].T): $($_.Exception.Message)" 'error'
        }
        Save-StarkState
    }
    Set-StarkExtra 'installedVersion' $script:Version
    Set-StarkExtra 'installedAt' (Get-Date).ToString('s')
    Save-StarkState
    if ($errors) { Write-StarkLog "Installazione completata con $errors avvisi. Dettagli nel log: $script:LogFile" 'warn' 100 }
    else { Write-StarkLog 'Installazione completata. Benvenuto a bordo.' 'ok' 100 }
}

function Restore-StarkRegistryValues {
    <#
        Riporta ogni valore salvato al suo stato originale (in ordine inverso).
        Con -OnlyChanged tocca solo i valori diversi dal backup e restituisce quanti ne ha corretti:
        serve come verifica finale, perche' Esplora risorse quando si chiude "normalmente"
        (ad esempio durante una disinstallazione) riscrive impostazioni che teneva in memoria.
    #>
    param([Parameter(Mandatory)]$State, [switch]$OnlyChanged)
    $fixed = 0
    for ($i = $State.registry.Count - 1; $i -ge 0; $i--) {
        $e = $State.registry[$i]
        if ($e.PSObject.Properties['observe'] -and $e.observe) { continue }
        try {
            $sub = Get-StarkHkcuSubKey $e.path
            if ($e.existed) {
                $type = if ($e.kind -in 'String', 'ExpandString', 'DWord', 'QWord', 'Binary', 'MultiString') { $e.kind } else { 'String' }
                $raw = if ($e.kind -eq 'Binary') { , [Convert]::FromBase64String($e.value) } else { $e.value }
                $data = ConvertTo-StarkRegData -Value $raw -Type $type
                $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($sub)
                try {
                    if ($OnlyChanged) {
                        $cur = $key.GetValue($e.name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                        $same = ($null -ne $cur) -and ((@($cur) -join '|') -ceq (@($data) -join '|'))
                        if ($same) { continue }
                    }
                    $key.SetValue($e.name, $data, [Microsoft.Win32.RegistryValueKind]$e.kind)
                    $fixed++
                } finally { $key.Close() }
            } else {
                $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub, $true)
                if ($key) {
                    try {
                        if ($key.GetValueNames() -contains $e.name) { $key.DeleteValue($e.name, $false); $fixed++ }
                    } finally { $key.Close() }
                }
            }
        } catch { Write-StarkLog "Registro: $($e.path)\$($e.name) non ripristinato ($($_.Exception.Message))" 'warn' }
    }
    $fixed
}

function Remove-StarkFontFile {
    <# Elimina un font installato da Stark HUD e rimette l'eventuale copia originale. Falso se resta bloccato. #>
    param([Parameter(Mandatory)]$Entry, [Parameter(Mandatory)][datetime]$Until)
    while (Test-Path -LiteralPath $Entry.target) {
        try { Remove-Item -LiteralPath $Entry.target -Force -ErrorAction Stop }
        catch {
            if ((Get-Date) -ge $Until) { return $false }
            [void][StarkHUD.Native]::RemoveFontResource($Entry.target); Send-StarkFontChange; Start-Sleep -Milliseconds 500
        }
    }
    if ($Entry.backup -and (Test-Path -LiteralPath $Entry.backup)) { Copy-Item -LiteralPath $Entry.backup -Destination $Entry.target -Force }
    $true
}

function Wait-StarkDwmSync {
    <# Attende che il colore delle finestre (DWM) torni a quello salvato nel backup. #>
    param([Parameter(Mandatory)]$State, [int]$TimeoutSec = 45)
    $entry = $State.registry | Where-Object { $_.path -eq 'HKCU:\Software\Microsoft\Windows\DWM' -and $_.name -eq 'ColorizationColor' } | Select-Object -First 1
    if (-not $entry -or -not $entry.existed) { return $true }
    $want = (ConvertTo-StarkUInt32 $entry.value) -band 0x00FFFFFF
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    do {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\DWM')
        $cur = if ($key) { try { $key.GetValue('ColorizationColor') } finally { $key.Close() } } else { $null }
        if ($null -ne $cur -and ((ConvertTo-StarkUInt32 $cur) -band 0x00FFFFFF) -eq $want) { return $true }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    $false
}

function Restore-StarkHUD {
    <# Riporta Windows esattamente com'era prima di Stark HUD. #>
    [CmdletBinding()]
    param([switch]$RemoveApps)
    if (-not (Test-Path $script:StateFile)) {
        Write-StarkLog 'Nessun backup Stark HUD trovato: niente da ripristinare.' 'warn' 100
        return
    }
    $s = Get-StarkState
    Initialize-StarkNative
    Write-StarkLog 'Ripristino della configurazione originale...' 'step' 0

    # 1) componenti in esecuzione
    $tbx = Get-StarkExtra 'taskbarxDir'
    if ($tbx -and (Test-Path -LiteralPath (Join-Path $tbx 'TaskbarX.exe'))) {
        try {
            $stop = Start-Process -FilePath (Join-Path $tbx 'TaskbarX.exe') -ArgumentList '-stop' -PassThru -ErrorAction Stop
            if (-not $stop.WaitForExit(15000)) { $stop | Stop-Process -Force -ErrorAction SilentlyContinue }
        } catch { }
        Get-Process TaskbarX -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    foreach ($p in 'Rainmeter', 'Flow.Launcher') { Get-Process $p -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
    $lively = Find-StarkExe @('%ProgramFiles%\Lively Wallpaper\Lively.exe', '%LOCALAPPDATA%\Programs\Lively Wallpaper\Lively.exe')
    if ($lively -and (Get-Process Lively -ErrorAction SilentlyContinue)) { Start-Process -FilePath $lively -ArgumentList 'closewp --monitor -1' }
    Start-Sleep -Seconds 1

    # 2) schermata di blocco prima del registro: l'API di Windows riscrive i valori di Spotlight.
    #    Se prima era attivo Spotlight basta ripristinare il registro (Spotlight riparte da solo).
    $spot = $s.registry | Where-Object { $_.name -eq 'RotatingLockScreenEnabled' } | Select-Object -First 1
    $spotlightWasOn = $spot -and $spot.existed -and [int]$spot.value -eq 1
    $lock = Get-StarkExtra 'previousLockScreen'
    if (-not $spotlightWasOn -and $lock -and (Test-Path -LiteralPath $lock)) {
        $keepLock = Join-Path $env:APPDATA 'Microsoft\Windows\Themes\StarkHUD-previous-lockscreen.jpg'
        Copy-Item -LiteralPath $lock -Destination $keepLock -Force
        Set-StarkLockScreen -ImagePath $keepLock
    }

    # 3) registro: valori originali in ordine inverso, poi rimozione delle chiavi create
    Write-StarkLog 'Ripristino dei valori di registro originali...' 'info' 45
    [void](Restore-StarkRegistryValues -State $s)
    for ($i = $s.keysCreated.Count - 1; $i -ge 0; $i--) {
        try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree((Get-StarkHkcuSubKey $s.keysCreated[$i]), $false) } catch { }
    }
    # Windows riallinea il colore delle finestre all'accento originale solo con questo avviso
    # e solo se vede ancora il colore Stark in DWM: per questo i valori DWM non vengono riscritti.
    # La breve pausa lascia a Windows il tempo di rileggere la chiave Accent appena ripristinata.
    Start-Sleep -Seconds 2
    Send-StarkSettingChange 'ImmersiveColorSet'

    # 4) font: scarica dalla sessione prima di cancellare i file
    $fontDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
    foreach ($file in $script:Fonts.Keys) {
        $fp = Join-Path $fontDir $file
        # AddFontResource conta i caricamenti (es. due installazioni): va rimosso finche' Windows lo trattiene
        for ($n = 0; $n -lt 16; $n++) { if (-not [StarkHUD.Native]::RemoveFontResource($fp)) { break } }
    }
    Send-StarkFontChange

    # 5) file: ripristina le copie, elimina quelli creati da Stark HUD
    Write-StarkLog 'Ripristino dei file...' 'info' 60
    # un font puo' restare aperto per qualche secondo (cache dei font, Esplora risorse):
    # quelli bloccati si riprovano dopo il riavvio di Esplora risorse, piu' sotto
    $pendingFonts = New-Object System.Collections.Generic.List[object]
    $fontDeadline = (Get-Date).AddSeconds(3)
    for ($i = $s.files.Count - 1; $i -ge 0; $i--) {
        $f = $s.files[$i]
        if ($f.target.StartsWith($fontDir, [StringComparison]::OrdinalIgnoreCase)) {
            if (-not (Remove-StarkFontFile -Entry $f -Until $fontDeadline)) { $pendingFonts.Add($f) }
            continue
        }
        try {
            if (Test-Path -LiteralPath $f.target) { Remove-Item -LiteralPath $f.target -Recurse -Force -ErrorAction Stop }
            if ($f.backup -and (Test-Path -LiteralPath $f.backup)) {
                Copy-Item -LiteralPath $f.backup -Destination $f.target -Recurse -Force
            }
        } catch {
            Write-StarkLog "File $($f.target) non ripristinato: $($_.Exception.Message)" 'warn'
        }
    }

    # 6) sfondo, piano energetico, menu contestuale
    $wpEntry = $s.registry | Where-Object { $_.path -eq 'HKCU:\Control Panel\Desktop' -and $_.name -eq 'Wallpaper' } | Select-Object -First 1
    $wpOriginal = if ($wpEntry -and $wpEntry.existed) { [Environment]::ExpandEnvironmentVariables([string]$wpEntry.value) } else { $null }
    $wp = Get-StarkExtra 'previousWallpaper'
    if (-not $wpEntry) {
        # lo sfondo non e' mai stato cambiato da Stark HUD: non toccarlo
    } elseif ($wpEntry.existed -and -not $wpOriginal) {
        [void][StarkHUD.Native]::SystemParametersInfo(0x0014, 0, '', 0x03)  # prima non c'era sfondo (tinta unita)
    } elseif ($wpOriginal -and (Test-Path -LiteralPath $wpOriginal)) {
        [void][StarkHUD.Native]::SystemParametersInfo(0x0014, 0, $wpOriginal, 0x03)
    } elseif ($wp -and (Test-Path -LiteralPath $wp)) {
        $keep = Join-Path $env:APPDATA 'Microsoft\Windows\Themes\StarkHUD-previous-wallpaper.jpg'
        Copy-Item -LiteralPath $wp -Destination $keep -Force
        [void][StarkHUD.Native]::SystemParametersInfo(0x0014, 0, $keep, 0x03)
    }
    $power = Get-StarkExtra 'powerScheme'
    if ($power) { & powercfg.exe /setactive $power 2>$null }
    $nss = Get-StarkExtra 'nilesoftBackup'
    if ($nss -and (Test-Path -LiteralPath $nss)) {
        $nssDir = Join-Path $env:ProgramFiles 'Nilesoft Shell'
        [void](Invoke-StarkElevated -Reason 'ripristino menu contestuale' -Script @"
Copy-Item -LiteralPath '$nss' -Destination (Join-Path '$nssDir' 'imports\theme.nss') -Force
(Get-Item -LiteralPath (Join-Path '$nssDir' 'shell.nss')).LastWriteTime = Get-Date
"@)
    }
    [void][StarkHUD.Native]::SystemParametersInfo(0x0057, 0, $null, 0x03)
    Send-StarkSettingChange

    # 7) app installate da Stark HUD (solo su richiesta)
    if ($RemoveApps) {
        foreach ($id in @($s.apps)) {
            Write-StarkLog "Disinstallazione $id..." 'info' 80
            # un'app ancora aperta blocca il disinstallatore in attesa di una conferma invisibile
            Stop-StarkAppProcesses -Id $id
            $null = Invoke-StarkWinget -Arguments @('uninstall', '--id', $id, '-e', '--silent', '--accept-source-agreements') -TimeoutSec 600
        }
    } elseif ($s.apps.Count) {
        Write-StarkLog ("App installate da Stark HUD lasciate al loro posto: {0}. Usa 'Rimuovi anche le app' per toglierle." -f ($s.apps -join ', ')) 'info'
    }

    Restart-StarkExplorer

    # 8) stato tenuto in memoria da Esplora risorse: si ripristina con il suo comando
    $icons = Get-StarkExtra 'desktopIconsVisible'
    if ($null -ne $icons) {
        Start-Sleep -Seconds 2  # il desktop di Esplora risorse appena riavviato deve essere pronto
        [void](Set-StarkDesktopIcons -Visible ([bool]$icons))
    }

    # font rimasti bloccati: non sono piu' registrati, quindi Esplora risorse appena riavviato non li riapre
    $fontDeadline = (Get-Date).AddSeconds(10)
    foreach ($f in $pendingFonts) {
        if (Remove-StarkFontFile -Entry $f -Until $fontDeadline) { continue }
        $name = [IO.Path]::GetFileName($f.target)
        $who = @(Get-StarkFileLocker -Path $f.target)
        $why = if ($who.Count) { 'aperto da ' + ($who -join ', ') } else { 'file bloccato' }
        if ($f.backup) {
            Write-StarkLog "Font $name non ripristinato ($why)." 'warn'
            continue
        }
        $runOnce = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\RunOnce')
        try { $runOnce.SetValue('StarkHUD-' + [IO.Path]::GetFileNameWithoutExtension($f.target), ('cmd.exe /c del /f /q "{0}"' -f $f.target)) } finally { $runOnce.Close() }
        Write-StarkLog "Font $name in uso ($why): verra' eliminato al prossimo accesso." 'info'
    }

    # 9) si aspetta che Windows abbia riallineato il colore delle finestre (DWM),
    #    poi si verifica che ogni valore salvato sia tornato com'era
    Write-StarkLog 'Attendo che Windows riallinei il colore delle finestre...' 'info' 90
    $synced = Wait-StarkDwmSync -State $s -TimeoutSec 15
    # Esplora risorse appena riavviato puo' perdere l'avviso: lo si ripete finche' DWM non torna com'era
    for ($n = 0; -not $synced -and $n -lt 3; $n++) {
        Send-StarkSettingChange 'ImmersiveColorSet'
        $synced = Wait-StarkDwmSync -State $s -TimeoutSec 10
    }
    if (-not $synced) {
        Write-StarkLog 'Il colore delle finestre non si e'' ancora riallineato: lo fara'' al prossimo accesso.' 'warn'
    }
    $fixed = Restore-StarkRegistryValues -State $s -OnlyChanged
    if ($fixed -gt 0) {
        Write-StarkLog "Verifica finale: $fixed impostazioni riscritte da Windows sono state riportate all'originale." 'info'
        Restart-StarkExplorer
        Send-StarkSettingChange
        [void](Wait-StarkDwmSync -State $s -TimeoutSec 30)
    }

    # Rainmeter era gia' presente prima di Stark HUD: riavvialo con il suo layout originale
    $rm = Find-StarkExe @('%ProgramFiles%\Rainmeter\Rainmeter.exe', '%ProgramFiles(x86)%\Rainmeter\Rainmeter.exe')
    if ($rm -and -not $RemoveApps -and -not ($s.apps -contains 'Rainmeter.Rainmeter')) { Start-Process -FilePath $rm }
    $archive = Join-Path $script:InstallRoot ('restored-{0:yyyyMMdd-HHmmss}' -f (Get-Date))
    Move-Item -LiteralPath $script:StateDir -Destination $archive -Force
    $assets = Join-Path $script:InstallRoot 'theme'
    if (Test-Path -LiteralPath $assets) { Remove-Item -LiteralPath $assets -Recurse -Force -ErrorAction SilentlyContinue }
    $apps = Join-Path $script:InstallRoot 'apps'
    if (Test-Path -LiteralPath $apps) { Remove-Item -LiteralPath $apps -Recurse -Force -ErrorAction SilentlyContinue }
    $script:State = $null
    Write-StarkLog "Ripristino completato. Backup archiviato in $archive" 'ok' 100
}

Export-ModuleMember -Function *-Stark*
