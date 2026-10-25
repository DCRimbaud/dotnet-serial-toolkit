# SPDX-License-Identifier: MIT
# Author: Xue Weisen <xueweisen96@gmail.com>
# GitHub: https://github.com/DCRimbaud
# ============================================================================
# check_dotnet_architecture.ps1 -- .NET host application dependency audit
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\check_dotnet_architecture.ps1 -Root <software root>
#   powershell -ExecutionPolicy Bypass -File scripts\check_dotnet_architecture.ps1 -Root <root> -FailOnWarning
#
# Checks:
#   - project-reference direction
#   - reference cycles
#   - Domain/Application/Presentation.Core I/O leaks
#   - Presentation -> Infrastructure leaks
#   - Infrastructure/Profile -> UI leaks
# ============================================================================
param(
    [string]$Root = ".",
    [switch]$FailOnWarning
)

$ErrorActionPreference = "Stop"
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$projects = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter *.csproj |
              Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })

function Get-ProjectLayer([string]$name) {
    if ($name -match '\.Domain$') { return 'Domain' }
    if ($name -match '\.Application$') { return 'Application' }
    if ($name -match '\.Protocol$') { return 'Protocol' }
    if ($name -match '\.Presentation\.Core$') { return 'PresentationCore' }
    if ($name -match '\.Presentation$') { return 'Presentation' }
    if ($name -match '\.Infrastructure(\.|$)') { return 'Infrastructure' }
    if ($name -match '\.Profiles\.') { return 'Profile' }
    if ($name -match '\.Tests$') { return 'Tests' }
    if ($name -match '\.PackageBuilder$') { return 'Tool' }
    if ($name -match '\.App$') { return 'Composition' }
    return 'Unknown'
}

function Test-AllowedReference([string]$fromLayer, [string]$toLayer) {
    if ($fromLayer -eq 'Composition' -or $fromLayer -eq 'Tests') { return $true }
    switch ($fromLayer) {
        'Domain' { return $false }
        'Application' { return $toLayer -in @('Domain') }
        'Protocol' { return $toLayer -in @('Domain') }
        'Infrastructure' { return $toLayer -in @('Application','Domain','Protocol') }
        'Profile' { return $toLayer -in @('Application','Domain','Protocol') }
        'PresentationCore' { return $toLayer -in @('Application','Domain') }
        'Presentation' { return $toLayer -in @('PresentationCore') }
        'Tool' { return $toLayer -in @('Application','Domain','Protocol','Infrastructure','Profile') }
        default { return $false }
    }
}

$errors = 0
$warnings = 0

$projectMap = @{}
foreach ($p in $projects) {
    # Windows PowerShell 5.1 may decode Get-Content with the system code page.
    # Read project XML explicitly as UTF-8 to avoid truncating non-ASCII metadata.
    $projectText = [IO.File]::ReadAllText($p.FullName, [Text.Encoding]::UTF8)
    [xml]$xml = $projectText
    $projectMap[$p.FullName] = [PSCustomObject]@{
        Name = $p.BaseName
        Path = $p.FullName
        Layer = Get-ProjectLayer $p.BaseName
        Xml = $xml
    }
}

