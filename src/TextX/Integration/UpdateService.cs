using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using TextX.Core;
using TextX.Core.Updates;

namespace TextX.Integration
{
    /// <summary>
    /// Keeps Text X up to date. Once a day it reads the release feed (latest.json on GitHub, any web address,
    /// or a file in a shared folder). When a newer version exists and this copy was installed as a Rhino
    /// package (.yak), it downloads the package, checks its SHA-256 and installs it next to the running
    /// version; Rhino uses it from the next start. Copies installed some other way are told where to
    /// download it. Never blocks Rhino; background failures are only logged.
    /// </summary>
    public sealed class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new Lazy<UpdateService>(() => new UpdateService());
        public static UpdateService Instance { get { return _instance.Value; } }

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(CreateClient);
        private readonly Lazy<PackageLocation> _package = new Lazy<PackageLocation>(() => PackageInstaller.Locate(BuildInfo.AssemblyPath, BuildInfo.PackageName));
        private int _checking;
        private int _installing;

        /// <summary>Raised (on the UI thread) when the known latest release or the install state changes.</summary>
        public event EventHandler Changed;

        private UpdateService()
        {
            LoadCached();
        }

        private static TextXSettings Settings { get { return TextXPlugIn.Instance.Options; } }

        public string FeedUrl
        {
            get
            {
                var o = Settings.UpdateFeedOverride;
                return !string.IsNullOrWhiteSpace(o) ? o.Trim() : BuildInfo.UpdateFeedUrl;
            }
        }

        public bool IsConfigured { get { return !string.IsNullOrWhiteSpace(FeedUrl); } }

        /// <summary>The newest release seen by the last successful check (may be the installed one).</summary>
        public ReleaseInfo Latest { get; private set; }

        public DateTime LastCheckUtc { get { return Settings.LastUpdateCheckUtc; } }

        /// <summary>Where this copy is installed as a Rhino package, or null if it was installed some other way.</summary>
        public PackageLocation Package { get { return _package.Value; } }

        /// <summary>True when this copy can install updates by itself (it was installed from a .yak package).</summary>
        public bool CanInstall { get { return Package != null; } }

        public bool IsInstalling { get { return Volatile.Read(ref _installing) == 1; } }

        /// <summary>A newer version that is installed and will be used after Rhino restarts, or null.</summary>
        public SemVersion PendingVersion
        {
            get
            {
                SemVersion v;
                var active = PackageInstaller.ReadActiveVersion(Package);
                return active != null && SemVersion.TryParse(active, out v) && v > BuildInfo.Version ? v : null;
            }
        }

        /// <summary>A newer release that is not installed yet.</summary>
        public bool UpdateAvailable
        {
            get
            {
                if (!ReleaseFeed.IsUpdate(BuildInfo.Version, Latest, Settings.IncludePreReleases) || !IsCompatible(Latest)) return false;
                var pending = PendingVersion;
                return pending == null || Latest.Version > pending;
            }
        }

        /// <summary>True when the available update can be installed from inside Rhino.</summary>
        public bool CanInstallLatest { get { return CanInstall && Latest != null && Latest.HasPackage; } }

        /// <summary>An update the user has not dismissed with "Later".</summary>
        public bool ShouldNotify
        {
            get { return UpdateAvailable && Settings.DismissedUpdateVersion != Latest.Version.ToString(); }
        }

        /// <summary>An installed update waiting for a restart that the user has not acknowledged yet.</summary>
        public bool ShouldShowRestartNotice
        {
            get
            {
                var pending = PendingVersion;
                return pending != null && Settings.DismissedUpdateVersion != pending.ToString();
            }
        }

        public void Dismiss()
        {
            if (!UpdateAvailable && PendingVersion != null) Settings.DismissedUpdateVersion = PendingVersion.ToString();
            else if (Latest != null && Latest.Version != null) Settings.DismissedUpdateVersion = Latest.Version.ToString();
            RaiseChanged();
        }

