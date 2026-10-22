<#
    Tipaldi AI - test d'integrazione su Windows reale.

    ATTENZIONE: installa davvero il pack e poi lo ripristina.
    Da eseguire solo su una macchina usa e getta (VM, Windows Sandbox, runner CI).

    Fasi:
      1. validazione degli asset con le API di Windows (cursori, suoni, font, XAML, JSON)
      2. fotografia del registro utente
      3. installazione + verifica di ogni modifica
      4. seconda installazione (idempotenza del backup)
      5. ripristino + confronto del registro con la fotografia iniziale
      6. avvio dell'interfaccia grafica in modalita' test con screenshot

    Uso:  powershell -ExecutionPolicy Bypass -STA -File tests\Test-StarkWindows.ps1 [-Components]
#>
param(
    [switch]$Components,
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
Import-Module (Join-Path $root 'src\StarkHUD.psm1') -Force

$script:pass = 0
$script:fail = 0
$script:failures = New-Object System.Collections.Generic.List[string]
function Check([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { $script:pass++; Write-Host "  ok   $Name" -ForegroundColor Green }
    else {
        $script:fail++
        $script:failures.Add("$Name $Detail")
        Write-Host "  FAIL $Name $Detail" -ForegroundColor Red
        if ($env:GITHUB_ACTIONS) { Write-Host "::error title=Tipaldi AI::$Name $Detail" }
    }
}
function Section([string]$Title) { Write-Host ''; Write-Host "=== $Title ===" -ForegroundColor Cyan }

function Get-RegValue([string]$Sub, [string]$Name) {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Sub)
    if (-not $k) { return $null }
    # la virgola impedisce a PowerShell di srotolare i valori binari (byte[]) in object[]
    try { , $k.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } finally { $k.Close() }
}

# Chiavi HKCU toccate dal pack: confrontate prima/dopo il ripristino
$Watch = @(
    'Control Panel\Cursors'
    'Control Panel\Desktop'
    'AppEvents\Schemes'
    'Software\Microsoft\Windows\CurrentVersion\Themes'
    'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
    'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    'Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize'
    'Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
    'Software\Microsoft\Windows\CurrentVersion\Search'
    'Software\Microsoft\Windows\CurrentVersion\GameDVR'
    'Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications'
    'Software\Microsoft\Windows\CurrentVersion\Feeds'
    'Software\Microsoft\Windows\DWM'
    'Software\Microsoft\GameBar'
    'Software\Policies\Microsoft\Windows\Explorer'
    'Software\Microsoft\Windows NT\CurrentVersion\Fonts'
    'Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID'
    'System\GameConfigStore'
)
# Valori che Windows ricalcola da solo (cache dello sfondo, indice della ricerca): esclusi dal confronto
$Volatile = @(
    'Control Panel\Desktop\`[TranscodedImageCache`]'
    'Control Panel\Desktop\`[TranscodedImageCount`]'
    'Software\Microsoft\Windows\CurrentVersion\Search\`[Installed*AppsRevision`]'
    'Software\Microsoft\Windows\CurrentVersion\Search\`[NamespaceSettingsRevision`]'
    'Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\`[TaskbarStateLastRun`]'
    'Software\Microsoft\Windows\CurrentVersion\Search\`[CortanaStateLastRun`]'
    'Software\Microsoft\Windows\CurrentVersion\Search\JumplistData\*'
    'Software\Microsoft\Windows\CurrentVersion\Search\Microsoft.Windows.Search_*'
)
# Su base Windows 11 (runner Server 2025) Windows non sempre riallinea da solo il colore dei bordi (DWM)
# dopo il ripristino: li si verifica a parte (sezione 5). Sul runner base Windows 10 il confronto resta rigoroso.
$isWin11Base = (Test-StarkEnvironment).IsWin11
$DwmValues = @(
    'Software\Microsoft\Windows\DWM\`[AccentColor`]'
    'Software\Microsoft\Windows\DWM\`[ColorizationAfterglow`]'
    'Software\Microsoft\Windows\DWM\`[ColorizationColor`]'
)
if ($isWin11Base) { $Volatile += $DwmValues }

function Get-RegSnapshot {
    $lines = New-Object System.Collections.Generic.List[string]
    $dump = {
        param([string]$Sub)
        $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Sub)
        if (-not $k) { $lines.Add("$Sub <assente>"); return }
        try {
            foreach ($n in $k.GetValueNames()) {
                $id = "$Sub\[$n]"
                if ($Volatile | Where-Object { $id -like $_ }) { continue }
                $v = $k.GetValue($n, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                if ($v -is [byte[]]) { $v = [Convert]::ToBase64String($v) } elseif ($v -is [array]) { $v = $v -join '|' }
                $lines.Add("$Sub\[$n]=$($k.GetValueKind($n)):$v")
            }
            foreach ($c in $k.GetSubKeyNames()) { & $dump "$Sub\$c" }
        } finally { $k.Close() }
    }
    foreach ($w in $Watch) { & $dump $w }
    $lines | Sort-Object
}

$installErrors = New-Object System.Collections.Generic.List[string]
$installWarnings = New-Object System.Collections.Generic.List[string]
Set-StarkLogger {
    param($m, $l, $p)
    Write-Host ("    [{0}] {1}" -f $l.ToUpper(), $m) -ForegroundColor $(switch ($l) { 'error' { 'Red' } 'warn' { 'Yellow' } 'ok' { 'Green' } default { 'Gray' } })
    if ($l -eq 'error') { $installErrors.Add($m) }
    if ($l -eq 'warn') { $installWarnings.Add($m) }
}

# =====================================================================================
Section 'Ambiente'
$envInfo = Test-StarkEnvironment
$envInfo | Format-List | Out-String | Write-Host
$disp = Get-StarkDisplay
Write-Host "Schermo: $($disp.Width)x$($disp.Height) scala $($disp.Scale)"
Check ($disp.Width -gt 0 -and $disp.Height -gt 0) 'Risoluzione leggibile'
Check $envInfo.PSDesktop 'Windows PowerShell 5.1'

# =====================================================================================
Section '1. Asset validati con le API di Windows'
Add-Type -Namespace StarkTest -Name User32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr LoadCursorFromFile(string lpFileName);
[DllImport("user32.dll")]
public static extern bool DestroyCursor(IntPtr hCursor);
'@
foreach ($f in Get-ChildItem (Join-Path $root 'theme\cursors') -File) {
    $h = [StarkTest.User32]::LoadCursorFromFile($f.FullName)
    Check ($h -ne [IntPtr]::Zero) "Cursore caricabile da Windows: $($f.Name)"
    if ($h -ne [IntPtr]::Zero) { [void][StarkTest.User32]::DestroyCursor($h) }
}
foreach ($f in Get-ChildItem (Join-Path $root 'theme\sounds') -Filter *.wav) {
    $ok = $true
    try { $sp = New-Object System.Media.SoundPlayer $f.FullName; $sp.Load(); $sp.Dispose() } catch { $ok = $false }
    Check $ok "Suono WAV valido: $($f.Name)"
}
Add-Type -AssemblyName System.Drawing
foreach ($f in Get-ChildItem (Join-Path $root 'theme\fonts') -Filter *.ttf) {
    $pfc = New-Object System.Drawing.Text.PrivateFontCollection
    $pfc.AddFontFile($f.FullName)
    Check ($pfc.Families.Count -eq 1 -and $pfc.Families[0].Name -match 'Rajdhani|Share Tech Mono') "Font leggibile: $($f.Name) -> $($pfc.Families[0].Name)"
    $pfc.Dispose()
}
foreach ($f in Get-ChildItem (Join-Path $root 'theme\wallpapers') -Filter *.jpg) {
    $img = [System.Drawing.Image]::FromFile($f.FullName)
    Check ($img.Width -ge 3000) "Sfondo $($f.Name) $($img.Width)x$($img.Height)"
    $img.Dispose()
}
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
try {
    [xml]$x = Get-Content (Join-Path $root 'src\MainWindow.xaml') -Raw -Encoding UTF8
    $w = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $x))
    Check ($null -ne $w.FindName('BtnInstall')) 'XAML installer caricato da WPF'
    $w.Close()
} catch { Check $false 'XAML installer caricato da WPF' $_.Exception.Message }
foreach ($j in 'components\lively\StarkReactor\LivelyInfo.json', 'components\lively\StarkReactor\LivelyProperties.json', 'config\tweaks.json') {
    $ok = $true
    try { $null = Get-Content (Join-Path $root $j) -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $ok = $false }
    Check $ok "JSON valido per PowerShell 5.1: $j"
}
$ttb = (Get-Content (Join-Path $root 'components\translucenttb\settings.json') -Raw) -replace '(?m)^\s*//.*$', ''
$ok = $true; try { $null = $ttb | ConvertFrom-Json } catch { $ok = $false }
Check $ok 'JSON TranslucentTB valido'
try { [xml]$null = Get-Content (Join-Path $root 'components\flowlauncher\StarkHUD.xaml') -Raw -Encoding UTF8; Check $true 'XAML Flow Launcher ben formato' }
catch { Check $false 'XAML Flow Launcher ben formato' $_.Exception.Message }

