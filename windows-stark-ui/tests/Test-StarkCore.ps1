<#
    Stark HUD - test delle funzioni pure (nessuna modifica al sistema).
    Funziona con Windows PowerShell 5.1 e con PowerShell 7 (anche su Linux/macOS).

    Uso:  powershell -ExecutionPolicy Bypass -File tests\Test-StarkCore.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src\StarkHUD.psm1') -Force

$script:failed = 0
$script:passed = 0
function Assert-Equal($Actual, $Expected, [string]$Name) {
    if ("$Actual" -ceq "$Expected") { $script:passed++; Write-Host "  ok   $Name" -ForegroundColor Green }
    else { $script:failed++; Write-Host "  FAIL $Name`n       atteso:  $Expected`n       ottenuto: $Actual" -ForegroundColor Red }
}
function Assert-True($Condition, [string]$Name) { Assert-Equal ([bool]$Condition) $true $Name }

Write-Host 'Colori'
Assert-Equal ('{0:X8}' -f (Get-StarkArgb -Hex '#0096C7')) 'C40096C7' 'ARGB con alpha C4'
Assert-Equal ('{0:X8}' -f (Get-StarkAbgr -Hex '#0096C7')) 'FFC79600' 'ABGR per AccentColor'
Assert-Equal (ConvertTo-StarkDword (Get-StarkAbgr -Hex '#0096C7')) -3697152 'DWORD con segno per il registro'
$pal = Get-StarkAccentPalette -Hex '#0096C7'
Assert-Equal $pal.Length 32 'Tavolozza da 32 byte'
Assert-Equal ('{0:X2}{1:X2}{2:X2}' -f $pal[12], $pal[13], $pal[14]) '0096C7' 'Colore base in posizione 4'
Assert-Equal ('{0:X2}{1:X2}{2:X2}' -f $pal[28], $pal[29], $pal[30]) 'F2B544' 'Oro Stark come complementare'
Assert-True ($pal[0] -gt $pal[12] -and $pal[16] -lt $pal[13]) 'Tinte chiare prima, scure dopo'

Write-Host 'Sfondo'
Assert-Equal (Select-StarkWallpaper -Width 3440 -Height 1440) 'StarkHUD-Desktop-Ultrawide.jpg' 'Ultrawide 21:9'
Assert-Equal (Select-StarkWallpaper -Width 1920 -Height 1080) 'StarkHUD-Desktop-4K.jpg' 'Full HD 16:9'
Assert-Equal (Select-StarkWallpaper -Width 1920 -Height 1200) 'StarkHUD-Desktop-4K.jpg' '16:10'

Write-Host 'File .theme'
$theme = New-StarkThemeContent -WallpaperPath 'C:\W\wall.jpg' -CursorDir 'C:\W\cursors' -SoundDir 'C:\W\sounds'
Assert-True ($theme -match '(?m)^ColorizationColor=0XC40096C7\r?$') 'Colore accento nel tema'
Assert-True ($theme -match '(?m)^Wait=C:.W.cursors.busy\.ani\r?$') 'Cursore di attesa animato'
Assert-True ($theme -match '(?m)^\[AppEvents\\Schemes\\Apps\\Explorer\\EmptyRecycleBin\]\r?$') 'Evento cestino'
Assert-True ($theme -match '(?m)^SystemMode=Dark\r?$') 'Modalita scura'
Assert-Equal ([regex]::Matches($theme, '(?m)^\[').Count) (6 + 1 + 13) 'Numero di sezioni'
$bare = New-StarkThemeContent -WallpaperPath 'C:\W\wall.jpg'
Assert-True ($bare -notmatch 'Control Panel\\Cursors' -and $bare -notmatch '\[Sounds\]') 'Tema senza cursori e suoni'

Write-Host 'Layout Rainmeter'
$layout = Get-StarkRainmeterLayout -ScreenWidth 3840 -ScreenHeight 2160 -Scale 2
Assert-True ($layout -match '(?m)^\[StarkHUD\\Control\]\r?$') 'Sezione Control'
Assert-True ($layout -match '(?m)^WindowX=269\r?$') 'Margine sinistro 7%'
Assert-True ($layout -match '(?m)^WindowY=1648\r?$') 'Schede sopra la barra a scala 2'
Assert-True ($layout -match '(?m)^WindowX=1429\r?$') 'Control dopo Weather e Stats a scala 2'

