# Text X for Rhino 8

Text X translates Rhino text and leaders, English to Hebrew by default (43 languages), and gets
right-to-left text right in Rhino: word order, numbers, punctuation and mixed English–Hebrew lines.
Layouts keep English on screen; the translation is switched in automatically when you print or export a PDF.

Type **`TR`** in Rhino's command line to open it.

| | |
|---|---|
| Version | see [`VERSION`](VERSION) and [`CHANGELOG.md`](CHANGELOG.md) |
| Requires | Rhino 8 for Windows |
| Package | `textx-<version>-rh8_0-win.yak` (Rhino package) |
| Licence | MIT |

---

## Install

1. Drag **`textx-<version>-rh8_0-win.yak`** onto the Rhino 8 window.
2. Restart Rhino.
3. Type **`TR`**.

Updating works the same way: drag in the newer `.yak` and restart Rhino. Settings, glossary and
translation memory are kept. To uninstall: *Tools > Package Manager > Installed > Text X > Uninstall*.
Full guide, including troubleshooting: [docs/INSTALL.md](docs/INSTALL.md).

## Using Text X

**New text.** Type in the English box; the translation appears within a second (turn off **Live** to translate
only when you click **Translate**). Check the **Sheet preview**, pick a **Display** option, then click
**Place text** (pick a point) or **Place leader** (pick the arrow point and leader points, Enter).

**Existing text.** Select text or leaders in Rhino and click **Load selected** (or run `TREdit`). Edit or
translate, then click **Update** – the same objects are changed in place (layer, font, height, alignment,
rotation, colour and the other properties are kept; Undo works). Right-click an object in the list to skip
it or select it in Rhino.

**Display options**

| Option | On screen | Printed / PDF |
|---|---|---|
| English on screen, Hebrew in print (recommended for layouts) | English | Hebrew |
| Hebrew on screen and in print | Hebrew | Hebrew |
| English only (keep translation) | English | English |

**Printing.** `Print`, `Save As` / `Export` to PDF, AI, EPS or SVG and `TRExportPdf` use the translation
automatically and switch back afterwards. Saved `.3dm` files always contain the screen text.

The **Format** and **Leader** tabs hold the options of Rhino's Text and Leader dialogs (annotation style,
font, height, bold / italic / underline, alignment, rotation, wrapping, mask, layer, colour, arrowheads,
landing). **Print / PDF** sets the print alignment and holds the PDF and recovery tools.

### Commands

| Command | |
|---|---|
| `TR` | Open the Text X window (loads pre-selected text). `TextX` does the same. |
| `TREdit` | Pick text / leaders and load them into the window |
| `TRPlaceText`, `TRPlaceLeader` | Place the text from the window |
| `TRApply` | Write the window's text into the loaded objects |
| `TRBatch` | Translate many annotations at once |
| `TRPreview` | Show print text in the viewports (On / Off / Toggle) |
| `TRExportPdf` | Export layouts to PDF with translations |
| `TRRestore` | Switch everything back to screen text (recovery) |
| `TRManage` | List translated objects, change display, remove translations |
| `TRRemove` | Remove translation data from objects |
| `TRSettings` | Translation service and keys, right-to-left options, print switching, glossary, theme, updates |
| `TRRtlTest` | Places right-to-left test samples (run once after a Rhino update) |
| `TRAbout` | Version, install folder, update status |
| `TRUpdate` | Check for a newer version now |

### Translation services

Google's free endpoint works without setup, with MyMemory as automatic fallback. For production work add a
key in `TRSettings` for DeepL, Google Cloud, Azure, Claude (Anthropic) or a LibreTranslate server. Keys are
encrypted for your Windows user. A glossary (`samples/glossary-sample-he.csv`) fixes your office's terms.

---

## For the developer

```
TextX/
├─ VERSION                     release number – the single source of the version
├─ Directory.Build.props       product name, author, package id, update location
├─ CHANGELOG.md                what changed in each version
├─ src/TextX/                  the plug-in (C#, RhinoCommon, Eto)
│  ├─ Core/                    Rhino-independent logic: bidi + shaping, translation engine, versions
│  ├─ Integration/             Rhino integration: annotations, print/PDF switching, settings, updates
│  ├─ Commands/                TR… commands
│  └─ UI/                      window, dialogs, theme (Theming/) and custom controls (Controls/)
├─ tests/TextX.Tests/          test-suite (no Rhino needed)
├─ packaging/                  package icon and the install guide that ships in the zip
├─ tools/                      build.ps1, release.ps1, pack.ps1 and helpers
├─ docs/                       INSTALL, RELEASING, DEVELOPING, TESTING
└─ .github/workflows/          CI: build + test on every push, publish on version tags
```

* **Build, test, package:** `powershell -ExecutionPolicy Bypass -File tools\build.ps1` → `dist\v<version>\`
* **Release a new version:** add notes under *[Unreleased]* in `CHANGELOG.md`, then
  `tools\release.ps1 -Bump patch|minor|major` – see [docs/RELEASING.md](docs/RELEASING.md).
* **Develop and debug:** [docs/DEVELOPING.md](docs/DEVELOPING.md). **Test plan:** [docs/TESTING.md](docs/TESTING.md).

Two identifiers must never change, or existing installations will not upgrade cleanly: the plug-in GUID
(`Properties/AssemblyInfo.cs`) and the package name `textx` (`Directory.Build.props`).
