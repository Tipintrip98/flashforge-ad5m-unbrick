<#
    Stark HUD - esperimento: quale evento fa riallineare a Windows il colore delle finestre (DWM)
    al colore accento? Da eseguire su una macchina pulita e usa e getta (CI / VM).
#>
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src\StarkHUD.psm1') -Force
Initialize-StarkNative

$dwmKey = 'Software\Microsoft\Windows\DWM'
$accKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
function Get-Reg($sub, $name) { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub); if ($k) { try { $k.GetValue($name) } finally { $k.Close() } } }
function Set-Accent([int64]$abgr, [byte[]]$palette) {
    $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($accKey)
    try {
        $k.SetValue('AccentColorMenu', [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$abgr), 0), 'DWord')
        $k.SetValue('AccentPalette', $palette, 'Binary')
    } finally { $k.Close() }
}
function Show([string]$step) {
    Start-Sleep -Seconds 4
    '{0,-52} DWM memoria 0x{1:X8} | DWM registro 0x{2:X8} | AccentColorMenu 0x{3:X8}' -f $step, (Get-StarkDwmColor), (Get-Reg $dwmKey 'ColorizationColor'), (Get-Reg $accKey 'AccentColorMenu')
}
function Restart-Shell {
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
}
function Run-Triggers([string]$label, [string]$wallpaper, [string]$lockImage) {
    Send-StarkSettingChange 'ImmersiveColorSet'
    Show "$label B) WM_SETTINGCHANGE ImmersiveColorSet"
    [void][StarkHUD.Native]::SystemParametersInfo(0x0014, 0, $wallpaper, 0x03)
    Show "$label C) SPI_SETDESKWALLPAPER"
    Set-StarkLockScreen -ImagePath $lockImage
    Show "$label D) LockScreen.SetImageFileAsync"
    Send-StarkSettingChange 'WindowsThemeElement'
    Show "$label E) WM_SETTINGCHANGE WindowsThemeElement"
    Restart-Shell
    Show "$label F) riavvio Esplora risorse"
    Start-Sleep -Seconds 20
    Show "$label G) +20 s"
}

Set-StarkLogger { param($m, $l, $p) Write-Host "    [$l] $m" }
$wall = Join-Path $root 'theme\wallpapers\StarkHUD-Desktop-4K.jpg'
$lock = Join-Path $root 'theme\wallpapers\StarkHUD-Lock-4K.jpg'
$origMenu = [uint32](ConvertTo-StarkUInt32 (Get-Reg $accKey 'AccentColorMenu'))
$origPalette = [byte[]](Get-Reg $accKey 'AccentPalette')
$origWall = [string](Get-Reg 'Control Panel\Desktop' 'WallPaper')

Show 'Iniziale'
Set-Accent (Get-StarkAbgr -Hex '#0096C7') (Get-StarkAccentPalette -Hex '#0096C7')
Show 'STARK A) solo registro Accent'
Run-Triggers 'STARK' $wall $lock

Set-Accent $origMenu $origPalette
Show 'ORIGINALE A) solo registro Accent'
Run-Triggers 'ORIGINALE' $origWall $lock
