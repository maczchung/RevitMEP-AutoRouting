# 用法 (project root):  powershell -ExecutionPolicy Bypass -File .\Deploy-Revit2025.ps1
# 先關咗 Revit 2025，否則 dll 會被鎖住。
param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root   = $PSScriptRoot
$proj   = Join-Path $root "MEPAutoRouting.csproj"
$target = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2025"
$old    = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\MEPAutoRouting.addin"

dotnet build $proj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$out = Join-Path $root "bin\$Configuration\net8.0-windows"
if (-not (Test-Path $out)) { throw "Output folder not found: $out" }
New-Item -ItemType Directory -Force -Path $target | Out-Null

Get-ChildItem $out -Filter *.dll |
    Where-Object { $_.Name -notmatch '^(RevitAPI|RevitAPIUI|AdWindows|UIFramework)' } |
    ForEach-Object { Copy-Item $_.FullName $target -Force; Write-Host "  + $($_.Name)" }
Copy-Item (Join-Path $root "MEPAutoRouting.addin") $target -Force

if (Test-Path $old) { Remove-Item $old -Force; Write-Host "Removed old 2024 manifest" -ForegroundColor Yellow }
Write-Host "Done -> $target" -ForegroundColor Green
