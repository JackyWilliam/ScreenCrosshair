param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputFile = if ($OutputPath) { $OutputPath } else { Join-Path (Split-Path $PSScriptRoot -Parent) 'ScreenCrosshair.exe' }
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
# Use the compiler shipped with Windows so the app needs no SDK or extra packages.
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ "/out:$outputFile" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\crosshair.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Crosshair compilation failed.' }
Write-Output $outputFile
