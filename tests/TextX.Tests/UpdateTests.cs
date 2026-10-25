using System;
using System.IO;
using System.IO.Compression;
using TextX.Core.Updates;
using static TextX.Tests.TestRunner;

namespace TextX.Tests
{
    public static partial class CoreTests
    {
        public static void Versions()
        {
            var v = SemVersion.Parse("1.2.3");
            Equal(1, v.Major, "major");
            Equal(2, v.Minor, "minor");
            Equal(3, v.Patch, "patch");
            Equal("1.2.3", v.ToString(), "to string");
            Equal("1.2.0", SemVersion.Parse("v1.2").ToString(), "tag with v and two parts");
            Equal("2.0.0-beta.1", SemVersion.Parse("2.0.0-beta.1+build.7").ToString(), "build metadata ignored");
            SemVersion tmp;
            True(!SemVersion.TryParse("1", out tmp), "single number rejected");
            True(!SemVersion.TryParse("version 2", out tmp), "text rejected");
            True(!SemVersion.TryParse(null, out tmp), "null rejected");

            // ordering (semver.org section 11)
            var ordered = new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
            for (int i = 0; i + 1 < ordered.Length; i++)
                True(SemVersion.Parse(ordered[i]) < SemVersion.Parse(ordered[i + 1]), ordered[i] + " < " + ordered[i + 1]);
            True(SemVersion.Parse("1.10.0") > SemVersion.Parse("1.9.0"), "numeric compare, not text");
            True(SemVersion.Parse("v1.2.0") == SemVersion.Parse("1.2.0") || SemVersion.Parse("v1.2.0").Equals(SemVersion.Parse("1.2.0")), "equal");

            // bumping
            Equal("1.2.4", SemVersion.Parse("1.2.3").Bump("patch").ToString(), "bump patch");
            Equal("1.3.0", SemVersion.Parse("1.2.3").Bump("minor").ToString(), "bump minor");
            Equal("2.0.0", SemVersion.Parse("1.2.3").Bump("major").ToString(), "bump major");
            Equal("1.3.0", SemVersion.Parse("1.3.0-beta.2").Bump("patch").ToString(), "finish pre-release");
        }

        public static void UpdateFeed()
        {
            var own = ReleaseFeed.Parse("{\"name\":\"Text X\",\"version\":\"1.2.0\",\"released\":\"2026-11-02\",\"download\":\"https://example.com/textx-1.2.0-rh8_0-win.yak\",\"page\":\"https://example.com/r/v1.2.0\",\"notes\":\"- Faster\",\"minRhino\":\"8.0\"}");
            Equal("1.2.0", own.Version.ToString(), "feed version");
            Equal("https://example.com/textx-1.2.0-rh8_0-win.yak", own.DownloadUrl, "feed download");
            Equal("https://example.com/r/v1.2.0", own.BestUrl, "page preferred");
            Equal("8.0", own.MinRhino, "min rhino");

            var gh = ReleaseFeed.Parse("{\"tag_name\":\"v1.3.0\",\"html_url\":\"https://github.com/o/r/releases/tag/v1.3.0\",\"body\":\"notes\",\"assets\":[{\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.3.0/TextX-1.3.0-Windows.zip\"},{\"browser_download_url\":\"https://github.com/o/r/releases/download/v1.3.0/textx-1.3.0-rh8_0-win.yak\"}]}");
            Equal("1.3.0", gh.Version.ToString(), "github version");
            True(gh.DownloadUrl.EndsWith(".yak"), "github .yak asset preferred");

            var installed = SemVersion.Parse("1.2.0");
            True(!ReleaseFeed.IsUpdate(installed, own, false), "same version is not an update");
            True(ReleaseFeed.IsUpdate(installed, gh, false), "newer version is an update");
            var beta = ReleaseFeed.Parse("{\"version\":\"1.4.0-beta.1\"}");
            True(!ReleaseFeed.IsUpdate(installed, beta, false), "pre-release hidden by default");
            True(ReleaseFeed.IsUpdate(installed, beta, true), "pre-release offered when enabled");
            True(!ReleaseFeed.IsUpdate(SemVersion.Parse("2.0.0"), gh, false), "older release is not an update");

            bool threw = false;
            try { ReleaseFeed.Parse("<html>404</html>"); } catch (FormatException) { threw = true; }
            True(threw, "html is rejected");
            threw = false;
            try { ReleaseFeed.Parse("{\"version\":\"latest\"}"); } catch (FormatException) { threw = true; }
            True(threw, "bad version rejected");

            // checksum and package detection
            var withSha = ReleaseFeed.Parse("{\"version\":\"1.2.0\",\"download\":\"https://x/y/textx-1.2.0-rh8_0-win.yak\",\"sha256\":\"ABCDEF\"}");
            Equal("abcdef", withSha.Sha256, "sha256 read, lower case");
            True(withSha.HasPackage, ".yak download is a package");
            True(!ReleaseFeed.Parse("{\"version\":\"1.2.0\",\"download\":\"https://x/y/TextX.zip\"}").HasPackage, ".zip is not a package");

            // feeds in a shared folder, links relative to the feed
            string path;
            True(ReleaseFeed.IsFileLocation("C:\\Updates\\latest.json", out path) && path == "C:\\Updates\\latest.json", "drive path is a file location");
            True(ReleaseFeed.IsFileLocation("\\\\server\\share\\latest.json", out path), "UNC path is a file location");
            True(!ReleaseFeed.IsFileLocation("https://github.com/o/r/releases/latest/download/latest.json", out path), "https is not a file location");
            Equal("\\\\server\\share\\textx-1.3.0-rh8_0-win.yak", ReleaseFeed.Resolve("\\\\server\\share\\latest.json", "textx-1.3.0-rh8_0-win.yak"), "relative link next to a shared feed");
            Equal("https://example.com/feed/textx.yak", ReleaseFeed.Resolve("https://example.com/feed/latest.json", "textx.yak"), "relative link next to a web feed");
            Equal("https://cdn.example.com/a.yak", ReleaseFeed.Resolve("C:\\feed\\latest.json", "https://cdn.example.com/a.yak"), "absolute link kept");
            var shared = ReleaseFeed.Parse("{\"version\":\"1.3.0\",\"download\":\"textx-1.3.0-rh8_0-win.yak\"}", "D:\\TextX\\latest.json");
            Equal("D:\\TextX\\textx-1.3.0-rh8_0-win.yak", shared.DownloadUrl, "parse resolves relative download");
        }

