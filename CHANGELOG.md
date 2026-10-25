# Changelog

All notable changes to Text X are listed here, newest first.
Versions follow [semantic versioning](https://semver.org): **MAJOR.MINOR.PATCH**.

* **PATCH** (1.1.0 → 1.1.1): bug fixes only.
* **MINOR** (1.1.0 → 1.2.0): new features; existing drawings and settings keep working.
* **MAJOR** (1.x → 2.0.0): changes that break existing drawings, settings or workflows.

Write the changes for the next version under **[Unreleased]**; `tools/release.ps1` moves them
into a dated section when you release.

## [Unreleased]

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
