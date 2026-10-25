using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TextX.Core.Text;

namespace TextX.Tests
{
    /// <summary>
    /// Runs the official Unicode conformance suites (BidiTest.txt and BidiCharacterTest.txt)
    /// against the bidi implementation used by the plugin.
    /// </summary>
    public static class BidiConformanceTests
    {
        private static readonly Dictionary<string, BidiClass> ClassNames =
            Enum.GetValues(typeof(BidiClass)).Cast<BidiClass>().ToDictionary(c => c.ToString(), c => c);

        public static bool RunBidiTest(string path, TextWriter log)
        {
            string[] expectedLevels = null;
            int[] expectedOrder = null;
            long cases = 0, failures = 0;
            int shown = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("@Levels:"))
                {
                    expectedLevels = line.Substring(8).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    continue;
                }
                if (line.StartsWith("@Reorder:"))
                {
                    expectedOrder = line.Substring(9).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                    continue;
                }
                if (line[0] == '@') continue;

                var parts = line.Split(';');
                var types = parts[0].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(s => ClassNames[s]).ToArray();
                int bitset = Convert.ToInt32(parts[1].Trim(), 16);
                foreach (var pl in new[] { 1, 2, 4 })
                {
                    if ((bitset & pl) == 0) continue;
                    cases++;
                    int paraLevel = pl == 1 ? -1 : (pl == 2 ? 0 : 1);
                    string error = Check(null, types, paraLevel, -1, expectedLevels, expectedOrder);
                    if (error != null)
                    {
                        failures++;
                        if (shown++ < 10) log.WriteLine("  BidiTest FAIL: " + line + " (para=" + paraLevel + ") " + error);
                    }
                }
            }
            log.WriteLine(string.Format("BidiTest.txt: {0} cases, {1} failures", cases, failures));
            return failures == 0;
        }

        public static bool RunBidiCharacterTest(string path, TextWriter log, out long caseCount, out long failureCount)
        {
            long cases = 0, failures = 0;
            int shown = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split(';');
                var cps = f[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToInt32(h, 16)).ToArray();
                int dir = int.Parse(f[1]);
                int resolvedPara = int.Parse(f[2]);
                var levels = f[3].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var order = f[4].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                var types = cps.Select(UnicodeProps.GetBidiClass).ToArray();
                int paraLevel = dir == 2 ? -1 : dir;
                cases++;
                string error = Check(cps, types, paraLevel, resolvedPara, levels, order);
                if (error != null)
                {
                    failures++;
                    if (shown++ < 10) log.WriteLine("  BidiCharacterTest FAIL: " + line + " -> " + error);
                }
            }
            caseCount = cases;
            failureCount = failures;
            log.WriteLine(string.Format("BidiCharacterTest.txt: {0} cases, {1} failures", cases, failures));
            return failures == 0;
        }

        private static string Check(int[] cps, BidiClass[] types, int paraLevel, int expectedPara, string[] expectedLevels, int[] expectedOrder)
        {
            var p = new BidiParagraph(cps, types, paraLevel);
            if (expectedPara >= 0 && p.ParagraphLevel != expectedPara)
                return "paragraph level " + p.ParagraphLevel + " != " + expectedPara;
            var lv = p.GetLineLevels(0, types.Length);
            for (int i = 0; i < types.Length; i++)
            {
                if (expectedLevels[i] == "x") continue;
                if (lv[i].ToString() != expectedLevels[i])
                    return "levels [" + string.Join(" ", Enumerable.Range(0, types.Length).Select(k => p.IsRemoved(k) ? "x" : lv[k].ToString())) + "]";
            }
            var order = p.GetVisualOrder(0, types.Length, true);
            if (!order.SequenceEqual(expectedOrder))
                return "order [" + string.Join(" ", order) + "] expected [" + string.Join(" ", expectedOrder) + "]";
            return null;
        }
    }
}
