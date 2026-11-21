# Changelog

All notable changes to Text X are listed here, newest first.
Versions follow [semantic versioning](https://semver.org): **MAJOR.MINOR.PATCH**.

* **PATCH** (1.1.0 → 1.1.1): bug fixes only.
* **MINOR** (1.1.0 → 1.2.0): new features; existing drawings and settings keep working.
* **MAJOR** (1.x → 2.0.0): changes that break existing drawings, settings or workflows.

Write the changes for the next version under **[Unreleased]**; `tools/release.ps1` moves them
into a dated section when you release.

## [Unreleased]

## [1.3.0] - 2026-11-21

### Added
- **Text X Studio** (`TRStudio`, or the Studio button in the Text X window): one window for documents and
  pictures. Open a file or drop it on the window, then pick what to do. No account or setup.
- **PDF translation keeping the layout.** Any language, detected automatically, into the chosen language
  (English by default). Headings, paragraphs, tables, images, drawings and title blocks stay in place; the
  translation is drawn in the same position, size, style and colour, made a little smaller when it is longer.
  Right-to-left languages, Chinese, Japanese, Korean, Thai and other scripts are supported. Fonts are embedded
  with only the characters used, so files stay small and the text can be selected and copied.
- **Scanned PDFs** are read with OCR (Tesseract, on your computer; the language data is downloaded the first
  time it is needed). Text colour and weight are taken from the scan.
- **Review and edit**: original and translation side by side, click a paragraph on the page or in the list
  to correct it; corrections are remembered and reused for identical text. Translate a paragraph again or
  keep it in the original language. Glossary terms are applied inside sentences.
- **Save as** PDF (same layout), Word (headings, paragraphs, tables and images) or text, also with both
  languages.
- **Checks**: changed or missing numbers, links and e-mail addresses, glossary terms, untranslated text, text
  in the wrong alphabet, unusually short or long translations, text made smaller to fit; and an optional
  meaning check that translates a page back.
- **Summary**: key points (sentences of the document itself), contents from the headings, and figures.
- **Recent documents**: each document keeps its translation, corrections and schedules; large documents can
  be stopped and continued.
- **Product schedules from drawings**: codes, categories, descriptions, materials, finishes, colours (RAL,
  NCS, Pantone, names), sizes, quantities, tag counts, manufacturers and models; materials and finishes;
  title-block details (project, client, architect, drawn by, scale, date, drawing number, revision); notes;
  dimensions; and the document's own tables. Search, sort, edit, show on the page, save as Excel, CSV or
  PDF, or place in Rhino as a table (`TRPlaceSchedule`).
- **Pictures and screenshots**: take a screenshot, paste or open a picture, or capture the Rhino view; the
  text is read and translated. **Speak**: Windows voice typing with live translation. **Check writing**:
  spelling and grammar suggestions from LanguageTool's free service.
- The Text X window accepts text from Studio, ready to place as text or a leader.
- **Drawings with outlined text** (CAD exports whose words are shapes, not text) are read with OCR, next to
  the real text on the page.

### Changed
- **New look**, in light and dark: a charcoal and warm-paper palette with one signal-orange accent, an icon
  rail for navigation, large rounded cards, pill buttons and chips, round icon buttons, and the document page
  as the centre of the Document view with a floating page bar. The Text X window uses the same colours,
  buttons and tabs.
- **Pictures that explain.** Each Home tile has a small line drawing that answers the pointer and says what
  it shows: a page and its translation with every piece in the same place, cabinets on a drawing and their
  rows in a schedule, a picture whose lines become text as the reading bar passes, a voice becoming text,
  and the library. They walk through what they do once when Studio opens. Empty Document and Schedules
  pages use them too. (Line figures after Hairline by Lucas Marques, MIT.)
- **Smooth hover.** Buttons, chips and tiles fade in and out, arrows lean the way they point, and the orange
  circle in the rail and the ink chip in tabs slide to the chosen item. With "Show animations in Windows"
  off, everything changes at once.

## [1.2.0] - 2026-10-25

### Added
- **Automatic updates.** Once a day Text X checks for a new version on GitHub. When there is one, it downloads it,
  checks that the download is complete and genuine (SHA-256), and installs it. The new version is used the next
  time you start Rhino; your settings, glossary and translation memory are kept.
  This works for copies installed from the `.yak` package (dragged into Rhino).
- **Install update** button in the Text X window and in TRAbout; **TRUpdate** now installs the update too.
- TRSettings > Appearance and updates: *Install new versions automatically* (on by default).
- The update location can also be a `latest.json` file in a shared folder, for offices that share Text X on a
  network drive.

### Changed
- Updates are published at https://github.com/dainjsp07/TextX-Rhino.

## [1.1.0] - 2026-10-23

### Added
- New name: **Text X**. Type **TR** to open it (the command **TextX** does the same, for anyone who has an alias called TR).
- New window design, "Graphite and Deep Navy": a deep-navy accent, an object list with status labels, a sheet preview that shows the screen and print text side by side, and display options as cards. The window is 720 px wide, remembers its size and position, and switches to a light version when Rhino is in light mode (TRSettings > Appearance and updates).
- Versioning and updates: **TRAbout** shows the installed version; **TRUpdate** checks for a newer release; Text X checks once a day and shows a banner with a Download button when a new version is published.
- Rhino package (`.yak`): install by dragging the file into Rhino 8; uninstall and update with Rhino's Package Manager. Ready to publish to the Rhino package server.
- Object list: right-click an object to skip it when updating, or to select it in Rhino.
- Release tooling: one-step build, test and packaging (`tools/build.ps1`), version bumping with changelog dating (`tools/release.ps1`), and a GitHub Actions workflow that publishes releases.

### Changed
- All commands now start with **TR**: TREdit, TRPlaceText, TRPlaceLeader, TRApply, TRBatch, TRPreview, TRRestore, TRExportPdf, TRManage, TRSettings, TRRemove, TRRtlTest.
- Settings, translation memory and glossary of the 1.0 preview ("Translator") are carried over automatically on first start.

### Compatibility
- Drawings translated with 1.0 keep working: the stored translation data is unchanged.

## [1.0.0] - 2026-10-22

First version, released as "Translator" (preview).

### Added
- Real-time translation in a separate window (English to Hebrew by default, 43 languages) with Google, Google Cloud, DeepL, Azure, Claude, LibreTranslate, MyMemory or manual entry, translation memory and glossary.
- Correct right-to-left text in Rhino (Unicode bidirectional algorithm, Arabic shaping) on screen and in PDF.
- English on screen, translation when printing or exporting PDF (automatic, also inside blocks), with safe restore and crash recovery.
- Place text or leaders, update existing objects in place (undoable), formatting options of Rhino's Text dialog, batch translation, PDF export of layouts.
