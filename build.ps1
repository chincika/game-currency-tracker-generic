$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root "tools\currency-tracker-winforms\CurrencyTracker.cs"
$icon = Join-Path $root "tools\currency-tracker-winforms\assets\app-icon.ico"
$manifest = Join-Path $root "tools\currency-tracker-winforms\app.manifest"
$outputDir = Join-Path $root "tools\currency-tracker-winforms\dist"
$outputName = -join ([char[]](0x91D1, 0x6761, 0x66F4, 0x65B0, 0x8BB0, 0x5F55)) + ".exe"
$output = Join-Path $outputDir $outputName
$tempOutput = Join-Path $outputDir "GoldBarTracker.build.exe"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

& $compiler /nologo /target:winexe /win32icon:$icon /win32manifest:$manifest /out:$tempOutput /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.DataVisualization.dll $source
Copy-Item -LiteralPath $tempOutput -Destination $output -Force
Remove-Item -LiteralPath $tempOutput -Force

Write-Host "Built $output"
