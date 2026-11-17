using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Synode.Inventor.AddIn.Services;
using Synode.Inventor.Core;
using Inv = global::Inventor;

namespace Synode.Inventor.IntegrationTests
{
    /// <summary>
    /// Integration test chạy trên Inventor 2027 THẬT. Exit code: 0 = tất cả PASS, 1 = có FAIL, 2 = không khởi động được Inventor.
    /// Tham số: --model &lt;file.ipt&gt; --out &lt;thư mục&gt; [--visible] [--keep-open]
    /// File mẫu được SAO CHÉP vào thư mục out trước khi mở (không bao giờ sửa file gốc).
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> Report = new List<string>();
        private static int _passed;
        private static int _failed;

        [STAThread]
        private static int Main(string[] args)
        {
            string? model = Arg(args, "--model");
            string outDir = Arg(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "test-output");
            bool visible = args.Contains("--visible");
            bool keepOpen = args.Contains("--keep-open");
            if (model == null || !File.Exists(model))
            {
                Console.WriteLine("Usage: SynodeInventor.IntegrationTests --model <part.ipt> --out <dir> [--visible] [--keep-open]");
                return 2;
            }
            Directory.CreateDirectory(outDir);
            var log = new FileLogger(Path.Combine(outDir, "integration.log"));
            Line("Synode Inventor Exporter - integration test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Line("Model source: " + model);

            string partCopy = Path.Combine(outDir, "input", Path.GetFileName(model));
            Directory.CreateDirectory(Path.GetDirectoryName(partCopy)!);
            File.Copy(model, partCopy, true);
            File.SetAttributes(partCopy, FileAttributes.Normal);

            Inv.Application? app = null;
            var sw = Stopwatch.StartNew();
            try
            {
                Type? t = Type.GetTypeFromProgID("Inventor.Application");
                if (t == null) { Line("FATAL: ProgID Inventor.Application is not registered."); return 2; }
                Line("Starting Inventor via COM...");
                app = (Inv.Application)Activator.CreateInstance(t)!;
                app.Visible = visible;
                WaitReady(app, TimeSpan.FromMinutes(5));
                Line("Inventor started in " + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s: " +
                     app.SoftwareVersion.DisplayVersion + " (" + app.SoftwareVersion.ProductName + ")");
                app.SilentOperation = true;
            }
            catch (Exception ex)
            {
                Line("FATAL: cannot start/connect to Inventor: " + ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + " " + ex.Message);
                Line("(If this is a licence/trial dialog, it is an environment issue, not a plugin failure.)");
                Save(outDir);
                return 2;
            }

            var exporter = new GlbExportService(app, log);
            var documents = new InventorDocumentService(app, log);
            var opened = new List<Inv.Document>();
            try
            {
                // ---------------- Part
                Inv.PartDocument? part = null;
                Check("Open part copy", () =>
                {
                    part = (Inv.PartDocument)app.Documents.Open(partCopy, true);
                    opened.Add((Inv.Document)part);
                    Line("    bodies=" + part.ComponentDefinition.SurfaceBodies.Count);
                });
                if (part != null)
                {
                    Check("Document service detects active part", () =>
                    {
                        ExportableDocument d = documents.GetActiveExportableDocument();
                        Assert(d.Kind == ExportDocumentKind.Part, "kind=" + d.Kind);
                        Line("    " + d.Describe());
                    });
                    string partGlb = Path.Combine(outDir, "part.glb");
                    Check("Part -> GLB (native glTF translator)", () => ExportAndVerify(exporter, (Inv.Document)part, partGlb, RangeSizeCm(part.ComponentDefinition.RangeBox), 1));
                    Check("Re-export overwrites existing file", () => ExportAndVerify(exporter, (Inv.Document)part, partGlb, null, 1));
                    Check("Locked output file -> OutputLocked", () =>
                    {
                        using (new FileStream(partGlb, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            ExpectCode(ExportErrorCode.OutputLocked, () => exporter.Export((Inv.Document)part, partGlb));
                        }
                        Assert(!Directory.GetFiles(outDir, "*" + ExportPathHelper.TempMarker + "*").Any(), "temp files left behind");
                    });
                    Check("Missing output folder -> InvalidOutputPath", () =>
                        ExpectCode(ExportErrorCode.InvalidOutputPath, () => exporter.Export((Inv.Document)part, Path.Combine(outDir, "no-such-dir", "x.glb"))));
                    Check(".gltf extension is normalised to .glb", () =>
                    {
                        GlbExportResult r = exporter.Export((Inv.Document)part, Path.Combine(outDir, "part-ext.gltf"));
                        Assert(r.OutputPath.EndsWith("part-ext.glb", StringComparison.Ordinal), r.OutputPath);
                        Assert(GlbValidator.ValidateFile(r.OutputPath).IsValid, "invalid");
                    });

                    // ---------------- Assembly (built from the part copy, saved in out dir)
                    Inv.AssemblyDocument? asm = null;
                    Check("Create assembly with 3 occurrences (one rotated)", () =>
                    {
                        string template = app.FileManager.GetTemplateFile(Inv.DocumentTypeEnum.kAssemblyDocumentObject);
                        asm = (Inv.AssemblyDocument)app.Documents.Add(Inv.DocumentTypeEnum.kAssemblyDocumentObject, template, true);
                        opened.Add((Inv.Document)asm);
                        Inv.TransientGeometry tg = app.TransientGeometry;
                        double step = Math.Max(5.0, RangeSizeCm(part.ComponentDefinition.RangeBox).Max() * 1.5);
                        for (int i = 0; i < 3; i++)
                        {
                            Inv.Matrix m = tg.CreateMatrix();
                            if (i == 2) m.SetToRotation(Math.PI / 2, tg.CreateVector(0, 0, 1), tg.CreatePoint(0, 0, 0));
                            m.SetTranslation(tg.CreateVector(step * i, 0, 0));
                            asm.ComponentDefinition.Occurrences.Add(partCopy, m);
                        }
                        ((Inv.Document)asm).SaveAs(Path.Combine(outDir, "input", "SynodeTestAssembly.iam"), false);
                        Line("    occurrences=" + asm.ComponentDefinition.Occurrences.Count + ", spacing=" + step.ToString("0.###", CultureInfo.InvariantCulture) + " cm");
                    });
                    if (asm != null)
                    {
                        Check("Document service detects active assembly", () =>
                        {
                            ExportableDocument d = documents.GetActiveExportableDocument();
                            Assert(d.Kind == ExportDocumentKind.Assembly, "kind=" + d.Kind);
                            Line("    " + d.Describe());
                        });
                        Check("Assembly -> GLB (native glTF translator)", () =>
                            ExportAndVerify(exporter, (Inv.Document)asm, Path.Combine(outDir, "assembly.glb"), RangeSizeCm(asm.ComponentDefinition.RangeBox), 3));
                    }
                }

                // ---------------- Unsupported document
                Check("Drawing document -> UnsupportedDocument", () =>
                {
                    string template = app.FileManager.GetTemplateFile(Inv.DocumentTypeEnum.kDrawingDocumentObject);
                    var drw = (Inv.Document)app.Documents.Add(Inv.DocumentTypeEnum.kDrawingDocumentObject, template, true);
                    opened.Add(drw);
                    ExpectCode(ExportErrorCode.UnsupportedDocument, () => documents.GetActiveExportableDocument());
                });
            }
            finally
            {
                if (!keepOpen)
                {
                    for (int i = opened.Count - 1; i >= 0; i--)
                    {
                        try { opened[i].Close(true); }
                        catch (COMException ex) { Line("warn: close failed: " + ex.Message); }
                    }
                    try { app.Quit(); Line("Inventor instance started by the test was closed."); }
                    catch (COMException ex) { Line("warn: Quit failed: " + ex.Message); }
                }
                Marshal.ReleaseComObject(app);
            }

            Line("");
            Line("RESULT: " + _passed + " passed, " + _failed + " failed (" + sw.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s)");
            Save(outDir);
            return _failed == 0 ? 0 : 1;
        }

        private static void ExportAndVerify(GlbExportService exporter, Inv.Document doc, string path, double[]? inventorSizeCm, int minMeshNodes)
        {
            GlbExportResult r = exporter.Export(doc, path);
            GlbValidationResult v = r.Validation;
            Line("    " + r.OutputPath + " | " + ExportPathHelper.FormatBytes(v.FileLength) + " | " + r.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            Line("    " + v.Summary().Replace(Environment.NewLine, Environment.NewLine + "    "));
            Assert(v.IsValid, "GlbValidator: " + string.Join("; ", v.Errors));
            Assert(v.TriangleCount > 0, "no triangles");

            // Validator độc lập: SharpGLTF (Strict).
            var model = SharpGLTF.Schema2.ModelRoot.Load(path);
            int meshNodes = model.LogicalNodes.Count(n => n.Mesh != null);
            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            foreach (var node in model.LogicalNodes.Where(n => n.Mesh != null))
            {
                Matrix4x4 world = node.WorldMatrix;
                foreach (var prim in node.Mesh.Primitives)
                {
                    var acc = prim.GetVertexAccessor("POSITION");
                    if (acc == null) continue;
                    foreach (Vector3 p in acc.AsVector3Array())
                    {
                        Vector3 w = Vector3.Transform(p, world);
                        min = Vector3.Min(min, w);
                        max = Vector3.Max(max, w);
                    }
                }
            }
            Vector3 size = max - min;
            Line("    SharpGLTF: OK, generator='" + model.Asset.Generator + "', mesh nodes=" + meshNodes + ", materials=" + model.LogicalMaterials.Count +
                 ", world size=" + Fmt(size.X) + " x " + Fmt(size.Y) + " x " + Fmt(size.Z) + " (glTF units = metres)");
            Assert(meshNodes >= minMeshNodes, "expected at least " + minMeshNodes + " mesh nodes, found " + meshNodes);

            if (inventorSizeCm != null)
            {
                double[] g = new[] { (double)size.X, size.Y, size.Z }.OrderBy(x => x).ToArray();
                double[] c = inventorSizeCm.OrderBy(x => x).ToArray();
                Line("    Inventor RangeBox size (cm): " + string.Join(" x ", inventorSizeCm.Select(Fmt)));
                double ratio = g[2] / c[2];
                Line("    scale ratio GLB/Inventor(cm) = " + ratio.ToString("0.######", CultureInfo.InvariantCulture) + " (0.01 => metres)");
                // RangeBox có thể lớn hơn hình học thật một chút -> dung sai 5 %.
                Assert(Math.Abs(ratio - 0.01) / 0.01 < 0.05, "unexpected unit scale " + ratio);
            }
        }

        private static double[] RangeSizeCm(Inv.Box box)
        {
            return new[] { box.MaxPoint.X - box.MinPoint.X, box.MaxPoint.Y - box.MinPoint.Y, box.MaxPoint.Z - box.MinPoint.Z };
        }

        private static void WaitReady(Inv.Application app, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (!app.Ready)
            {
                if (sw.Elapsed > timeout) throw new TimeoutException("Inventor did not become ready within " + timeout.TotalMinutes + " min.");
                System.Threading.Thread.Sleep(500);
            }
        }

        private static void Check(string name, Action test)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                test();
                _passed++;
                Line("PASS  " + name + "  (" + sw.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s)");
            }
            catch (Exception ex)
            {
                _failed++;
                Line("FAIL  " + name + " -> " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void ExpectCode(ExportErrorCode code, Action action)
        {
            try { action(); }
            catch (ExportException ex)
            {
                Assert(ex.Code == code, "expected " + code + " but got " + ex.Code + ": " + ex.Message);
                Line("    got expected " + code + ": " + ex.Message.Split('\n')[0].Trim());
                return;
            }
            throw new Exception("expected ExportException " + code);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static string Fmt(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        private static string Fmt(float v) => Fmt((double)v);

        private static string? Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Line(string text)
        {
            Console.WriteLine(text);
            Report.Add(text);
        }

        private static void Save(string outDir)
        {
            File.WriteAllLines(Path.Combine(outDir, "integration-report.txt"), Report, new UTF8Encoding(false));
        }
    }
}
