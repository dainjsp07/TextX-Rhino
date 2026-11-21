<#
  Development builds: runs Text X Studio outside Rhino (tests/TextX.UiPreview) through its screens
  with the sample document made by the tests, and saves screenshots in artifacts/test-output/ui-<theme>.
  Needs the natives, OCR data and sample from tools/build.ps1 (run it first).
#>
param([string[]]$Themes = @('light', 'dark'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts/test-output'
$bin = Join-Path $root 'artifacts/ui-preview'
$env:TEXTX_NATIVES = Join-Path $root 'artifacts/natives'
$env:TEXTX_TESSDATA = Join-Path $root 'artifacts/tessdata'
& dotnet build (Join-Path $root 'tests/TextX.UiPreview/TextX.UiPreview.csproj') -c Release -o $bin -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "UI preview build failed" }
$sample = Join-Path $out 'sample-fr.pdf'
$problems = 0
foreach ($theme in $Themes) {
  $env:TEXTX_PREVIEW_THEME = $theme
  $log = Join-Path $out "ui-preview-$theme.log"
  & (Join-Path $bin 'TextX.UiPreview.exe') (Join-Path $out "ui-$theme") $sample *>&1 | Tee-Object -FilePath $log
  $problems += $LASTEXITCODE
}
if ($problems -ne 0) { Write-Output "::warning title=UI preview::$problems problem(s), see ui-preview-*.log"; exit 1 }
