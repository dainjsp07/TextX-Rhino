<#
  Builds, tests and packages Text X with the .NET SDK (Windows).

    powershell -ExecutionPolicy Bypass -File tools\build.ps1              build + test + package
    powershell -ExecutionPolicy Bypass -File tools\build.ps1 -NoPack      build + test only
    ... -RhinoSystemDir "D:\Rhino 8\System"                                 if Rhino is not in the default folder
    ... -UseRhinoNuGet                                                      compile against the RhinoCommon NuGet package

  Requirements: .NET SDK 8 (https://dotnet.microsoft.com/download), Rhino 8 (or -UseRhinoNuGet).
  Unicode conformance data for the full test run: put BidiTest.txt and BidiCharacterTest.txt
  (https://www.unicode.org/Public/UCD/latest/ucd/) in tests\data. Without them only the unit tests run.
#>
param(
  [switch]$NoPack,
  [switch]$SkipTests,
  [switch]$UseRhinoNuGet,
  [string]$RhinoSystemDir = ""
)
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot 'TextX.Build.psm1') -Force
$root = Get-TextXRoot
$product = Get-TextXProduct -Root $root
Write-Host "== Building $($product.ProductName) $($product.Version)"

$buildArgs = @('build', (Join-Path $root 'src/TextX/TextX.csproj'), '-c', 'Release', '-f', 'net48', '-nologo')
if ($UseRhinoNuGet) { $buildArgs += '-p:UseRhinoNuGet=true' }
if ($RhinoSystemDir) { $buildArgs += "-p:RhinoSystemDir=$RhinoSystemDir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$rhp = Join-Path $root 'src/TextX/bin/Release/net48/TextX.rhp'

if (-not $SkipTests) {
  Write-Host "== Testing"
  $data = Join-Path $root 'tests/data'
  & dotnet run --project (Join-Path $root 'tests/TextX.Tests/TextX.Tests.csproj') -c Release -- $data
  if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (-not $NoPack) {
  Write-Host "== Packaging"
  & (Join-Path $PSScriptRoot 'pack.ps1') -Rhp $rhp
}
