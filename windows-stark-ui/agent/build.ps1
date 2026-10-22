<#
    Tipaldi AI - compila agent\bin\TipaldiAI.exe con il compilatore C# incluso in Windows (.NET Framework 4).
    Nessun SDK da installare. Copia accanto all'exe i font e le icone dei contesti.

    Uso:  powershell -ExecutionPolicy Bypass -File agent\build.ps1 [-Out <cartella>]
#>
param([string]$Out = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$pack = Split-Path -Parent $PSScriptRoot
$src = Join-Path $PSScriptRoot 'src'
$fw = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw "Compilatore C# non trovato: $csc" }

New-Item -ItemType Directory -Path $Out -Force | Out-Null
$exe = Join-Path $Out 'TipaldiAI.exe'
$sources = @(Get-ChildItem -LiteralPath $src -Filter *.cs | ForEach-Object { $_.FullName })
$refs = 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Management.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Xaml.dll', 'WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll',
    'UIAutomationClient.dll', 'UIAutomationTypes.dll', 'UIAutomationProvider.dll'
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', "/out:$exe", "/lib:$(Join-Path $fw 'WPF')",
    "/win32icon:$(Join-Path $pack 'tools\setup\TipaldiAI.ico')", "/resource:$(Join-Path $src 'Hud.xaml'),TipaldiAI.Hud.xaml") +
    @($refs | ForEach-Object { "/r:$_" }) + $sources
& $csc $cscArgs | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Compilazione di Tipaldi AI fallita (codice $LASTEXITCODE)" }

# font del desktop e icone dei contesti
$fonts = Join-Path $Out 'fonts'
New-Item -ItemType Directory -Path $fonts -Force | Out-Null
Copy-Item -Path (Join-Path $pack 'theme\fonts\*.ttf') -Destination $fonts -Force
$icons = Join-Path $Out 'icons'
New-Item -ItemType Directory -Path $icons -Force | Out-Null
Copy-Item -Path (Join-Path $pack 'theme\icons\*.ico') -Destination $icons -Force
Write-Host ("TipaldiAI.exe compilato: {0:N0} KB" -f ((Get-Item -LiteralPath $exe).Length / 1KB))
$exe