$refs = @()
foreach ($p in $projectMap.Values) {
    $nodes = @($p.Xml.SelectNodes('//*[local-name()="ProjectReference"]'))
    foreach ($node in $nodes) {
        $include = $node.GetAttribute('Include')
        if (-not $include -or $include -match '\$\(') { continue }
        $refPath = [IO.Path]::GetFullPath((Join-Path (Split-Path $p.Path -Parent) $include)).Replace('/','\')
        if (-not $projectMap.ContainsKey($refPath)) {
            Write-Output "[WARN] unresolved project reference: $($p.Name) -> $include"
            $warnings++
            continue
        }
        $target = $projectMap[$refPath]
        $refs += [PSCustomObject]@{ From = $p; To = $target }
        if (-not (Test-AllowedReference $p.Layer $target.Layer)) {
            Write-Output "[ERROR] forbidden project reference: $($p.Name) [$($p.Layer)] -> $($target.Name) [$($target.Layer)]"
            $errors++
        }
    }
}

$visiting = @{}
$visited = @{}
function Visit-Project([string]$path) {
    if ($visiting.ContainsKey($path)) {
        Write-Output "[ERROR] project reference cycle at: $($projectMap[$path].Name)"
        $script:errors++
        return
    }
    if ($visited.ContainsKey($path)) { return }
    $visiting[$path] = $true
    foreach ($edge in $refs | Where-Object { $_.From.Path -eq $path }) {
        Visit-Project $edge.To.Path
    }
    $visiting.Remove($path)
    $visited[$path] = $true
}
foreach ($p in $projectMap.Values) { Visit-Project $p.Path }

$rules = @{
    'Domain' = @(
        'using\s+System\.IO(?:\s*;|\.)', 'using\s+System\.IO\.Ports',
        'using\s+System\.Management', 'using\s+System\.Net(?:\s*;|\.)',
        'using\s+System\.Text\.Json',
        'using\s+(?:Avalonia|System\.Windows|Microsoft\.UI|Windows\.UI|Qt)',
        'using\s+Microsoft\.Extensions\.Logging',
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.(?:Infrastructure|Presentation)(?:\.|\s*;)',
        '\b(FileStream|StreamWriter|File\.|Path\.|Directory\.|SerialPort|HttpClient|DbContext)\b',
        '\bDateTime(?:Offset)?\.(?:Now|UtcNow)\b'
    )
    'Application' = @(
        'using\s+System\.IO(?:\s*;|\.)', 'using\s+System\.IO\.Ports',
        'using\s+System\.Management', 'using\s+System\.Net\.Http',
        'using\s+System\.Text\.Json',
        'using\s+(?:Avalonia|System\.Windows|Microsoft\.UI|Windows\.UI|Qt)',
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.(?:Infrastructure|Presentation)(?:\.|\s*;)',
        '\bnew\s+(?:FileStream|StreamWriter|SerialPort|HttpClient|DbContext)\b'
    )
    'Protocol' = @(
        'using\s+System\.IO(?:\s*;|\.)',
        'using\s+(?:Avalonia|System\.Windows|Microsoft\.UI|Windows\.UI|Qt)',
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.(?:Infrastructure|Presentation)(?:\.|\s*;)',
        '\b(SerialPort|HttpClient)\b'
    )
    'PresentationCore' = @(
        'using\s+System\.IO(?:\s*;|\.)', 'using\s+System\.IO\.Ports',
        'using\s+System\.Management', 'using\s+System\.Net\.Http',
        'using\s+(?:Avalonia|System\.Windows|Microsoft\.UI|Windows\.UI|Qt)',
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.Infrastructure(?:\.|\s*;)',
        '\b(FileStream|StreamWriter|File\.|Directory\.|Path\.|SerialPort|HttpClient)\b',
        '\bApp\.Current\b'
    )
    'Presentation' = @(
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.Infrastructure(?:\.|\s*;)',
        '\bnew\s+(?:SerialPort|HttpClient|DbContext)\b'
    )
    'Infrastructure' = @(
        'using\s+(?:Avalonia|System\.Windows|Microsoft\.UI|Windows\.UI|Qt)',
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.Presentation(?:\.|\s*;)',
        '\bApp\.Current\b'
    )
    'Profile' = @(
        'using\s+Avalonia',
        'using\s+[A-Za-z_][A-Za-z0-9_.]*\.(?:Infrastructure|Presentation)(?:\.|\s*;)',
        'using\s+System\.IO\.Ports'
    )
}

foreach ($p in $projectMap.Values) {
    if (-not $rules.ContainsKey($p.Layer)) { continue }
    $dir = Split-Path $p.Path -Parent
    $files = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter *.cs |
               Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    foreach ($f in $files) {
        $text = [IO.File]::ReadAllText($f.FullName, [Text.Encoding]::UTF8)
        foreach ($pattern in $rules[$p.Layer]) {
            if ($text -match $pattern) {
                Write-Output "[ERROR] layer leak: $($p.Name) [$($p.Layer)] -> $($f.FullName.Substring($rootPath.Length + 1)) pattern=$pattern"
                $errors++
            }
        }
        if ($p.Layer -in @('Domain','Application','Infrastructure','Profile','PresentationCore') -and
            $text -match 'async\s+void') {
            Write-Output "[WARN] async void outside UI event boundary: $($f.FullName.Substring($rootPath.Length + 1))"
            $warnings++
        }
        if ($p.Layer -in @('Domain','Application','PresentationCore') -and
            $text -match '\b(?:GetRequiredService|GetService)\s*\(') {
            Write-Output "[WARN] possible service locator: $($f.FullName.Substring($rootPath.Length + 1))"
            $warnings++
        }
        if ($p.Layer -in @('Domain','Application','PresentationCore') -and
            $text -match '\bTask\.Run\s*\(') {
            Write-Output "[WARN] Task.Run inside core/application layer: $($f.FullName.Substring($rootPath.Length + 1))"
            $warnings++
        }
        if ($f.FullName -match '[\\/]ViewModels?[\\/]') {
            $vmLeak = $text -match '\b(FileStream|StreamWriter|File\.|Directory\.|Path\.|SerialPort|HttpClient)\b'
            if ($vmLeak) {
                Write-Output "[ERROR] ViewModel direct I/O leak: $($f.FullName.Substring($rootPath.Length + 1))"
                $errors++
            }
            if ($text -match '\bTask\.Run\s*\(') {
                Write-Output "[WARN] ViewModel uses Task.Run; move blocking work behind an adapter: $($f.FullName.Substring($rootPath.Length + 1))"
                $warnings++
            }
        }
        if ($p.Layer -eq 'Presentation' -and $text -match 'async\s+void') {
            Write-Output "[WARN] async void in UI layer; verify it is an event boundary with top-level exception handling: $($f.FullName.Substring($rootPath.Length + 1))"
            $warnings++
        }
    }
}

Write-Output ""
Write-Output "DOTNET ARCH RESULT: projects=$($projects.Count) refs=$($refs.Count) errors=$errors warnings=$warnings"
if ($errors -gt 0 -or ($FailOnWarning -and $warnings -gt 0)) { exit 1 }
exit 0
