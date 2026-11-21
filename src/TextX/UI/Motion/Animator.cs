using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Eto.Forms;
using TextX.Core.Motion;
using TextX.Integration;

namespace TextX.UI.Motion
{
    /// <summary>A control's place in the shared frame loop: woken by input, asleep once it has settled.</summary>
    public sealed class Board
    {
        internal readonly Control Owner;
        internal readonly Func<double, double, bool> Tick;
        internal bool Awake;

        internal Board(Control owner, Func<double, double, bool> tick)
        {
            Owner = owner;
            Tick = tick;
        }

        /// <summary>Asks for frames again; they run until the tick returns false.</summary>
        public void Wake() { Animator.Wake(this); }
    }

    /// <summary>
    /// One frame loop for every animated control (after Hairline's stage): a 60 Hz timer that runs only
    /// while something is moving, skips controls that are not on screen, and follows the Windows setting
    /// "Show animations in Windows" (off = everything lands at once).
    /// </summary>
    public static class Animator
    {
        private static readonly List<Board> Awake = new List<Board>();
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        private static UITimer _timer;
        private static bool _running;
        private static double _last, _checked = -1e9;

        public static double Now { get { return Watch.Elapsed.TotalMilliseconds; } }

        /// <summary>Follow "Show animations in Windows" (true), or animate regardless (previews and recordings).</summary>
        public static bool FollowWindowsSetting { get; set; } = true;

        public static Board Register(Control owner, Func<double, double, bool> tick)
        {
            return new Board(owner, tick);
        }

        internal static void Wake(Board b)
        {
            if (b == null) return;
            CheckMotion();
            if (!b.Awake)
            {
                b.Awake = true;
                Awake.Add(b);
            }
            if (_running) return;
            try
            {
                if (_timer == null)
                {
                    _timer = new UITimer { Interval = 1.0 / 60.0 };
                    _timer.Elapsed += (s, e) => Frame();
                }
                _last = Now;
                _timer.Start();
                _running = true;
            }
            catch (Exception ex)
            {
                // no UI loop (tests, shutdown): draw the end state once
                Diagnostics.Log("animator: " + ex.Message);
                Awake.Remove(b);
                b.Awake = false;
            }
        }

        private static void Frame()
        {
            double now = Now, dt = Math.Min(0.05, Math.Max(0, (now - _last) / 1000.0));
            _last = now;
            foreach (var b in Awake.ToArray())
            {
                bool more;
                try { more = Shown(b.Owner) && b.Tick(dt, now); }
                catch (Exception ex) { Diagnostics.Log("animation: " + ex.Message); more = false; }
                if (!more) { b.Awake = false; Awake.Remove(b); }
            }
            if (Awake.Count == 0 && _timer != null)
            {
                _timer.Stop();
                _running = false;
            }
        }

        private static bool Shown(Control c)
        {
            if (c == null || !c.Visible || !c.Loaded) return false;
            var w = c.ParentWindow;
            return w != null && w.Visible && w.WindowState != WindowState.Minimized;
        }

        // ------------------------------------------------------------------ reduced motion
        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint winIni);

        /// <summary>Reads "Show animations in Windows" at most every 5 seconds.</summary>
        private static void CheckMotion()
        {
            if (!FollowWindowsSetting) { Clock.Reduced = false; return; }
            double now = Now;
            if (now - _checked < 5000) return;
            _checked = now;
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
                bool on = true;
                if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0)) Clock.Reduced = !on;
            }
            catch (Exception)
            {
                // not Windows, or an old one: keep the animations
            }
        }
    }
}