        public static void PackageInstall()
        {
            var root = Path.Combine(Path.GetTempPath(), "textx-pkgtest-" + Guid.NewGuid().ToString("N"));
            try
            {
                // an installed package: <root>/textx/1.2.0/TextX.rhp + <root>/textx/manifest.txt
                var pkg = Path.Combine(root, "textx");
                var v120 = Path.Combine(pkg, "1.2.0");
                Directory.CreateDirectory(v120);
                File.WriteAllText(Path.Combine(v120, "TextX.rhp"), "old");
                File.WriteAllText(Path.Combine(pkg, "manifest.txt"), "1.2.0\n");

                var loc = PackageInstaller.Locate(Path.Combine(v120, "TextX.rhp"), "textx");
                True(loc != null, "package install detected");
                Equal("1.2.0", loc == null ? null : loc.RunningVersion, "running version folder");
                Equal("1.2.0", PackageInstaller.ReadActiveVersion(loc), "active version read");
                True(PackageInstaller.Locate(Path.Combine(root, "elsewhere", "TextX.rhp"), "textx") == null, "hand-registered copy is not a package");
                Directory.CreateDirectory(Path.Combine(v120, "net48"));
                True(PackageInstaller.Locate(Path.Combine(v120, "net48", "TextX.rhp"), "textx") != null, "runtime sub-folder detected");

                // a newer package
                var yak = Path.Combine(root, "textx-1.3.0-rh8_0-win.yak");
                MakeYak(yak, "textx", "1.3.0");
                string sha = PackageInstaller.Sha256(yak);
                Equal(64, sha.Length, "sha256 length");

                bool threw = false;
                try { PackageInstaller.Install(yak, loc, "otherpkg", SemVersion.Parse("1.3.0")); } catch (InvalidDataException) { threw = true; }
                True(threw, "wrong package name refused");
                threw = false;
                try { PackageInstaller.Install(yak, loc, "textx", SemVersion.Parse("1.4.0")); } catch (InvalidDataException) { threw = true; }
                True(threw, "wrong version refused");
                Equal("1.2.0", PackageInstaller.ReadActiveVersion(loc), "refused install leaves active version");

                var installedTo = PackageInstaller.Install(yak, loc, "textx", SemVersion.Parse("1.3.0"));
                Equal(Path.Combine(pkg, "1.3.0"), installedTo, "installed into the version folder");
                Equal("new 1.3.0", File.ReadAllText(Path.Combine(pkg, "1.3.0", "TextX.rhp")), "package files extracted");
                True(File.Exists(Path.Combine(pkg, "1.3.0", "manifest.yml")), "manifest.yml kept");
                Equal("1.3.0\n", File.ReadAllText(Path.Combine(pkg, "manifest.txt")), "manifest.txt switched to the new version");
                Equal("old", File.ReadAllText(Path.Combine(v120, "TextX.rhp")), "running version untouched");
                True(!Directory.Exists(Path.Combine(pkg, ".1.3.0.partial")), "no staging folder left");

                // installing again (for example after an interrupted attempt) replaces the folder
                PackageInstaller.Install(yak, loc, "textx", SemVersion.Parse("1.3.0"));
                Equal("1.3.0", PackageInstaller.ReadActiveVersion(loc), "re-install keeps the new version active");

                threw = false;
                var same = Path.Combine(root, "same.yak");
                MakeYak(same, "textx", "1.2.0");
                try { PackageInstaller.Install(same, loc, "textx", SemVersion.Parse("1.2.0")); } catch (InvalidOperationException) { threw = true; }
                True(threw, "running version is never overwritten");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void MakeYak(string path, string name, string version)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var w = new StreamWriter(zip.CreateEntry("manifest.yml").Open()))
                    w.Write("---\nname: " + name + "\nversion: " + version + "\nauthors:\n- Someone\ndescription: >\n  name: not-this\n");
                using (var w = new StreamWriter(zip.CreateEntry("TextX.rhp").Open()))
                    w.Write("new " + version);
            }
        }
    }
}
