<#
  Packages a built TextX.rhp for distribution. Output in dist\v<version>\ :

    textx-<version>-rh8_0-win.yak     the Rhino package: drag it into Rhino 8, or publish it with "yak push"
    TextX-<version>-Windows.zip       for sharing by e-mail / chat / drive: the .yak plus install instructions
    latest.json                       the update feed (attach it to the GitHub release)
    release-notes.md                  this version's section of CHANGELOG.md
    SHA256SUMS.txt                    checksums

    powershell -ExecutionPolicy Bypass -File tools\pack.ps1 [-Rhp <path to TextX.rhp>]
#>
param(
  [string]$Rhp = "",
  [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot 'TextX.Build.psm1') -Force
$root = Get-TextXRoot
$product = Get-TextXProduct -Root $root
$v = $product.Version

if (-not $Rhp) {
  $candidates = @(
    (Join-Path $root 'src/TextX/bin/Release/net48/TextX.rhp'),
    (Join-Path $root 'artifacts/bin/net48/TextX.rhp')
  ) | Where-Object { Test-Path $_ } | Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending
  if (-not $candidates) { throw "TextX.rhp not found. Build first (tools\build.ps1) or pass -Rhp." }
  $Rhp = @($candidates)[0]
}
$Rhp = (Resolve-Path $Rhp).Path

# the plug-in must carry the version being packaged
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Rhp)
if ($info.ProductVersion -and ($info.ProductVersion.Split('+')[0] -ne $v)) {
  throw "TextX.rhp reports version '$($info.ProductVersion)' but VERSION is '$v'. Rebuild before packaging."
}

if (-not $OutDir) { $OutDir = Join-Path $root ("dist/v" + $v) }
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Path $OutDir | Out-Null
$stage = Join-Path $OutDir '_stage'
New-Item -ItemType Directory -Path $stage | Out-Null

$pkg = $product.PackageName
$yakName = "$pkg-$v-rh8_0-win.yak"
$zipName = ($product.ProductName -replace '\s', '') + "-$v-Windows.zip"
$notes = Get-TextXReleaseNotes -Version $v -Root $root
if (-not $notes) { Write-Warning "CHANGELOG.md has no section for $v." }

# ---- manifest.yml (Rhino package manager)
$desc = ($product.Description -replace '\s+', ' ').Trim()
$manifest = New-Object System.Collections.Generic.List[string]
$manifest.Add('---')
$manifest.Add("name: $pkg")
$manifest.Add("version: $v")
$manifest.Add('authors:')
foreach ($a in ($product.Authors -split ';')) { if ($a.Trim()) { $manifest.Add("- $($a.Trim())") } }
$manifest.Add('description: >')
$manifest.Add("  $desc")
if ($product.Website) { $manifest.Add("url: $($product.Website)") } else { Write-Warning "No website / GitHub repository set: the package has no url (needed for 'yak push')." }
$manifest.Add('icon: icon.png')
$manifest.Add('keywords:')
foreach ($k in @('translation', 'translate', 'hebrew', 'rtl', 'text', 'leader', 'annotation', 'layout', 'pdf', 'guid:6f2c6b0e-4f7a-4e1b-9d6a-2b7f1d8a3c51')) { $manifest.Add("- $k") }
$manifestPath = Join-Path $stage 'manifest.yml'
[System.IO.File]::WriteAllLines($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

# ---- the .yak package
$items = @(
  @{ File = $manifestPath; Name = 'manifest.yml' },
  @{ File = $Rhp; Name = 'TextX.rhp' },
  @{ File = (Join-Path $root 'packaging/icon.png'); Name = 'icon.png' },
  @{ File = (Join-Path $root 'README.md'); Name = 'misc/README.md' },
  @{ File = (Join-Path $root 'CHANGELOG.md'); Name = 'misc/CHANGELOG.md' },
  @{ File = (Join-Path $root 'LICENSE'); Name = 'misc/LICENSE.txt' },
  @{ File = (Join-Path $root 'samples/glossary-sample-he.csv'); Name = 'misc/glossary-sample-he.csv' }
)
$yakPath = Join-Path $OutDir $yakName
New-TextXZip -Path $yakPath -Items $items

# ---- the shareable zip
$guide = (Get-Content -Raw -Encoding UTF8 (Join-Path $root 'packaging/INSTALL.txt'))
$guide = $guide.Replace('{VERSION}', $v).Replace('{YAK}', $yakName).Replace('{PRODUCT}', $product.ProductName)
$guidePath = Join-Path $stage 'Install Text X.txt'
[System.IO.File]::WriteAllText($guidePath, ($guide -replace "`r?`n", "`r`n"), (New-Object System.Text.UTF8Encoding($true)))
New-TextXZip -Path (Join-Path $OutDir $zipName) -Items @(
  @{ File = $yakPath; Name = $yakName },
  @{ File = $guidePath; Name = 'Install Text X.txt' },
  @{ File = (Join-Path $root 'CHANGELOG.md'); Name = 'CHANGELOG.md' }
)

# ---- release notes and update feed
$notesPath = Join-Path $OutDir 'release-notes.md'
$body = "## $($product.ProductName) $v`n`n$notes`n`n### Install`nDownload **$yakName**, drag it into Rhino 8 and restart Rhino. Type **TR** to open $($product.ProductName).`n"
[System.IO.File]::WriteAllText($notesPath, $body, (New-Object System.Text.UTF8Encoding($false)))

$download = ''; $page = ''
if ($product.GitHubRepo) {
  $download = "https://github.com/$($product.GitHubRepo)/releases/download/v$v/$yakName"
  $page = "https://github.com/$($product.GitHubRepo)/releases/tag/v$v"
}
$feed = [ordered]@{
  name     = $product.ProductName
  package  = $pkg
  version  = $v
  released = (Get-Date).ToString('yyyy-MM-dd')
  download = $download
  page     = $page
  notes    = $notes
  minRhino = '8.0'
  sha256   = (Get-TextXSha256 $yakPath)
}
$feedJson = $feed | ConvertTo-Json -Depth 3
[System.IO.File]::WriteAllText((Join-Path $OutDir 'latest.json'), $feedJson, (New-Object System.Text.UTF8Encoding($false)))

$sums = Get-ChildItem -Path $OutDir -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name |
  ForEach-Object { (Get-TextXSha256 $_.FullName) + '  ' + $_.Name }
[System.IO.File]::WriteAllLines((Join-Path $OutDir 'SHA256SUMS.txt'), $sums, (New-Object System.Text.UTF8Encoding($false)))
Remove-Item -Recurse -Force $stage

Write-Host ""
Write-Host "$($product.ProductName) $v packaged in $OutDir"
Get-ChildItem $OutDir | ForEach-Object { Write-Host ("  {0,-40} {1,10:N0} bytes" -f $_.Name, $_.Length) }
