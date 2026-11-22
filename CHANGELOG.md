# Changelog

All notable changes to Text X are listed here, newest first.
Versions follow [semantic versioning](https://semver.org): **MAJOR.MINOR.PATCH**.

* **PATCH** (1.1.0 → 1.1.1): bug fixes only.
* **MINOR** (1.1.0 → 1.2.0): new features; existing drawings and settings keep working.
* **MAJOR** (1.x → 2.0.0): changes that break existing drawings, settings or workflows.

Write the changes for the next version under **[Unreleased]**; `tools/release.ps1` moves them
into a dated section when you release.

## [Unreleased]

## [2.0.1] - 2026-11-22

### Fixed
- **PDF translation of drawings and title blocks.**
  - Texts in neighbouring cells are no longer read as one ("Date Scale"): a ruled line between two texts keeps
    them apart, also in tight cells where the line runs through the letters' tails (a title block's
    Description and Hardware rows stay two paragraphs).
  - A Hebrew label keeps its place in English: right-aligned stays right-aligned, centred stays centred.
    The English grows only into free room in its cell, away from lines, shapes, pictures and other text, and
    takes a second line where it does not fit.
  - Labels that already have their English beside them (bilingual title blocks: "Project name  שם פרויקט")
    are not written twice: the Hebrew is removed.
  - The lines of a callout are translated together, so each line is read in context.
  - Notes that start with a bullet touching the text ("*Note") are separate items.
  - Hebrew words joined to a number ("חתך1") and Hebrew names next to English labels are translated.
  - Brackets around English words inside Hebrew lines come out the right way round.
  - Notes, legend texts and a heading with its text that stand one under the other are laid out together:
    a longer English text pushes the next one down instead of running over it.
  - A label that does not fit on one line at three quarters of its size takes a second line where there is
    room under it; text that still does not fit is made smaller rather than spilling off the sheet. A word
    is not cut in two ("Descript ion", "dishwas her"): a word a little wider than its box stands whole on
    its line, and a smaller text stays under the ruled line above it.
  - Title-block and joinery words get draughtsman's English: Scale, Revision, Page, Sheet size, Finish
    schedule, Plinth, Soft-close, Top view, Rear elevation and more (your own glossary always wins).
    Also kitchen and site words (built-in microwave, integrated dishwasher, flap door, ice machine, Tami4,
    mitred joints, shadow-gap profile, plasterboard bulkhead), "ע"פ מקרא" as "as per legend" (not "according
    to the Bible"), and labels with a reference: "קנ"מ 1:25" is "Scale 1:25", "חזית A" is "Elevation A",
    "תוכנית 02.1" is "Plan 02.1", "אי קפיטריה" is "Cafeteria island".
    Glossary terms are also found with a Hebrew prefix joined to them (וסכומון), and a sentence that did
    not use the glossary's word is asked again with the word in place ("a cutlery tray", not "a watermelon").
  - Callouts and legend texts on a drawing that line up across the sheet are no longer treated as one
    table: they are laid out one under the other like other notes, without running over each other.
  - A paragraph that needs more room keeps one straight edge, and starts under the ruled line above it.
  - When the free Google service is busy during a long document, Studio waits and asks again instead of
    switching to the backup service; texts too long for the backup service are sent in pieces, and any
    paragraph left untranslated is marked so **Continue** translates it (before, some stayed in Hebrew).
  - Documents in the history are read again with these rules when you open them; every paragraph whose
    text is unchanged keeps its translation and your edits (scanned pages are not read by OCR again).
- **Home spacing.** Nothing runs past the right edge any more; the translator and the tiles get exact widths.
  Under the translator, the five tiles sit in one row; in a short window they become buttons. Icon-only
  buttons stay square.
- Hebrew file names show right to left in the title and in Recent.

### Changed
- The button at the foot of the left bar switches **dark and light mode** again. Settings are in the
  translation service menu at the top (**Settings...**).

## [2.0.0] - 2026-11-21

Text X Studio is now the Text X window: one app for Rhino text, documents, drawings and pictures.

### Changed
- **One app.** `TR` (and `TextX`, `TREdit`) open **Text X Studio** on **Home**, where the translator of the old
  Text X window now lives: type, dictate or load text and leaders from Rhino, see the translation as you type,
  choose what shows on screen and in print, then **Place text**, **Place leader** or **Update objects**.
  The separate Text X window is gone; `TRPlaceText`, `TRPlaceLeader` and `TRApply` work with Studio.
- **Home**: the translator on the left; **Translate a PDF** and four tiles (Product schedule, Picture or
  screenshot, Voice typing, Library) on the right. In a narrower window the tiles move under the translator,
  and in a small one the display options fold into one menu and secondary buttons keep only their icons.
- **One language pair and one translation service for every page**, chosen at the top of the window.
- **Picture or screenshot** replaces the Tools page: take a screenshot, paste or open a picture, or capture the
  Rhino view; its text is read on this computer and translated. **Use on Home** puts it in the translator.
- Messages appear for a few seconds under the page instead of in a permanent status bar; long tasks keep their
  progress bar and Stop button there.
- Design: one type scale (20, 16, 14, 13, 12 and 11 px), controls of 24, 28 and 32 px, spacing on a 4 px grid,
  orange kept for the main action of each page, and the round arrows of the tiles turn orange on hover.

### Added
- **Voice typing** and **Check writing** on Home, in the translator's text: Windows voice typing writes what you
  say and the translation follows; LanguageTool's suggestions (free, no account) appear as chips under the
  texts, click one to apply it.
- **Layer** for placed text and leaders, from the layers of the open Rhino file (current layer by default).
- **Text properties**: Rhino's text and leader properties (annotation style, font, height, bold, italic,
  underline, alignment, rotation, wrapping, mask, frame, layer, colour, arrowhead, leader curve, landing) and
  the print tools (alignment in print, Export PDF with translation, show print text, manage translated objects,
  restore screen text) open in place of the texts.
- **On screen** and **In print** previews of the text with its font, emphasis, mask, frame, colour and alignment.
- **New PDF** on the Document page: closes the open document (it stays in Recent) and opens another.
- The objects menu on Home: pick the loaded object to edit, skip one in Update, select it in Rhino, translate
  them all, or start a new text.

### Text X Studio (new since 1.2.0)

#### Added
- **Text X Studio** (`TRStudio`): one window for documents and
  pictures. Open a file or drop it on the window, then pick what to do. No account or setup.
- **PDF translation keeping the layout.** Any language, detected automatically, into the language chosen at
  the top. Headings, paragraphs, tables, images, drawings and title blocks stay in place; the
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
  text is read and translated.
- **Drawings with outlined text** (CAD exports whose words are shapes, not text) are read with OCR, next to
  the real text on the page.

#### Changed
- **New look**, in light and dark: a charcoal and warm-paper palette with one signal-orange accent, an icon
  rail for navigation, large rounded cards, pill buttons and chips, round icon buttons, and the document page
  as the centre of the Document view with a floating page bar.
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
