<#
  Minimal Roslyn compiler driver (used to build and test without the .NET SDK).
  Requires PowerShell 7 (which ships Microsoft.CodeAnalysis.CSharp).

  Example:
    pwsh tools/roslyn-build.ps1 -Out bin/x.dll -SourceDirs src/A,src/B -RefDirs refs -Refs a.dll,b.dll -Define NET48
#>
param(
  [Parameter(Mandatory = $true)][string]$Out,
  [string[]]$SourceDirs = @(),
  [string[]]$SourceFiles = @(),
  [string[]]$Refs = @(),
  [string[]]$RefDirs = @(),
  [string]$RefDirFilter = "*.dll",
  [string[]]$Define = @(),
  [string]$AssemblyName = "",
  [switch]$Exe,
  [switch]$DebugBuild
)
$ErrorActionPreference = "Stop"
# allow comma separated lists when invoked with -File
function SplitList($a) { $r = @(); foreach ($x in $a) { $r += ($x -split ',') | Where-Object { $_ -ne '' } }; return ,$r }
$SourceDirs = SplitList $SourceDirs; $SourceFiles = SplitList $SourceFiles; $Refs = SplitList $Refs; $RefDirs = SplitList $RefDirs; $Define = SplitList $Define
$pwshDir = Split-Path ([System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName)
Add-Type -Path (Join-Path $pwshDir "Microsoft.CodeAnalysis.dll")
Add-Type -Path (Join-Path $pwshDir "Microsoft.CodeAnalysis.CSharp.dll")

$files = New-Object System.Collections.Generic.List[string]
foreach ($d in $SourceDirs) { Get-ChildItem -Path $d -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' } | ForEach-Object { $files.Add($_.FullName) } }
foreach ($f in $SourceFiles) { $files.Add((Resolve-Path $f).Path) }

$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp7_3, [Microsoft.CodeAnalysis.DocumentationMode]::Parse, [Microsoft.CodeAnalysis.SourceCodeKind]::Regular, [string[]]$Define)
$trees = New-Object System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]
foreach ($f in $files) {
  $text = [System.IO.File]::ReadAllText($f)
  $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($text, $parseOptions, $f, [System.Text.Encoding]::UTF8, [System.Threading.CancellationToken]::None))
}

$refList = New-Object System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]
$seen = @{}
function AddRef([string]$p) {
  $name = [System.IO.Path]::GetFileName($p).ToLowerInvariant()
  if ($seen.ContainsKey($name)) { return }
  try {
    $fs = [System.IO.File]::OpenRead($p)
    $pe = New-Object System.Reflection.PortableExecutable.PEReader($fs)
    $ok = $pe.HasMetadata
    $pe.Dispose(); $fs.Dispose()
    if (-not $ok) { return }
  } catch { return }
  $seen[$name] = $true
  $refList.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($p))
}
foreach ($r in $Refs) { AddRef (Resolve-Path $r).Path }
foreach ($d in $RefDirs) { Get-ChildItem -Path $d -Filter $RefDirFilter | ForEach-Object { AddRef $_.FullName } }

$kind = if ($Exe) { [Microsoft.CodeAnalysis.OutputKind]::ConsoleApplication } else { [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary }
$opt = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($kind)
$opt = $opt.WithOptimizationLevel($(if ($DebugBuild) { [Microsoft.CodeAnalysis.OptimizationLevel]::Debug } else { [Microsoft.CodeAnalysis.OptimizationLevel]::Release }))
$opt = $opt.WithPlatform([Microsoft.CodeAnalysis.Platform]::AnyCpu).WithAllowUnsafe($false).WithDeterministic($true)
$opt = $opt.WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Disable)

if ([string]::IsNullOrEmpty($AssemblyName)) { $AssemblyName = [System.IO.Path]::GetFileNameWithoutExtension($Out) }
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($AssemblyName, $trees, $refList, $opt)

$outDir = Split-Path -Parent $Out
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$fsOut = [System.IO.File]::Create($Out)
$xmlPath = [System.IO.Path]::ChangeExtension($Out, ".xml")
$fsXml = [System.IO.File]::Create($xmlPath)
try {
  $res = $comp.Emit($fsOut, $null, $fsXml)
} finally { $fsOut.Dispose(); $fsXml.Dispose() }

$errors = 0; $warnings = 0
foreach ($d in $res.Diagnostics) {
  if ($d.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Error) { $errors++; Write-Host ("ERROR " + $d.ToString()) }
  elseif ($d.Severity -eq [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $d.Id -notin @('CS1591','CS1701','CS1702','CS1705')) { $warnings++; Write-Host ("warn  " + $d.ToString()) }
}
if (-not $res.Success) { Remove-Item $Out -ErrorAction SilentlyContinue; Write-Host "BUILD FAILED ($errors errors)"; exit 1 }
Write-Host ("BUILD OK -> $Out  (" + $files.Count + " files, $warnings warnings)")
