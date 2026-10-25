# Installing Text X

Text X is a Rhino 8 plug-in for Windows, delivered as one file: **`textx-<version>-rh8_0-win.yak`**
(a Rhino package). The shareable zip `TextX-<version>-Windows.zip` contains that file and a short guide.

## Install

1. Start **Rhino 8**.
2. **Drag the `.yak` file onto the Rhino window.** Rhino installs the package (into its packages folder,
   `%APPDATA%\McNeel\Rhinoceros\packages\8.0\textx\`) and asks you to restart.
3. Restart Rhino.
4. Type **`TR`** and press Enter.

Nothing else is needed: no source code, no .NET installation, no administrator rights.

## Update

Updates are automatic:

* Once a day, when Rhino starts, Text X checks https://github.com/dainjsp07/TextX-Rhino for a newer version.
* If there is one, it downloads it in the background, checks it (SHA-256) and installs it. The command line
  says *"Text X 1.3.0 has been downloaded and installed. It will be used the next time you start Rhino."* and the
  window shows *"Restart Rhino to start using it"*.
* The next time Rhino starts, the new version is used. Settings, API keys, glossary and translation memory are kept.

`TRUpdate` checks and installs immediately; the **Install update** button in the window and in `TRAbout` does the
same. To be asked first, untick *TRSettings > Appearance and updates > Install new versions automatically*.

Automatic installing needs Text X to have been installed from the `.yak` (as above). A copy registered some
other way only shows *"Text X 1.3.0 is available"* with a **Download** button; drag that `.yak` into Rhino once and
it updates itself from then on.

`TRAbout` shows the installed version, its folder and the update status. Rhino's
*Tools > Package Manager > Installed* also lists the version.

## Uninstall

*Tools > Package Manager > Installed > Text X > Uninstall*, then restart Rhino.
Translated drawings keep their text; the translation data stays in the objects, harmlessly, in case Text X
is installed again.

## Troubleshooting

| Problem | Fix |
|---|---|
| Dragging the `.yak` does nothing | Make sure it is Rhino **8** (Rhino 7 cannot install `.yak` files by dragging). Fallback: copy the `.yak`, rename the copy to `.zip`, extract it to a permanent folder and drag the `TextX.rhp` inside into Rhino. |
| Windows says the file is blocked | Right-click the file > Properties > tick **Unblock** > OK. |
| `TR` runs something else | You have a Rhino alias named TR (*Options > Aliases*). Use **`TextX`** instead, or rename the alias. Text X prints a note about this at start-up. |
| "Text X failed to load" / plug-in conflict | You may still have the 1.0 preview ("Translator", `RhinoTranslator.rhp`). Close Rhino, delete or move `RhinoTranslator.rhp`, start Rhino, then install the `.yak`. Text X takes over its settings. |
| The window does not open | Run `TRAbout`. If the command is unknown, check *Tools > Options > Plug-ins* that Text X is enabled, and that Rhino is not in Safe Mode. |
| Hebrew reads backwards in Rhino | Run `TRRtlTest` and follow the note in the command line (see TESTING.md, calibration). |
| Translation errors (429, quota) | The free Google service limits heavy use; Text X falls back to MyMemory and pauses Google for 3 minutes. Add a DeepL / Google Cloud / Azure / Claude key in `TRSettings` for daily work. |

Diagnostic log: create an empty file named `TextX.trace` next to `TextX.rhp`
(`TRAbout` shows the folder) and restart Rhino; Text X then writes `TextX.log` in the same folder.