# =====================================================================================
Section '2. Fotografia del registro'
$before = @(Get-RegSnapshot)
$diag = New-Object System.Collections.Generic.List[string]
# i runner CI non hanno sempre il desktop di Esplora risorse: va tenuto presente leggendo i risultati
$diag.Add(('Desktop di Esplora risorse: {0}' -f $(if ((Get-StarkDesktopView) -ne [IntPtr]::Zero) { 'presente' } else { 'assente' })))
function Get-DwmRgb { $v = Get-RegValue 'Software\Microsoft\Windows\DWM' 'ColorizationColor'; if ($null -ne $v) { (ConvertTo-StarkUInt32 $v) -band 0xFFFFFF } else { -1 } }
$dwmBefore = Get-DwmRgb
$diag.Add(('DWM prima: memoria 0x{0:X8}, registro 0x{1:X8}, accento 0x{2:X8}' -f (Get-StarkDwmColor), (Get-RegValue 'Software\Microsoft\Windows\DWM' 'ColorizationColor'), (Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' 'AccentColorMenu')))
$before | Set-Content (Join-Path $OutDir 'registry-before.txt') -Encoding UTF8
Write-Host "Valori registrati: $($before.Count)"

# =====================================================================================
Section '3. Installazione'
$o = Get-StarkPreset 'Default'
$allTweaks = @(Get-StarkTweaks)
$o.Tweaks = @($allTweaks | ForEach-Object { $_.id })
$o.RestartExplorer = $false
$o.RestorePoint = $true
$o.City = 'Roma'
foreach ($k in 'Rainmeter', 'Lively', 'FlowLauncher', 'TranslucentTB', 'TaskbarX', 'Nilesoft', 'LivelyAudio') { $o[$k] = [bool]$Components }
$o.Windhawk = $false
# Tipaldi AI sul runner: niente riparazioni, riordini o piano energetico automatici (cambierebbero la macchina di test)
$tipHome = Join-Path $env:LOCALAPPDATA 'TipaldiAI'
New-Item -ItemType Directory -Path $tipHome -Force | Out-Null
Set-Content -Path (Join-Path $tipHome 'settings.json') -Encoding UTF8 -Value '{"autoFixSafe":false,"claudeAutoResolve":false,"overdrive":false,"organizeDownloads":false,"organizeDesktop":false,"weeklyReport":false,"skills":false,"hud":true}'
$claudeCfg = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
$claudeCfgBefore = Test-Path $claudeCfg
$t = Measure-Command { Install-StarkHUD -Options $o }
Write-Host ("Durata installazione: {0:N0} s" -f $t.TotalSeconds)
Check ($installErrors.Count -eq 0) 'Installazione senza errori' ($installErrors -join ' | ')

$assets = Join-Path $env:LOCALAPPDATA 'StarkHUD\theme'
Section '3a. Verifica tema nativo'
$arrow = Get-RegValue 'Control Panel\Cursors' 'Arrow'
Check ($arrow -like '*\cursors\arrow.cur' -and (Test-Path $arrow)) 'Cursore Arrow' $arrow
Check ((Get-RegValue 'Control Panel\Cursors' 'Wait') -like '*busy.ani') 'Cursore Wait animato'
$scheme = Get-RegValue 'Control Panel\Cursors\Schemes' 'Tipaldi AI'
Check ($scheme -and ($scheme -split ',').Count -eq 17) 'Schema cursori con 17 voci'
Check ((Get-RegValue 'AppEvents\Schemes' '') -eq 'StarkHUD') 'Schema suoni attivo'
$ast = Get-RegValue 'AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current' ''
Check ($ast -like '*asterisk.wav' -and (Test-Path $ast)) 'Suono SystemAsterisk' $ast
Check ((Get-RegValue 'AppEvents\Schemes\Apps\Explorer\EmptyRecycleBin\.Current' '') -like '*recycle.wav') 'Suono cestino'
$fontDir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
foreach ($ff in 'Rajdhani-Light.ttf', 'ShareTechMono-Regular.ttf') { Check (Test-Path (Join-Path $fontDir $ff)) "Font installato: $ff" }
Check ((Get-RegValue 'Software\Microsoft\Windows NT\CurrentVersion\Fonts' 'Rajdhani Light (TrueType)') -like '*Rajdhani-Light.ttf') 'Font registrato per l''utente'
Check ((Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'AppsUseLightTheme') -eq 0) 'Modalita'' scura app'
Check ((Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' 'EnableTransparency') -eq 1) 'Trasparenze'
$pal = Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' 'AccentPalette'
Check ($pal -is [byte[]] -and $pal.Length -eq 32) 'Tavolozza accento 32 byte'
Check ((Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' 'AccentColorMenu') -eq -3697152) 'AccentColorMenu ABGR ciano'
$diag.Add(('DWM dopo installazione: memoria 0x{0:X8}, registro 0x{1:X8}, accento 0x{2:X8}' -f (Get-StarkDwmColor), (Get-RegValue 'Software\Microsoft\Windows\DWM' 'ColorizationColor'), (Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' 'AccentColorMenu')))
$themeFile = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Themes\StarkHUD.theme'
Check (Test-Path $themeFile) 'File StarkHUD.theme creato'
if (Test-Path $themeFile) {
    $tc = Get-Content $themeFile -Raw
    $wpLine = ([regex]::Match($tc, '(?m)^Wallpaper=(.+?)\r?$')).Groups[1].Value
    Check (Test-Path $wpLine) 'Il tema punta a uno sfondo esistente' $wpLine
}
$wall = Get-RegValue 'Control Panel\Desktop' 'Wallpaper'
Check ($wall -like '*StarkHUD-Desktop-*.jpg') 'Sfondo del desktop applicato' $wall
$startup = [Environment]::GetFolderPath('Startup')
Check (Test-Path (Join-Path $startup 'Tipaldi AI - suono di avvio.lnk')) 'Collegamento suono di avvio'
# all'accesso non deve comparire nessuna finestra di PowerShell: niente in Esecuzione automatica punta a powershell.exe
$wsh = New-Object -ComObject WScript.Shell
$psLinks = @(Get-ChildItem -LiteralPath $startup -Filter '*.lnk' -ErrorAction SilentlyContinue | Where-Object { $wsh.CreateShortcut($_.FullName).TargetPath -match 'powershell' })
Check ($psLinks.Count -eq 0) 'Nessun avvio automatico tramite PowerShell' (($psLinks | ForEach-Object Name) -join ', ')
$soundLnk = Join-Path $startup 'Tipaldi AI - suono di avvio.lnk'
if (Test-Path $soundLnk) { Check ($wsh.CreateShortcut($soundLnk).TargetPath -like '*\TipaldiAI.exe') 'Suono di avvio suonato da TipaldiAI.exe (app senza finestre)' $wsh.CreateShortcut($soundLnk).TargetPath }

Section '3a2. Verifica Tipaldi AI'
$tipExe = Join-Path $tipHome 'app\TipaldiAI.exe'
Check (Test-Path $tipExe) 'TipaldiAI.exe installato'
Check (Test-Path (Join-Path $tipHome 'app\icons\video.ico')) 'Icone dei contesti accanto all''exe'
Check (Test-Path (Join-Path $startup 'Tipaldi AI.lnk')) 'Tipaldi AI all''avvio di Windows'
$cfg = if (Test-Path $claudeCfg) { Get-Content $claudeCfg -Raw | ConvertFrom-Json } else { $null }
Check ($cfg -and $cfg.mcpServers.'tipaldi-ai'.command -eq $tipExe -and $cfg.mcpServers.'tipaldi-ai'.args[0] -eq '--mcp') 'Server MCP registrato in Claude Desktop' "$(if ($cfg) { $cfg | ConvertTo-Json -Depth 5 -Compress })"
Start-Sleep -Seconds 3
Check (@(Get-Process TipaldiAI -ErrorAction SilentlyContinue).Count -ge 1) 'Tipaldi AI in esecuzione'
Check ((Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{20D04FE0-3AEA-1069-A2D8-08002B30309D}\DefaultIcon' '') -like '*thispc.ico,0') 'Icona Tipaldi AI per Questo PC'
Check ((Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon' 'full') -like '*recycle-full.ico,0') 'Icona Tipaldi AI per il Cestino pieno'
# il server MCP risponde come lo userebbe Claude Desktop
$psi = New-Object Diagnostics.ProcessStartInfo $tipExe, '--mcp'
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.CreateNoWindow = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$mcp = [Diagnostics.Process]::Start($psi)
$mcp.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}')
$mcp.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"run_health_check","arguments":{"deep":true}}}')
$mcp.StandardInput.Close()
$mcpOut = $mcp.StandardOutput.ReadToEnd()
[void]$mcp.WaitForExit(60000)
$mcpLines = @($mcpOut -split "`n" | Where-Object { $_.Trim() })
Check ($mcpLines.Count -eq 2 -and $mcpLines[0] -match '"tipaldi-ai"' -and $mcpLines[1] -match 'dischi_fisici') 'Server MCP: initialize e diagnosi completa' ($mcpOut.Substring(0, [Math]::Min(300, $mcpOut.Length)))
$diag.Add('Diagnosi Tipaldi AI via MCP: ' + $(try { (($mcpLines[1] | ConvertFrom-Json).result.content[0].text | ConvertFrom-Json).stato } catch { '?' }))

Section '3b. Verifica ottimizzazioni'
foreach ($tw in $allTweaks) {
    if ($tw.PSObject.Properties['registry']) {
        foreach ($r in $tw.registry) {
            $sub = $r.path -replace '^HKCU:\\', ''
            $v = Get-RegValue $sub $r.name
            Check ("$v" -eq "$($r.value)") "Ottimizzazione $($tw.id): $($r.name)" "(atteso $($r.value), trovato $v)"
        }
    }
}

if ($Components) {
    Section '3c. Verifica componenti'
    $skins = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter\Skins\StarkHUD'
    Check (Test-Path (Join-Path $skins 'Clock\Clock.ini')) 'Skin Rainmeter copiate'
    Check (Test-Path (Join-Path $skins '@Resources\Fonts\Rajdhani-Light.ttf')) 'Font nelle skin Rainmeter'
    if (Test-Path (Join-Path $skins '@Resources\Variables.inc')) {
        $vars = Get-Content (Join-Path $skins '@Resources\Variables.inc') -Raw
        Check ($vars -match '(?m)^City=Roma\r?$') 'Citta'' meteo impostata'
        Check ($vars -match '(?m)^Scale=\d') 'Scala HiDPI impostata'
    }
    Check (Test-Path (Join-Path $env:APPDATA 'Rainmeter\Layouts\StarkHUD\Rainmeter.ini')) 'Layout Rainmeter generato'
    $livelyDir = Join-Path $env:LOCALAPPDATA 'Lively Wallpaper\Library\wallpapers\StarkReactor'
    Check (Test-Path (Join-Path $livelyDir 'index.html')) 'Sfondo Lively nella libreria'
    Check (Test-Path (Join-Path $livelyDir 'fonts\Rajdhani-Light.ttf')) 'Font nello sfondo Lively'
    if (Test-Path (Join-Path $livelyDir 'LivelyInfo.json')) {
        $li = Get-Content (Join-Path $livelyDir 'LivelyInfo.json') -Raw | ConvertFrom-Json
        Check ($li.Arguments -match '--audio') 'Lively: audio attivato su richiesta'
        $lp = Get-Content (Join-Path $livelyDir 'LivelyProperties.json') -Raw | ConvertFrom-Json
        Check ($lp.showClock.value -eq $false) 'Lively: orologio spento quando c''e'' Rainmeter'
    }
    $flowTheme = Join-Path $env:APPDATA 'FlowLauncher\Themes\StarkHUD.xaml'
    Check (Test-Path $flowTheme) 'Tema Flow Launcher copiato'
    $flowSettings = Join-Path $env:APPDATA 'FlowLauncher\Settings\Settings.json'
    if (Test-Path $flowSettings) {
        $fs = Get-Content $flowSettings -Raw -Encoding UTF8 | ConvertFrom-Json
        Check ($fs.Theme -eq 'StarkHUD') 'Flow Launcher usa il tema Tipaldi AI'
    } else { Check $false 'Impostazioni Flow Launcher scritte' }
    $ttbCfg = Join-Path $env:LOCALAPPDATA 'Packages\28017CharlesMilette.TranslucentTB_v826wp6bftszj\RoamingState\settings.json'
    Check (Test-Path $ttbCfg) 'Configurazione TranslucentTB'
    Check (Test-Path (Join-Path $env:LOCALAPPDATA 'StarkHUD\apps\TaskbarX\TaskbarX.exe')) 'TaskbarX scaricato e verificato'
    Check (Test-Path (Join-Path $startup 'Tipaldi AI - TaskbarX.lnk')) 'TaskbarX all''avvio'
    $nss = Join-Path $env:ProgramFiles 'Nilesoft Shell\imports\theme.nss'
    Check ((Test-Path $nss) -and ((Get-Content $nss -Raw) -match 'Stark HUD')) 'Tema Nilesoft Shell'
}

# =====================================================================================
Section '4. Seconda installazione (idempotenza)'
$state1 = Get-Content (Join-Path $env:LOCALAPPDATA 'StarkHUD\state\backup.json') -Raw | ConvertFrom-Json
$o.RestorePoint = $false
$installErrors.Clear()
Install-StarkHUD -Options $o
$state2 = Get-Content (Join-Path $env:LOCALAPPDATA 'StarkHUD\state\backup.json') -Raw | ConvertFrom-Json
Check (@($state1.registry).Count -eq @($state2.registry).Count) 'Il backup non si duplica' "($(@($state1.registry).Count) vs $(@($state2.registry).Count))"
Check (@($state1.files).Count -eq @($state2.files).Count) 'I file di backup non si duplicano'
Check ($installErrors.Count -eq 0) 'Seconda installazione senza errori' ($installErrors -join ' | ')

# =====================================================================================
Section '5. Ripristino'
$installErrors.Clear()
$installWarnings.Clear()
$t = Measure-Command { Restore-StarkHUD -RemoveApps:$Components }
Write-Host ("Durata ripristino: {0:N0} s" -f $t.TotalSeconds)
Check ($installErrors.Count -eq 0) 'Ripristino senza errori' ($installErrors -join ' | ')
$diag.Add(('DWM subito dopo il ripristino: memoria 0x{0:X8}, registro 0x{1:X8}' -f (Get-StarkDwmColor), (Get-RegValue 'Software\Microsoft\Windows\DWM' 'ColorizationColor')))
# Windows riallinea alcuni valori con qualche secondo di ritardo: si confronta fino a 60 s
for ($try = 1; $try -le 12; $try++) {
    Start-Sleep -Seconds 5
    $after = @(Get-RegSnapshot)
    if (-not (Compare-Object -ReferenceObject $before -DifferenceObject $after)) { break }
}
$diag.Add("Registro confrontato dopo $([Math]::Min($try, 12) * 5) s")
$diag.Add(('DWM a fine confronto: memoria 0x{0:X8}, registro 0x{1:X8}, accento 0x{2:X8}' -f (Get-StarkDwmColor), (Get-RegValue 'Software\Microsoft\Windows\DWM' 'ColorizationColor'), (Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\Accent' 'AccentColorMenu')))
$after | Set-Content (Join-Path $OutDir 'registry-after.txt') -Encoding UTF8
$diff = @(Compare-Object -ReferenceObject $before -DifferenceObject $after)
foreach ($d in $diff) { Write-Host ("    {0} {1}" -f $(if ($d.SideIndicator -eq '<=') { 'PRIMA ' } else { 'DOPO  ' }), $d.InputObject) -ForegroundColor Yellow }
$diffText = ($diff | Select-Object -First 16 | ForEach-Object { "`n      $(if ($_.SideIndicator -eq '<=') { 'PRIMA' } else { 'DOPO ' }) $($_.InputObject)" }) -join ''
Check ($diff.Count -eq 0) 'Registro identico a prima dell''installazione' "($($diff.Count) differenze)$diffText"
if ($isWin11Base) {
    $dwmBack = (Get-DwmRgb) -eq $dwmBefore
    $told = [bool]($installWarnings | Where-Object { $_ -like '*bordi e barre del titolo*' })
    $diag.Add("Base Windows 11: colore bordi riallineato da Windows = $dwmBack, utente avvisato = $told")
    Check ($dwmBack -or $told) 'Colore dei bordi riallineato, o utente avvisato con le istruzioni'
}
# Il servizio cache dei font di Windows puo' tenere aperti i file: allora devono essere disinstallati
# (registro, gia' confrontato sopra) e programmati per la cancellazione al prossimo accesso
$runOnce = 'Software\Microsoft\Windows\CurrentVersion\RunOnce'
$fontsLeft = @(Get-ChildItem (Join-Path $root 'theme\fonts') -Filter *.ttf | Where-Object { Test-Path (Join-Path $fontDir $_.Name) })
$fontsLost = @($fontsLeft | Where-Object { (Get-RegValue $runOnce ('StarkHUD-' + $_.BaseName)) -notlike "*$($_.Name)*" })
$diag.Add("Font da cancellare al prossimo accesso: $($fontsLeft.Count)")
Check ($fontsLost.Count -eq 0) 'Font rimossi o programmati per la cancellazione al prossimo accesso' ($fontsLost.Name -join ', ')
Check (-not (Test-Path $themeFile)) 'File tema rimosso'
Check (@(Get-Process TipaldiAI -ErrorAction SilentlyContinue).Count -eq 0) 'Tipaldi AI chiuso'
Check (-not (Test-Path (Join-Path $tipHome 'app'))) 'Programma Tipaldi AI rimosso'
Check (-not (Test-Path (Join-Path $startup 'Tipaldi AI.lnk'))) 'Tipaldi AI tolto dall''avvio'
$cfgAfter = if (Test-Path $claudeCfg) { Get-Content $claudeCfg -Raw } else { '' }
Check ($cfgAfter -notmatch 'tipaldi-ai') 'Server MCP rimosso da Claude Desktop'
Check ($claudeCfgBefore -or -not (Test-Path $claudeCfg)) 'Nessuna configurazione di Claude lasciata se prima non c''era'
Check (Test-Path (Join-Path $tipHome 'settings.json')) 'Dati dell''utente di Tipaldi AI conservati'
Check (-not (Test-Path (Join-Path $startup 'Tipaldi AI - suono di avvio.lnk'))) 'Collegamento di avvio rimosso'
Check (-not (Test-Path (Join-Path $env:LOCALAPPDATA 'StarkHUD\state'))) 'Backup archiviato'
Check (-not (Test-Path $assets)) 'Risorse rimosse'
$cur = Get-RegValue 'Software\Microsoft\Windows\CurrentVersion\Themes' 'CurrentTheme'
Check (-not $cur -or (Test-Path $cur)) 'Il tema attivo punta a un file esistente' "$cur"
if ($Components) {
    Check (-not (Test-Path (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter\Skins\StarkHUD'))) 'Skin Rainmeter rimosse'
    Check (-not (Test-Path (Join-Path $env:LOCALAPPDATA 'Lively Wallpaper\Library\wallpapers\StarkReactor'))) 'Sfondo Lively rimosso'
    Check (-not (Test-Path (Join-Path $env:APPDATA 'FlowLauncher\Themes\StarkHUD.xaml'))) 'Tema Flow rimosso'
    Check (-not (Test-Path (Join-Path $startup 'Tipaldi AI - TaskbarX.lnk'))) 'TaskbarX rimosso dall''avvio'
}

# =====================================================================================
Section '6. Interfaccia grafica (modalita'' test)'
$shot = Join-Path $OutDir 'gui.png'
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$p = Start-Process -FilePath $ps -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-File', "`"$(Join-Path $root 'StarkUI.ps1')`"", '-SmokeTest', '-Screenshot', "`"$shot`"") `
    -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $OutDir 'gui-stdout.txt') -RedirectStandardError (Join-Path $OutDir 'gui-stderr.txt')
$null = $p.Handle  # senza questo PowerShell 5.1 perde il codice di uscita
if (-not $p.WaitForExit(120000)) { $p | Stop-Process -Force; Check $false 'Interfaccia: si chiude da sola' 'timeout 120 s' }
else { Check ($p.ExitCode -eq 0) 'Interfaccia avviata e chiusa senza errori' "(codice $($p.ExitCode))" }
Get-Content (Join-Path $OutDir 'gui-stdout.txt'), (Join-Path $OutDir 'gui-stderr.txt') -ErrorAction SilentlyContinue | Write-Host
foreach ($tab in 'TabTheme', 'TabComponents', 'TabTweaks') {
    $png = $shot -replace '\.png$', "-$tab.png"
    Check (Test-Path $png) "Screenshot interfaccia: $tab"
    if ((Test-Path $png) -and $env:STARK_PRINT_SCREENSHOTS -and $tab -eq 'TabTheme') {
        # anteprima JPEG ridotta stampata nel log (base64) per chi non puo' scaricare gli artifact
        $src = New-Object System.Windows.Media.Imaging.BitmapImage
        $src.BeginInit(); $src.UriSource = New-Object Uri $png; $src.CacheOption = 'OnLoad'; $src.EndInit()
        $scaled = New-Object System.Windows.Media.Imaging.TransformedBitmap($src, (New-Object System.Windows.Media.ScaleTransform(0.5, 0.5)))
        $enc = New-Object System.Windows.Media.Imaging.JpegBitmapEncoder
        $enc.QualityLevel = 72
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($scaled))
        $ms = New-Object System.IO.MemoryStream
        $enc.Save($ms)
        $b64 = [Convert]::ToBase64String($ms.ToArray())
        Write-Host "=====BEGIN $tab====="
        for ($i = 0; $i -lt $b64.Length; $i += 3000) { Write-Host $b64.Substring($i, [Math]::Min(3000, $b64.Length - $i)) }
        Write-Host "=====END $tab====="
    }
}

# =====================================================================================
Section 'Risultato'
Copy-Item -Path (Join-Path $env:LOCALAPPDATA 'StarkHUD\logs\*') -Destination $OutDir -ErrorAction SilentlyContinue
Write-Host ("{0} ok, {1} falliti" -f $script:pass, $script:fail) -ForegroundColor $(if ($script:fail) { 'Red' } else { 'Green' })
foreach ($f in $script:failures) { Write-Host " - $f" -ForegroundColor Red }
Write-Host 'Diagnostica:'
foreach ($d in $diag) { Write-Host "   $d" }
exit $script:fail
