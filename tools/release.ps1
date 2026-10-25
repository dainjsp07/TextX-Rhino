<#
  Makes a new release: bumps the version, dates the changelog, builds, tests and packages.

    1. Describe your changes under "## [Unreleased]" in CHANGELOG.md.
    2. Run one of:
         powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Bump patch     bug fixes          1.1.0 -> 1.1.1
         powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Bump minor     new features       1.1.0 -> 1.2.0
         powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Bump major     breaking changes   1.1.0 -> 2.0.0
         powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.2.0-beta.1               test version
    3. Share dist\v<version>\TextX-<version>-Windows.zip, or publish (see docs\RELEASING.md).
       -Publish commits, tags and pushes; GitHub Actions then builds the release and publishes it,
       including latest.json, so every installed copy is told about the new version.
#>
param(
  [ValidateSet('patch', 'minor', 'major')][string]$Bump = 'patch',
  [string]$Version = "",
  [switch]$SkipTests,
  [switch]$Publish,
  [switch]$UseRhinoNuGet,
  [string]$RhinoSystemDir = ""
)
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot 'TextX.Build.psm1') -Force
$root = Get-TextXRoot
$product = Get-TextXProduct -Root $root
$current = $product.Version

# ---- next version
if ($Version) {
  if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') { throw "Use a version like 1.2.0 or 1.2.0-beta.1." }
  $next = $Version
}
else {
  $m = [regex]::Match($current, '^(\d+)\.(\d+)\.(\d+)(-.+)?$')
  $maj = [int]$m.Groups[1].Value; $min = [int]$m.Groups[2].Value; $pat = [int]$m.Groups[3].Value; $pre = $m.Groups[4].Success
  switch ($Bump) {
    'major' { $next = "$($maj + 1).0.0" }
    'minor' { $next = "$maj.$($min + 1).0" }
    'patch' { if ($pre) { $next = "$maj.$min.$pat" } else { $next = "$maj.$min.$($pat + 1)" } }
  }
}
if ($next -eq $current) { throw "The new version equals the current one ($current)." }

# ---- changelog: "## [Unreleased]" -> "## [next] - date"
$changelog = Join-Path $root 'CHANGELOG.md'
$text = [System.IO.File]::ReadAllText($changelog)
$m = [regex]::Match($text, '(?ms)^## \[Unreleased\][^\n]*\n(?<body>.*?)(?=^## \[|\z)')
if (-not $m.Success) { throw "CHANGELOG.md needs a '## [Unreleased]' section." }
$body = $m.Groups['body'].Value.Trim()
if (-not $body -or $body -match '^\s*-\s*$') { throw "Describe the changes under '## [Unreleased]' in CHANGELOG.md first." }
$date = (Get-Date).ToString('yyyy-MM-dd')
$replacement = "## [Unreleased]`n`n## [$next] - $date`n`n$body`n`n"
$text = $text.Substring(0, $m.Index) + $replacement + $text.Substring($m.Index + $m.Length)
[System.IO.File]::WriteAllText($changelog, $text, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText((Join-Path $root 'VERSION'), "$next`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Version $current -> $next"

# ---- build, test, package
$buildArgs = @{}
if ($SkipTests) { $buildArgs['SkipTests'] = $true }
if ($UseRhinoNuGet) { $buildArgs['UseRhinoNuGet'] = $true }
if ($RhinoSystemDir) { $buildArgs['RhinoSystemDir'] = $RhinoSystemDir }
& (Join-Path $PSScriptRoot 'build.ps1') @buildArgs
$dist = Join-Path $root "dist/v$next"

if ($Publish) {
  # GitHub Actions (.github/workflows/build.yml) builds, tests and publishes the release when the tag arrives.
  git -C $root add -A
  git -C $root commit -m "Release $next"
  git -C $root tag "v$next"
  git -C $root push
  git -C $root push origin "v$next"
  if ($product.GitHubRepo) { Write-Host "Pushed v$next. The release appears at https://github.com/$($product.GitHubRepo)/releases in a few minutes." }
}
else {
  Write-Host ""
  Write-Host "Release $next is ready in $dist"
  Write-Host "Share TextX-$next-Windows.zip directly, or publish it on GitHub:"
  Write-Host "  powershell -File tools\release.ps1 ... -Publish   (or: git commit, git tag v$next, git push --follow-tags)"
}
