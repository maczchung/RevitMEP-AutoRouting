<#
  Resolve-MergeConflicts.ps1  (MEPAutoRouting v4.4)
  Removes git conflict blocks and KEEPS THE HEAD SIDE (= v4.2 crash-fix version).
  A backup "<file>.conflict.bak" is written before each change.

  Usage (project root):
    powershell -ExecutionPolicy Bypass -File .\Tools\Resolve-MergeConflicts.ps1          # dry run
    powershell -ExecutionPolicy Bypass -File .\Tools\Resolve-MergeConflicts.ps1 -Apply   # write files
#>
param(
    [string]$Root = (Get-Location).Path,
    [switch]$Apply
)

$pattern = '(?ms)^<<<<<<< [^\r\n]*\r?\n(.*?)^=======[ \t]*\r?\n.*?^>>>>>>> [^\r\n]*(\r?\n|\z)'
$utf8Bom = New-Object System.Text.UTF8Encoding($true)

$files = Get-ChildItem -Path $Root -Recurse -File -Include *.cs,*.xaml,*.md,*.csproj,*.addin |
         Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git|\.vs)\\' }

$hit = 0
foreach ($f in $files) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    if ($text -notmatch '(?m)^<<<<<<< ') { continue }

    $blocks = ([regex]::Matches($text, $pattern)).Count
    $hit++
    $rel = $f.FullName.Substring($Root.Length)
    if (-not $Apply) { Write-Host "[CONFLICT] $rel  ($blocks block(s))" -ForegroundColor Yellow; continue }

    Copy-Item $f.FullName "$($f.FullName).conflict.bak" -Force
    $new = [regex]::Replace($text, $pattern, '$1')
    [System.IO.File]::WriteAllText($f.FullName, $new, $utf8Bom)

    if ($new -match '(?m)^(<<<<<<< |=======\s*$|>>>>>>> )') {
        Write-Host "[CHECK]    $rel  – markers still present, fix manually" -ForegroundColor Red
    } else {
        Write-Host "[FIXED]    $rel  ($blocks block(s), HEAD kept)" -ForegroundColor Green
    }
}

if ($hit -eq 0) { Write-Host "No conflict markers found." -ForegroundColor Green }
elseif (-not $Apply) { Write-Host "`n$hit file(s) have conflicts. Run again with -Apply to keep the HEAD side." }
else { Write-Host "`nDone. Now run: dotnet build -c Release" }
