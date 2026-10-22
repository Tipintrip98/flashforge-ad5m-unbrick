<#
    Tipaldi AI - verifica di W\TipaldiAI-Setup.exe su Windows.

      1. l'exe estrae esattamente i file del pack attuale (stesso contenuto, nessun file in piu' o in meno)
      2. l'exe apre l'installer in modalita' test e lo chiude senza errori (screenshot di ogni scheda)

    Uso:  powershell -ExecutionPolicy Bypass -File tests\Test-StarkSetup.ps1 [-Exe ..\W\TipaldiAI-Setup.exe]
#>
param(
    [string]$Exe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'W\TipaldiAI-Setup.exe'),
    [string]$OutDir = (Join-Path $PSScriptRoot 'out')
)
$ErrorActionPreference = 'Stop'
$fail = 0
function Check([bool]$Condition, [string]$Name, [string]$Detail = '') {
    if ($Condition) { Write-Host "  ok   $Name" -ForegroundColor Green }
    else {
        $script:fail++
        Write-Host "  FAIL $Name $Detail" -ForegroundColor Red
        if ($env:GITHUB_ACTIONS) { Write-Host "::error title=TipaldiAI-Setup.exe::$Name $Detail" }
    }
}
function Invoke-Exe([string[]]$Arguments) {
    # Handle letto subito: senza, PowerShell 5.1 a volte non riporta il codice di uscita
    $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru
    $null = $p.Handle
    $p.WaitForExit()
    $p.ExitCode
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$Exe = (Resolve-Path -LiteralPath $Exe).Path
Write-Host ("TipaldiAI-Setup.exe: {0:N1} MB" -f ((Get-Item -LiteralPath $Exe).Length / 1MB))

# 1) contenuto
$expected = & (Join-Path (Split-Path -Parent $PSScriptRoot) 'tools\Build-Setup.ps1') -ListOnly
$dir = Join-Path ([IO.Path]::GetTempPath()) ('starkhud-extract-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$code = Invoke-Exe @('--estrai', "`"$dir`"")
Check ($code -eq 0) 'Estrazione senza errori' "(codice $code)"
$found = @(Get-ChildItem -LiteralPath $dir -Recurse -File)
Check ($found.Count -eq $expected.Count) 'Numero di file uguale al pack' "($($found.Count) invece di $($expected.Count))"
$diff = foreach ($k in $expected.Keys) {
    $x = Join-Path $dir $k
    if (-not (Test-Path -LiteralPath $x)) { "manca $k"; continue }
    # i binari compilati cambiano a ogni compilazione: per loro basta che ci siano
    if ($k -like 'agent\bin\*') { continue }
    if ((Get-FileHash -LiteralPath $x).Hash -ne (Get-FileHash -LiteralPath $expected[$k]).Hash) { "diverso $k" }
}
Check (@($diff).Count -eq 0) 'Contenuto identico al pack attuale' (@($diff | Select-Object -First 5) -join ', ')
Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue

# 2) installer avviato dall'exe
$shot = Join-Path $OutDir 'setup-exe.png'
$p = Start-Process -FilePath $Exe -ArgumentList @('--test', "`"$shot`"") -PassThru
$null = $p.Handle
$ownWindow = $false; $psWindow = $false
while (-not $p.HasExited) {
    # la finestra dell'installer deve appartenere a TipaldiAI-Setup.exe, non a powershell.exe
    $me = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
    if ($me -and $me.MainWindowTitle) { $ownWindow = $true }
    if (Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID -and $_.MainWindowTitle -like '*Tipaldi*' }) { $psWindow = $true }
    Start-Sleep -Milliseconds 300
}
$p.WaitForExit()
$code = $p.ExitCode
Check ($code -eq 0) 'Installer avviato dall''exe e chiuso senza errori' "(codice $code)"
Check $ownWindow 'Finestra dell''installer di TipaldiAI-Setup.exe (icona e nome propri nella barra)'
Check (-not $psWindow) 'Nessuna finestra di PowerShell'
Check (@(Get-ChildItem -LiteralPath $OutDir -Filter 'setup-exe-*.png').Count -ge 3) 'Screenshot delle schede'
Check (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'TipaldiAI\pack\StarkUI.ps1')) 'Pack estratto in %LOCALAPPDATA%\TipaldiAI\pack'

Write-Host ''
if ($fail) { Write-Host "$fail controlli falliti" -ForegroundColor Red; exit 1 }
Write-Host 'TipaldiAI-Setup.exe verificato.' -ForegroundColor Green
exit 0
