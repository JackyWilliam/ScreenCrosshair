param([string]$ArtifactsPath = '', [switch]$Interactive)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$ArtifactsPath) { $ArtifactsPath = Join-Path $PSScriptRoot 'artifacts' }
New-Item -ItemType Directory -Path $ArtifactsPath -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $ArtifactsPath 'r5apex.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $root 'source') -Filter '*.cs' | ForEach-Object FullName
# A synthetic foreground window exercises the actual capture guard; this does not launch Apex.
& $compiler /nologo /target:exe /main:RecognitionTests /define:CROSSHAIR_SELF_TEST /warnaserror+ "/out:$executable" "/win32manifest:$root\source\app.manifest" "/win32icon:$root\source\crosshair.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll $sources "$PSScriptRoot\RecognitionTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
if ($Interactive) { & $executable --interactive } else { & $executable }
if ($LASTEXITCODE -ne 0) { throw 'Recognition tests failed.' }
