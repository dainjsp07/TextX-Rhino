using System;
using System.Collections.Generic;
using System.Linq;

namespace TextX.Core.Updates
{
    /// <summary>One published release, as described by the update feed.</summary>
    public sealed class ReleaseInfo
    {
        public SemVersion Version;
        /// <summary>Direct download of the package (.yak), if known.</summary>
        public string DownloadUrl = string.Empty;
        /// <summary>Release page with notes and all downloads.</summary>
        public string PageUrl = string.Empty;
        public string Notes = string.Empty;
        public string Published = string.Empty;
        /// <summary>Lowest Rhino version the release supports ("8.0"), if stated.</summary>
        public string MinRhino = string.Empty;

        /// <summary>The best link to send the user to.</summary>
        public string BestUrl
        {
            get { return !string.IsNullOrEmpty(PageUrl) ? PageUrl : DownloadUrl; }
        }
    }

    /// <summary>
    /// Reads the update feed. Two formats are accepted:
    /// <list type="bullet">
    /// <item>Text X's own <c>latest.json</c>, written by tools/pack.ps1:
    /// <c>{"name":"Text X","version":"1.2.0","released":"2026-11-02","download":"…/textx-1.2.0-rh8_0-win.yak","page":"…","notes":"…","minRhino":"8.0"}</c></item>
    /// <item>A GitHub "latest release" API response (<c>tag_name</c>, <c>html_url</c>, <c>body</c>, <c>assets</c>).</item>
    /// </list>
    /// </summary>
    public static class ReleaseFeed
    {
        public static ReleaseInfo Parse(string json)
        {
            object root;
            try
            {
                root = Json.Parse(json);
            }
            catch (Exception ex)
            {
                throw new FormatException("The update feed is not valid JSON (" + ex.Message + ").");
            }
            if (!(root is Dictionary<string, object>)) throw new FormatException("The update feed has an unexpected format.");

            var info = new ReleaseInfo();
            var tag = Json.GetString(root, "tag_name");
            if (!string.IsNullOrEmpty(tag))
            {
                // GitHub API format
                info.Version = ParseVersion(tag);
                info.PageUrl = Json.GetString(root, "html_url") ?? string.Empty;
                info.Notes = Json.GetString(root, "body") ?? string.Empty;
                info.Published = Json.GetString(root, "published_at") ?? string.Empty;
                var assets = Json.GetList(root, "assets") ?? new List<object>();
                var urls = assets.Select(a => Json.GetString(a, "browser_download_url")).Where(u => !string.IsNullOrEmpty(u)).ToList();
                info.DownloadUrl = urls.FirstOrDefault(u => u.EndsWith(".yak", StringComparison.OrdinalIgnoreCase))
                                   ?? urls.FirstOrDefault(u => u.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                   ?? string.Empty;
                return info;
            }

            info.Version = ParseVersion(Json.GetString(root, "version"));
            info.DownloadUrl = Json.GetString(root, "download") ?? string.Empty;
            info.PageUrl = Json.GetString(root, "page") ?? string.Empty;
            info.Notes = Json.GetString(root, "notes") ?? string.Empty;
            info.Published = Json.GetString(root, "released") ?? string.Empty;
            info.MinRhino = Json.GetString(root, "minRhino") ?? string.Empty;
            return info;
        }

        private static SemVersion ParseVersion(string text)
        {
            SemVersion v;
            if (!SemVersion.TryParse(text, out v)) throw new FormatException("The update feed does not contain a valid version number ('" + text + "').");
            return v;
        }

        /// <summary>True when <paramref name="latest"/> should be offered to a user running <paramref name="installed"/>.</summary>
        public static bool IsUpdate(SemVersion installed, ReleaseInfo latest, bool includePreReleases)
        {
            if (latest == null || latest.Version == null || installed == null) return false;
            if (latest.Version.IsPreRelease && !includePreReleases) return false;
            return latest.Version > installed;
        }
    }
}
