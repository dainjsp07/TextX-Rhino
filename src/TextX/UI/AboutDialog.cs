using System;
using System.Threading;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using TextX.Integration;
using TextX.UI.Controls;
using TextX.UI.Theming;

namespace TextX.UI
{
    /// <summary>TRAbout: version, install location, update status and links.</summary>
    public sealed class AboutDialog : Dialog
    {
        private readonly ThemeScope _scope = new ThemeScope();
        private readonly Label _status;
        private readonly FlatButton _download;

        public static void ShowFor(Control parent)
        {
            var dlg = new AboutDialog();
            dlg.ShowModal(parent ?? (Control)RhinoEtoApp.MainWindow);
        }

        private AboutDialog()
        {
            Theme.Refresh();
            Title = "About " + BuildInfo.ProductName;
            Resizable = false;
            Padding = new Padding(0);
            ClientSize = new Size(500, 450);
            this.UseRhinoStyle();
            Icons.ApplyWindowIcon(this);
            KeyDown += (s, e) => { if (e.Key == Keys.Tab) TextX.UI.Controls.ThemedDrawable.NoteKeyboardNavigation(); };
            _scope.Bind(this, (x, p) => x.BackgroundColor = p.Window);

            var logo = new ImageView { Image = Icons.CreateAppIcon(64), Size = new Size(64, 64) };
            var name = Ui.Label(_scope, BuildInfo.ProductName, 22f, true);
            var version = Ui.Label(_scope, "Version " + BuildInfo.VersionText + (string.IsNullOrEmpty(BuildInfo.BuildDate) ? string.Empty : "  ·  built " + BuildInfo.BuildDate), 13f, false, p => p.Muted);
            var titleBlock = new TableLayout { Spacing = new Size(0, 2), Rows = { name, version } };

            var description = Ui.Label(_scope, "Translate Rhino text and leaders with correct right-to-left rendering. English stays on screen; the translation is used when printing and exporting PDF. Type TR to open the window.", 12.5f, false, p => p.Text, true);
            description.Width = 420;

            var details = new TableLayout { Spacing = new Size(10, 4) };
            details.Rows.Add(new TableRow(Ui.Label(_scope, "Installed in", 12f, false, p => p.Muted), Ui.Label(_scope, BuildInfo.InstallFolder, 12f)));
            details.Rows.Add(new TableRow(Ui.Label(_scope, "Rhino runtime", 12f, false, p => p.Muted), Ui.Label(_scope, BuildInfo.Runtime, 12f)));
            if (!string.IsNullOrEmpty(BuildInfo.Authors))
                details.Rows.Add(new TableRow(Ui.Label(_scope, "Author", 12f, false, p => p.Muted), Ui.Label(_scope, BuildInfo.Authors, 12f)));

            _status = Ui.Label(_scope, string.Empty, 12.5f, false, p => p.Text, true);
            _status.Width = 420;
            var check = new FlatButton("Check for updates", IconKind.Refresh, ButtonKind.Secondary, 32);
            check.Click += (s, e) => CheckNow();
            _download = new FlatButton("Install update", IconKind.Download, ButtonKind.Primary, 32) { Visible = false };
            _download.Click += (s, e) => InstallOrDownload();
            var website = new FlatButton("Website", IconKind.None, ButtonKind.Ghost, 32) { Visible = !string.IsNullOrEmpty(BuildInfo.Website) };
            website.Click += (s, e) => UpdateService.OpenUrl(BuildInfo.Website);
            var close = new FlatButton("Close", IconKind.None, ButtonKind.Ghost, 32);
            close.Click += (s, e) => Close();

            var layout = new TableLayout { Spacing = new Size(0, 14), Padding = new Padding(22, 20) };
            layout.Rows.Add(new TableRow(Ui.Row(16, logo, titleBlock)));
            layout.Rows.Add(new TableRow(description));
            layout.Rows.Add(new TableRow(details));
            layout.Rows.Add(new TableRow(Ui.HLine(_scope)));
            layout.Rows.Add(new TableRow(_status));
            layout.Rows.Add(new TableRow(Ui.Row(8, check, _download, website, Ui.Spring(), close)));
            Content = layout;
            AbortButton = null;
            ShowStatus();
            EventHandler changed = (s, e) => Application.Instance.AsyncInvoke(ShowStatus);
            UpdateService.Instance.Changed += changed;
            Closed += (s, e) => UpdateService.Instance.Changed -= changed;
        }

        private void InstallOrDownload()
        {
            var svc = UpdateService.Instance;
            var latest = svc.Latest;
            if (latest == null) return;
            if (!svc.CanInstallLatest)
            {
                UpdateService.OpenUrl(latest.BestUrl);
                return;
            }
            svc.InstallAsync(latest, CancellationToken.None).ContinueWith(t =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        _status.Text = "The update could not be installed: " + (ex == null ? "unknown error" : ex.Message) + ".";
                        return;
                    }
                    ShowStatus();
                });
            });
            ShowStatus();
        }

        private void ShowStatus()
        {
            var svc = UpdateService.Instance;
            if (!svc.IsConfigured)
            {
                _status.Text = "This copy has no update location, so it cannot check for new versions. Ask whoever shared Text X with you for the latest .yak file.";
                _download.Visible = false;
                return;
            }
            if (svc.IsInstalling)
            {
                _status.Text = "Downloading and installing " + BuildInfo.ProductName + " " + (svc.Latest != null ? svc.Latest.Version.ToString() : string.Empty) + "...";
                _download.Visible = false;
                return;
            }
            if (svc.UpdateAvailable)
            {
                _status.Text = svc.CanInstallLatest
                    ? BuildInfo.ProductName + " " + svc.Latest.Version + " is available. Install it now; it will be used the next time you start Rhino."
                    : BuildInfo.ProductName + " " + svc.Latest.Version + " is available. Download it, drag the .yak file into Rhino and restart Rhino.";
                _download.Text = svc.CanInstallLatest ? "Install update" : "Download update";
                _download.Visible = true;
                return;
            }
            _download.Visible = false;
            var pending = svc.PendingVersion;
            if (pending != null)
            {
                _status.Text = BuildInfo.ProductName + " " + pending + " is installed. Restart Rhino to start using it.";
                return;
            }
            var last = svc.LastCheckUtc;
            var auto = svc.CanInstall
                ? (TextXPlugIn.Instance.Options.InstallUpdatesAutomatically ? " New versions install themselves." : " Automatic installs are off (TRSettings).")
                : " This copy was not installed from a .yak package, so new versions must be installed by hand.";
            _status.Text = (last == DateTime.MinValue
                ? "Updates have not been checked yet."
                : "You have the latest version (last checked " + last.ToLocalTime().ToString("d MMM yyyy, HH:mm") + ").") + auto;
        }

        private void CheckNow()
        {
            var svc = UpdateService.Instance;
            if (!svc.IsConfigured) { ShowStatus(); return; }
            _status.Text = "Checking for updates...";
            svc.CheckAsync(CancellationToken.None).ContinueWith(t =>
            {
                Application.Instance.AsyncInvoke(() =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        _status.Text = "Could not check for updates: " + (ex == null ? "unknown error" : ex.Message);
                        return;
                    }
                    ShowStatus();
                });
            });
        }
    }
}
