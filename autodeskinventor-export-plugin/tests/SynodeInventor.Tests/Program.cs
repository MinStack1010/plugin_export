using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Synode.Inventor.Core;

namespace Synode.Inventor.Tests
{
    /// <summary>
    /// Bộ chạy unit test tối giản. Exit code 0 = tất cả PASS.
    /// Dùng thêm: SynodeInventor.Tests.exe --validate file1.glb [file2.glb ...] để kiểm tra GLB bất kỳ.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--validate") return ValidateFiles(args);

            Console.WriteLine("Synode Inventor Exporter - unit tests");
            Console.WriteLine();

            // ---------------- GlbValidator
            Run("GLB: valid triangle with nested nodes", () =>
            {
                GlbValidationResult r = GlbValidator.Validate(TestGlb.Triangle());
                AssertTrue(r.IsValid, string.Join("; ", r.Errors));
                AssertEqual(2u, r.Version);
                AssertEqual(1, r.MeshCount);
                AssertEqual(2, r.NodeCount);
                AssertEqual(1L, r.TriangleCount);
                AssertEqual(3L, r.VertexCount);
                AssertEqual("synode-test", r.Generator);
            });
            Run("GLB: valid file via ValidateFile", () =>
            {
                string path = TempFile(".glb");
                File.WriteAllBytes(path, TestGlb.Triangle());
                try { AssertTrue(GlbValidator.ValidateFile(path).IsValid, "file should be valid"); }
                finally { File.Delete(path); }
            });
            Run("GLB: missing file", () => ExpectError(GlbValidator.ValidateFile(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".glb")), "does not exist"));
            Run("GLB: empty file", () => ExpectError(GlbValidator.Validate(new byte[0]), "empty"));
            Run("GLB: too small", () => ExpectError(GlbValidator.Validate(new byte[] { 0x67, 0x6C, 0x54, 0x46 }), "too small"));
            Run("GLB: bad magic", () =>
            {
                byte[] d = TestGlb.Triangle(); d[0] = 0x00;
                ExpectError(GlbValidator.Validate(d), "magic");
            });
            Run("GLB: .gltf JSON saved as .glb is rejected", () =>
                ExpectError(GlbValidator.Validate(Encoding.UTF8.GetBytes(TestGlb.TriangleJson)), ".gltf"));
            Run("GLB: version 1 rejected", () => ExpectError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson, TestGlb.TriangleBin(), 1)), "version"));
            Run("GLB: declared length mismatch", () =>
            {
                byte[] d = TestGlb.Triangle();
                byte[] longer = new byte[d.Length + 4];
                Buffer.BlockCopy(d, 0, longer, 0, d.Length);
                ExpectError(GlbValidator.Validate(longer), "Declared length");
            });
            Run("GLB: truncated file", () =>
            {
                byte[] d = TestGlb.Triangle();
                byte[] cut = new byte[d.Length - 10];
                Buffer.BlockCopy(d, 0, cut, 0, cut.Length);
                BitConverter.GetBytes((uint)cut.Length).CopyTo(cut, 8);
                ExpectError(GlbValidator.Validate(cut), "exceeds");
            });
            Run("GLB: misaligned JSON chunk", () =>
            {
                string json = TestGlb.TriangleJson + " ";
                while ((Encoding.UTF8.GetByteCount(json) % 4) == 0) json += " ";
                ExpectError(GlbValidator.Validate(TestGlb.Build(json, TestGlb.TriangleBin(), padJson: false)), "aligned");
            });
            Run("GLB: invalid JSON", () => ExpectError(GlbValidator.Validate(TestGlb.Build("{\"asset\":", TestGlb.TriangleBin())), "not valid JSON"));
            Run("GLB: missing asset", () => ExpectError(GlbValidator.Validate(TestGlb.Build("{\"scenes\":[]}", null)), "asset.version"));
            Run("GLB: wrong asset version", () =>
                ExpectError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"version\":\"2.0\"", "\"version\":\"1.0\""), TestGlb.TriangleBin())), "asset.version"));
            Run("GLB: buffer longer than BIN chunk", () =>
                ExpectAnyError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"byteLength\":44", "\"byteLength\":400"), TestGlb.TriangleBin()))));
            Run("GLB: bufferView out of range", () =>
                ExpectAnyError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"byteOffset\":36,\"byteLength\":6", "\"byteOffset\":40,\"byteLength\":60"), TestGlb.TriangleBin()))));
            Run("GLB: accessor larger than bufferView", () =>
                ExpectAnyError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"count\":3,\"type\":\"VEC3\"", "\"count\":30,\"type\":\"VEC3\""), TestGlb.TriangleBin()))));
            Run("GLB: node references missing mesh", () =>
                ExpectAnyError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"mesh\":0", "\"mesh\":5"), TestGlb.TriangleBin()))));
            Run("GLB: primitive references missing material", () =>
                ExpectAnyError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"material\":0", "\"material\":9"), TestGlb.TriangleBin()))));
            Run("GLB: node with two parents", () =>
                ExpectAnyError(GlbValidator.Validate(TestGlb.Build(TestGlb.TriangleJson.Replace("\"name\":\"child\",\"mesh\":0", "\"name\":\"child\",\"mesh\":0,\"children\":[]},{\"children\":[1]"), TestGlb.TriangleBin()))));
            Run("GLB: no mesh geometry", () =>
                ExpectError(GlbValidator.Validate(TestGlb.Build("{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{}]}", null)), "mesh"));

            // ---------------- ExportPathHelper
            Run("Path: suggest from full file name", () => AssertEqual("Bracket.glb", ExportPathHelper.SuggestFileName(@"C:\work\Bracket.ipt", "Bracket.ipt")));
            Run("Path: suggest from display name (unsaved)", () => AssertEqual("Part1.glb", ExportPathHelper.SuggestFileName("", "Part1")));
            Run("Path: suggest strips .iam from display name", () => AssertEqual("Assembly1.glb", ExportPathHelper.SuggestFileName(null, "Assembly1.iam")));
            Run("Path: suggest sanitizes invalid chars", () => AssertEqual("a_b_c.glb", ExportPathHelper.SuggestFileName(null, "a:b?c")));
            Run("Path: suggest default", () => AssertEqual("inventor-export.glb", ExportPathHelper.SuggestFileName(null, "  ")));
            Run("Path: normalize appends .glb", () =>
            {
                string dir = Path.GetTempPath();
                AssertEqual(Path.Combine(dir, "model.glb"), ExportPathHelper.NormalizeOutputPath(Path.Combine(dir, "model")));
            });
            Run("Path: normalize replaces .gltf", () =>
            {
                string dir = Path.GetTempPath();
                AssertEqual(Path.Combine(dir, "model.glb"), ExportPathHelper.NormalizeOutputPath(Path.Combine(dir, "model.gltf")));
            });
            Run("Path: normalize keeps other dotted names", () =>
            {
                string dir = Path.GetTempPath();
                AssertEqual(Path.Combine(dir, "model.v2.glb"), ExportPathHelper.NormalizeOutputPath(Path.Combine(dir, "model.v2")));
            });
            Run("Path: normalize lower-cases .GLB", () =>
            {
                string dir = Path.GetTempPath();
                AssertEqual(Path.Combine(dir, "M.glb"), ExportPathHelper.NormalizeOutputPath(Path.Combine(dir, "M.GLB")));
            });
            Run("Path: empty path rejected", () => ExpectCode(ExportErrorCode.InvalidOutputPath, () => ExportPathHelper.NormalizeOutputPath("  ")));
            Run("Path: relative path rejected", () => ExpectCode(ExportErrorCode.InvalidOutputPath, () => ExportPathHelper.NormalizeOutputPath("model.glb")));
            Run("Path: missing folder rejected", () => ExpectCode(ExportErrorCode.InvalidOutputPath,
                () => ExportPathHelper.NormalizeOutputPath(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"), "m.glb"))));
            Run("Path: temp path is a .glb next to target", () =>
            {
                string target = Path.Combine(Path.GetTempPath(), "model.glb");
                string temp = ExportPathHelper.MakeTempPath(target, Guid.Parse("0123456789abcdef0123456789abcdef"));
                AssertEqual(Path.Combine(Path.GetTempPath(), "model.synode-tmp-0123456789ab.glb"), temp);
            });
            Run("Path: temp dir writable", () => ExportPathHelper.EnsureDirectoryWritable(Path.GetTempPath()));
            Run("Path: lock detection + replace", () =>
            {
                string target = TempFile(".glb");
                string temp = TempFile(".glb");
                File.WriteAllText(target, "old");
                File.WriteAllText(temp, "new");
                try
                {
                    using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        AssertTrue(ExportPathHelper.IsFileLocked(target), "should be locked");
                        ExpectCode(ExportErrorCode.OutputLocked, () => ExportPathHelper.ReplaceFile(temp, target));
                    }
                    AssertTrue(!ExportPathHelper.IsFileLocked(target), "should be unlocked");
                    ExportPathHelper.ReplaceFile(temp, target);
                    AssertEqual("new", File.ReadAllText(target));
                    AssertTrue(!File.Exists(temp), "temp should be moved");
                }
                finally
                {
                    ExportPathHelper.TryDelete(target, null);
                    ExportPathHelper.TryDelete(temp, null);
                }
            });
            Run("Path: FormatBytes", () =>
            {
                AssertEqual("512 B", ExportPathHelper.FormatBytes(512));
                AssertEqual("2.0 KB", ExportPathHelper.FormatBytes(2048));
                AssertEqual("3.00 MB", ExportPathHelper.FormatBytes(3L * 1024 * 1024));
            });

            // ---------------- IconRenderer
            Run("Icon: 16 and 32 px rendered with transparent corners and opaque centre", () =>
            {
                foreach (int size in new[] { 16, 32 })
                {
                    uint[] px = IconRenderer.Render(size);
                    AssertEqual(size * size, px.Length);
                    AssertEqual(0u, px[0] >> 24);
                    AssertEqual(255u, px[(size / 2) * size + size / 2] >> 24);
                }
            });

            // ---------------- FileLogger
            Run("Logger: writes INFO and ERROR lines", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "synode-log-test-" + Guid.NewGuid().ToString("N"));
                string path = Path.Combine(dir, "logs", "test.log");
                try
                {
                    var log = new FileLogger(path);
                    log.Info("hello");
                    log.Error("boom", new InvalidOperationException("bad"));
                    string text = File.ReadAllText(path);
                    AssertTrue(text.Contains("[INFO ] hello"), text);
                    AssertTrue(text.Contains("[ERROR] boom") && text.Contains("bad"), text);
                }
                finally
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
            });

            Console.WriteLine();
            Console.WriteLine("Result: " + _passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static int ValidateFiles(string[] args)
        {
            int bad = 0;
            for (int i = 1; i < args.Length; i++)
            {
                GlbValidationResult r = GlbValidator.ValidateFile(args[i]);
                Console.WriteLine((r.IsValid ? "VALID   " : "INVALID ") + args[i]);
                Console.WriteLine("  " + r.Summary().Replace(Environment.NewLine, Environment.NewLine + "  "));
                if (!r.IsValid) bad++;
            }
            return bad == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- mini framework
        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name + " -> " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion failed: " + message);
        }

        private static void AssertEqual<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception("Expected <" + expected + "> but was <" + actual + ">");
        }

        private static void ExpectError(GlbValidationResult r, string fragment)
        {
            AssertTrue(!r.IsValid, "expected invalid GLB");
            foreach (string e in r.Errors)
            {
                if (e.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return;
            }
            throw new Exception("No error containing '" + fragment + "'. Errors: " + string.Join(" | ", r.Errors));
        }

        private static void ExpectAnyError(GlbValidationResult r)
        {
            AssertTrue(!r.IsValid, "expected invalid GLB");
        }

        private static void ExpectCode(ExportErrorCode code, Action action)
        {
            try
            {
                action();
            }
            catch (ExportException ex)
            {
                AssertEqual(code, ex.Code);
                return;
            }
            throw new Exception("Expected ExportException " + code);
        }

        private static string TempFile(string ext) => Path.Combine(Path.GetTempPath(), "synode-test-" + Guid.NewGuid().ToString("N") + ext);
    }
}
