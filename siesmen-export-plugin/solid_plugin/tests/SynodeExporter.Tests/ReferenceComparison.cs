#if REFERENCE_SW
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using SynodeExporter.Core;
using Sw = PluginSynode.Export;

namespace SynodeExporter.Tests
{
    /// <summary>
    /// Đối chiếu GlbWriter (Solid Edge) với writer GỐC của plugin SOLIDWORKS
    /// (CoordinateConverter + GltfSceneWriter + SharpGLTF 1.0.6, mã nguồn liên kết chỉ-đọc).
    /// Cùng một scene -> JSON (trừ chuỗi generator) và BIN phải trùng từng byte.
    /// </summary>
    internal static class ReferenceComparison
    {
        public static void Register(TestRunner r)
        {
            foreach (var pair in TestScenes.All())
            {
                var factory = pair.Value;
                string name = pair.Key;
                r.Add("SW/SharpGLTF reference == GlbWriter: " + name, () => Compare(factory(), name));
            }

            // SharpGLTF ghi số bằng System.Text.Json: so sánh FormatFloat/FormatDouble trên nhiều giá trị ngẫu nhiên.
            r.Add("number formatting == System.Text.Json (300k random float/double)", () =>
            {
                var rnd = new Random(12345);
                int checkedCount = 0;
                var bytes = new byte[4];
                for (int i = 0; i < 300000; i++)
                {
                    float f;
                    if (i % 3 == 0)
                    {
                        rnd.NextBytes(bytes);
                        f = BitConverter.ToSingle(bytes, 0);
                    }
                    else
                    {
                        f = (float)((rnd.NextDouble() - 0.5) * Math.Pow(10, rnd.Next(-8, 8)));
                    }
                    if (float.IsNaN(f) || float.IsInfinity(f)) { continue; }
                    checkedCount++;
                    string expected = System.Text.Json.JsonSerializer.Serialize(f);
                    Assert.Equal(expected, JsonText.FormatFloat(f), "float " + f.ToString("R"));
                    string expectedD = System.Text.Json.JsonSerializer.Serialize((double)f);
                    Assert.Equal(expectedD, JsonText.FormatDouble(f), "double " + ((double)f).ToString("R"));
                }
                Assert.True(checkedCount > 250000, "enough samples");
            });
        }

        private static void Compare(ExtractedScene scene, string name)
        {
            byte[] expected = Reference(scene);
            byte[] actual = GlbWriter.BuildGlb(CoordinateConverter.ConvertToGltf(scene, new ExportOptions()));
            GlbFile e = GlbFile.Parse(expected);
            GlbFile a = GlbFile.Parse(actual);

            // Chỉ khác chuỗi generator (SharpGLTF ghi phiên bản của nó): thay bằng regex trên JSON gốc.
            var generator = new System.Text.RegularExpressions.Regex("\"generator\":\"(?:[^\"\\\\]|\\\\.)*\"");
            string expectedJson = generator.Replace(e.JsonText.TrimEnd(' '), "\"generator\":" + Quote(GlbWriter.Generator), 1);
            Assert.TextEqual(expectedJson, a.JsonText.TrimEnd(' '), name + " JSON");
            Assert.BytesEqual(e.Bin ?? new byte[0], a.Bin ?? new byte[0], name + " BIN");
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder();
            JsonText.WriteString(sb, s, GlbWriter.Escaping);
            return sb.ToString();
        }

        private static byte[] Reference(ExtractedScene scene)
        {
            var sw = new Sw.ExtractedScene { Root = ToSw(scene.Root) };
            Sw.ExtractedScene converted = Sw.CoordinateConverter.ConvertSolidWorksSceneToGltf(sw, new Sw.ExportOptions { UnitScale = 1f });
            string path = Path.Combine(Path.GetTempPath(), "synode_ref_" + Guid.NewGuid().ToString("N") + ".glb");
            try
            {
                Sw.GltfSceneWriter.WriteGlb(converted, path);
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path)) { File.Delete(path); }
            }
        }

        private static Sw.ExtractedNode ToSw(ExtractedNode n)
        {
            float[] m = n.LocalTransform;
            var node = new Sw.ExtractedNode
            {
                Name = n.Name,
                LocalTransform = new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15])
            };
            foreach (var mesh in n.Meshes)
            {
                var sm = new Sw.ExtractedMesh { Name = mesh.Name, Material = ToSw(mesh.Material) };
                sm.Positions.AddRange(mesh.Positions.Select(p => new Vector3(p.X, p.Y, p.Z)));
                sm.Normals.AddRange(mesh.Normals.Select(p => new Vector3(p.X, p.Y, p.Z)));
                sm.Indices.AddRange(mesh.Indices);
                node.Meshes.Add(sm);
            }
            foreach (var c in n.Children) { node.Children.Add(ToSw(c)); }
            return node;
        }

        private static Sw.ExtractedMaterial ToSw(ExtractedMaterial m)
        {
            if (m == null) { return null; }
            return new Sw.ExtractedMaterial
            {
                Name = m.Name,
                BaseColour = new Vector4(m.BaseColour.X, m.BaseColour.Y, m.BaseColour.Z, m.BaseColour.W),
                Metallic = m.Metallic,
                Roughness = m.Roughness,
                Alpha = m.Alpha == AlphaType.Blend ? Sw.ExtractedMaterial.AlphaType.Blend
                    : m.Alpha == AlphaType.Mask ? Sw.ExtractedMaterial.AlphaType.Mask : Sw.ExtractedMaterial.AlphaType.Opaque
            };
        }
    }
}
#endif
