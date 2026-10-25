using System;
using System.Linq;
using System.Reflection;
using TextX.Core.Updates;

namespace TextX
{
    /// <summary>
    /// Product name, version and release settings of this build. The values are compiled in from
    /// VERSION and Directory.Build.props, so a release only needs those two files changed.
    /// </summary>
    public static class BuildInfo
    {
        private static Assembly Asm { get { return typeof(BuildInfo).Assembly; } }

        public static string ProductName
        {
            get
            {
                var a = Asm.GetCustomAttribute<AssemblyTitleAttribute>();
                return a != null && !string.IsNullOrEmpty(a.Title) ? a.Title : "Text X";
            }
        }

        /// <summary>The release number, e.g. "1.1.0" or "1.2.0-beta.1".</summary>
        public static string VersionText
        {
            get
            {
                var a = Asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var v = a != null ? a.InformationalVersion : null;
                if (string.IsNullOrEmpty(v))
                {
                    var n = Asm.GetName().Version;
                    v = n == null ? "0.0.0" : n.ToString(3);
                }
                int plus = v.IndexOf('+');
                return plus > 0 ? v.Substring(0, plus) : v;
            }
        }

        public static SemVersion Version
        {
            get
            {
                SemVersion v;
                return SemVersion.TryParse(VersionText, out v) ? v : new SemVersion(0, 0, 0);
            }
        }

        public static string PackageName { get { return Meta("TextX.PackageName", "textx"); } }
        public static string UpdateFeedUrl { get { return Meta("TextX.UpdateFeedUrl", string.Empty); } }
        public static string Website { get { return Meta("TextX.Website", string.Empty); } }
        public static string GitHubRepo { get { return Meta("TextX.GitHubRepo", string.Empty); } }
        public static string BuildDate { get { return Meta("TextX.BuildDate", string.Empty); } }

        public static string Authors
        {
            get
            {
                var a = Asm.GetCustomAttribute<AssemblyCompanyAttribute>();
                return a != null ? a.Company : string.Empty;
            }
        }

        /// <summary>Full path of the loaded TextX.rhp.</summary>
        public static string AssemblyPath
        {
            get
            {
                try { return Asm.Location ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        public static string InstallFolder
        {
            get
            {
                try { return System.IO.Path.GetDirectoryName(Asm.Location) ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        public static string Runtime
        {
            get
            {
                try { return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; }
                catch { return Environment.Version.ToString(); }
            }
        }

        /// <summary>"Text X 1.1.0"</summary>
        public static string DisplayName { get { return ProductName + " " + VersionText; } }

        private static string Meta(string key, string fallback)
        {
            var m = Asm.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key);
            return m != null && m.Value != null ? m.Value : fallback;
        }
    }
}
