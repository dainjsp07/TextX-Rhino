# Testing Text X

Two parts: automated tests of the translation and right-to-left core (no Rhino needed) and a manual
procedure in Rhino 8. Results recorded for each release are listed at the end.

## 1. Automated tests

```
dotnet run --project tests\TextX.Tests -c Release -- tests\data
```

Put `BidiTest.txt` and `BidiCharacterTest.txt` from https://www.unicode.org/Public/UCD/latest/ucd/ in
`tests\data` for the conformance suites; without them only the unit tests run.

| Suite | What it proves |
|---|---|
| `BidiTest.txt` / `BidiCharacterTest.txt` | The bidirectional algorithm matches the Unicode reference for every case. |
| `HebrewVisualOrder`, `MixedContent` | Words, final letters, numbers, ratios, codes, punctuation, mirrored brackets, `-0.45`, ranges, mixed paragraphs, visual→logical repair. |
| `FieldsAndMarks`, `ArabicShaping`, `Wrapping` | Rhino fields kept intact; niqqud; Arabic shaping vs. python-arabic-reshaper; RTL wrapping. |
| `Json`, `Metadata`, `Languages`, `Segmentation`, `Glossary`, `ProviderParsing` | Settings and metadata round-trips, provider language codes, do-not-translate protection, glossary, memory, fallback, cooldown, batching, response parsing for every service. |
| `Versions`, `UpdateFeed` | Semantic-version parsing, ordering (incl. pre-releases) and bumping; `latest.json` and GitHub release parsing; update offered only when newer. |

## 2. Manual test in Rhino 8

Use a new, unsaved document.

| # | Do this | Expected |
|---|---|---|
| 1 | Drag `textx-<version>-rh8_0-win.yak` into Rhino, restart, type `TR`. | The Text X window opens: deep-navy accent, *From English ⇄ To Hebrew עברית*, service chip with a green dot, tabs Text / Format / Leader / Print / PDF. The command line said "Text X <version> is installed". |
| 2 | `TRAbout` | Shows the version from `VERSION`, the package folder and the update status. |
| 3 | Type `Ground floor plan - Scale 1:50`. | Within a second the Hebrew appears, right-aligned. The sheet preview shows ON SCREEN English and PRINT / PDF Hebrew. The status line names the service. |
| 4 | **Place text**, click in a viewport. | An orange preview follows the cursor; the text is placed. |
| 5 | Select it, **Load selected**. | It appears in the object list with the label *Dual*. |
| 6 | Choose *Hebrew on screen and in print*, **Update 1 object**; `Undo`, `Redo`. | The object shows correct Hebrew; Undo restores English; Redo Hebrew. |
| 7 | `TRRtlTest`, pick a point, zoom extents. | Row A (and C, Arabic) read correctly; rows B and D are reversed / broken. |
| 8 | `Print` → Rhino PDF, Vector, Extents → save. | During printing the dual text turns Hebrew; the PDF shows correct Hebrew; the screen returns to English. |
| 9 | `_-SaveAs` a `.pdf`, click Print in the PDF dialog. | Same as 8. |
| 10 | **Place leader**: arrow point, a second point, Enter. | A leader with arrowhead, landing and text. |
| 11 | `TRPreview _On`, `_-SaveAs` a `.3dm`, `TRPreview _Off`. | The saved file shows English for dual objects. |
| 12 | Layout (`_-Layout "Sheet A" 297 210 0`), place a title, `TRExportPdf`. | Layout shows English, the PDF the Hebrew title. |
| 13 | Format tab: change font, height, alignment; Leader tab: arrowhead. Update. | Only the changed properties change. |
| 14 | Right-click an object in the list → *Skip*, Update. | The skipped object is unchanged; *Select in Rhino* selects it. |
| 15 | Resize the window, close and reopen with `TR`. | Size and position are restored. |
| 16 | `TRSettings` → Appearance and updates → *Light*, OK. | The window switches to the light colours (and back with *Follow Rhino*). |
| 17 | `TRSettings` → service with a key → **Test**. | `OK: "Ground floor plan" -> ...` |
| 18 | With an update location configured and a newer `latest.json` published: `TRUpdate`. | Reports the new version and offers to open the download page; the window shows the update banner. |
| 19 | With the window open: `_-New _None`, open a file, several times. | No errors; the window resets for each document. |

## 3. Calibration after Rhino updates

Run `TRRtlTest` after each Rhino service release. If a future Rhino renders right-to-left text natively,
row **B** reads correctly instead of row A: then set *TRSettings > Right-to-left > Screen* and *Print and PDF*
to **Logical**.

## Recorded results

**1.2.0** (2026-10-25)
* Automated: BidiTest.txt 770 241 cases and BidiCharacterTest.txt 91 670 cases, 0 failures; 192 unit assertions
  passed (new: package install into a version folder, manifest.txt switch, wrong name / version refused, running
  version never overwritten, re-install over a left-over folder, feed checksum and relative links).
* API check: all 566 RhinoCommon / Eto / Rhino.UI member references resolve against Rhino 8.
* Rhino 8 (Windows, Safe Mode), Text X loaded from a test package folder (`…\packages\textx\1.2.0`) with a local
  feed offering 1.2.1: the daily check downloaded, verified (SHA-256) and installed 1.2.1 by itself
  (`…\textx\1.2.1\` created, `manifest.txt` = `1.2.1`, running version untouched); the window banner, TRUpdate and
  TRAbout show "1.2.1 is installed, restart Rhino"; with `manifest.txt` reset, TRUpdate offered the update, asked,
  and re-installed it over the existing folder. Rhino's start-up log shows its package manager removing inactive
  versions named by `manifest.txt`, the mechanism the update relies on.
* Not run live: the download from a published GitHub release (checked separately once the release exists) and
  Rhino actually starting the new version after a restart (that step is Rhino's own package loading).

**1.1.0** (2026-10-23)
* Automated: BidiTest.txt 770 241 cases and BidiCharacterTest.txt 91 670 cases, 0 failures; 165 unit assertions passed.
* API check: all 564 RhinoCommon / Eto / Rhino.UI member references of `TextX.rhp` resolve against Rhino 8.35 (8.35.26251.13001).
* Rhino 8.35 (Windows, .NET 8.0.14, dark mode), installed over the 1.0 preview: `TR` opens the Graphite and Deep
  Navy window with the Text X title-bar icon; 1.0 settings were carried over; live translation (Google free,
  about 1.6 s) and the sheet preview; Place text; Load selected; display options; Update 1 object (Hebrew
  renders correctly in the viewport); Format, Leader and Print / PDF tabs; object list right-click menu;
  TRSettings > Appearance and updates, including a live switch to the light theme and back; About dialog
  (layout, no focus ring on open, keyboard focus ring after Tab); "no update location" message.
* Not run live for 1.1.0: installing from the `.yak` through Rhino's Package Manager, the update banner
  against a published release, printing to a physical printer.

**1.0.0** (preview "Translator", Rhino 8.35, .NET 8.0.14): steps covering install, RTL rendering, print and
Save As PDF switching, `.3dm` guard, real-time translation with fallback, placement preview, update / undo /
redo, leaders, print preview, layout PDF export and document switching passed.
