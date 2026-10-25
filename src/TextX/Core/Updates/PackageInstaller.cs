using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TextX.Core.Updates
{
    /// <summary>Where an installed Rhino package lives: <c>&lt;packages&gt;\&lt;name&gt;\&lt;version&gt;\</c>.</summary>
    public sealed class PackageLocation
    {
        /// <summary>The package's own folder (<c>…\packages\8.0\textx</c>), which holds manifest.txt and one folder per version.</summary>
        public string PackageFolder;
        /// <summary>The version folder the running copy was loaded from.</summary>
        public string VersionFolder;
        /// <summary>Name of the running version folder (for example "1.2.0").</summary>
        public string RunningVersion;

        public string ActiveManifestPath { get { return Path.Combine(PackageFolder, PackageInstaller.ActiveVersionFile); } }
    }

    /// <summary>
    /// Installs a newer version of a Rhino package (.yak) next to the running one, exactly as Rhino's own
    /// package installer (Yak) does: the package is extracted to <c>&lt;package folder&gt;\&lt;version&gt;\</c> and
    /// <c>manifest.txt</c> is set to the new version. Rhino reads manifest.txt at start-up
    /// (RhinoCommon <c>HostUtils.GetActivePlugInVersionFolders</c>), so the new version is used from the next
    /// start; the running version's files are never touched, and Rhino removes the old folder itself.
    /// </summary>
    public static class PackageInstaller
    {
        public const string ActiveVersionFile = "manifest.txt";
        public const string PackageManifest = "manifest.yml";

        /// <summary>
        /// Finds the package folder of an assembly installed as a Rhino package, or returns null when it was
        /// installed some other way (for example registered from a folder by hand).
        /// </summary>
        public static PackageLocation Locate(string assemblyPath, string packageName)
        {
            if (string.IsNullOrEmpty(assemblyPath) || string.IsNullOrEmpty(packageName)) return null;
            try
            {
                var child = new FileInfo(assemblyPath).Directory;
                // the plug-in may sit in the version folder itself or in a runtime sub-folder (net48, net7.0 …)
                for (int depth = 0; depth < 3 && child != null && child.Parent != null; depth++)
                {
                    var parent = child.Parent;
                    if (string.Equals(parent.Name, packageName, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(Path.Combine(parent.FullName, ActiveVersionFile)))
                    {
                        return new PackageLocation { PackageFolder = parent.FullName, VersionFolder = child.FullName, RunningVersion = child.Name };
                    }
                    child = parent;
                }
            }
            catch
            {
                // unusual path: treat as not a package install
            }
            return null;
        }

        /// <summary>The version Rhino will load at the next start (the first line of manifest.txt), or null.</summary>
        public static string ReadActiveVersion(PackageLocation location)
        {
            if (location == null) return null;
            try
            {
                if (!File.Exists(location.ActiveManifestPath)) return null;
                foreach (var line in File.ReadAllLines(location.ActiveManifestPath))
                {
                    var t = line.Trim();
                    if (t.Length > 0) return t;
                }
            }
            catch
            {
                // unreadable: unknown
            }
            return null;
        }

        /// <summary>Reads <c>name</c> and <c>version</c> from a package's manifest.yml.</summary>
        public static bool TryReadManifest(TextReader reader, out string name, out string version)
        {
            name = null;
            version = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || char.IsWhiteSpace(line[0])) continue; // only top-level keys
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim().Trim('"', '\'');
                if (key == "name") name = value;
                else if (key == "version") version = value;
            }
            return !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(version);
        }

        /// <summary>
        /// Installs <paramref name="yakPath"/> into <paramref name="location"/>'s package folder and makes it the
        /// version Rhino loads next time. Checks that the package is <paramref name="expectedName"/> at
        /// <paramref name="expectedVersion"/>. Returns the new version folder.
        /// </summary>
        public static string Install(string yakPath, PackageLocation location, string expectedName, SemVersion expectedVersion)
        {
            if (location == null) throw new InvalidOperationException("This copy was not installed as a Rhino package, so it cannot update itself.");
            if (!File.Exists(yakPath)) throw new FileNotFoundException("The downloaded package was not found.", yakPath);

            string name, version;
            using (var zip = ZipFile.OpenRead(yakPath))
            {
                var entry = zip.GetEntry(PackageManifest);
                if (entry == null) throw new InvalidDataException("The download is not a Rhino package (no manifest.yml).");
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    if (!TryReadManifest(reader, out name, out version)) throw new InvalidDataException("The package manifest has no name or version.");
                }
            }
            if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download is the package '" + name + "', not '" + expectedName + "'.");
            SemVersion packaged;
            if (!SemVersion.TryParse(version, out packaged) || (expectedVersion != null && packaged.CompareTo(expectedVersion) != 0))
                throw new InvalidDataException("The download contains version " + version + ", expected " + expectedVersion + ".");
            if (string.Equals(version, location.RunningVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Version " + version + " is the one that is running.");

            var target = Path.Combine(location.PackageFolder, version);
            var staging = Path.Combine(location.PackageFolder, "." + version + ".partial");
            TryDelete(staging);
            Directory.CreateDirectory(staging);
            try
            {
                // ExtractToDirectory refuses entries that would land outside the folder.
                ZipFile.ExtractToDirectory(yakPath, staging);
                TryDelete(target); // left over from an earlier, interrupted attempt
                if (Directory.Exists(target)) throw new IOException("The folder " + target + " is in use.");
                Directory.Move(staging, target);
            }
            catch
            {
                TryDelete(staging);
                throw;
            }

            // switch the active version last, so an interrupted install leaves the current one in place
            var manifest = location.ActiveManifestPath;
            var temp = manifest + ".new";
            File.WriteAllText(temp, version + "\n", new UTF8Encoding(false));
            if (File.Exists(manifest)) File.Delete(manifest);
            File.Move(temp, manifest);
            return target;
        }

        /// <summary>SHA-256 of a file as lower-case hex.</summary>
        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void TryDelete(string folder)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch
            {
                // checked by the caller where it matters
            }
        }
    }
}