        private static bool IsCompatible(ReleaseInfo r)
        {
            if (r == null || string.IsNullOrEmpty(r.MinRhino)) return true;
            int major;
            var head = r.MinRhino.Split('.')[0];
            return !int.TryParse(head, out major) || major <= RhinoApp.ExeVersion;
        }

        private void LoadCached()
        {
            try
            {
                var json = Settings.LatestReleaseJson;
                if (!string.IsNullOrEmpty(json)) Latest = ReleaseFeed.Parse(json);
            }
            catch
            {
                Latest = null;
            }
        }

        // ------------------------------------------------------------------ checking
        /// <summary>
        /// At most once a day: checks for a newer version and, when automatic installs are on and possible,
        /// downloads and installs it. Otherwise prints a note in the command line.
        /// </summary>
        public void CheckInBackgroundIfDue()
        {
            if (!IsConfigured || !Settings.CheckForUpdates) return;
            var last = Settings.LastUpdateCheckUtc;
            if (last != DateTime.MinValue && DateTime.UtcNow - last < CheckInterval)
            {
                // checked recently, but an update found then may still need installing
                if (UpdateAvailable) HandleAvailableUpdate();
                return;
            }
            CheckAsync(CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                    Diagnostics.Log("update check failed: " + (ex == null ? "?" : ex.Message));
                    return;
                }
                if (UpdateAvailable) HandleAvailableUpdate();
            }, TaskScheduler.Default);
        }

        private void HandleAvailableUpdate()
        {
            var latest = Latest;
            if (latest == null) return;
            if (Settings.InstallUpdatesAutomatically && CanInstallLatest)
            {
                InstallAsync(latest, CancellationToken.None).ContinueWith(i =>
                {
                    if (i.IsFaulted)
                    {
                        var ex = i.Exception == null ? null : i.Exception.GetBaseException();
                        Diagnostics.Log("automatic update failed: " + (ex == null ? "?" : ex.Message));
                        WriteLine("{0} {1} is available, but it could not be installed automatically ({2}). Run TRUpdate to try again.",
                            BuildInfo.ProductName, latest.Version, ex == null ? "unknown error" : ex.Message);
                        return;
                    }
                    WriteLine("{0} {1} has been downloaded and installed. It will be used the next time you start Rhino.",
                        BuildInfo.ProductName, latest.Version);
                }, TaskScheduler.Default);
            }
            else if (ShouldNotify)
            {
                WriteLine("{0} {1} is available (you have {2}). Run TRUpdate to {3} it.",
                    BuildInfo.ProductName, latest.Version, BuildInfo.VersionText, CanInstallLatest ? "install" : "download");
            }
        }

        /// <summary>Reads the feed. Returns the latest release, or throws with a readable message.</summary>
        public async Task<ReleaseInfo> CheckAsync(CancellationToken ct)
        {
            if (!IsConfigured) throw new InvalidOperationException("No update location is configured for this build.");
            if (Interlocked.Exchange(ref _checking, 1) == 1) return Latest;
            try
            {
                var feed = FeedUrl;
                string body;
                string path;
                if (ReleaseFeed.IsFileLocation(feed, out path))
                {
                    if (!File.Exists(path)) throw new Exception("the update file " + path + " was not found.");
                    body = await Task.Run(() => File.ReadAllText(path), ct).ConfigureAwait(false);
                }
                else
                {
                    body = await GetStringAsync(feed, ct).ConfigureAwait(false);
                }
                var info = ReleaseFeed.Parse(body, feed);
                Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                Settings.LatestReleaseJson = Serialize(info);
                Latest = info;
                RaiseChanged();
                return info;
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Accept.ParseAdd("application/json");
                    HttpResponseMessage response;
                    try
                    {
                        response = await Client.Value.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new Exception("the update server did not answer in time.");
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new Exception("network error - " + ex.Message, ex);
                    }
                    using (response)
                    {
                        if ((int)response.StatusCode == 404) throw new Exception("no release has been published at " + url + " yet.");
                        if (!response.IsSuccessStatusCode) throw new Exception("the update server answered " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ installing
        /// <summary>
        /// Downloads <paramref name="release"/>'s package, verifies it and installs it for the next Rhino start.
        /// Returns the folder it was installed to.
        /// </summary>
        public async Task<string> InstallAsync(ReleaseInfo release, CancellationToken ct)
        {
            if (!CanInstall) throw new InvalidOperationException("this copy was not installed from a .yak package, so it cannot install updates itself");
            if (release == null || !release.HasPackage) throw new InvalidOperationException("the release has no Rhino package to download");
            if (Interlocked.Exchange(ref _installing, 1) == 1) throw new InvalidOperationException("an update is already being installed");
            RaiseChanged();
            string file = null;
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "TextX-update");
                Directory.CreateDirectory(dir);
                file = Path.Combine(dir, BuildInfo.PackageName + "-" + release.Version + ".yak");
                await DownloadAsync(release.DownloadUrl, file, ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(release.Sha256))
                {
                    var actual = PackageInstaller.Sha256(file);
                    if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("the download is damaged (its checksum does not match). Try again later");
                }

                var package = Package;
                var target = await Task.Run(() => PackageInstaller.Install(file, package, BuildInfo.PackageName, release.Version), ct).ConfigureAwait(false);
                Settings.DismissedUpdateVersion = string.Empty;
                Diagnostics.Log("installed update " + release.Version + " to " + target);
                return target;
            }
            finally
            {
                if (file != null)
                {
                    try { File.Delete(file); } catch { /* temp file */ }
                }
                Interlocked.Exchange(ref _installing, 0);
                RaiseChanged();
            }
        }

        private static async Task DownloadAsync(string url, string destination, CancellationToken ct)
        {
            string path;
            if (ReleaseFeed.IsFileLocation(url, out path))
            {
                if (!File.Exists(path)) throw new Exception("the package " + path + " was not found");
                await Task.Run(() => File.Copy(path, destination, true), ct).ConfigureAwait(false);
                return;
            }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                HttpResponseMessage response;
                try
                {
                    response = await Client.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new Exception("the download timed out");
                }
                catch (HttpRequestException ex)
                {
                    throw new Exception("network error - " + ex.Message, ex);
                }
                using (response)
                {
                    if (!response.IsSuccessStatusCode) throw new Exception("the download failed (" + (int)response.StatusCode + " " + response.ReasonPhrase + ")");
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await source.CopyToAsync(target, 81920, timeout.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ helpers
        private static void WriteLine(string format, params object[] args)
        {
            var text = string.Format(format, args);
            RhinoApp.InvokeOnUiThread(new Action(() => RhinoApp.WriteLine(text)));
        }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h == null) return;
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try { h(this, EventArgs.Empty); }
                catch (Exception ex) { Diagnostics.Log("update Changed handler: " + ex.Message); }
            }));
        }

        private static string Serialize(ReleaseInfo r)
        {
            var d = new System.Collections.Generic.Dictionary<string, object>
            {
                { "version", r.Version.ToString() },
                { "download", r.DownloadUrl ?? string.Empty },
                { "page", r.PageUrl ?? string.Empty },
                { "notes", r.Notes ?? string.Empty },
                { "released", r.Published ?? string.Empty },
                { "minRhino", r.MinRhino ?? string.Empty },
                { "sha256", r.Sha256 ?? string.Empty },
            };
            return Json.Serialize(d);
        }

        private static HttpClient CreateClient()
        {
#if NETFRAMEWORK
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch
            {
                // older OS; ignore
            }
#endif
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TextX/" + BuildInfo.Version.ToString().Split('-')[0]);
            return client;
        }

        /// <summary>Opens a web page in the default browser.</summary>
        public static bool OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("{0}: could not open {1} ({2}).", BuildInfo.ProductName, url, ex.Message);
                return false;
            }
        }
    }
}
