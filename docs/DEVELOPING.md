# Developing Text X

## Requirements

* Windows with **Rhino 8**
* **.NET SDK 8** (`dotnet --version`), or Visual Studio 2022 with the ".NET desktop development" workload
* PowerShell 5.1 (built into Windows) or PowerShell 7

## Build and run

```
powershell -ExecutionPolicy Bypass -File tools\build.ps1          # build + test + package
dotnet build src\TextX\TextX.csproj -c Debug -f net48            # just build
```

`TextX.csproj` references RhinoCommon, Rhino.UI and Eto from `C:\Program Files\Rhino 8\System`
(`-p:RhinoSystemDir=...` for another folder, `-p:UseRhinoNuGet=true` for the NuGet package).

The `net48` build is the one that ships. It runs in Rhino 8 on .NET 8 (Rhino's default) and on .NET Framework
(`SetDotNetRuntime`). Keep the code at C# 7.3 so it compiles for both targets.

**Trying a build in Rhino:** uninstall the `.yak` version first (Package Manager), then drag
`src\TextX\bin\Debug\net48\TextX.rhp` into Rhino once – Rhino remembers the path, so after each build you just
restart Rhino. To debug, open `TextX.sln` in Visual Studio and start the *Rhino 8* profile
(`Properties/launchSettings.json`), or attach to `Rhino.exe`.

Without the .NET SDK, `tools\build-roslyn.ps1` builds with the Roslyn compiler that ships with PowerShell 7.

## Structure

| Folder | Contents |
|---|---|
| `src/TextX/Core` | No Rhino dependency, fully unit-tested: Unicode bidi algorithm (`Text/Bidi.cs`), Arabic shaping, RTL composition and wrapping, translation engine, providers, glossary, memory, JSON, versions and update feed (`Updates/`) |
| `src/TextX/Integration` | Rhino side: reading / writing annotations (`AnnotationEngine`), metadata, print / PDF switching (`PrintSwapManager`), settings (`TextXSettings`), update checks (`UpdateService`), diagnostics |
| `src/TextX/Commands` | The `TR…` commands |
| `src/TextX/UI` | `TextXWindow` (the TR window), dialogs (`SettingsDialog`, `ManageDialog`, `AboutDialog`), `FormatPanel`, viewport preview conduit |
| `src/TextX/UI/Theming` | Colours (`ThemeColors`: Graphite and Deep Navy, plus light), fonts, vector icons |
| `src/TextX/UI/Controls` | Custom-drawn Eto controls: buttons, cards, tabs, object list, pickers, options, sheet preview |
| `src/TextX/BuildInfo.cs` | Version, package name and update location as compiled in from `VERSION` / `Directory.Build.props` |

### How the important parts work

* **Right-to-left text.** Rhino 8 draws annotation text strictly left to right. Text X stores the logical text
  (as typed) in the object's user strings and writes the *visual* order into the annotation, computed with a
  full implementation of the Unicode bidi algorithm (UAX #9) plus Arabic shaping. Fields (`%<...>%`) are protected.
* **English on screen, translation in print.** `PrintSwapManager` swaps annotation text to the print state when
  `Print` starts, when Rhino is about to write a PDF/AI/EPS/SVG file, and in `TRExportPdf`, then restores it.
  `.3dm` files are always saved with screen text; a state flag in each object allows recovery after a crash.
* **Stored data** uses the user-string prefix `RhinoTranslator.` (from 1.0). Keep it, or old drawings lose
  their translations.
* **Theme.** All colours come from `ThemeColors`; custom controls read the palette when they paint, and native
  controls are bound through `ThemeScope` so the theme can change while the window is open.
* **Updates.** `UpdateService` downloads `latest.json` (or a GitHub "latest release" response) at most once a
  day in the background and remembers the result; the window shows a banner, `TRUpdate` / `TRAbout` check on demand.

## Tests

```
dotnet run --project tests\TextX.Tests -c Release -- tests\data
```

Covers the Unicode bidi conformance files (about 860 000 cases, when present in `tests\data`), Arabic shaping,
wrapping, segmentation, glossary, metadata, provider response parsing, version comparison and the update feed.
Rhino-dependent behaviour is tested by hand: [TESTING.md](TESTING.md).
