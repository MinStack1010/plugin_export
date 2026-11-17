using System;
using System.Collections.Generic;
using System.IO;

namespace SynodeExporter.Tests
{
    /// <summary>
    /// Bộ chạy test tối giản (không cần NuGet/xUnit).
    ///   SynodeExporter.Tests.exe                      : chạy toàn bộ unit test
    ///   SynodeExporter.Tests.exe --dump DIR           : chạy test + ghi GLB của mọi scene mẫu vào DIR
    ///   SynodeExporter.Tests.exe --compare-net48 DIR  : chạy test + so sánh GLB do bản net48 ghi với bản hiện tại
    /// Mã thoát 0 = mọi test PASS.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string dumpDir = null;
            string compareDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--dump" && i + 1 < args.Length) { dumpDir = args[++i]; }
                else if (args[i] == "--compare-net48" && i + 1 < args.Length) { compareDir = args[++i]; }
#if REFERENCE_SW
                else if (args[i] == "--probe")
                {
                    foreach (double d in new[] { 1e15, 1117971262996480, 1e16, 1.5e17, 1e20, 1.2345e22, 1e-5, 1.5e-7, 0.3, (double)1e-5f, 1e21, 1e-7 })
                    {
                        Console.WriteLine("double " + d.ToString("R") + " -> " + System.Text.Json.JsonSerializer.Serialize(d) + " | ours " + SynodeExporter.Core.JsonText.FormatDouble(d));
                    }
                    foreach (float f in new[] { 1e7f, 2.5e7f, 1e8f, 1.5e10f, 3e38f, 1e-5f, 1.5e-7f, 16777216f, 1.2345e15f, 1e-7f })
                    {
                        Console.WriteLine("float " + f.ToString("R") + " -> " + System.Text.Json.JsonSerializer.Serialize(f) + " | ours " + SynodeExporter.Core.JsonText.FormatFloat(f));
                    }
                    return 0;
                }
#endif
            }

            var runner = new TestRunner();
            Console.WriteLine("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                + (Environment.Is64BitProcess ? " x64" : " x86"));

            CoreTests.Register(runner);
            GlbStructureTests.Register(runner);
#if REFERENCE_SW
            ReferenceComparison.Register(runner);
#endif
            if (dumpDir != null)
            {
                runner.Add("dump scenes -> " + dumpDir, () =>
                {
                    Directory.CreateDirectory(dumpDir);
                    foreach (var pair in TestScenes.All())
                    {
                        File.WriteAllBytes(Path.Combine(dumpDir, pair.Key + ".glb"), SynodeExporter.Core.GlbWriter.BuildGlb(pair.Value()));
                    }
                    File.WriteAllLines(Path.Combine(dumpDir, "numbers.txt"), NumberSamples());
                });
            }
            if (compareDir != null)
            {
                runner.Add("net48 number formatting == current runtime (100k samples)", () =>
                {
                    string[] expected = File.ReadAllLines(Path.Combine(compareDir, "numbers.txt"));
                    string[] actual = NumberSamples();
                    Assert.Equal(expected.Length, actual.Length, "sample count");
                    for (int i = 0; i < actual.Length; i++) { Assert.Equal(expected[i], actual[i], "sample " + i); }
                });
                foreach (var pair in TestScenes.All())
                {
                    string name = pair.Key;
                    Func<SynodeExporter.Core.ExtractedScene> factory = pair.Value;
                    runner.Add("net48 == " + Environment.Version.Major + " : " + name, () =>
                    {
                        byte[] expected = File.ReadAllBytes(Path.Combine(compareDir, name + ".glb"));
                        byte[] actual = SynodeExporter.Core.GlbWriter.BuildGlb(factory());
                        Assert.BytesEqual(expected, actual, name);
                    });
                }
            }

            return runner.Run();
        }

        /// <summary>Mẫu số ngẫu nhiên cố định (bit pattern) -> FormatFloat/FormatDouble, để so sánh giữa 2 runtime.</summary>
        private static string[] NumberSamples()
        {
            var rnd = new Random(777);
            var bytes = new byte[4];
            var lines = new List<string>();
            while (lines.Count < 100000)
            {
                rnd.NextBytes(bytes);
                float f = BitConverter.ToSingle(bytes, 0);
                if (float.IsNaN(f) || float.IsInfinity(f)) { continue; }
                lines.Add(SynodeExporter.Core.JsonText.FormatFloat(f) + " " + SynodeExporter.Core.JsonText.FormatDouble(f));
            }
            return lines.ToArray();
        }
    }

    internal sealed class TestRunner
    {
        private readonly List<KeyValuePair<string, Action>> _tests = new List<KeyValuePair<string, Action>>();

        public void Add(string name, Action test)
        {
            _tests.Add(new KeyValuePair<string, Action>(name, test));
        }

        public int Run()
        {
            int passed = 0;
            var failed = new List<string>();
            foreach (var t in _tests)
            {
                try
                {
                    t.Value();
                    passed++;
                    Console.WriteLine("PASS  " + t.Key);
                }
                catch (Exception ex)
                {
                    failed.Add(t.Key);
                    Console.WriteLine("FAIL  " + t.Key);
                    Console.WriteLine("      " + ex.GetType().Name + ": " + ex.Message.Replace("\n", "\n      "));
                }
            }
            Console.WriteLine();
            Console.WriteLine("Tong: " + _tests.Count + "  PASS: " + passed + "  FAIL: " + failed.Count);
            return failed.Count == 0 ? 0 : 1;
        }
    }

    internal sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    internal static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) { throw new AssertException(message); }
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new AssertException(what + ": expected <" + expected + "> but was <" + actual + ">");
            }
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (Math.Abs(expected - actual) > tolerance || double.IsNaN(actual))
            {
                throw new AssertException(what + ": expected " + expected.ToString("R") + " +/- " + tolerance + " but was " + actual.ToString("R"));
            }
        }

        public static T Throws<T>(Action action, string what) where T : Exception
        {
            try
            {
                action();
            }
            catch (T ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new AssertException(what + ": expected " + typeof(T).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }
            throw new AssertException(what + ": expected " + typeof(T).Name + " but nothing was thrown");
        }

        public static void BytesEqual(byte[] expected, byte[] actual, string what)
        {
            int n = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < n; i++)
            {
                if (expected[i] != actual[i])
                {
                    throw new AssertException(what + ": first byte difference at offset " + i + " (expected 0x" + expected[i].ToString("X2")
                        + ", actual 0x" + actual[i].ToString("X2") + "), lengths " + expected.Length + "/" + actual.Length);
                }
            }
            if (expected.Length != actual.Length)
            {
                throw new AssertException(what + ": length differs " + expected.Length + " vs " + actual.Length);
            }
        }

        public static void TextEqual(string expected, string actual, string what)
        {
            if (expected == actual) { return; }
            int n = Math.Min(expected.Length, actual.Length);
            int i = 0;
            while (i < n && expected[i] == actual[i]) { i++; }
            int start = Math.Max(0, i - 80);
            throw new AssertException(what + ": first difference at char " + i + "\n expected: ..." + Snip(expected, start, 200)
                + "\n actual  : ..." + Snip(actual, start, 200));
        }

        private static string Snip(string s, int start, int length)
        {
            if (start >= s.Length) { return "<end>"; }
            return s.Substring(start, Math.Min(length, s.Length - start));
        }
    }
}
