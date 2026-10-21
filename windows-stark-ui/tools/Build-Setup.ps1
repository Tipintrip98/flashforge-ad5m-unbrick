<#
    Stark HUD - costruisce W\StarkHUD-Setup.exe: un solo file con dentro tutto il pack.

    Gira su Windows (PowerShell 5.1) e usa il compilatore C# incluso in Windows (.NET Framework 4):
    nessun programma da installare. Ricostruisce l'exe solo se e' cambiato un file del pack o del
    programma di setup (impronta in W\StarkHUD-Setup.sha256), cosi' l'exe nel repository resta stabile.

    Uso:  powershell -ExecutionPolicy Bypass -File tools\Build-Setup.ps1 [-Force] [-ListOnly]
#>
param(
    [switch]$Force,
    # restituisce solo l'elenco dei file del pack (percorso nel pack -> file sorgente), usato dai test
    [switch]$ListOnly
)
$ErrorActionPreference = 'Stop'
$pack = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $pack
$outDir = Join-Path $repo 'W'
$exe = Join-Path $outDir 'StarkHUD-Setup.exe'
$hashFile = Join-Path $outDir 'StarkHUD-Setup.sha256'
$setupDir = Join-Path $PSScriptRoot 'setup'

# file che servono all'installer (niente test, strumenti e documentazione)
$map = [ordered]@{}
foreach ($item in 'Installa.cmd', 'Ripristina.cmd', 'StarkUI.ps1', 'README.md', 'src', 'config', 'theme', 'components') {
    $src = Join-Path $pack $item
    if (Test-Path -LiteralPath $src -PathType Container) {
        foreach ($f in Get-ChildItem -LiteralPath $src -Recurse -File | Sort-Object FullName) {
            $map[$f.FullName.Substring($pack.Length + 1)] = $f.FullName
        }
    } else {
        $map[$item] = $src
    }
}
$map['LICENSE.txt'] = Join-Path $repo 'LICENSE'
if ($ListOnly) { return $map }

# impronta del contenuto: pack + sorgente del setup
$lines = foreach ($k in $map.Keys) { '{0} {1}' -f (Get-FileHash -LiteralPath $map[$k] -Algorithm SHA256).Hash, ($k -replace '\\', '/') }
$lines += foreach ($f in Get-ChildItem -LiteralPath $setupDir -File | Sort-Object Name) { '{0} setup/{1}' -f (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash, $f.Name }
$sha = [Security.Cryptography.SHA256]::Create()
$fingerprint = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))) | ForEach-Object { $_.ToString('x2') })

$current = if (Test-Path -LiteralPath $hashFile) { (Get-Content -LiteralPath $hashFile -Raw).Trim() } else { '' }
if (-not $Force -and (Test-Path -LiteralPath $exe) -and $current -eq $fingerprint) {
    Write-Host "StarkHUD-Setup.exe e' gia' aggiornato ($fingerprint)."
    return [pscustomobject]@{ Built = $false; Fingerprint = $fingerprint; Files = $map.Count }
}

# 1) pack.zip con la stessa struttura della cartella windows-stark-ui
$work = Join-Path ([IO.Path]::GetTempPath()) ('starkhud-setup-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$stage = Join-Path $work 'pack'
foreach ($k in $map.Keys) {
    $dst = Join-Path $stage $k
    New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
    Copy-Item -LiteralPath $map[$k] -Destination $dst
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $work 'pack.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

# 2) exe con il compilatore C# di .NET Framework
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "Compilatore C# non trovato: $csc" }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$tmpExe = Join-Path $work 'StarkHUD-Setup.exe'
& $csc /nologo /target:winexe /optimize+ "/out:$tmpExe" "/win32icon:$(Join-Path $setupDir 'StarkHUD.ico')" "/resource:$zip,StarkHUD.pack.zip" `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll (Join-Path $setupDir 'StarkSetup.cs') | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita (codice $LASTEXITCODE)" }
Copy-Item -LiteralPath $tmpExe -Destination $exe -Force
Set-Content -LiteralPath $hashFile -Value $fingerprint -Encoding Ascii
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue

$size = (Get-Item -LiteralPath $exe).Length / 1MB
Write-Host ("StarkHUD-Setup.exe creato: {0:N1} MB, {1} file del pack." -f $size, $map.Count)
[pscustomobject]@{ Built = $true; Fingerprint = $fingerprint; Files = $map.Count }
