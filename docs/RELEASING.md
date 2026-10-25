# Releasing Text X

Everything that identifies a release lives in two files:

* **`VERSION`** – the release number.
* **`Directory.Build.props`** – product name, author, package id and the update location.

The build stamps these into the plug-in (`TRAbout` shows them), the Rhino package (`manifest.yml`), the file
names and the update feed. You never edit version numbers anywhere else.

## Version numbers

Text X uses [semantic versioning](https://semver.org), `MAJOR.MINOR.PATCH`:

| Change | Bump | Example |
|---|---|---|
| Bug fixes only | `patch` | 1.1.0 → 1.1.1 |
| New features, everything existing keeps working | `minor` | 1.1.0 → 1.2.0 |
| Breaks existing drawings, settings or workflows | `major` | 1.4.2 → 2.0.0 |
| Test version for a few people | explicit pre-release | 1.2.0-beta.1 |

Pre-releases are never offered to normal users by the update check (only to those who tick
*Include test versions* in TRSettings).

Never change the plug-in GUID (`src/TextX/Properties/AssemblyInfo.cs`) or the package name `textx`: Rhino and
the Package Manager use them to recognise Text X as the same product across versions.

## One-time setup: where updates are published

Installed copies learn about new versions from a small file, **`latest.json`**, published with each release,
and then download and install the `.yak` themselves (the SHA-256 in `latest.json` is checked first).
This project publishes on GitHub (`dainjsp07/TextX-Rhino`, public). To set it up for another repository:

1. Create a GitHub repository (for example `textx`) and push this folder to it.
2. In `Directory.Build.props` set `<TextXGitHubRepo>your-name/textx</TextXGitHubRepo>`.
   Builds now check `https://github.com/your-name/textx/releases/latest/download/latest.json` once a day.
3. Commit and push. The workflow in `.github/workflows/build.yml` builds and tests every push.

Copies built **before** this setting was made have no update location and cannot be told about new versions,
so set it before you share the first build widely. (Users can also paste the address in
*TRSettings > Appearance and updates > Update location*.)

Prefer not to use GitHub? Put `latest.json` and the `.yak` on any web server, or in a shared network folder
(`\\server\share\TextX\latest.json`), and set `<TextXUpdateFeedUrl>` (or *TRSettings > Update location*) to it.
The `download` link in `latest.json` may be relative to the file, e.g. `textx-1.3.0-rh8_0-win.yak`.

## Making a release

1. Write what changed under `## [Unreleased]` in `CHANGELOG.md`, in words a user understands.
2. Run, in PowerShell from the repository folder:

   ```
   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Bump minor
   ```

   This updates `VERSION`, turns *[Unreleased]* into a dated section, builds, runs the tests and writes
   `dist\v<version>\`:

   | File | Purpose |
   |---|---|
   | `textx-<version>-rh8_0-win.yak` | The Rhino package |
   | `TextX-<version>-Windows.zip` | For sharing: the `.yak` plus "Install Text X.txt" and the changelog |
   | `latest.json` | Update feed |
   | `release-notes.md`, `SHA256SUMS.txt` | Notes for the release page, checksums |

3. Test the `.yak` on your own Rhino (drag it in, restart, `TRAbout` shows the new version).
4. Publish – one of:
   * **GitHub (recommended):** commit and push the changes, then on GitHub open **Actions > build > Run workflow**,
     tick **Publish a release** and click **Run workflow**. GitHub builds and tests the version in `VERSION`,
     creates the tag `v<version>` and publishes the release with the `.yak`, the zip and `latest.json`.
     (Pushing a tag yourself does the same: run the command above with `-Publish`, or `git tag v<version>`
     and `git push --follow-tags`.) Every copy installed from a `.yak` then installs the new version by itself
     at its next daily check and uses it from the following Rhino start.
   * **Directly:** send `TextX-<version>-Windows.zip` to your friends (e-mail, WhatsApp, Drive).
   * **Rhino package server** (optional, makes Text X searchable in Rhino's Package Manager, which then also
     offers updates): with a free Rhino account,

     ```
     "C:\Program Files\Rhino 8\System\Yak.exe" login
     "C:\Program Files\Rhino 8\System\Yak.exe" push dist\v<version>\textx-<version>-rh8_0-win.yak
     ```

     The package name `textx` must still be free on the server the first time; if it is taken, change
     `TextXPackageName` before the first public release. A website / repository URL is required (`url` in the manifest).

## Fixing a broken release

Release a new patch version (never replace files of a published version: copies that already updated would not
notice). To withdraw a GitHub release, mark it as a pre-release or delete it – `latest.json` of the previous
release becomes "latest" again.

## Checklist

- [ ] `CHANGELOG.md` describes the changes
- [ ] `tools\release.ps1` finished without errors (build, tests, package)
- [ ] the `.yak` installs on a clean Rhino 8 and `TR` opens the window
- [ ] `TRAbout` shows the new version
- [ ] published (tag pushed / zip shared / yak pushed)
