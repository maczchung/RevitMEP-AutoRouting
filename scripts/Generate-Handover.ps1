<#
.SYNOPSIS
    Generate a daily handover report (Markdown) for MEP Auto Routing.

.DESCRIPTION
    Collects Git history, changed files, build result, TODO/FIXME markers,
    open GitHub Issues and manual notes (docs/HANDOVER_NOTES.md), then writes:
        reports/YYYY/Handover_YYYY-MM-DD.md
        reports/LATEST.md
    Works in GitHub Actions (pwsh 7) and locally (Windows PowerShell 5.1 / pwsh 7).

.EXAMPLE
    ./scripts/Generate-Handover.ps1
    ./scripts/Generate-Handover.ps1 -BuildOutcome success -BuildLog build.log -Hours 48
#>
[CmdletBinding()]
param(
    [string]$ProjectName  = 'MEP Auto Routing',
    [string]$OutputDir    = 'reports',
    [string]$NotesFile    = 'docs/HANDOVER_NOTES.md',
    [string]$BuildOutcome = 'skipped',     # success | failure | skipped
    [string]$BuildLog     = 'build.log',
    [int]   $Hours        = 24,
    [int]   $MaxTodos     = 40
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding           = [System.Text.Encoding]::UTF8
git config core.quotepath off 2>$null

# ---------- Helpers ----------
function Invoke-Git([string[]]$GitArgs) {
    $out = & git @GitArgs 2>$null
    if ($null -eq $out) { return @() }
    return @($out)
}
function Write-Utf8([string]$Path, [string]$Text) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path)), $Text, (New-Object System.Text.UTF8Encoding($false)))
}
function Get-NotesSection([string]$Notes, [string]$Heading) {
    # Returns body text under "## <Heading>" until next "## "
    $pattern = '(?ms)^##[ \t]+' + [regex]::Escape($Heading) + '[ \t]*\r?$(.*?)(?=^##[ \t]|\z)'
    $m = [regex]::Match($Notes, $pattern)
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

# ---------- Time (HKT) ----------
$nowUtc  = [DateTime]::UtcNow
$nowHkt  = $nowUtc.AddHours(8)
$dateStr = $nowHkt.ToString('yyyy-MM-dd')
$timeStr = $nowHkt.ToString('yyyy-MM-dd HH:mm') + ' HKT'
$since   = $nowUtc.AddHours(-$Hours).ToString('yyyy-MM-ddTHH:mm:ssZ')

# ---------- Git info ----------
$branch     = (Invoke-Git @('rev-parse','--abbrev-ref','HEAD')) -join ''
$lastCommit = (Invoke-Git @('log','-1','--pretty=format:%h | %an | %ad | %s','--date=format:%Y-%m-%d %H:%M')) -join ''
$lastTag    = (Invoke-Git @('describe','--tags','--abbrev=0')) -join ''
if (-not $lastTag) { $lastTag = '(no tag)' }

# Commits in window (exclude bot report commits)
$commits = Invoke-Git @('log',"--since=$since",'--invert-grep','--grep=\[handover\]',
                        '--pretty=format:%h|%an|%ad|%s','--date=format:%m-%d %H:%M')
$commits = @($commits | Where-Object { $_ -and $_.Trim() })

# Changed files in window
$fileLines = Invoke-Git @('log',"--since=$since",'--invert-grep','--grep=\[handover\]',
                          '--name-status','--pretty=format:')
$changed = [ordered]@{}
foreach ($l in $fileLines) {
    if (-not $l -or -not $l.Trim()) { continue }
    $parts = $l -split "`t"
    if ($parts.Count -lt 2) { continue }
    $status = $parts[0].Substring(0,1)
    $file   = $parts[-1]
    if ($file -like "$OutputDir/*") { continue }
    if (-not $changed.Contains($file)) { $changed[$file] = $status }
}
$added    = @($changed.Keys | Where-Object { $changed[$_] -eq 'A' })
$modified = @($changed.Keys | Where-Object { $changed[$_] -eq 'M' -or $changed[$_] -eq 'R' })
$deleted  = @($changed.Keys | Where-Object { $changed[$_] -eq 'D' })

# Line stats
$ins = 0; $del = 0
$numstat = Invoke-Git @('log',"--since=$since",'--invert-grep','--grep=\[handover\]','--numstat','--pretty=format:')
foreach ($l in $numstat) {
    $p = $l -split "`t"
    if ($p.Count -ge 3 -and $p[0] -match '^\d+$') { $ins += [int]$p[0]; $del += [int]$p[1] }
}

# ---------- Version ----------
$version = ''
if (Test-Path 'VERSION') { $version = (Get-Content 'VERSION' -TotalCount 1).Trim() }
if (-not $version) {
    $csproj = Get-ChildItem -Recurse -Filter *.csproj -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($csproj) {
        $m = Select-String -Path $csproj.FullName -Pattern '<(Version|AssemblyVersion)>([^<]+)<' | Select-Object -First 1
        if ($m) { $version = $m.Matches[0].Groups[2].Value }
    }
}
if (-not $version) { $version = $lastTag }

# ---------- Build ----------
# Emoji built from code points so the .ps1 stays pure ASCII (safe for Windows PowerShell 5.1)
$icoPass = [char]::ConvertFromUtf32(0x2705); $icoFail = [char]::ConvertFromUtf32(0x274C); $icoSkip = [char]::ConvertFromUtf32(0x23ED)
$buildIcon = switch ($BuildOutcome) { 'success' {"$icoPass PASS"} 'failure' {"$icoFail FAIL"} default {"$icoSkip SKIPPED"} }
$errCount = '-'; $warnCount = '-'; $buildErrors = @()
if (Test-Path $BuildLog) {
    $log = Get-Content $BuildLog -ErrorAction SilentlyContinue
    $w = $log | Select-String -Pattern '(\d+)\s+Warning\(s\)' | Select-Object -Last 1
    $e = $log | Select-String -Pattern '(\d+)\s+Error\(s\)'   | Select-Object -Last 1
    if ($w) { $warnCount = $w.Matches[0].Groups[1].Value }
    if ($e) { $errCount  = $e.Matches[0].Groups[1].Value }
    $buildErrors = @($log | Select-String -Pattern ':\s+error\s+' | Select-Object -First 15 | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique)
}

# ---------- TODO / FIXME ----------
$todos = Invoke-Git @('grep','-n','-I','-E','(TODO|FIXME|HACK)','--','*.cs','*.xaml','*.ps1')
$todoTotal = @($todos).Count
$todos = @($todos | Select-Object -First $MaxTodos)

# ---------- GitHub Issues ----------
$issuesMd = '_GitHub CLI not available or no token - skipped._'
if (Get-Command gh -ErrorAction SilentlyContinue) {
    try {
        $json = gh issue list --state open --limit 30 --json number,title,labels 2>$null
        if ($LASTEXITCODE -eq 0 -and $json) {
            $issues = $json | ConvertFrom-Json
            if (@($issues).Count -eq 0) { $issuesMd = '_No open issues._' }
            else {
                $issuesMd = (@($issues) | ForEach-Object {
                    $lbl = (@($_.labels) | ForEach-Object { $_.name }) -join ', '
                    if ($lbl) { "- #$($_.number) $($_.title)  ``[$lbl]``" } else { "- #$($_.number) $($_.title)" }
                }) -join "`n"
            }
        }
    } catch { }
}

# ---------- Manual notes ----------
$notes = ''
if (Test-Path $NotesFile) { $notes = [System.IO.File]::ReadAllText((Resolve-Path $NotesFile).Path, [System.Text.Encoding]::UTF8) }
$nStatus   = Get-NotesSection $notes 'Status'
$nEnv      = Get-NotesSection $notes 'Environment'
$nDone     = Get-NotesSection $notes 'Completed Features'
$nToday    = Get-NotesSection $notes 'Today'
$nIssues   = Get-NotesSection $notes 'Known Issues'
$nNext     = Get-NotesSection $notes 'Next Priority'
$nTests    = Get-NotesSection $notes 'Test Results'
$nDecision = Get-NotesSection $notes 'Design Decisions'
function OrDash([string]$s) { if ($s) { $s } else { '_(not filled in docs/HANDOVER_NOTES.md)_' } }

# ---------- Build Markdown ----------
$sb = New-Object System.Text.StringBuilder
function Add([string]$s = '') { [void]$sb.AppendLine($s) }

Add "# $ProjectName - Daily Handover Report"
Add ''
Add '| Item | Value |'
Add '|---|---|'
Add "| Date | $dateStr |"
Add "| Generated | $timeStr |"
Add "| Version | $version |"
Add "| Branch | ``$branch`` |"
Add "| Latest tag | $lastTag |"
Add "| Latest commit | $lastCommit |"
Add "| Build | $buildIcon (Errors: $errCount / Warnings: $warnCount) |"
Add "| Activity (last $Hours h) | $(@($commits).Count) commits, $($changed.Count) files, +$ins / -$del lines |"
Add ''
Add '## 1. Status'
Add (OrDash $nStatus)
Add ''
Add '## 2. Environment'
Add (OrDash $nEnv)
Add ''
Add "## 3. Commits (last $Hours h)"
if (@($commits).Count -eq 0) { Add '_No new commits._' }
else {
    Add '| Hash | Author | Time | Message |'
    Add '|---|---|---|---|'
    foreach ($c in $commits) {
        $p = $c -split '\|', 4
        if ($p.Count -eq 4) { Add "| ``$($p[0])`` | $($p[1]) | $($p[2]) | $($p[3] -replace '\|','/') |" }
    }
}
Add ''
Add '## 4. Files Changed'
Add "**Added ($($added.Count))**"
if ($added.Count)    { $added    | ForEach-Object { Add "- ``$_``" } } else { Add '- none' }
Add "`n**Modified ($($modified.Count))**"
if ($modified.Count) { $modified | ForEach-Object { Add "- ``$_``" } } else { Add '- none' }
Add "`n**Deleted ($($deleted.Count))**"
if ($deleted.Count)  { $deleted  | ForEach-Object { Add "- ``$_``" } } else { Add '- none' }
Add ''
Add '## 5. Build Result'
Add "- Result: $buildIcon"
Add "- Errors: $errCount"
Add "- Warnings: $warnCount"
if ($buildErrors.Count) {
    Add ''
    Add '```text'
    $buildErrors | ForEach-Object { Add $_ }
    Add '```'
}
Add ''
Add '## 6. Work Done Today (manual)'
Add (OrDash $nToday)
Add ''
Add '## 7. Completed Features'
Add (OrDash $nDone)
Add ''
Add '## 8. Test Results'
Add (OrDash $nTests)
Add ''
Add '## 9. Known Issues'
Add (OrDash $nIssues)
Add ''
Add '### Open GitHub Issues'
Add $issuesMd
Add ''
Add "## 10. TODO / FIXME in Code ($todoTotal found, showing up to $MaxTodos)"
if ($todos.Count) { Add '```text'; $todos | ForEach-Object { Add $_ }; Add '```' } else { Add '_None._' }
Add ''
Add '## 11. Next Priority'
Add (OrDash $nNext)
Add ''
Add '## 12. Design Decisions'
Add (OrDash $nDecision)
Add ''
Add '## 13. Recovery Prompt (copy into a new Copilot chat)'
Add '```text'
Add "Continue development of $ProjectName (Revit add-in)."
Add "Assume previous chat history is lost. Use this handover as the source of truth."
Add ''
Add "Current version: $version"
Add "Branch: $branch | Latest commit: $lastCommit"
Add "Build status: $BuildOutcome (Errors: $errCount, Warnings: $warnCount)"
Add ''
Add 'Environment:'
Add $(if ($nEnv) { $nEnv } else { '- (see repo)' })
Add ''
Add 'Completed features:'
Add $(if ($nDone) { $nDone } else { '- (see repo)' })
Add ''
Add 'Known issues:'
Add $(if ($nIssues) { $nIssues } else { '- none recorded' })
Add ''
Add 'Recent commits:'
if (@($commits).Count) { $commits | Select-Object -First 10 | ForEach-Object { Add "- $(($_ -split '\|',4)[-1])" } } else { Add '- none in last period' }
Add ''
Add 'Next priority:'
Add $(if ($nNext) { $nNext } else { '- (not set)' })
Add ''
Add 'Continue from the latest stable state. Ask me to paste specific source files if needed.'
Add '```'
Add ''
Add '---'
Add "_Auto-generated by scripts/Generate-Handover.ps1 - Cundall HK / Matthew Kwok_"

$report = $sb.ToString()

# ---------- Write ----------
$yearDir   = Join-Path $OutputDir $nowHkt.ToString('yyyy')
$dailyPath = Join-Path $yearDir "Handover_$dateStr.md"
$latest    = Join-Path $OutputDir 'LATEST.md'
Write-Utf8 $dailyPath $report
Write-Utf8 $latest    $report

Write-Host "Report written: $dailyPath"
Write-Host "Latest copy  : $latest"
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $report -Encoding utf8 }
