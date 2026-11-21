<#
  Downloads the native libraries that Text X Documents uses (Windows x64) into artifacts\natives:

    textx_pdfium.dll            PDFium (reads, renders and writes PDF files)      from bblanchon.PDFium.Win32
    x64\tesseract50.dll         Tesseract OCR (scanned pages)                     from the Tesseract package
    x64\leptonica-1.82.0.dll    Leptonica (image library Tesseract needs)

  pdfium.dll is renamed so that it can never clash with another copy loaded by Rhino or a plug-in.
  Versions come from tools\natives.json ("latest" = newest stable, printed so it can be pinned).

    pwsh tools\get-natives.ps1                       download if missing
    pwsh tools\get-natives.ps1 -Force                download again
    pwsh tools\get-natives.ps1 -TessData eng,osd     also fetch OCR language data (for tests) into artifacts\tessdata
#>
param(
  [string]$OutDir = "",
  [switch]$Force,
  [string[]]$TessData = @()
)
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot 'TextX.Build.psm1') -Force
$root = Get-TextXRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts/natives' }
$cfg = Get-Content (Join-Path $PSScriptRoot 'natives.json') -Raw | ConvertFrom-Json
$cache = Join-Path $root 'artifacts/nuget-cache'
New-Item -ItemType Directory -Force -Path $OutDir, (Join-Path $OutDir 'x64'), $cache | Out-Null

function Resolve-Version([string]$id, [string]$version) {
  if ($version -and $version -ne 'latest') { return $version }
  $index = Invoke-RestMethod -Uri ("https://api.nuget.org/v3-flatcontainer/{0}/index.json" -f $id.ToLowerInvariant())
  $stable = @($index.versions | Where-Object { $_ -notmatch '-' })
  if ($stable.Count -eq 0) { throw "No stable version of $id found." }
  return $stable[-1]
}

function Get-Package([string]$id, [string]$version) {
  $lid = $id.ToLowerInvariant()
  $folder = Join-Path $cache "$lid.$version"
  if ((Test-Path $folder) -and -not $Force) { return $folder }
  $nupkg = Join-Path $cache "$lid.$version.nupkg"
  $url = "https://api.nuget.org/v3-flatcontainer/$lid/$version/$lid.$version.nupkg"
  Write-Host "   downloading $id $version"
  Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
  if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $folder)
  return $folder
}

function Find-One([string]$folder, [string]$pattern, [string]$mustContain) {
  $hits = @(Get-ChildItem -Path $folder -Recurse -File -Filter $pattern | Where-Object { $_.FullName.Replace('\', '/') -match $mustContain })
  if ($hits.Count -eq 0) { throw "$pattern ($mustContain) not found in $folder." }
  return $hits[0].FullName
}

$resolved = [ordered]@{}

# --- PDFium
$pdfiumTarget = Join-Path $OutDir 'textx_pdfium.dll'
if ($Force -or -not (Test-Path $pdfiumTarget)) {
  $v = Resolve-Version $cfg.pdfium.package $cfg.pdfium.version
  $pkg = Get-Package $cfg.pdfium.package $v
  Copy-Item (Find-One $pkg 'pdfium.dll' 'win-x64') $pdfiumTarget -Force
  $resolved.pdfium = "$($cfg.pdfium.package) $v"
}

# --- Tesseract + Leptonica
$tessTarget = Join-Path $OutDir 'x64/tesseract50.dll'
if ($Force -or -not (Test-Path $tessTarget)) {
  $v = Resolve-Version $cfg.tesseract.package $cfg.tesseract.version
  $pkg = Get-Package $cfg.tesseract.package $v
  Copy-Item (Find-One $pkg 'tesseract*.dll' '/x64/') (Join-Path $OutDir 'x64') -Force
  Copy-Item (Find-One $pkg 'leptonica*.dll' '/x64/') (Join-Path $OutDir 'x64') -Force
  $resolved.tesseract = "$($cfg.tesseract.package) $v"
}

foreach ($k in $resolved.Keys) { Write-Host "   $k : $($resolved[$k])" }
if ($env:GITHUB_ACTIONS -eq 'true' -and $resolved.Count -gt 0) {
  Write-Output ("::notice title=Native libraries::" + (($resolved.Keys | ForEach-Object { "$_ = $($resolved[$_])" }) -join ', '))
}

# --- OCR language data (tests only; Text X downloads what it needs at run time)
if ($TessData.Count -gt 0) {
  $tess = Join-Path $root 'artifacts/tessdata'
  New-Item -ItemType Directory -Force -Path $tess | Out-Null
  foreach ($lang in $TessData) {
    $file = Join-Path $tess "$lang.traineddata"
    if ((Test-Path $file) -and -not $Force) { continue }
    $url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/$lang.traineddata"
    Write-Host "   downloading OCR data $lang"
    Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
  }
}

Get-ChildItem -Recurse -File $OutDir | ForEach-Object { Write-Host ("   {0,-28} {1,10:N0} bytes" -f $_.FullName.Substring($OutDir.Length + 1), $_.Length) }
