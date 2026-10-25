using System;
using System.IO;
using System.Linq;
using Rhino;
using Rhino.PlugIns;
using TextX.Integration;

namespace TextX
{
    /// <summary>
    /// Text X for Rhino 8: translate annotation text (default English to Hebrew) with correct
    /// right-to-left rendering, keep English on screen and print/export the translated text.
    /// Loads at Rhino start-up so printing and PDF export are always handled. Type TR to open it.
    /// </summary>
    public class TextXPlugIn : PlugIn
    {
        public TextXPlugIn()
        {
            Instance = this;
        }

        public static TextXPlugIn Instance { get; private set; }

        /// <summary>Typed plug-in settings.</summary>
        public TextXSettings Options { get; private set; }

        public override PlugInLoadTime LoadTime
        {
            get { return PlugInLoadTime.AtStartup; }
        }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            try
            {
                // must run before Settings is first read, so settings of the 1.0 preview are picked up
                SettingsMigration.Run(SettingsDirectory, Id);
                Options = new TextXSettings(Settings, SettingsDirectory);
                if (Diagnostics.Enabled)
                {
                    Diagnostics.Log(BuildInfo.DisplayName + " loaded, runtime " + BuildInfo.Runtime);
                    AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
                    {
                        var st = e.Exception.StackTrace ?? string.Empty;
                        if (st.Contains("TextX.")) Diagnostics.Log("first-chance " + e.Exception.GetType().Name + ": " + e.Exception.Message + " @ " + st.Replace(System.Environment.NewLine, " | "));
                    };
                    AppDomain.CurrentDomain.UnhandledException += (s, e) => Diagnostics.Log("UNHANDLED " + e.ExceptionObject);
                }
                PrintSwapManager.Instance.Attach();
                RhinoApp.Closing += OnRhinoClosing;
                RhinoApp.Idle += OnFirstIdle;
                return LoadReturnCode.Success;
            }
            catch (Exception ex)
            {
                errorMessage = BuildInfo.ProductName + " failed to load: " + ex.Message;
                return LoadReturnCode.ErrorShowDialog;
            }
        }

        /// <summary>Start-up tasks that should not slow down Rhino's launch.</summary>
        private void OnFirstIdle(object sender, EventArgs e)
        {
            RhinoApp.Idle -= OnFirstIdle;
            try
            {
                var current = BuildInfo.VersionText;
                var previous = Options.LastRunVersion;
                if (!string.IsNullOrEmpty(previous) && previous != current)
                    RhinoApp.WriteLine("{0} was updated from {1} to {2}. Type TR to open it, TRAbout for details.", BuildInfo.ProductName, previous, current);
                else if (string.IsNullOrEmpty(previous))
                    RhinoApp.WriteLine("{0} is installed. Type TR to open it.", BuildInfo.DisplayName);
                Options.LastRunVersion = current;

                WarnIfAliasHidesCommand();
                UpdateService.Instance.CheckInBackgroundIfDue();
            }
            catch (Exception ex)
            {
                Diagnostics.Log("first idle: " + ex.Message);
            }
        }

        /// <summary>A Rhino alias with the same name takes precedence over a command, so tell the user.</summary>
        private static void WarnIfAliasHidesCommand()
        {
            try
            {
                if (!Rhino.ApplicationSettings.CommandAliasList.IsAlias("TR")) return;
                var macro = Rhino.ApplicationSettings.CommandAliasList.GetMacro("TR");
                RhinoApp.WriteLine("{0}: your alias \"TR\" ({1}) hides the TR command. Use the TextX command instead, or remove the alias in Options > Aliases.",
                    BuildInfo.ProductName, macro);
            }
            catch
            {
                // alias list not available
            }
        }

        private void OnRhinoClosing(object sender, EventArgs e)
        {
            Shutdown();
        }

        protected override void OnShutdown()
        {
            Shutdown();
            base.OnShutdown();
        }

        private bool _shutDown;

        private void Shutdown()
        {
            if (_shutDown) return;
            _shutDown = true;
            try
            {
                PrintSwapManager.Instance.RestoreAllOnShutdown();
                PrintSwapManager.Instance.Detach();
                TranslationServices.SaveCache();
            }
            catch
            {
                // Rhino is closing
            }
        }
    }

    /// <summary>
    /// Rhino keeps plug-in settings in a folder named after the plug-in ("Plug-ins\Name (id)\settings").
    /// The 1.0 preview was called "Translator"; Text X has the same id, so its settings, translation
    /// memory and glossary are copied over once.
    /// </summary>
    internal static class SettingsMigration
    {
        public static void Run(string settingsDirectory, Guid id)
        {
            try
            {
                if (string.IsNullOrEmpty(settingsDirectory)) return;
                var current = new DirectoryInfo(settingsDirectory);
                var pluginFolder = current.Parent;                    // "...\Plug-ins\Text X (id)"
                var pluginsRoot = pluginFolder == null ? null : pluginFolder.Parent; // "...\Plug-ins"
                if (pluginsRoot == null || !pluginsRoot.Exists) return;
                if (current.Exists && current.EnumerateFiles().Any()) return; // already has settings

                var suffix = "(" + id.ToString("D") + ")";
                var old = pluginsRoot.EnumerateDirectories()
                    .Where(d => d.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(d.FullName.TrimEnd('\\', '/'), pluginFolder.FullName.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    .Select(d => new DirectoryInfo(Path.Combine(d.FullName, current.Name)))
                    .Where(d => d.Exists)
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (old == null) return;

                Directory.CreateDirectory(current.FullName);
                foreach (var f in old.EnumerateFiles())
                {
                    var target = Path.Combine(current.FullName, f.Name);
                    if (!File.Exists(target)) f.CopyTo(target);
                }
                RhinoApp.WriteLine("Text X: settings, translation memory and glossary were copied from the earlier Translator version.");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("settings migration: " + ex.Message);
            }
        }
    }
}