Write-Host 'JSON Flow Launcher'
$j = '{' + "`r`n" + '  "Hotkey": "Alt + Space",' + "`r`n" + '  "Theme": "Win11Light",' + "`r`n" + '  "UseClock": false,' + "`r`n" + '  "WindowSize": 580' + "`r`n" + '}'
$j2 = Set-StarkJsonScalar -Json $j -Key 'Theme' -Value 'StarkHUD'
$j2 = Set-StarkJsonScalar -Json $j2 -Key 'UseClock' -Value $true
$j2 = Set-StarkJsonScalar -Json $j2 -Key 'WindowSize' -Value 660
$j2 = Set-StarkJsonScalar -Json $j2 -Key 'QueryBoxFont' -Value 'Rajdhani'
$obj = $j2 | ConvertFrom-Json
Assert-Equal $obj.Theme 'StarkHUD' 'Sostituisce stringa'
Assert-Equal $obj.UseClock $true 'Sostituisce booleano'
Assert-Equal $obj.WindowSize 660 'Sostituisce numero'
Assert-Equal $obj.QueryBoxFont 'Rajdhani' 'Aggiunge chiave mancante'
Assert-Equal $obj.Hotkey 'Alt + Space' 'Non tocca le altre chiavi'
$empty = (Set-StarkJsonScalar -Json '{}' -Key 'Theme' -Value 'StarkHUD') | ConvertFrom-Json
Assert-Equal $empty.Theme 'StarkHUD' 'File impostazioni vuoto'
$quoted = (Set-StarkJsonScalar -Json '{"A":"x"}' -Key 'A' -Value 'C:\a "b"') | ConvertFrom-Json
Assert-Equal $quoted.A 'C:\a "b"' 'Escape di backslash e virgolette'

Write-Host 'Catalogo ottimizzazioni'
$tweaks = Get-StarkTweaks
Assert-True ($tweaks.Count -ge 10) 'Almeno 10 ottimizzazioni'
$ids = @($tweaks | ForEach-Object { $_.id })
Assert-Equal (@($ids | Select-Object -Unique).Count) $ids.Count 'Id univoci'
foreach ($t in $tweaks) {
    $type = if ($t.PSObject.Properties['type']) { $t.type } else { 'registry' }
    if ($type -eq 'registry') {
        foreach ($r in $t.registry) {
            if ($r.path -notmatch '^HKCU:\\') { Assert-True $false "Solo HKCU: $($t.id) -> $($r.path)" }
            if ('String', 'DWord' -notcontains $r.type) { Assert-True $false "Tipo valido: $($t.id)" }
        }
    }
}
Assert-True $true 'Tutte le chiavi sono in HKCU'
$preset = Get-StarkPreset 'Default'
Assert-True ($preset.Tweaks -contains 'menu-delay' -and $preset.Tweaks -notcontains 'power-high') 'Preset predefinito prudente'
Assert-Equal (Get-StarkPreset 'Minimal').Rainmeter $false 'Preset minimo solo nativo'

Write-Host 'Registro (regressioni trovate sui runner Windows)'
$bin = ConvertTo-StarkRegData -Value (Get-StarkAccentPalette -Hex '#0096C7') -Type Binary
Assert-Equal $bin.GetType().Name 'Byte[]' 'Binary resta byte[] (prima diventava object[])'
Assert-Equal $bin.Length 32 'Binary conserva i 32 byte'
$multi = ConvertTo-StarkRegData -Value @('a') -Type MultiString
Assert-Equal $multi.GetType().Name 'String[]' 'MultiString resta string[] anche con un solo elemento'
Assert-Equal (ConvertTo-StarkRegData -Value (Get-StarkAbgr -Hex '#0096C7') -Type DWord) -3697152 'DWORD oltre Int32 convertito con segno'

Write-Host 'winget (regressione: flag spezzato in caratteri)'
& (Get-Module StarkHUD) { $script:WingetFlags = @('--disable-interactivity') }
$line = & (Get-Module StarkHUD) { Join-StarkArguments (@('list', '--id', 'Rainmeter.Rainmeter', '-e') + @(Get-StarkWingetFlags)) }
Assert-Equal $line 'list --id Rainmeter.Rainmeter -e --disable-interactivity' 'Il flag arriva intero a winget'
Assert-Equal (Join-StarkArguments @('setwp', '--file', 'C:\Users\Mario Rossi\x')) 'setwp --file "C:\Users\Mario Rossi\x"' 'Percorsi con spazi tra virgolette'
& (Get-Module StarkHUD) { $script:WingetFlags = $null }

Write-Host 'Sicurezza'
$threw = $false
try { Set-StarkRegValue -Path 'HKLM:\SOFTWARE\Test' -Name 'x' -Value 1 -Type DWord } catch { $threw = $_.Exception.Message -match 'solo in HKCU' }
Assert-True $threw 'Rifiuta scritture fuori da HKCU'

Write-Host ''
Write-Host ("Risultato: {0} ok, {1} falliti" -f $script:passed, $script:failed) -ForegroundColor $(if ($script:failed) { 'Red' } else { 'Green' })
exit $script:failed
