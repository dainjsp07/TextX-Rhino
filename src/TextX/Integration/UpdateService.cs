using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using TextX.Core;
using TextX.Core.Updates;

namespace TextX.Integration
{
    /// <summary>
    /// Checks the release feed (latest.json on GitHub, or any URL) for a newer version of Text X.
    /// Checks at most once a day in the background, never blocks Rhino, and stays silent on errors.
    /// The result is remembered so the window can show "update available" immediately.
    /// </summary>
    public sealed class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new Lazy<UpdateService>(() => new UpdateService());
        public static UpdateService Instance { get { return _instance.Value; } }

        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
        private static readonly Lazy<HttpClient> Client = new Lazy<HttpClient>(CreateClient);
        private int _checking;

        /// <summary>Raised (on the UI thread) when the known latest release changes.</summary>
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

        public bool UpdateAvailable
        {
            get { return ReleaseFeed.IsUpdate(BuildInfo.Version, Latest, Settings.IncludePreReleases); }
        }

        /// <summary>An update the user has not dismissed with "Later".</summary>
        public bool ShouldNotify
        {
            get { return UpdateAvailable && Settings.DismissedUpdateVersion != Latest.Version.ToString(); }
        }

        public void Dismiss()
        {
            if (Latest != null && Latest.Version != null) Settings.DismissedUpdateVersion = Latest.Version.ToString();
            RaiseChanged();
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

        /// <summary>Starts a background check when automatic checks are on and the last one is older than a day.</summary>
        public void CheckInBackgroundIfDue()
        {
            if (!IsConfigured || !Settings.CheckForUpdates) return;
            var last = Settings.LastUpdateCheckUtc;
            if (last != DateTime.MinValue && DateTime.UtcNow - last < CheckInterval) return;
            CheckAsync(CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                    Diagnostics.Log("update check failed: " + (ex == null ? "?" : ex.Message));
                    return;
                }
                if (t.Result != null && ShouldNotify)
                {
                    RhinoApp.InvokeOnUiThread(new Action(() =>
                        RhinoApp.WriteLine("{0} {1} is available (you have {2}). Run TRUpdate to download it.",
                            BuildInfo.ProductName, Latest.Version, BuildInfo.VersionText)));
                }
            }, TaskScheduler.Default);
        }

        /// <summary>Downloads and reads the feed. Returns the latest release, or throws with a readable message.</summary>
        public async Task<ReleaseInfo> CheckAsync(CancellationToken ct)
        {
            if (!IsConfigured) throw new InvalidOperationException("No update location is configured for this build.");
            if (Interlocked.Exchange(ref _checking, 1) == 1) return Latest;
            try
            {
                string body;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    using (var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl))
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
                            if ((int)response.StatusCode == 404) throw new Exception("no release has been published at " + FeedUrl + " yet.");
                            if (!response.IsSuccessStatusCode) throw new Exception("the update server answered " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                            body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        }
                    }
                }
                var info = ReleaseFeed.Parse(body);
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
