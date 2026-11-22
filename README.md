# Text X for Rhino 8

Text X translates Rhino text and leaders, English to Hebrew by default (43 languages), and gets
right-to-left text right in Rhino: word order, numbers, punctuation and mixed English–Hebrew lines.
Layouts keep English on screen; the translation is switched in automatically when you print or export a PDF.

Type **`TR`** in Rhino's command line to open **Text X Studio**. Its Home is the translator for Rhino text and
leaders; its other pages translate whole PDFs keeping their layout, turn drawings into product schedules, and
read the text of pictures and screenshots. No account, no login, nothing to set up.

| | |
|---|---|
| Version | see [`VERSION`](VERSION) and [`CHANGELOG.md`](CHANGELOG.md) |
| Requires | Rhino 8 for Windows |
| Package | `textx-<version>-rh8_0-win.yak` (Rhino package) |
| Download | [Latest release](https://github.com/dainjsp07/TextX-Rhino/releases/latest) |
| Licence | MIT |

---

## Install

1. Drag **`textx-<version>-rh8_0-win.yak`** onto the Rhino 8 window.
2. Restart Rhino.
3. Type **`TR`**.

Updates are automatic: Text X checks for a new version each time Rhino starts, installs it in the background and uses
it from the next Rhino start. Settings, glossary and translation memory are kept. To uninstall: *Tools > Package Manager > Installed > Text X > Uninstall*.
Full guide, including troubleshooting: [docs/INSTALL.md](docs/INSTALL.md).

## Using Text X

Choose the languages and the translation service at the top of the window; they apply to every page.

**New text.** On **Home**, type in the text box (or click **Voice typing** and speak); the translation appears
within a second (turn off **Live** to translate only when you ask). **Check writing** lists spelling and grammar
suggestions as chips; click one to apply it. Check the **On screen** and **In print** previews, pick a display
option, choose the **Layer**, then click **Place text** (pick a point) or **Place leader** (pick the arrow point
and leader points, Enter).

**Existing text.** Select text or leaders in Rhino and click **Load selected** (or run `TR` or `TREdit` with them
selected). Edit or translate, then click **Update objects** – the same objects are changed in place (layer, font,
height, alignment, rotation, colour and the other properties are kept; Undo works). The objects menu (next to
Load selected) picks the object to edit, skips one, selects it in Rhino or translates them all.

**Display options**

| Option | On screen | Printed / PDF |
|---|---|---|
| English on screen, Hebrew in print (recommended for layouts) | English | Hebrew |
| Hebrew on screen and in print | Hebrew | Hebrew |
| English only (keep translation) | English | English |

**Printing.** `Print`, `Save As` / `Export` to PDF, AI, EPS or SVG and `TRExportPdf` use the translation
automatically and switch back afterwards. Saved `.3dm` files always contain the screen text.

**Text properties** holds the options of Rhino's Text and Leader dialogs (annotation style, font, height,
bold / italic / underline, alignment, rotation, wrapping, mask, layer, colour, arrowheads, landing), the print
alignment, and the PDF and recovery tools.

### Text X Studio

Open a file (or drop it on the window), then pick what you need:

* **Translate a PDF** – any language, detected automatically, into the language chosen at the top (English
  by default). The translated PDF keeps the original's layout: headings, paragraphs, tables, images, drawings
  and title blocks stay where they are; only the text changes. Scanned pages are read with OCR. Check the
  translation side by side with the original, click any paragraph to correct it (your corrections are
  remembered and reused), add glossary terms, and save as **PDF**, **Word** or **text** (also with both
  languages). **Check** looks for changed numbers, links, missing text, glossary terms and words left in the
  wrong alphabet; **Check the meaning** translates a page back to compare. **Summary** gives the key points,
  contents and figures. Large documents are translated in batches and can be stopped and continued.
* **Product schedule** – reads drawings and specifications into editable lists: products and items (codes,
  categories, descriptions, materials, finishes, colours, sizes, quantities, how many times each code is tagged,
  manufacturers and models), materials and finishes, project details from title blocks (project, client,
  architect, drawn by, scale, date, drawing number, revision), notes, dimensions and the document's own
  tables. Search, sort, edit, add or delete rows, see where a row was found, then save to **Excel**, **CSV** or
  **PDF**, or **place the schedule in Rhino** as a table (`TRPlaceSchedule`).
* **Picture or screenshot** – take a screenshot, paste or open a picture, or capture the Rhino view: its text
  is read on your computer and translated, ready to copy or to place in Rhino (**Use on Home**).

Every document you open is kept in **Recent documents** with its translation, corrections and schedules.
Schedules are read with rules and word lists for English, French, German, Spanish, Italian and Dutch
drawings (any language when read from the English translation): results can be checked against the page
and edited.

### Commands

| Command | |
|---|---|
| `TR` | Open Text X Studio on Home, the translator (loads pre-selected text). `TextX` does the same. |
| `TREdit` | Pick text / leaders and load them into the translator |
| `TRPlaceText`, `TRPlaceLeader` | Place the translator's text |
| `TRApply` | Write the translator's text into the loaded objects |
| `TRBatch` | Translate many annotations at once |
| `TRPreview` | Show print text in the viewports (On / Off / Toggle) |
| `TRExportPdf` | Export layouts to PDF with translations |
| `TRRestore` | Switch everything back to screen text (recovery) |
| `TRManage` | List translated objects, change display, remove translations |
| `TRRemove` | Remove translation data from objects |
| `TRStudio` | Open Text X Studio (the same window as `TR`) |
| `TRPlaceSchedule` | Place the schedule shown in Text X Studio as a table (text and lines, grouped) |
| `TRSettings` | Translation service and keys, right-to-left options, print switching, glossary, theme, updates |
| `TRRtlTest` | Places right-to-left test samples (run once after a Rhino update) |
| `TRAbout` | Version, install folder, update status |
| `TRUpdate` | Check for a newer version now and install it |

### Translation services

Google's free endpoint works without setup, with MyMemory as automatic fallback (Text X Studio uses the same service). For production work add a
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
