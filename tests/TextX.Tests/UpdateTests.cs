using System;
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
        }
    }
}
