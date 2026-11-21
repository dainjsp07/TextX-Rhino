using System;
using System.IO;
using System.Linq;
using TextX.Core.Translation;

// Stand-ins for the Rhino APIs the Studio window touches, so it can run on its own.

namespace Rhino
{
    public class RhinoDoc
    {
        public static RhinoDoc ActiveDoc { get { return null; } }
        public uint RuntimeSerialNumber { get { return 0; } }
        public static RhinoDoc FromRuntimeSerialNumber(uint serial) { return null; }
    }

    public static class RhinoApp
    {
        public static bool RunScript(string script, bool echo)
        {
            Console.WriteLine("  (Rhino command) " + script);
            return true;
        }
    }

    namespace Runtime
    {
        public static class HostUtils
        {
            public static bool RunningInDarkMode { get { return Environment.GetEnvironmentVariable("TEXTX_PREVIEW_THEME") == "dark"; } }
        }
    }

    namespace Commands
    {
        public class Command
        {
            public static bool InCommand() { return false; }
        }
    }

    namespace UI
    {
        public static class RhinoEtoApp
        {
            public static Eto.Forms.Window MainWindow { get { return null; } }
        }

        public static class EtoExtensions
        {
            public static void UseRhinoStyle(this Eto.Forms.Window window) { }
        }
    }
}

namespace TextX
{
    public sealed class TextXPlugIn
    {
        private static readonly TextXPlugIn _instance = new TextXPlugIn();
        public static TextXPlugIn Instance { get { return _instance; } }
        private readonly Integration.TextXSettings _options = new Integration.TextXSettings();
        public Integration.TextXSettings Options { get { return _options; } }
    }
}

namespace TextX.Integration
{
    public sealed class TextXSettings
    {
        public TextXSettings()
        {
            SettingsDirectory = Path.Combine(Path.GetTempPath(), "textx-preview-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(SettingsDirectory);
            StudioSourceLanguage = Core.Languages.AutoDetect;
            StudioTargetLanguage = "en";
            SourceLanguage = Core.Languages.AutoDetect;
            TargetLanguage = "en";
            ProviderId = ProviderIds.GoogleFree;
            RealTimeTranslation = true;
            RealTimeDelayMs = 600;
            DefaultMode = Core.TranslationDisplayMode.Dual;
            StudioBounds = string.Empty;
            SchedulesFromTranslation = true;
            ThemeMode = Environment.GetEnvironmentVariable("TEXTX_PREVIEW_THEME") == "dark" ? "Dark" : "Light";
        }

        public string SettingsDirectory { get; private set; }
        public string DocumentsDirectory { get { return Path.Combine(SettingsDirectory, "documents"); } }
        public string TessDataDirectory
        {
            get { return Environment.GetEnvironmentVariable("TEXTX_TESSDATA") ?? Path.Combine(SettingsDirectory, "tessdata"); }
        }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public string ProviderId { get; set; }
        public bool RealTimeTranslation { get; set; }
        public int RealTimeDelayMs { get; set; }
        public Core.TranslationDisplayMode DefaultMode { get; set; }
        public string StudioSourceLanguage { get; set; }
        public string StudioTargetLanguage { get; set; }
        public string StudioBounds { get; set; }
        public bool SchedulesFromTranslation { get; set; }
        public double ScheduleTextHeight { get; set; }
        public string ThemeMode { get; set; }
    }

    public static class TranslationServices
    {
        private static readonly Glossary _glossary = new Glossary();
        private static readonly TranslationCache _cache = new TranslationCache();

        public static Glossary Glossary { get { return _glossary; } }
        public static TranslationCache Cache { get { return _cache; } }

        public static TranslationEngine CreateEngine()
        {
            return new TranslationEngine(ProviderFactory.Create(ProviderIds.GoogleFree, new ProviderSettings()), null, Glossary, Cache);
        }

        public static TranslationEngine CreateDocumentEngine()
        {
            return new TranslationEngine(new JoiningProvider(ProviderFactory.Create(ProviderIds.GoogleFree, new ProviderSettings())), null, Glossary, Cache);
        }

        public static void SaveCache() { }
        public static void SaveGlossary() { }
    }

    public static class RhinoCapture
    {
        public static Core.Tools.GrayImage ActiveView(Rhino.RhinoDoc doc) { return null; }
    }

    /// <summary>No update checks in the preview.</summary>
    public sealed class UpdateService
    {
        private static readonly UpdateService _instance = new UpdateService();
        public static UpdateService Instance { get { return _instance; } }
        public event EventHandler Changed { add { } remove { } }
        public Core.Updates.ReleaseInfo Latest { get { return null; } }
        public bool IsInstalling { get { return false; } }
        public bool ShouldNotify { get { return false; } }
        public bool ShouldShowRestartNotice { get { return false; } }
        public bool CanInstallLatest { get { return false; } }
        public Core.Updates.SemVersion PendingVersion { get { return null; } }
        public void Dismiss() { }
        public System.Threading.Tasks.Task<string> InstallAsync(Core.Updates.ReleaseInfo release, System.Threading.CancellationToken ct)
        {
            return System.Threading.Tasks.Task.FromResult(string.Empty);
        }
        public static bool OpenUrl(string url) { return false; }
    }
}

namespace TextX.UI
{
    public static class SettingsDialog
    {
        public static void ShowFor(Eto.Forms.Control parent) { }
    }
}
