# SPDX-License-Identifier: MIT
# Author: Xue Weisen <xueweisen96@gmail.com>
# GitHub: https://github.com/DCRimbaud
# ============================================================================
# analyze_change_coupling.ps1 -- Git co-change and implicit-coupling analysis
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\analyze_change_coupling.ps1 `
#     -Root <repository-root> -Since "90 days ago" -Top 30
#
# Requires only Git and PowerShell; no Code Maat, Node, or network access.
# Reports by default; use -FailOnConfidence and -FailOnCoChanges for enforcement.
# ============================================================================
param(
    [string]$Root = ".",
    [string]$Since = "90 days ago",
    [int]$MinCoChanges = 2,
    [int]$Top = 30,
    [string]$Extensions = ".cs,.xaml,.csproj,.props,.targets,.c,.h,.cpp,.hpp,.cc,.cxx,.v,.sv,.vh,.svh,.vhd,.xdc,.tcl,.ps1,.py",
    [string]$PathPrefix = "",
    [switch]$ProductionOnly,
    [double]$FailOnConfidence = 0,
    [int]$FailOnCoChanges = 0,
    [string]$CsvPath = ""
)

$ErrorActionPreference = "Stop"
$rootPath = (Resolve-Path -LiteralPath $Root).Path
if (-not (Test-Path -LiteralPath (Join-Path $rootPath ".git"))) {
    throw "Not a Git worktree: $rootPath"
}

$allowedExtensions = @($Extensions -split ',' | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })

function Test-CodeFile([string]$path) {
    $normalized = $path.Replace('\', '/')
    if ($normalized -match '(^|/)(bin|obj|build|artifacts|\.git|\.vs|packages)/') { return $false }
    if ($PathPrefix) {
        $prefix = $PathPrefix.Replace('\', '/').TrimStart('/')
        if (-not $normalized.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
    }
    if ($ProductionOnly -and $normalized -match '(^|/)(tests?|testdata|__tests__)(/|$)') { return $false }
    $extension = [IO.Path]::GetExtension($normalized).ToLowerInvariant()
    return $allowedExtensions -contains $extension
}

Push-Location $rootPath
try {
    $log = & git log "--since=$Since" --name-only --pretty=format:'COMMIT|%H' 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git log failed: $($log -join ' ')"
    }
} finally {
    Pop-Location
}

$commits = [System.Collections.Generic.List[object]]::new()
$current = $null
foreach ($rawLine in $log) {
    $line = [string]$rawLine
    if ($line.StartsWith("COMMIT|", [System.StringComparison]::Ordinal)) {
        if ($null -ne $current -and $current.Count -gt 0) {
            $commits.Add($current.ToArray())
        }
        $current = [System.Collections.Generic.List[string]]::new()
        continue
    }

    $path = $line.Trim()
    if ($null -eq $current -or [string]::IsNullOrWhiteSpace($path)) { continue }
    if (Test-CodeFile $path) {
        $normalized = $path.Replace('\', '/')
        if (-not $current.Contains($normalized)) {
            $current.Add($normalized)
        }
    }
}
if ($null -ne $current -and $current.Count -gt 0) {
    $commits.Add($current.ToArray())
}

$fileCounts = @{}
$pairCounts = @{}
foreach ($files in $commits) {
    foreach ($file in $files) {
        if (-not $fileCounts.ContainsKey($file)) { $fileCounts[$file] = 0 }
        $fileCounts[$file]++
    }

    if ($files.Count -lt 2) { continue }
    $sorted = @($files | Sort-Object)
    for ($i = 0; $i -lt $sorted.Count; $i++) {
        for ($j = $i + 1; $j -lt $sorted.Count; $j++) {
            $key = "$($sorted[$i])`t$($sorted[$j])"
            if (-not $pairCounts.ContainsKey($key)) { $pairCounts[$key] = 0 }
            $pairCounts[$key]++
        }
    }
}

$pairs = foreach ($entry in $pairCounts.GetEnumerator()) {
    if ($entry.Value -lt $MinCoChanges) { continue }
    $parts = $entry.Key -split "`t", 2
    $a = $parts[0]
    $b = $parts[1]
    $countA = [int]$fileCounts[$a]
    $countB = [int]$fileCounts[$b]
    $minCount = [Math]::Min($countA, $countB)
    $union = $countA + $countB - $entry.Value
    [PSCustomObject]@{
        CoChanges = [int]$entry.Value
        Confidence = if ($minCount -gt 0) { [Math]::Round($entry.Value / $minCount, 3) } else { 0 }
        Jaccard = if ($union -gt 0) { [Math]::Round($entry.Value / $union, 3) } else { 0 }
        CountA = $countA
        CountB = $countB
        FileA = $a
        FileB = $b
    }
}

$ranked = @($pairs | Sort-Object CoChanges, Confidence -Descending | Select-Object -First $Top)
if ($CsvPath) {
    $csvFull = [IO.Path]::GetFullPath((Join-Path $rootPath $CsvPath))
    $ranked | Export-Csv -LiteralPath $csvFull -NoTypeInformation -Encoding utf8
    Write-Output "CSV: $csvFull"
}

Write-Output "CHANGE COUPLING RESULT: commits=$($commits.Count) files=$($fileCounts.Count) pairs=$($pairCounts.Count) shown=$($ranked.Count)"
$ranked | Format-Table -AutoSize -Wrap

if ($ranked.Count -gt 0 -and $FailOnCoChanges -gt 0 -and $FailOnConfidence -gt 0) {
    $violations = @($ranked | Where-Object {
        $_.CoChanges -ge $FailOnCoChanges -and $_.Confidence -ge $FailOnConfidence
    })
    if ($violations.Count -gt 0) {
        Write-Output "[FAIL] High co-change coupling pairs detected: $($violations.Count)"
        exit 1
    }
}
exit 0
