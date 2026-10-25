using System;
using System.Collections.Generic;
using System.IO;

namespace TextX.Tests
{
    /// <summary>Tiny assertion helper and entry point for the core test-suite.</summary>
    public static class TestRunner
    {
        private static int _failures;
        private static int _passes;
        private static TextWriter _log = Console.Out;

        public static void Equal<T>(T expected, T actual, string name)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                _passes++;
                return;
            }
            _failures++;
            _log.WriteLine("  FAIL " + name + "\n     expected: " + Show(expected) + "\n     actual:   " + Show(actual));
        }

        public static void True(bool condition, string name)
        {
            if (condition) { _passes++; return; }
            _failures++;
            _log.WriteLine("  FAIL " + name);
        }

        private static string Show(object o)
        {
            if (o == null) return "<null>";
            var s = o.ToString();
            var sb = new System.Text.StringBuilder();
            sb.Append('"').Append(s).Append('"').Append("  [");
            foreach (var ch in s) sb.Append(((int)ch).ToString("X4")).Append(' ');
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Runs all tests. Returns the number of failures.</summary>
        public static int Run(string dataDir)
        {
            _failures = 0;
            _passes = 0;
            _log.WriteLine("== Unicode bidi conformance");
            var bidiTest = Path.Combine(dataDir, "BidiTest.txt");
            var bidiCharTest = Path.Combine(dataDir, "BidiCharacterTest.txt");
            if (File.Exists(bidiTest) && File.Exists(bidiCharTest))
            {
                bool ok1 = BidiConformanceTests.RunBidiTest(bidiTest, _log);
                long cases, fails;
                bool ok2 = BidiConformanceTests.RunBidiCharacterTest(bidiCharTest, _log, out cases, out fails);
                if (!ok1) _failures++;
                if (!ok2) _failures++;
            }
            else
            {
                _log.WriteLine("  skipped (BidiTest.txt / BidiCharacterTest.txt not found in " + dataDir + ")");
            }

            _log.WriteLine("== Unit tests");
            foreach (var t in new Action[]
            {
                CoreTests.HebrewVisualOrder,
                CoreTests.MixedContent,
                CoreTests.FieldsAndMarks,
                CoreTests.ArabicShaping,
                CoreTests.Wrapping,
                CoreTests.Json,
                CoreTests.Segmentation,
                CoreTests.Glossary,
                CoreTests.Metadata,
                CoreTests.Languages,
                CoreTests.ProviderParsing,
                CoreTests.Versions,
                CoreTests.UpdateFeed,
            })
            {
                try { t(); }
                catch (Exception ex)
                {
                    _failures++;
                    _log.WriteLine("  EXCEPTION in " + t.Method.Name + ": " + ex);
                }
            }
            _log.WriteLine(string.Format("== Unit assertions: {0} passed, {1} failed", _passes, _failures));
            return _failures;
        }
    }
}
