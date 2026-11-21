using System;
using System.IO;
using System.Linq;
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
            // the build machine has Windows animations off; the preview records the motion anyway
            TextX.UI.Motion.Animator.FollowWindowsSetting = false;
            _window = StudioWindow.ShowWindow(null);
            _window.ClientSize = new Eto.Drawing.Size(1280, 760);
            await Task.Delay(1500);
            Capture("studio-1-home.png");

            // the translator on Home: a text, its translation and the one-line previews
            var source = (TextArea)Field("_qSource");
            source.Text = "Portes des meubles hauts en placage de chêne, finition laquée mate.";
            await Task.Delay(5000);
            Capture("studio-1a-home-translator.png");

            // the tiles' figures: one pointer on each, then frame sequences of two of them
            var tiles = (System.Collections.Generic.List<TextX.UI.Controls.BentoTile>)Field("_tiles");
            await Task.Delay(6000); // let the welcome tour finish
            for (int i = 0; i < tiles.Count; i++) Point(tiles[i], i == 0 ? 1 : 0);
            await Task.Delay(1400);
            Capture("studio-1b-home-hover.png");
            foreach (var t in tiles) t.SimulatePointer(null);
            await Task.Delay(1500);
            await Frames(tiles[0], "translate", 0, 2, 4);
            await Frames(tiles[1], "schedule", 0, 1, 2);
            await Frames(tiles[2], "read", 0, 1, 2);
            await Frames(tiles[3], "voice", 0, 1, 2);
            await Frames(tiles[4], "library", 0, 1, 2);

            // empty pages explain themselves
            Call("ShowTab", 1);
            await Task.Delay(2600);
            Capture("studio-1c-document-empty.png");
            Call("ShowTab", 2);
            await Task.Delay(2600);
            Capture("studio-1d-schedules-empty.png");
            Call("ShowTab", 0);
            await Task.Delay(500);

            if (sample == null || !File.Exists(sample)) { Problem("no sample PDF"); return; }
            _window.OpenPath(sample);
            await WaitIdle(90);
            await Task.Delay(2500);
            Capture("studio-2-document.png");

            Call("TranslateDocument", false);
            await WaitIdle(180);
            await Task.Delay(4000);
            var project = (TextX.Core.Documents.DocProject)Field("_project");
            var paragraph = project.Pages[0].Blocks.FirstOrDefault(b => b.Source.StartsWith("Les caissons", StringComparison.Ordinal));
            if (paragraph != null) Call("SelectBlock", paragraph, true);
            await Task.Delay(1500);
            Capture("studio-3-translated.png");
            ((TextX.UI.Controls.SegmentedControl)Field("_viewMode")).SelectedIndex = 1;
            await Task.Delay(2500);
            Capture("studio-3b-translation-only.png");
            ((TextX.UI.Controls.SegmentedControl)Field("_viewMode")).SelectedIndex = 0;
            await Task.Delay(1500);
            Call("GoToPage", 2);
            await Task.Delay(3500);
            Capture("studio-4-drawing.png");

            Call("ShowTab", 2);
            Call("ReadSchedules");
            await WaitIdle(60);
            await Task.Delay(1500);
            Capture("studio-5-schedules.png");

            Call("ShowTab", 3);
            var input = (TextArea)Field("_toolInput");
            input.Text = "Les portes des meubles hauts sont en placage de chêne avec une finition laquée mate.";
            Call("TranslateToolText");
            await Task.Delay(5000);
            Capture("studio-6-picture.png");

            // writing check on Home: suggestions as chips under the texts
            Call("ShowTab", 0);
            source.Text = "The cabinat doors is made from oak veneer and has a matt finish.";
            Call("CheckTranslatorWriting");
            await Task.Delay(7000);
            Capture("studio-7-writing.png");

            Call("ToggleProperties", true);
            await Task.Delay(1200);
            Capture("studio-7b-text-properties.png");
            Call("ToggleProperties", false);

            source.Text = string.Empty;
            await Task.Delay(1500);
            Capture("studio-8-history.png");

            // narrower window: the tiles move under the translator
            _window.ClientSize = new Eto.Drawing.Size(940, 820);
            await Task.Delay(2000);
            Capture("studio-9-home-narrow.png");

            // the tiles beside a narrow translator (as in a window just wider than the build machine's screen)
            typeof(StudioWindow).GetField("HomeWideFrom", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 700);
            typeof(StudioWindow).GetField("_homeWide", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_window, null);
            Call("ArrangeHome");
            source.Text = "Ground floor plan - Scale 1:50";
            await Task.Delay(4000);
            Capture("studio-9b-home-wide-compact.png");
        }

        private static void Point(TextX.UI.Controls.BentoTile tile, int stop)
        {
            var stops = tile.Figure.Stops.Where(x => x != null).ToList();
            var v = stops[Math.Min(stop, stops.Count - 1)].Value;
            tile.SimulatePointer(new Eto.Drawing.PointF(v.X, v.Y));
        }

        /// <summary>Moves an unseen pointer to each stop in turn and saves 8 frames of the tile at each (80 ms apart).</summary>
        private static async Task Frames(TextX.UI.Controls.BentoTile tile, string name, params int[] stops)
        {
            int n = 0;
            foreach (var stop in stops)
            {
                Point(tile, stop);
                for (int k = 0; k < 8; k++)
                {
                    await Task.Delay(80);
                    CaptureControl(tile, "anim/" + name + "-" + (n++).ToString("00") + ".png");
                }
            }
            tile.SimulatePointer(null);
            for (int k = 0; k < 8; k++)
            {
                await Task.Delay(110);
                CaptureControl(tile, "anim/" + name + "-" + (n++).ToString("00") + ".png");
            }
        }

        /// <summary>One control as a PNG at twice the size (drawn through a visual brush, so its place in the window does not matter).</summary>
        private static void CaptureControl(Control control, string name)
        {
            try
            {
                var fe = control.ControlObject as System.Windows.FrameworkElement;
                if (fe == null) { Problem("no WPF element for " + name); return; }
                fe.InvalidateVisual();
                fe.UpdateLayout();
                int w = (int)Math.Ceiling(fe.ActualWidth), h = (int)Math.Ceiling(fe.ActualHeight);
                if (w < 2 || h < 2) { Problem("empty control for " + name); return; }
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawRectangle(new System.Windows.Media.VisualBrush(fe), null, new System.Windows.Rect(0, 0, w, h));
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w * 2, h * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(dv);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                var path = Path.Combine(_out, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var fs = File.Create(path)) encoder.Save(fs);
            }
            catch (Exception ex) { Problem("capture " + name + ": " + ex.Message); }
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
