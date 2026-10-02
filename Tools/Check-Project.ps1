# 用法（project root）：powershell -ExecutionPolicy Bypass -File .\Tools\Check-Project.ps1
# 檢查：重複 class、中文用戶字串、舊 routing 路徑、Strategy、Transaction status
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$cs = Get-ChildItem $root -Recurse -Include *.cs -File |
      Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' }

Write-Host "`n=== 1. Duplicate helper classes (each should be 1) ===" -ForegroundColor Cyan
$classes = 'SlopeSettings','SlopeApplier','RouteProblem','PathValidator','BoundaryPicker','SpaceVolume',
           'MassVolume','SolidVolume','RoutingConstraints','RoutingOptions','RevitActionQueue',
           'ConnectorEndpoint','PipeSizeCatalog','RouteService','RouteFailureCollector','AppInfo','SpacePicker'
$dupFound = $false
foreach ($c in $classes) {
    $hits = $cs | Select-String -Pattern "\b(class|record|struct)\s+$c\b" -List
    $n = @($hits).Count
    if ($c -eq 'SpacePicker' -and $n -gt 0) {
        Write-Host ("  [OLD]  {0} still exists – delete it:" -f $c) -ForegroundColor Yellow
        $hits | ForEach-Object { Write-Host "         $($_.Path)" }
        $dupFound = $true
    } elseif ($n -gt 1) {
        Write-Host ("  [DUP]  {0} x{1}" -f $c, $n) -ForegroundColor Red
        $hits | ForEach-Object { Write-Host "         $($_.Path)" }
        $dupFound = $true
    } elseif ($n -eq 0 -and $c -ne 'SpacePicker') {
        Write-Host ("  [MISS] {0} not found" -f $c) -ForegroundColor Yellow
    }
}
if (-not $dupFound) { Write-Host "  OK" -ForegroundColor Green }

Write-Host "`n=== 2. Chinese text in string literals (should be none) ===" -ForegroundColor Cyan
$cjk = $cs | Select-String -Pattern '"[^"]*[\u4e00-\u9fff][^"]*"' |
       Where-Object { $_.Line.Trim() -notmatch '^//' }
if ($cjk) { $cjk | ForEach-Object { Write-Host ("  {0}:{1}  {2}" -f $_.Filename, $_.LineNumber, $_.Line.Trim()) -ForegroundColor Yellow } }
else { Write-Host "  OK" -ForegroundColor Green }

Write-Host "`n=== 3. Routing entry points ===" -ForegroundColor Cyan
foreach ($p in 'RouteService\.Run','ExecuteRoute\(','RoutingEventHandler','ExternalEvent\.Create','\.Raise\(\)') {
    $h = $cs | Select-String -Pattern $p
    Write-Host ("  {0,-24} {1} hit(s)" -f $p, @($h).Count)
    $h | Select-Object -First 5 | ForEach-Object { Write-Host ("      {0}:{1}" -f $_.Filename, $_.LineNumber) }
}
Write-Host "  -> RouteService.Run should only be called from ExecuteRoute; old RoutingEventHandler should not be raised."

Write-Host "`n=== 4. Strategy usage (does 'Horizontal X -> Y' bypass A*?) ===" -ForegroundColor Cyan
$s = $cs | Select-String -Pattern 'Strategy\.|RoutingStrategy\.|case\s+.*Strategy'
$s | Select-Object -First 15 | ForEach-Object { Write-Host ("  {0}:{1}  {2}" -f $_.Filename, $_.LineNumber, $_.Line.Trim()) }
Write-Host "  -> Every strategy should call AStarPathfinder.FindPath, not an old L-shape builder."

Write-Host "`n=== 5. Transaction commit status checked ===" -ForegroundColor Cyan
$tx = $cs | Select-String -Pattern 'TransactionStatus\.Committed'
if ($tx) { Write-Host "  OK ($(@($tx).Count) place(s))" -ForegroundColor Green }
else { Write-Host "  Not found – merge [v4.1-3] from Reference/RouteService.reference.cs.txt" -ForegroundColor Yellow }
Write-Host ""
