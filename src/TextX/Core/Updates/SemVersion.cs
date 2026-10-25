using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TextX.Core.Updates
{
    /// <summary>
    /// A semantic version (https://semver.org): MAJOR.MINOR.PATCH with an optional pre-release label
    /// such as "1.2.0-beta.1". Build metadata after '+' is accepted and ignored for comparison.
    /// A leading "v" (as in git tags, "v1.2.0") is accepted.
    /// </summary>
    public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
    {
        private static readonly Regex Pattern = new Regex(
            @"^\s*[vV]?(?<major>\d+)\.(?<minor>\d+)(\.(?<patch>\d+))?(-(?<pre>[0-9A-Za-z.\-]+))?(\+[0-9A-Za-z.\-]+)?\s*$",
            RegexOptions.CultureInvariant);

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }

        /// <summary>Pre-release label ("beta.1"), or empty for a release.</summary>
        public string PreRelease { get; private set; }

        public bool IsPreRelease { get { return PreRelease.Length > 0; } }

        public SemVersion(int major, int minor, int patch, string preRelease = null)
        {
            if (major < 0 || minor < 0 || patch < 0) throw new ArgumentOutOfRangeException("major");
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? string.Empty;
        }

        public static bool TryParse(string text, out SemVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var m = Pattern.Match(text);
            if (!m.Success) return false;
            int major, minor, patch = 0;
            if (!int.TryParse(m.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)) return false;
            if (!int.TryParse(m.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)) return false;
            if (m.Groups["patch"].Success && !int.TryParse(m.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            version = new SemVersion(major, minor, patch, m.Groups["pre"].Success ? m.Groups["pre"].Value : string.Empty);
            return true;
        }

        public static SemVersion Parse(string text)
        {
            SemVersion v;
            if (!TryParse(text, out v)) throw new FormatException("Not a version number: '" + text + "'.");
            return v;
        }

        /// <summary>The next version for a release of the given kind ("major", "minor" or "patch").</summary>
        public SemVersion Bump(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "major": return new SemVersion(Major + 1, 0, 0);
                case "minor": return new SemVersion(Major, Minor + 1, 0);
                case "patch":
                    // 1.2.0-beta.1 -> 1.2.0 (finishing a pre-release), 1.2.0 -> 1.2.1
                    return IsPreRelease ? new SemVersion(Major, Minor, Patch) : new SemVersion(Major, Minor, Patch + 1);
                default: throw new ArgumentException("Use major, minor or patch.", "kind");
            }
        }

        public int CompareTo(SemVersion other)
        {
            if (ReferenceEquals(other, null)) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            // a release is newer than any pre-release of the same number
            if (!IsPreRelease && !other.IsPreRelease) return 0;
            if (!IsPreRelease) return 1;
            if (!other.IsPreRelease) return -1;
            return ComparePreRelease(PreRelease, other.PreRelease);
        }

        private static int ComparePreRelease(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
            {
                int na, nb;
                bool ia = int.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out na);
                bool ib = int.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out nb);
                int c;
                if (ia && ib) c = na.CompareTo(nb);
                else if (ia) c = -1; // numeric identifiers sort before alphanumeric ones
                else if (ib) c = 1;
                else c = string.CompareOrdinal(pa[i], pb[i]);
                if (c != 0) return c;
            }
            return pa.Length.CompareTo(pb.Length);
        }

        public bool Equals(SemVersion other)
        {
            return CompareTo(other) == 0;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SemVersion);
        }

        public override int GetHashCode()
        {
            unchecked { return ((Major * 397) ^ Minor) * 397 ^ Patch ^ PreRelease.GetHashCode(); }
        }

        public static bool operator >(SemVersion a, SemVersion b) { return Compare(a, b) > 0; }
        public static bool operator <(SemVersion a, SemVersion b) { return Compare(a, b) < 0; }
        public static bool operator >=(SemVersion a, SemVersion b) { return Compare(a, b) >= 0; }
        public static bool operator <=(SemVersion a, SemVersion b) { return Compare(a, b) <= 0; }

        private static int Compare(SemVersion a, SemVersion b)
        {
            if (ReferenceEquals(a, null)) return ReferenceEquals(b, null) ? 0 : -1;
            return a.CompareTo(b);
        }

        public override string ToString()
        {
            var s = Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture) + "." + Patch.ToString(CultureInfo.InvariantCulture);
            return IsPreRelease ? s + "-" + PreRelease : s;
        }
    }
}
