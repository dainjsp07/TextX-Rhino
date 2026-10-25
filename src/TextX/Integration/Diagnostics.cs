using System;
using System.IO;
using System.Reflection;

namespace TextX.Integration
{
    /// <summary>
    /// Opt-in trace log for troubleshooting. Enabled only when a file named
    /// "TextX.trace" exists next to TextX.rhp; the log is written to
    /// "TextX.log" in the same folder.
    /// </summary>
    internal static class Diagnostics
    {
        private static readonly object Lock = new object();
        private static readonly string LogPath;
        public static readonly bool Enabled;

        static Diagnostics()
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "TextX.trace")))
                {
                    LogPath = Path.Combine(dir, "TextX.log");
                    Enabled = true;
                }
            }
            catch
            {
                Enabled = false;
            }
        }

        public static void Log(string message)
        {
            if (!Enabled) return;
            try
            {
                lock (Lock)
                {
                    File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " [" + Environment.CurrentManagedThreadId + "] " + message + Environment.NewLine);
                }
            }
            catch
            {
                // diagnostics must never throw
            }
        }
    }
}
