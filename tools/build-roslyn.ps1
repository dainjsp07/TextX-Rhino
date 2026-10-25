<#
  Builds TextX.rhp WITHOUT the .NET SDK (fallback for machines that only have PowerShell 7,
  which ships the Roslyn compiler). Normal builds use tools\build.ps1 (dotnet).

    pwsh tools/build-roslyn.ps1 -RhinoSystemDir "C:\Program Files\Rhino 8\System" -NetFxRefDir <.NET Framework 4.8 reference assemblies>

  -NetFxRefDir must contain mscorlib.dll, System*.dll and a Facades sub-folder
  (e.g. "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8").
#>
param(
  [Parameter(Mandatory = $true)][string]$RhinoSystemDir,
  [Parameter(Mandatory = $true)][string]$NetFxRefDir,
  [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot 'TextX.Build.psm1') -Force
$root = Get-TextXRoot
$product = Get-TextXProduct -Root $root
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts/bin/net48' }
$info = Join-Path $root 'artifacts/obj/TextX.AssemblyInfo.g.cs'
New-TextXAssemblyInfo -Product $product -Path $info

$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Net.Http.dll', 'System.Xml.dll', 'System.Runtime.Serialization.dll',
  'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll') |
  ForEach-Object { Join-Path $NetFxRefDir $_ }
$refs += @('RhinoCommon.dll', 'Rhino.UI.dll', 'Eto.dll') | ForEach-Object { Join-Path $RhinoSystemDir $_ }

& (Join-Path $PSScriptRoot 'roslyn-build.ps1') -Out (Join-Path $OutDir 'TextX.rhp') -AssemblyName TextX `
  -SourceDirs (Join-Path $root 'src/TextX') -SourceFiles $info -Define 'NETFRAMEWORK,NET48' `
  -Refs ($refs -join ',') -RefDirs (Join-Path $NetFxRefDir 'Facades')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built $($product.ProductName) $($product.Version)"
