using System;
using System.Collections.Generic;
using System.Linq;
using SynodeExporter.Core;

namespace SynodeExporter.Tests
{
    /// <summary>Unit test cho các thành phần Core (không cần Solid Edge).</summary>
    internal static class CoreTests
    {
        public static void Register(TestRunner r)
        {
            r.Add("JSON FormatFloat", () =>
            {
                Assert.Equal("1", JsonText.FormatFloat(1f), "1");
                Assert.Equal("0", JsonText.FormatFloat(0f), "0");
                Assert.Equal("-0", JsonText.FormatFloat(-0f), "-0");
                Assert.Equal("0.5", JsonText.FormatFloat(0.5f), "0.5");
                Assert.Equal("0.1", JsonText.FormatFloat(0.1f), "0.1f");
                Assert.Equal("1E-05", JsonText.FormatFloat(1e-5f), "1e-5f");
                Assert.Equal("0.33333334", JsonText.FormatFloat(1f / 3f), "1/3");
                Assert.Equal("-2.5", JsonText.FormatFloat(-2.5f), "-2.5");
                Assert.Equal("123456", JsonText.FormatFloat(123456f), "integral");
                foreach (float v in new[] { 0.1f, 1f / 3f, 1e-7f, 3.4e38f, 1.17549435e-38f, 12345.678f, -0.000123f })
                {
                    float back = float.Parse(JsonText.FormatFloat(v), System.Globalization.CultureInfo.InvariantCulture);
                    Assert.Equal(v, back, "round-trip " + v.ToString("R"));
                }
            });

            r.Add("JSON FormatDouble", () =>
            {
                Assert.Equal("0.10000000149011612", JsonText.FormatDouble(0.1f), "(double)0.1f");
                Assert.Equal("1", JsonText.FormatDouble(1.0), "1");
                Assert.Equal("65536", JsonText.FormatDouble(65536.0), "65536");
            });

            r.Add("JSON escaping + ordered object", () =>
            {
                var o = new JsonObject().Set("b", 1).Set("a", "x\"y\\z\n").Set("b", 2);
                Assert.Equal("{\"b\":2,\"a\":\"x\\u0022y\\\\z\\n\"}", JsonText.Serialize(o, JsonEscaping.Default), "default escaping");
                Assert.Equal("{\"b\":2,\"a\":\"x\\\"y\\\\z\\n\"}", JsonText.Serialize(o, JsonEscaping.Relaxed), "relaxed escaping");
                Assert.Equal("[\"á<\"]", JsonText.Serialize(new List<object> { "á<" }, JsonEscaping.Relaxed), "relaxed keeps non-ASCII and <");
                Assert.Equal("\"\\u00E1\\u003C\\u0026\"", JsonText.Serialize("á<&", JsonEscaping.Default), "default escapes non-ASCII/HTML");
            });

            r.Add("Materials: sRGB->linear, equivalence, opacity", () =>
            {
                Assert.Equal(0f, MaterialFactory.SrgbToLinear(0f), "0");
                Assert.Near(1.0, MaterialFactory.SrgbToLinear(1f), 1e-6, "1");
                Assert.Near(0.21404114, MaterialFactory.SrgbToLinear(0.5f), 1e-6, "0.5");
                Assert.Near(0.02 / 12.92, MaterialFactory.SrgbToLinear(0.02f), 1e-9, "linear segment");
                Assert.Equal(1f, MaterialFactory.SrgbToLinear(2f), "clamped");

                var opaque = MaterialFactory.FromDisplayColour("Steel", 0.5f, 0.5f, 0.5f, 1f, 0f, 0f);
                Assert.Equal("Steel_Material", opaque.Name, "name");
                Assert.Equal(AlphaType.Opaque, opaque.Alpha, "opaque");
                Assert.Equal(1f, opaque.BaseColour.W, "alpha 1");
                var glass = MaterialFactory.FromDisplayColour("Glass", 0.5f, 0.5f, 0.5f, 0.25f, 0f, 0f);
                Assert.Equal(AlphaType.Blend, glass.Alpha, "blend");
                Assert.Near(0.25, glass.BaseColour.W, 1e-6, "alpha = opacity");
                Assert.True(MaterialFactory.AreEquivalent(opaque, MaterialFactory.FromDisplayColour("Other", 0.50001f, 0.5f, 0.5f, 1f, 0f, 0f)), "equivalent ignores name/tiny diff");
                Assert.True(!MaterialFactory.AreEquivalent(opaque, glass), "different alpha");
                Assert.True(!MaterialFactory.AreEquivalent(opaque, null), "null");
            });

            r.Add("MeshAccumulator: dedup, winding fix, degenerate, zero normal fallback", () =>
            {
                var acc = new MeshAccumulator("m", null);
                Vec3 a = new Vec3(0, 0, 0), b = new Vec3(1, 0, 0), c = new Vec3(0, 1, 0), d = new Vec3(1, 1, 0);
                Vec3 up = new Vec3(0, 0, 2); // chưa chuẩn hóa
                Assert.True(acc.AddTriangle(a, b, c, up, up, up), "tri 1");
                Assert.True(acc.AddTriangle(b, d, c, up, up, up), "tri 2");
                Assert.Equal(4, acc.Mesh.VertexCount, "shared vertices deduplicated");
                Assert.Equal(1f, acc.Mesh.Normals[0].Z, "normal normalized");

                // Thứ tự cuộn ngược với normal -> bị đảo lại.
                var acc2 = new MeshAccumulator("m2", null);
                acc2.AddTriangle(a, c, b, up, up, up);
                Vec3 p0 = acc2.Mesh.Positions[acc2.Mesh.Indices[0]], p1 = acc2.Mesh.Positions[acc2.Mesh.Indices[1]], p2 = acc2.Mesh.Positions[acc2.Mesh.Indices[2]];
                Assert.True(Vec3.Cross(p1 - p0, p2 - p0).Z > 0, "winding follows normal");

                Assert.True(!acc.AddTriangle(a, a, b, up, up, up), "degenerate rejected");
                Assert.True(!acc.AddTriangle(a, b, new Vec3(float.NaN, 0, 0), up, up, up), "NaN rejected");
                Assert.Equal(2, acc.DegenerateCount, "degenerate count");

                var acc3 = new MeshAccumulator("m3", null);
                acc3.AddTriangle(a, b, c, new Vec3(0, 0, 0), null, up);
                Assert.Equal(2, acc3.ZeroNormalCount, "zero/missing normals fall back to facet normal");
                Assert.True(acc3.Mesh.Normals.All(n => n.Z == 1f), "fallback = geometric normal");
                MeshValidator.Validate(acc.Mesh);
            });

            r.Add("MeshValidator rejects invalid meshes", () =>
            {
                var m = new ExtractedMesh { Name = "bad" };
                Assert.Throws<ExportException>(() => MeshValidator.Validate(m), "empty");
                m.Positions.Add(new Vec3(0, 0, 0)); m.Normals.Add(new Vec3(0, 0, 1));
                m.Indices.AddRange(new[] { 0, 0, 5 });
                Assert.Throws<ExportException>(() => MeshValidator.Validate(m), "out of range");
            });

            r.Add("FacetReader: 1-based SAFEARRAY, per-vertex / per-facet / no normals", () =>
            {
                double[] pts = { 0, 0, 0, 0.01, 0, 0, 0, 0.01, 0, /* tri 2 */ 0.01, 0, 0, 0.01, 0.01, 0, 0, 0.01, 0 };
                Array points = Array.CreateInstance(typeof(double), new[] { pts.Length }, new[] { 1 });
                for (int i = 0; i < pts.Length; i++) { points.SetValue(pts[i], i + 1); }

                double[] perVertex = Enumerable.Range(0, 6).SelectMany(_ => new double[] { 0, 0, 1 }).ToArray();
                var acc = new MeshAccumulator("pv", null);
                Assert.Equal(2, FacetReader.AddFacets(acc, 2, points, perVertex), "per-vertex");
                Assert.Equal(4, acc.Mesh.VertexCount, "per-vertex dedup");

                var acc2 = new MeshAccumulator("pf", null);
                Assert.Equal(2, FacetReader.AddFacets(acc2, 2, points, new object[] { 0.0, 0.0, -1.0, 0.0, 0.0, -1.0 }), "per-facet");
                Assert.True(acc2.Mesh.Normals.All(n => n.Z == -1f), "per-facet normals kept");

                var acc3 = new MeshAccumulator("nn", null);
                Assert.Equal(2, FacetReader.AddFacets(acc3, 2, points, null), "no normals");
                Assert.Equal(6, acc3.ZeroNormalCount, "fallback normals");

                Assert.Throws<ExportException>(() => FacetReader.AddFacets(new MeshAccumulator("x", null), 3, points, null), "short points");
                Assert.Equal(0, FacetReader.AddFacets(new MeshAccumulator("x", null), 0, null, null), "zero facets");
                Assert.Equal(18, FacetReader.ToDoubles(new float[,] { { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, { 1, 2, 3, 4, 5, 6, 7, 8, 9 } }).Length, "2D array flattened");
            });

            r.Add("SolidEdgeMatrix: layout detection and normalization", () =>
            {
                double[] rowMajorTranslationLast = { 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0.1, 0.2, 0.3, 1 };
                SolidEdgeMatrixLayout layout;
                bool detected;
                double[] origin = { 0.1, 0.2, 0.3 };
                double[] n1 = SolidEdgeMatrix.Normalize(rowMajorTranslationLast, origin, SolidEdgeMatrixLayout.TranslationInColumn, out layout, out detected);
                Assert.True(detected && layout == SolidEdgeMatrixLayout.TranslationLast, "translation last detected");
                Assert.True(n1.SequenceEqual(rowMajorTranslationLast), "unchanged");

                double[] column = Mat4.Transpose(rowMajorTranslationLast);
                double[] n2 = SolidEdgeMatrix.Normalize(column, origin, SolidEdgeMatrixLayout.TranslationLast, out layout, out detected);
                Assert.True(detected && layout == SolidEdgeMatrixLayout.TranslationInColumn, "column layout detected");
                Assert.True(n2.SequenceEqual(rowMajorTranslationLast), "transposed back");

                // Không có origin: dùng heuristic affine.
                Assert.True(SolidEdgeMatrix.TryDetectLayout(column, null, out layout) && layout == SolidEdgeMatrixLayout.TranslationInColumn, "affine heuristic");

                // Chỉ xoay, không tịnh tiến -> không xác định được, dùng fallback.
                double[] rotOnly = { 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
                SolidEdgeMatrix.Normalize(rotOnly, new double[] { 0, 0, 0 }, SolidEdgeMatrixLayout.TranslationInColumn, out layout, out detected);
                Assert.True(!detected && layout == SolidEdgeMatrixLayout.TranslationInColumn, "fallback used");

                double[] bad = (double[])rotOnly.Clone();
                bad[0] = double.NaN;
                Assert.Throws<ExportException>(() => SolidEdgeMatrix.Normalize(bad, null, SolidEdgeMatrixLayout.TranslationLast, out layout, out detected), "NaN");
            });

            r.Add("Mat4: identity, multiply, transform point (row-vector convention)", () =>
            {
                Assert.True(Mat4.IsIdentity(Mat4.Identity()), "identity");
                double[] t = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1, 2, 3, 1 };
                double[] p = Mat4.TransformPoint(t, 1, 1, 1);
                Assert.True(p[0] == 2 && p[1] == 3 && p[2] == 4, "translation applied");
                double[] i = Mat4.Multiply(t, new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 });
                Assert.True(i.SequenceEqual(t), "multiply by identity");
            });

            r.Add("CoordinateConverter: UnitScale, normals, transform cloned", () =>
            {
                ExtractedScene s = TestScenes.AssemblyNested();
                ExtractedScene same = CoordinateConverter.ConvertToGltf(s, new ExportOptions());
                Assert.True(same.Root.Children[1].LocalTransform.SequenceEqual(s.Root.Children[1].LocalTransform), "scale 1 keeps transforms");
                Assert.True(!ReferenceEquals(same.Root.Children[1].LocalTransform, s.Root.Children[1].LocalTransform), "transform cloned");

                ExtractedScene mm = CoordinateConverter.ConvertToGltf(s, new ExportOptions { UnitScale = 1000f });
                Assert.Equal(s.Root.Children[0].Meshes[0].Positions[7].X * 1000f, mm.Root.Children[0].Meshes[0].Positions[7].X, "position scaled");
                Assert.Equal(s.Root.Children[1].LocalTransform[12] * 1000f, mm.Root.Children[1].LocalTransform[12], "translation scaled");
                Assert.Equal(s.Root.Children[1].LocalTransform[0], mm.Root.Children[1].LocalTransform[0], "rotation kept");
                Assert.Throws<ArgumentOutOfRangeException>(() => CoordinateConverter.ConvertToGltf(s, new ExportOptions { UnitScale = 0 }), "scale 0");
            });

            r.Add("ExportOptions.ResolveChordTolerance", () =>
            {
                var o = new ExportOptions();
                Assert.Near(0.0005, o.ResolveChordTolerance(1.0), 1e-15, "relative");
                Assert.Near(1e-5, o.ResolveChordTolerance(0.001), 1e-15, "minimum");
                Assert.Near(1e-5, o.ResolveChordTolerance(double.NaN), 1e-15, "NaN -> minimum");
                o.ChordTolerance = 0.002;
                Assert.Near(0.002, o.ResolveChordTolerance(1.0), 0, "explicit");
            });
        }
    }
}
