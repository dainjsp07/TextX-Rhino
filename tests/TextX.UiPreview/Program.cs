using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Eto.Forms;
using TextX.UI.Studio;

namespace TextX.UiPreview
{
    /// <summary>
    /// Opens Text X Studio, walks through its screens with a sample document and saves a screenshot
    /// of each (PNG) in the output folder. Exit code = number of problems (exceptions, timeouts).
    /// Arguments: output folder, sample PDF.
    /// </summary>
    public static class Program
    {
        private static string _out;
        private static int _problems;
        private static StudioWindow _window;

        [STAThread]
        public static int Main(string[] args)
        {
            _out = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "ui-preview");
            var sample = args.Length > 1 ? args[1] : null;
            Directory.CreateDirectory(_out);
            var natives = Environment.GetEnvironmentVariable("TEXTX_NATIVES");
            if (!string.IsNullOrEmpty(natives)) TextX.Core.Documents.Native.NativeLibraries.Directory = natives;

            var app = new Application("Eto.Wpf.Platform, Eto.Wpf");
            app.UnhandledException += (s, e) => Problem("unhandled: " + e.ExceptionObject);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Problem("unhandled (domain): " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) => { Problem("unobserved task: " + e.Exception); e.SetObserved(); };
            app.Initialized += async (s, e) =>
            {
                try { await Walk(sample); }
                catch (Exception ex) { Problem("walk: " + ex); }
                finally
                {
                    try { if (_window != null) _window.Close(); } catch (Exception ex) { Problem("close: " + ex); }
                    app.Quit();
                }
            };
            app.Run();
            Console.WriteLine(_problems == 0 ? "UI PREVIEW OK" : "UI PREVIEW: " + _problems + " problem(s)");
            return _problems;
        }

        private static void Problem(string text)
        {
            _problems++;
            Console.WriteLine("  FAIL " + text);
        }

        private static async Task Walk(string sample)
        {
            _window = StudioWindow.ShowWindow(null);
            _window.ClientSize = new Eto.Drawing.Size(1360, 860);
            await Task.Delay(1500);
            Capture("studio-1-home.png");

            if (sample == null || !File.Exists(sample)) { Problem("no sample PDF"); return; }
            _window.OpenPath(sample);
            await WaitIdle(90);
            await Task.Delay(2500);
            Capture("studio-2-document.png");

            Call("TranslateDocument", false);
            await WaitIdle(180);
            await Task.Delay(4000);
            Capture("studio-3-translated.png");
            Call("GoToPage", 2);
            await Task.Delay(3500);
            Capture("studio-4-drawing.png");

            Call("ShowTab", 2);
            Call("ReadSchedules");
            await WaitIdle(60);
            await Task.Delay(1500);
            Capture("studio-5-schedules.png");

            Call("ShowTab", 3);
            Call("ShowTool", 2);
            var input = (TextArea)Field("_toolInput");
            input.Text = "Les portes des meubles hauts sont en placage de chêne avec une finition laquée mate.";
            Call("TranslateToolText");
            await Task.Delay(5000);
            Capture("studio-6-tools.png");

            Call("ShowTool", 3);
            var writing = (TextArea)Field("_writingText");
            writing.Text = "The cabinat doors is made from oak veneer and has a matt finish.";
            Call("CheckWriting");
            await WaitIdle(60);
            await Task.Delay(1500);
            Capture("studio-7-writing.png");

            Call("ShowTab", 0);
            await Task.Delay(1500);
            Capture("studio-8-history.png");
        }

        private static object Field(string name)
        {
            var f = typeof(StudioWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null) throw new MissingFieldException(name);
            return f.GetValue(_window);
        }

        private static void Call(string method, params object[] args)
        {
            var m = typeof(StudioWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new MissingMethodException(method);
            Console.WriteLine("  > " + method);
            m.Invoke(_window, args);
        }

        private static async Task WaitIdle(int seconds)
        {
            await Task.Delay(400);
            var start = DateTime.UtcNow;
            while (Field("_cts") != null)
            {
                if ((DateTime.UtcNow - start).TotalSeconds > seconds) { Problem("timed out waiting (" + seconds + " s)"); return; }
                await Task.Delay(250);
            }
            var status = Field("_status");
            var text = status.GetType().GetProperty("Text").GetValue(status) as string;
            Console.WriteLine("  status: " + text);
        }

        /// <summary>Saves the window's content as a PNG (WPF render, independent of the screen).</summary>
        private static void Capture(string name)
        {
            try
            {
                var native = _window.ControlObject as System.Windows.Window;
                var content = native == null ? null : native.Content as System.Windows.FrameworkElement;
                if (content == null) { Problem("no WPF content to capture"); return; }
                content.UpdateLayout();
                int w = (int)Math.Ceiling(content.ActualWidth), h = (int)Math.Ceiling(content.ActualHeight);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(content);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(_out, name))) encoder.Save(fs);
                Console.WriteLine("  saved " + name + " (" + w + " x " + h + ")");
            }
            catch (Exception ex) { Problem("capture " + name + ": " + ex.Message); }
        }
    }
}
