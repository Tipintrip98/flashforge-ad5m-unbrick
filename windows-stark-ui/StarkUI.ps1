<#
.SYNOPSIS
    Stark HUD - Windows 10 UI Pack (installer grafico e da riga di comando).

.DESCRIPTION
    Senza parametri apre l'installer grafico.
    Con -Install / -Restore lavora da riga di comando (utile per automazioni).

.EXAMPLE
    .\StarkUI.ps1
    .\StarkUI.ps1 -Install -Preset Default -City Roma -Yes
    .\StarkUI.ps1 -Restore -RemoveApps -Yes
#>
[CmdletBinding()]
param(
    [switch]$Install,
    [switch]$Restore,
    [switch]$RemoveApps,
    [ValidateSet('Default', 'Minimal', 'Full')][string]$Preset = 'Default',
    [string]$City = '',
    [switch]$Yes,
    # Modalita' test: apre la finestra, salva uno screenshot per scheda e si chiude da sola
    [switch]$SmokeTest,
    [string]$Screenshot = ''
)

$ErrorActionPreference = 'Stop'

# --- Serve Windows PowerShell 5.1, 64 bit, STA (WPF + API WinRT della schermata di blocco)
$needsRelaunch = ($PSVersionTable.PSEdition -eq 'Core') -or
    ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) -or
    ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA')
