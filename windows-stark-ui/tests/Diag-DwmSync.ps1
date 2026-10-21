<#
    Stark HUD - esperimento: quale azione fa aggiornare a Windows il colore delle finestre (DWM)?
    Cambia l'accento in modi diversi, uno alla volta, e misura il colore che DWM ha in memoria.
    Solo per macchine usa e getta (CI / VM). Alla fine riporta tutto com'era.
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src\StarkHUD.psm1') -Force
Initialize-StarkNative

Add-Type -Namespace StarkDiag -Name Api -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)]
public struct Pref { public uint StartColor; public uint AccentColor; }
[DllImport("uxtheme.dll", EntryPoint = "SetUserColorPreference", SetLastError = true)]
public static extern int SetUserColorPreference(ref Pref p, int forceCommit);
[DllImport("uxtheme.dll", EntryPoint = "GetUserColorPreference", SetLastError = true)]
public static extern int GetUserColorPreference(out Pref p, int forceReload);
[DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
public static extern IntPtr GetProcAddress(IntPtr hModule, string name);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
public static extern IntPtr LoadLibrary(string name);
'@

$dwmKey = 'Software\Microsoft\Windows\DWM'
$accKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
function Get-Reg($sub, $name) { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub); if ($k) { try { $k.GetValue($name) } finally { $k.Close() } } }
function Set-Reg($sub, $name, $value) { $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($sub); try { $k.SetValue($name, [int32]$value, 'DWord') } finally { $k.Close() } }
function Show([string]$step) {
    Start-Sleep -Seconds 3
    '{0,-58} DWM memoria 0x{1:X8} | DWM reg 0x{2:X8} | AccentColorMenu 0x{3:X8}' -f $step, (Get-StarkDwmColor), (Get-Reg $dwmKey 'ColorizationColor'), (Get-Reg $accKey 'AccentColorMenu')
}
function Bump([uint32]$abgr) { [BitConverter]::ToInt32([BitConverter]::GetBytes($abgr), 0) }

$orig = @{
    Menu = Get-Reg $accKey 'AccentColorMenu'; Start = Get-Reg $accKey 'StartColorMenu'; Palette = Get-Reg $accKey 'AccentPalette'
    DwmColor = Get-Reg $dwmKey 'ColorizationColor'; DwmAfter = Get-Reg $dwmKey 'ColorizationAfterglow'; DwmAccent = Get-Reg $dwmKey 'AccentColor'
}
$h = [StarkDiag.Api]::LoadLibrary('uxtheme.dll')
'uxtheme SetUserColorPreference: {0}' -f $(if ([StarkDiag.Api]::GetProcAddress($h, 'SetUserColorPreference') -ne [IntPtr]::Zero) { 'presente' } else { 'ASSENTE' })
'uxtheme GetUserColorPreference: {0}' -f $(if ([StarkDiag.Api]::GetProcAddress($h, 'GetUserColorPreference') -ne [IntPtr]::Zero) { 'presente' } else { 'ASSENTE' })
$p = New-Object 'StarkDiag.Api+Pref'
try { 'GetUserColorPreference hr=0x{0:X8} accent=0x{1:X8} start=0x{2:X8}' -f [StarkDiag.Api]::GetUserColorPreference([ref]$p, 1), $p.AccentColor, $p.StartColor } catch { "GetUserColorPreference eccezione: $($_.Exception.Message)" }

Show 'Iniziale'

# A) solo registro Accent (rosso)
Set-Reg $accKey 'AccentColorMenu' (Bump 0xFF2020E0)
Set-Reg $accKey 'StartColorMenu' (Bump 0xFF101070)
Show 'A) registro AccentColorMenu rosso, nessun avviso'

# B) + broadcast ImmersiveColorSet
Send-StarkSettingChange 'ImmersiveColorSet'
Show 'B) + WM_SETTINGCHANGE ImmersiveColorSet'

# C) + SPI_SETDESKWALLPAPER (stesso sfondo)
$wp = [string](Get-Reg 'Control Panel\Desktop' 'WallPaper')
[void][StarkHUD.Native]::SystemParametersInfo(0x0014, 0, $wp, 0x03)
Show 'C) + SPI_SETDESKWALLPAPER'

# D) SetUserColorPreference verde
$p.AccentColor = 0x0020C020; $p.StartColor = 0x00106010
try { $hr = [StarkDiag.Api]::SetUserColorPreference([ref]$p, 1); 'SetUserColorPreference hr=0x{0:X8} lastError={1}' -f $hr, [Runtime.InteropServices.Marshal]::GetLastWin32Error() } catch { "SetUserColorPreference eccezione: $($_.Exception.GetType().Name) $($_.Exception.Message)" }
Show 'D) SetUserColorPreference verde'

# E) DwmSetColorizationParameters giallo
$dp = New-Object 'StarkHUD.Native+DwmColorParams'
try {
    $g = [StarkHUD.Native]::DwmGetColorizationParameters([ref]$dp)
    $dp.Color = 0xC4D0C020; $dp.Afterglow = 0xC4D0C020
    $s = [StarkHUD.Native]::DwmSetColorizationParameters([ref]$dp, 0)
    'DwmGetColorizationParameters hr=0x{0:X8}, DwmSetColorizationParameters hr=0x{1:X8}' -f $g, $s
} catch { "DwmSetColorizationParameters eccezione: $($_.Exception.Message)" }
Show 'E) DwmSetColorizationParameters giallo'

# F) registro DWM magenta + ImmersiveColorSet
Set-Reg $dwmKey 'ColorizationColor' (Bump 0xC4C020C0)
Set-Reg $dwmKey 'ColorizationAfterglow' (Bump 0xC4C020C0)
Send-StarkSettingChange 'ImmersiveColorSet'
Show 'F) registro DWM magenta + ImmersiveColorSet'

# G) riavvio di Esplora risorse
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
Show 'G) riavvio Esplora risorse'

# H) WM_SETTINGCHANGE senza parametro (come dopo un cambio tema)
[void][StarkHUD.Native]::SendMessageTimeout([IntPtr]0xFFFF, 0x001A, [UIntPtr]::Zero, $null, 0x0002, 3000, [ref]([UIntPtr]::Zero))
Show 'H) WM_SETTINGCHANGE generico'

# Ripristino dei valori iniziali
Set-Reg $accKey 'AccentColorMenu' $orig.Menu
Set-Reg $accKey 'StartColorMenu' $orig.Start
if ($orig.Palette) { $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($accKey); $k.SetValue('AccentPalette', [byte[]]$orig.Palette, 'Binary'); $k.Close() }
Set-Reg $dwmKey 'ColorizationColor' $orig.DwmColor
Set-Reg $dwmKey 'ColorizationAfterglow' $orig.DwmAfter
Set-Reg $dwmKey 'AccentColor' $orig.DwmAccent
Send-StarkSettingChange 'ImmersiveColorSet'
Show 'Z) valori iniziali riscritti'
