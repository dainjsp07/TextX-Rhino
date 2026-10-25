using System;
using System.IO;

namespace TextX.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var dataDir = args.Length > 0 ? args[0] : AppContext.BaseDirectory;
            if (!File.Exists(Path.Combine(dataDir, "BidiTest.txt")))
                Console.WriteLine("Note: BidiTest.txt / BidiCharacterTest.txt not found in '" + dataDir + "' - conformance suites will fail to load.");
            int failures = TestRunner.Run(dataDir);
            Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : failures + " FAILURE(S)");
            return failures == 0 ? 0 : 1;
        }
    }
}