if ($needsRelaunch) {
    $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
        $ps = Join-Path $env:SystemRoot 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    }
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-File', "`"$PSCommandPath`"")
    foreach ($kv in $PSBoundParameters.GetEnumerator()) {
        if ($kv.Value -is [switch]) { if ($kv.Value) { $forward += "-$($kv.Key)" } }
        else { $forward += "-$($kv.Key)"; $forward += "`"$($kv.Value)`"" }
    }
    Start-Process -FilePath $ps -ArgumentList $forward
    return
}

$Root = $PSScriptRoot

# Errori imprevisti: messaggio chiaro invece di una finestra che sparisce
trap {
    $msg = "Errore: $($_.Exception.Message)`n`nPuoi sempre ripristinare con Ripristina.cmd."
    if ($Install -or $Restore -or $SmokeTest) {
        Write-Host $msg -ForegroundColor Red
        Write-Host $_.ScriptStackTrace
        exit 1
    }
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show($msg, 'Stark HUD', 'OK', 'Error')
    break
}

# File scaricati da internet: rimuove il blocco "Mark of the Web" (anche per lo sfondo animato)
Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

Import-Module (Join-Path $Root 'src\StarkHUD.psm1') -Force

# =====================================================================================
#  Riga di comando
# =====================================================================================
if ($Install -or $Restore) {
    if (-not $Yes) {
        $what = if ($Restore) { 'ripristinare la configurazione originale' } else { "installare Stark HUD (preset $Preset)" }
        $answer = Read-Host "Stai per $what. Esplora risorse verra' riavviato. Procedere? (S/N)"
        if ($answer -notmatch '^[sSyY]') { Write-Host 'Annullato.'; return }
    }
    if ($Restore) { Restore-StarkHUD -RemoveApps:$RemoveApps }
    else {
        $options = Get-StarkPreset $Preset
        $options.City = $City
        Install-StarkHUD -Options $options
    }
    return
}

# =====================================================================================
#  Interfaccia grafica
# =====================================================================================
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml

[xml]$xaml = Get-Content -Path (Join-Path $Root 'src\MainWindow.xaml') -Raw -Encoding UTF8
$window = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))

$ui = @{}
foreach ($name in 'TitleBar', 'BtnMin', 'BtnClose', 'InfoText', 'TabTheme', 'TabComponents', 'TabTweaks', 'TabLog',
    'Scroller', 'PanelTheme', 'PanelComponents', 'PanelTweaks', 'PanelLog', 'StatusText', 'ProgressScale',
    'ChkRemoveApps', 'BtnRestore', 'BtnInstall') {
    $ui[$name] = $window.FindName($name)
}

# Font del pack caricati direttamente dalla cartella (non serve installarli per la GUI)
$fontBase = New-Object System.Uri ((Join-Path $Root 'theme\fonts') + '\')
$window.Resources['Display'] = New-Object System.Windows.Media.FontFamily($fontBase, './#Rajdhani, Segoe UI Light')
$window.Resources['Mono'] = New-Object System.Windows.Media.FontFamily($fontBase, './#Share Tech Mono, Consolas')

$script:Toggles = @{}
$bc = New-Object System.Windows.Media.BrushConverter

function New-TextBlock([string]$Text, [string]$Style) {
    $t = New-Object System.Windows.Controls.TextBlock
    $t.Text = $Text
    if ($Style) { $t.Style = $window.FindResource($Style) }
    $t
}

function Add-Group($Panel, [string]$Title) {
    [void]$Panel.Children.Add((New-TextBlock $Title.ToUpper() 'GroupHeader'))
}

function Add-Toggle($Panel, [string]$Key, [string]$Title, [string]$Desc, [bool]$Checked, [string]$Badge = '') {
    $card = New-Object System.Windows.Controls.Border
    $card.Style = $window.FindResource('RowCard')
    $grid = New-Object System.Windows.Controls.Grid
    $c1 = New-Object System.Windows.Controls.ColumnDefinition
    $c1.Width = New-Object System.Windows.GridLength(1, [System.Windows.GridUnitType]::Star)
    $c2 = New-Object System.Windows.Controls.ColumnDefinition
    $c2.Width = [System.Windows.GridLength]::Auto
    $grid.ColumnDefinitions.Add($c1); $grid.ColumnDefinitions.Add($c2)

    $stack = New-Object System.Windows.Controls.StackPanel
    $title = New-TextBlock '' 'RowTitle'
    [void]$title.Inlines.Add((New-Object System.Windows.Documents.Run $Title))
    if ($Badge) {
        $run = New-Object System.Windows.Documents.Run ("   " + $Badge.ToUpper())
        $run.Foreground = $bc.ConvertFromString('#F2B544')
        $run.FontSize = 10.5
        $run.FontFamily = $window.Resources['Mono']
        [void]$title.Inlines.Add($run)
    }
    [void]$stack.Children.Add($title)
    [void]$stack.Children.Add((New-TextBlock $Desc 'RowDesc'))

    $cb = New-Object System.Windows.Controls.CheckBox
    $cb.Style = $window.FindResource('Toggle')
    $cb.IsChecked = $Checked
    [System.Windows.Controls.Grid]::SetColumn($cb, 1)

    [void]$grid.Children.Add($stack)
    [void]$grid.Children.Add($cb)
    $card.Child = $grid
    $card.Tag = $cb
    $card.Add_MouseLeftButtonUp({ param($s, $e) if ($s.Tag.IsEnabled) { $s.Tag.IsChecked = -not $s.Tag.IsChecked } })
    [void]$Panel.Children.Add($card)
    $script:Toggles[$Key] = $cb
}

# --- Contenuti delle schede ----------------------------------------------------------
$defaults = Get-StarkPreset 'Default'

Add-Group $ui.PanelTheme 'Livello 1 // Nativo Windows (nessuna app aggiuntiva)'
Add-Toggle $ui.PanelTheme 'Theme' 'Tema scuro, colori e sfondo 4K' 'Modalita'' scura, accento ciano reattore con tavolozza oro, trasparenze, sfondo "Arc Reactor" 4K (ultrawide incluso).' $defaults.Theme 'consigliato'
Add-Toggle $ui.PanelTheme 'Cursors' 'Cursori Stark HUD' '17 puntatori vettoriali multi-risoluzione (fino a 4K), attesa e caricamento animati.' $defaults.Cursors
Add-Toggle $ui.PanelTheme 'Sounds' 'Schema suoni' 'Notifiche "vetro" in stile Apple, errori morbidi, connessione dispositivi, cestino "dissolvenza".' $defaults.Sounds
Add-Toggle $ui.PanelTheme 'StartupSound' 'Suono "power-up" all''accesso' 'Il reattore si accende quando entri in Windows.' $defaults.StartupSound
Add-Toggle $ui.PanelTheme 'LockScreen' 'Schermata di blocco e di accesso' 'Reattore centrato sulla schermata di blocco (l''immagine attuale viene salvata).' $defaults.LockScreen
Add-Toggle $ui.PanelTheme 'Fonts' 'Font HUD' 'Rajdhani e Share Tech Mono (licenza OFL), installati solo per il tuo utente.' $defaults.Fonts

Add-Group $ui.PanelTheme 'Preferenze'
$cityCard = New-Object System.Windows.Controls.Border
$cityCard.Style = $window.FindResource('RowCard')
$cityCard.Cursor = [System.Windows.Input.Cursors]::Arrow
$cityStack = New-Object System.Windows.Controls.StackPanel
[void]$cityStack.Children.Add((New-TextBlock 'Citta'' per il meteo' 'RowTitle'))
[void]$cityStack.Children.Add((New-TextBlock 'Lascia vuoto per la posizione automatica. Esempi: Roma, Milano, Napoli.' 'RowDesc'))
$script:CityBox = New-Object System.Windows.Controls.TextBox
$script:CityBox.Style = $window.FindResource('CityBox')
$script:CityBox.Margin = '0,10,0,0'
$script:CityBox.MaxWidth = 320
$script:CityBox.HorizontalAlignment = 'Left'
$script:CityBox.MinWidth = 240
[void]$cityStack.Children.Add($script:CityBox)
$cityCard.Child = $cityStack
[void]$ui.PanelTheme.Children.Add($cityCard)
Add-Toggle $ui.PanelTheme 'Clock24' 'Orologio 24 ore' 'Disattiva per il formato 12 ore (AM/PM).' $defaults.Clock24

Add-Group $ui.PanelComponents 'Livello 2 // Overlay open source (nessuna iniezione nei processi di sistema)'
Add-Toggle $ui.PanelComponents 'Lively' 'Sfondo animato Arc Reactor' 'Lively Wallpaper: anelli in rotazione sulla GPU, telemetria CPU/RAM/GPU/rete, scheda Now Playing. Si mette in pausa con giochi e video a schermo intero.' $defaults.Lively 'consigliato'
Add-Toggle $ui.PanelComponents 'LivelyAudio' 'Reattore che pulsa con la musica' 'Il nucleo reagisce all''audio di sistema. Leggero, ma tiene attiva la cattura audio: lascialo spento se vuoi il massimo risparmio.' $defaults.LivelyAudio
Add-Toggle $ui.PanelComponents 'Rainmeter' 'Widget HUD sul desktop' 'Rainmeter: orologio stile Apple TV, meteo, anelli telemetria stile Apple Watch, control center stile HyperOS. Scalati per schermi HiDPI/4K.' $defaults.Rainmeter 'consigliato'
Add-Toggle $ui.PanelComponents 'FlowLauncher' 'Ricerca stile Spotlight (Alt+Spazio)' 'Flow Launcher con tema Stark HUD: app, file, calcoli e web da un''unica barra.' $defaults.FlowLauncher
Add-Toggle $ui.PanelComponents 'TranslucentTB' 'Barra delle applicazioni trasparente' 'TranslucentTB: trasparente sul desktop, acrilico grafite con finestre aperte, opaca in risparmio batteria.' $defaults.TranslucentTB
Add-Toggle $ui.PanelComponents 'TaskbarX' 'Icone centrate stile dock' 'TaskbarX 1.7.8 (archiviato, ma senza iniezioni): download verificato con SHA256, avvio automatico per il tuo utente.' $defaults.TaskbarX

Add-Group $ui.PanelComponents 'Livello 3 // Avanzato (modifica Esplora risorse, opzionale)'
Add-Toggle $ui.PanelComponents 'Nilesoft' 'Menu contestuale Stark' 'Nilesoft Shell: menu del tasto destro in acrilico scuro con accenti ciano. Richiede UAC.' $defaults.Nilesoft 'avanzato'
Add-Toggle $ui.PanelComponents 'Windhawk' 'Windhawk (mod della shell)' 'Installa Windhawk: poi scegli tu le mod compatibili con Windows 10 (orologio personalizzato, menu scuri). Vedi docs\RICERCA.md.' $defaults.Windhawk 'avanzato'

$tweaks = Get-StarkTweaks
$groupLabels = @{
    'Fluidità' = 'Fluidità // reattività dell''interfaccia'
    'Prestazioni' = 'Prestazioni // senza toccare servizi o driver'
    'Pulizia' = 'Pulizia // meno distrazioni'
    'Interfaccia' = 'Interfaccia // look minimale'
}
foreach ($group in ($tweaks | ForEach-Object { $_.group } | Select-Object -Unique)) {
    $label = if ($groupLabels.ContainsKey($group)) { $groupLabels[$group] } else { $group }
    Add-Group $ui.PanelTweaks $label
    foreach ($t in ($tweaks | Where-Object { $_.group -eq $group })) {
        Add-Toggle $ui.PanelTweaks ("tw:" + $t.id) $t.title $t.desc ([bool]$t.default)
    }
}
Add-Group $ui.PanelTweaks 'Sicurezza'
Add-Toggle $ui.PanelTweaks 'RestorePoint' 'Punto di ripristino di Windows' 'Oltre al backup Stark HUD, crea un punto di ripristino di sistema (richiede UAC).' $defaults.RestorePoint 'consigliato'
Add-Toggle $ui.PanelTweaks 'RestartExplorer' 'Riavvia Esplora risorse alla fine' 'Applica subito barra e colori. Le finestre di Esplora file aperte verranno chiuse.' $defaults.RestartExplorer

# --- Stato del sistema ----------------------------------------------------------------
$envInfo = Test-StarkEnvironment
$display = Get-StarkDisplay
$check = { param($ok) if ($ok) { '[ OK ]' } else { '[ !! ]' } }
$osLine = if ($envInfo.IsWin10) { "Windows 10 build $($envInfo.Build)" } elseif ($envInfo.IsWin11) { "Windows 11 build $($envInfo.Build) (parziale)" } else { "Build $($envInfo.Build) non supportata" }
$lines = @(
    ('{0} {1}' -f (& $check ($envInfo.IsWin10 -or $envInfo.IsWin11)), $osLine)
    ('{0} winget {1}' -f (& $check $envInfo.HasWinget), $(if ($envInfo.HasWinget) { 'disponibile' } else { 'mancante: solo Livello 1' }))
    ('[ OK ] Schermo {0}x{1} @{2}%' -f $display.Width, $display.Height, [int]($display.Scale * 100))
    ('[ OK ] Backup automatico di ogni modifica')
)
$state = Join-Path $env:LOCALAPPDATA 'StarkHUD\state\backup.json'
if (Test-Path $state) { $lines += '[ ** ] Stark HUD gia'' installato' }
$ui.InfoText.Text = $lines -join "`n"
if (-not $envInfo.HasWinget) {
    foreach ($k in 'Lively', 'LivelyAudio', 'Rainmeter', 'FlowLauncher', 'TranslucentTB', 'Nilesoft', 'Windhawk') {
        $script:Toggles[$k].IsChecked = $false
        $script:Toggles[$k].IsEnabled = $false
    }
}

# --- Navigazione -----------------------------------------------------------------------
$panels = @{ TabTheme = 'PanelTheme'; TabComponents = 'PanelComponents'; TabTweaks = 'PanelTweaks'; TabLog = 'PanelLog' }
function Show-Panel([string]$Tab) {
    foreach ($k in $panels.Keys) {
        $ui[$panels[$k]].Visibility = if ($k -eq $Tab) { 'Visible' } else { 'Collapsed' }
    }
    $ui.Scroller.ScrollToTop()
}
foreach ($k in $panels.Keys) {
    $ui[$k].Tag = $k
    $ui[$k].Add_Checked({ param($s, $e) Show-Panel $s.Tag })
}

$ui.TitleBar.Add_MouseLeftButtonDown({ $window.DragMove() })
$ui.BtnMin.Add_Click({ $window.WindowState = 'Minimized' })

# --- Registro e avanzamento ----------------------------------------------------------------
$levelColors = @{ info = '#8FA6B8'; ok = '#5FE0A0'; warn = '#F2B544'; error = '#FF6B5E'; step = '#38D6FF' }
function Add-LogLine([string]$Message, [string]$Level) {
    $t = New-Object System.Windows.Controls.TextBlock
    $t.Text = ('{0:HH:mm:ss}  {1}' -f (Get-Date), $Message)
    $t.Foreground = $bc.ConvertFromString($levelColors[$Level])
    $t.FontFamily = $window.Resources['Mono']
    $t.FontSize = 12.5
    $t.TextWrapping = 'Wrap'
    $t.Margin = '2,0,0,6'
    [void]$ui.PanelLog.Children.Add($t)
    if ($ui.PanelLog.Visibility -eq 'Visible') { $ui.Scroller.ScrollToEnd() }
}

function Set-Progress([int]$Percent) {
    $anim = New-Object System.Windows.Media.Animation.DoubleAnimation
    $anim.To = [math]::Max(0, [math]::Min(1, $Percent / 100))
    $anim.Duration = [TimeSpan]::FromMilliseconds(450)
    $ease = New-Object System.Windows.Media.Animation.CubicEase
    $ease.EasingMode = 'EaseOut'
    $anim.EasingFunction = $ease
    $ui.ProgressScale.BeginAnimation([System.Windows.Media.ScaleTransform]::ScaleXProperty, $anim)
}

# --- Lavoro in background (la finestra resta fluida) --------------------------------------
$sync = [hashtable]::Synchronized(@{
        Queue = New-Object 'System.Collections.Concurrent.ConcurrentQueue[object]'
        Done = $false
    })
$script:Job = $null

function Start-StarkJob([string]$Mode, [hashtable]$Options, [bool]$RemoveAppsFlag) {
    $sync.Done = $false
    $ui.BtnInstall.IsEnabled = $false
    $ui.BtnRestore.IsEnabled = $false
    $ui.TabLog.IsChecked = $true
    Set-Progress 2

    $rs = [runspacefactory]::CreateRunspace()
    $rs.ApartmentState = 'STA'
    $rs.ThreadOptions = 'ReuseThread'
    $rs.Open()
    $rs.SessionStateProxy.SetVariable('sync', $sync)
    $ps = [powershell]::Create()
    $ps.Runspace = $rs
    [void]$ps.AddScript({
            param($ModulePath, $Mode, $Options, $RemoveAppsFlag)
            try {
                Import-Module $ModulePath -Force
                Set-StarkLogger { param($m, $l, $p) $sync.Queue.Enqueue(@{ m = $m; l = $l; p = $p }) }
                if ($Mode -eq 'install') { Install-StarkHUD -Options $Options }
                else { Restore-StarkHUD -RemoveApps:$RemoveAppsFlag }
            } catch {
                $sync.Queue.Enqueue(@{ m = $_.Exception.Message; l = 'error'; p = -1 })
            } finally {
                $sync.Done = $true
            }
        }).AddArgument((Join-Path $Root 'src\StarkHUD.psm1')).AddArgument($Mode).AddArgument($Options).AddArgument($RemoveAppsFlag)
    $script:Job = @{ PS = $ps; RS = $rs; Handle = $ps.BeginInvoke() }
}

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(120)
$timer.Add_Tick({
        $item = $null
        while ($sync.Queue.TryDequeue([ref]$item)) {
            Add-LogLine $item.m $item.l
            if ($item.l -ne 'info') { $ui.StatusText.Text = $item.m }
            if ($item.p -ge 0) { Set-Progress $item.p }
        }
        if ($sync.Done -and $script:Job) {
            try { $script:Job.PS.EndInvoke($script:Job.Handle) } catch { }
            $script:Job.PS.Dispose(); $script:Job.RS.Dispose()
            $script:Job = $null
            $ui.BtnInstall.IsEnabled = $true
            $ui.BtnRestore.IsEnabled = $true
        }
    })
$timer.Start()

function Get-SelectedOptions {
    $o = Get-StarkPreset 'Default'
    foreach ($k in 'Theme', 'Cursors', 'Sounds', 'StartupSound', 'LockScreen', 'Fonts', 'Clock24', 'Lively', 'LivelyAudio',
        'Rainmeter', 'FlowLauncher', 'TranslucentTB', 'TaskbarX', 'Nilesoft', 'Windhawk', 'RestorePoint', 'RestartExplorer') {
        $o[$k] = [bool]$script:Toggles[$k].IsChecked
    }
    $o.Tweaks = @($tweaks | Where-Object { $script:Toggles['tw:' + $_.id].IsChecked } | ForEach-Object { $_.id })
    $o.City = $script:CityBox.Text.Trim()
    if ($o.LivelyAudio -and -not $o.Lively) { $o.LivelyAudio = $false }
    $o
}

$ui.BtnInstall.Add_Click({
        $o = Get-SelectedOptions
        $msg = "Installare Stark HUD con le opzioni scelte?`n`n" +
        "- Ogni modifica viene salvata e si annulla con 'Ripristina originale'.`n" +
        "- Per le app aggiuntive possono comparire richieste UAC di winget.`n" +
        $(if ($o.RestartExplorer) { "- Alla fine Esplora risorse si riavvia (le sue finestre aperte si chiudono).`n" } else { '' })
        $r = [System.Windows.MessageBox]::Show($window, $msg, 'Stark HUD', 'YesNo', 'Question')
        if ($r -eq 'Yes') { Start-StarkJob 'install' $o $false }
    })

$ui.BtnRestore.Add_Click({
        $remove = [bool]$ui.ChkRemoveApps.IsChecked
        $msg = "Ripristinare Windows esattamente com'era prima di Stark HUD?" +
        $(if ($remove) { "`n`nVerranno disinstallate anche le app aggiunte da Stark HUD." } else { '' })
        $r = [System.Windows.MessageBox]::Show($window, $msg, 'Stark HUD', 'YesNo', 'Warning')
        if ($r -eq 'Yes') { Start-StarkJob 'restore' @{} $remove }
    })

$ui.BtnClose.Add_Click({ $window.Close() })
$window.Add_Closing({
        param($s, $e)
        if ($script:Job) {
            [void][System.Windows.MessageBox]::Show($window, "Attendi la fine dell'operazione in corso: interromperla a meta' lascerebbe il sistema in uno stato intermedio (comunque ripristinabile).", 'Stark HUD', 'OK', 'Information')
            $e.Cancel = $true
        } else { $timer.Stop() }
    })

Add-LogLine "Stark HUD pronto. Cartella del pack: $Root" 'info'

if ($SmokeTest) {
    # Verifica la mappatura opzioni -> interruttori, poi fotografa ogni scheda e chiude
    $opts = Get-SelectedOptions
    Write-Output ('SMOKE opzioni: ' + (($opts.Keys | Sort-Object) -join ', '))
    Write-Output ('SMOKE ottimizzazioni selezionate: ' + ($opts.Tweaks -join ', '))
    $script:Shots = @('TabTheme', 'TabComponents', 'TabTweaks')
    $script:ShotIndex = 0
    $shotTimer = New-Object System.Windows.Threading.DispatcherTimer
    $shotTimer.Interval = [TimeSpan]::FromMilliseconds(1500)
    $shotTimer.Add_Tick({
            try {
                $content = $window.Content
                $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap([int]$content.ActualWidth, [int]$content.ActualHeight, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
                $rtb.Render($content)
                if ($Screenshot) {
                    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
                    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
                    $file = $Screenshot -replace '\.png$', ('-{0}.png' -f $script:Shots[$script:ShotIndex])
                    $fs = [IO.File]::Create($file)
                    try { $enc.Save($fs) } finally { $fs.Close() }
                    Write-Host "SMOKE screenshot: $file"
                }
                $script:ShotIndex++
                if ($script:ShotIndex -ge $script:Shots.Count) { $shotTimer.Stop(); $window.Close() }
                else { $ui[$script:Shots[$script:ShotIndex]].IsChecked = $true }
            } catch {
                Write-Host "SMOKE errore: $($_.Exception.Message)"
                $shotTimer.Stop(); $window.Close()
                $script:SmokeFailed = $true
            }
        })
    $shotTimer.Start()
}

[void]$window.ShowDialog()
if ($SmokeTest -and $script:SmokeFailed) { exit 1 }
