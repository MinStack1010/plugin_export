using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SynodeExporter.Core;

namespace SynodeExporter.Tests
{
    /// <summary>Kiểm tra GLB do GlbWriter tạo: hợp lệ glTF 2.0 và giữ nguyên hình học/hierarchy/transform/vật liệu.</summary>
    internal static class GlbStructureTests
    {
        public static void Register(TestRunner r)
        {
            foreach (var pair in TestScenes.All())
            {
                var factory = pair.Value;
                r.Add("glb valid + geometry preserved: " + pair.Key, () =>
                {
                    ExtractedScene scene = factory();
                    GlbFile glb = GlbFile.Parse(GlbWriter.BuildGlb(scene));
                    Validate(glb);
                    CheckGeometryPreserved(scene, glb);
                    CheckHierarchy(scene, glb);
                });
            }

            r.Add("part cube: 1 node, 24 vertices, 36 indices, uint16, identity omitted", () =>
            {
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(TestScenes.PartCube()));
                Assert.Equal(1, g.Array("nodes").Count, "nodes");
                Assert.True(!J.Has(g.Array("nodes")[0], "matrix"), "identity matrix must be omitted");
                Assert.Equal("Block.par", J.Str(g.Array("nodes")[0], "name"), "root name");
                var acc = g.Array("accessors");
                Assert.Equal(24, J.Int(acc[0], "count"), "vertex count");
                Assert.Equal(36, J.Int(acc[2], "count"), "index count");
                Assert.Equal(5123, J.Int(acc[2], "componentType"), "uint16 indices");
                Assert.Equal("2.0", J.Str(J.Get(g.Json, "asset"), "version"), "asset.version");
                Assert.Equal(GlbWriter.Generator, J.Str(J.Get(g.Json, "asset"), "generator"), "generator");
            });

            r.Add("index type: 65535 vertices -> uint16, 65536 -> uint32, uint32 applies to all", () =>
            {
                Assert.Equal(5123, IndexTypes(TestScenes.All().First(p => p.Key == "uint16_boundary_65535").Value()).Single(), "65535");
                Assert.Equal(5125, IndexTypes(TestScenes.All().First(p => p.Key == "uint32_65536").Value()).Single(), "65536");
                var mixed = IndexTypes(TestScenes.MixedSmallAndLarge());
                Assert.True(mixed.Count == 2 && mixed.All(t => t == 5125), "mixed -> all uint32");
            });

            r.Add("empty root: no buffers/meshes, no BIN chunk", () =>
            {
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(new ExtractedScene { Root = new ExtractedNode { Name = "E" } }));
                Assert.True(!g.Json.ContainsKey("meshes") && !g.Json.ContainsKey("buffers") && !g.Json.ContainsKey("accessors"), "no mesh data");
                Assert.True(g.Bin == null, "no BIN chunk");
                Assert.Equal(1, g.Array("nodes").Count, "one node");
            });

            r.Add("null names -> Root/Component/Mesh/Material, empty mesh skipped", () =>
            {
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(TestScenes.NullNames()));
                var nodes = g.Array("nodes");
                Assert.Equal("Root", J.Str(nodes[0], "name"), "root");
                Assert.Equal("Component", J.Str(nodes[1], "name"), "child");
                Assert.Equal(1, g.Array("meshes").Count, "empty mesh skipped");
                Assert.Equal("Mesh", J.Str(g.Array("meshes")[0], "name"), "mesh");
                Assert.Equal("Material", J.Str(g.Array("materials")[0], "name"), "material");
            });

            r.Add("multiple meshes: first on node, rest on unnamed child nodes", () =>
            {
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(TestScenes.MultiMeshPacking()));
                var nodes = g.Array("nodes");
                Assert.Equal(6, g.Array("meshes").Count, "meshes");
                Assert.Equal(6, nodes.Count, "root + 5 extra nodes");
                Assert.True(J.Has(nodes[0], "mesh"), "root carries first mesh");
                Assert.Equal(5, J.Arr(J.Get(nodes[0], "children")).Count, "extra nodes are children of root");
                for (int i = 1; i < nodes.Count; i++)
                {
                    Assert.True(!J.Has(nodes[i], "name") && J.Has(nodes[i], "mesh") && !J.Has(nodes[i], "matrix"), "extra node " + i);
                }
            });

            r.Add("materials: dedupe by content, BLEND/MASK, defaults", () =>
            {
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(TestScenes.MaterialsVariants()));
                var mats = g.Array("materials");
                Assert.Equal(6, mats.Count, "unique materials");
                var byName = mats.GroupBy(m => J.Str(m, "name")).ToDictionary(x => x.Key, x => x.First());
                Assert.Equal("BLEND", J.Str(byName["Blend"], "alphaMode"), "blend");
                Assert.Equal("MASK", J.Str(byName["Mask"], "alphaMode"), "mask");
                Assert.True(!J.Has(byName["Half"], "alphaMode"), "opaque omitted");
                foreach (var m in mats)
                {
                    Assert.True(J.Has(m, "doubleSided") && (bool)J.Get(m, "doubleSided"), "doubleSided");
                }
                var half = J.Obj(J.Get(byName["Half"], "pbrMetallicRoughness"));
                Assert.Near(0.5, (double)half["metallicFactor"], 0, "metallic");
                Assert.Near(0.25, (double)half["roughnessFactor"], 0, "roughness");
            });

            r.Add("assembly: component transforms written exactly (float32)", () =>
            {
                ExtractedScene s = TestScenes.AssemblyNested();
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(s));
                var nodes = g.Array("nodes");
                var sub = nodes.First(n => J.Has(n, "name") && J.Str(n, "name") == "Shaft_Sub.asm:1");
                var m = J.Arr(J.Get(sub, "matrix")).Select(v => (float)(double)v).ToArray();
                float[] expected = s.Root.Children[1].LocalTransform;
                for (int i = 0; i < 16; i++) { Assert.Equal(expected[i], m[i], "matrix[" + i + "]"); }
                Assert.Equal(3, nodes.Count(n => J.Has(n, "name") && J.Str(n, "name").StartsWith("Gear.par:", StringComparison.Ordinal)), "repeated part instances");
            });

            r.Add("names: non-ASCII, quotes, control chars round-trip through JSON", () =>
            {
                ExtractedScene s = TestScenes.NamesEscaping();
                GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(s));
                var names = g.Array("nodes").Where(n => J.Has(n, "name")).Select(n => J.Str(n, "name")).ToList();
                Assert.True(names.Contains("Bánh răng ống.asm"), "Vietnamese root name");
                Assert.True(names.Contains(s.Root.Children[0].Name), "special chars name");
                Assert.True(names.Contains(s.Root.Children[1].Name), "control chars/emoji name");
                Assert.True(g.Array("materials").Any(m => J.Str(m, "name") == "Thép không gỉ_Material_Material"), "material name");
            });

            r.Add("WriteGlb: writes file, overwrites, leaves no temp file", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "SynodeSeTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    string path = Path.Combine(dir, "out.glb");
                    File.WriteAllText(path, "old");
                    GlbWriter.WriteGlb(TestScenes.PartCube(), path);
                    GlbFile.Parse(File.ReadAllBytes(path));
                    Assert.Equal(1, Directory.GetFiles(dir).Length, "only output file remains");
                }
                finally
                {
                    Directory.Delete(dir, true);
                }
            });

            r.Add("WriteGlb: missing folder -> ExportException; null root -> ExportException", () =>
            {
                string path = Path.Combine(Path.GetTempPath(), "no_such_dir_" + Guid.NewGuid().ToString("N"), "x.glb");
                Assert.Throws<ExportException>(() => GlbWriter.WriteGlb(TestScenes.PartCube(), path), "missing folder");
                Assert.Throws<ExportException>(() => GlbWriter.BuildGlb(new ExtractedScene()), "null root");
            });
        }

        private static List<int> IndexTypes(ExtractedScene s)
        {
            GlbFile g = GlbFile.Parse(GlbWriter.BuildGlb(s));
            var acc = g.Array("accessors");
            var result = new List<int>();
            foreach (var mesh in g.Array("meshes"))
            {
                foreach (var prim in J.Arr(J.Get(mesh, "primitives")))
                {
                    result.Add(J.Int(acc[J.Int(prim, "indices")], "componentType"));
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- validation

        public static void Validate(GlbFile g)
        {
            var asset = J.Obj(g.Json["asset"]);
            Assert.Equal("2.0", (string)asset["version"], "asset.version");
            var nodes = g.Array("nodes");
            var scenes = g.Array("scenes");
            Assert.Equal(1, scenes.Count, "scene count");
            Assert.Equal(0, J.Int(g.Json, "scene"), "default scene");

            // Mỗi node được tham chiếu đúng 1 lần (cây, không chu trình).
            var refCount = new int[nodes.Count];
            foreach (var root in J.Arr(J.Get(scenes[0], "nodes"))) { refCount[(int)(double)root]++; }
            for (int i = 0; i < nodes.Count; i++)
            {
                object children;
                if (J.Obj(nodes[i]).TryGetValue("children", out children))
                {
                    foreach (var c in J.Arr(children)) { refCount[(int)(double)c]++; }
                }
                object matrix;
                if (J.Obj(nodes[i]).TryGetValue("matrix", out matrix))
                {
                    var m = J.Arr(matrix).Select(v => (float)(double)v).ToArray();
                    Assert.Equal(16, m.Length, "matrix length");
                    Assert.True(!Mat4.IsIdentity(m), "identity matrix must be omitted");
                }
            }
            Assert.True(refCount.All(c => c == 1), "every node must be referenced exactly once");

            var accessors = g.Array("accessors");
            var views = g.Array("bufferViews");
            var buffers = g.Array("buffers");
            int binLength = g.Bin == null ? 0 : g.Bin.Length;
            if (buffers.Count > 0)
            {
                Assert.Equal(1, buffers.Count, "buffer count");
                Assert.True(J.Int(buffers[0], "byteLength") <= binLength, "buffer.byteLength <= BIN length");
            }
            foreach (var v in views)
            {
                Assert.True(J.IntOr(v, "byteOffset", 0) + J.Int(v, "byteLength") <= binLength, "bufferView inside buffer");
            }
            var materials = g.Array("materials");
            foreach (var mesh in g.Array("meshes"))
            {
                var prims = J.Arr(J.Get(mesh, "primitives"));
                Assert.Equal(1, prims.Count, "one primitive per mesh");
                foreach (var prim in prims)
                {
                    Assert.Equal(4, J.IntOr(prim, "mode", 4), "triangles");
                    var attrs = J.Obj(J.Get(prim, "attributes"));
                    float[] pos = ReadVec3(g, accessors[(int)(double)attrs["POSITION"]], views);
                    float[] nrm = ReadVec3(g, accessors[(int)(double)attrs["NORMAL"]], views);
                    Assert.Equal(pos.Length, nrm.Length, "normal count");
                    int vertexCount = pos.Length / 3;
                    for (int i = 0; i < vertexCount; i++)
                    {
                        double len = Math.Sqrt(nrm[i * 3] * nrm[i * 3] + nrm[i * 3 + 1] * nrm[i * 3 + 1] + nrm[i * 3 + 2] * nrm[i * 3 + 2]);
                        Assert.Near(1.0, len, 1e-5, "unit normal");
                    }
                    CheckMinMax(accessors[(int)(double)attrs["POSITION"]], pos);
                    int[] idx = ReadIndices(g, accessors[J.Int(prim, "indices")], views);
                    Assert.True(idx.Length % 3 == 0 && idx.Length > 0, "index count multiple of 3");
                    Assert.True(idx.All(i => i >= 0 && i < vertexCount), "indices in range");
                    var used = new bool[vertexCount];
                    foreach (int i in idx) { used[i] = true; }
                    Assert.True(used.All(u => u), "no unused vertices");
                    if (J.Has(prim, "material"))
                    {
                        Assert.True(J.Int(prim, "material") < materials.Count, "material index");
                    }
                }
            }
        }

        private static void CheckMinMax(object accessor, float[] pos)
        {
            var min = J.Arr(J.Get(accessor, "min")).Select(v => (float)(double)v).ToArray();
            var max = J.Arr(J.Get(accessor, "max")).Select(v => (float)(double)v).ToArray();
            for (int c = 0; c < 3; c++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = c; i < pos.Length; i += 3) { lo = Math.Min(lo, pos[i]); hi = Math.Max(hi, pos[i]); }
                Assert.Equal(lo, min[c], "POSITION.min[" + c + "]");
                Assert.Equal(hi, max[c], "POSITION.max[" + c + "]");
            }
        }

        private static float[] ReadVec3(GlbFile g, object accessor, List<object> views)
        {
            Assert.Equal(5126, J.Int(accessor, "componentType"), "float accessor");
            Assert.Equal("VEC3", J.Str(accessor, "type"), "VEC3");
            var view = views[J.Int(accessor, "bufferView")];
            int stride = J.IntOr(view, "byteStride", 12);
            int count = J.Int(accessor, "count");
            int start = J.IntOr(view, "byteOffset", 0) + J.IntOr(accessor, "byteOffset", 0);
            Assert.True(count == 0 || start - J.IntOr(view, "byteOffset", 0) + (count - 1) * stride + 12 <= J.Int(view, "byteLength"), "accessor inside view");
            var result = new float[count * 3];
            for (int i = 0; i < count; i++)
            {
                for (int c = 0; c < 3; c++) { result[i * 3 + c] = BitConverter.ToSingle(g.Bin, start + i * stride + c * 4); }
            }
            return result;
        }

        private static int[] ReadIndices(GlbFile g, object accessor, List<object> views)
        {
            Assert.Equal("SCALAR", J.Str(accessor, "type"), "SCALAR");
            int type = J.Int(accessor, "componentType");
            int size = type == 5123 ? 2 : type == 5125 ? 4 : 0;
            Assert.True(size > 0, "index component type");
            var view = views[J.Int(accessor, "bufferView")];
            int count = J.Int(accessor, "count");
            int start = J.IntOr(view, "byteOffset", 0) + J.IntOr(accessor, "byteOffset", 0);
            Assert.True(J.IntOr(accessor, "byteOffset", 0) + count * size <= J.Int(view, "byteLength"), "index accessor inside view");
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = size == 2 ? BitConverter.ToUInt16(g.Bin, start + i * 2) : (int)BitConverter.ToUInt32(g.Bin, start + i * 4);
            }
            return result;
        }

        // ---------------------------------------------------------------- geometry & hierarchy

        private static string Key(Vec3 p, Vec3 n)
        {
            return F(p.X) + "," + F(p.Y) + "," + F(p.Z) + "/" + F(n.X) + "," + F(n.Y) + "," + F(n.Z);
        }

        private static string F(float v)
        {
            return (v + 0f).ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Tập tam giác (không suy biến) theo giá trị đỉnh, giữ thứ tự cuộn (xoay vòng về đỉnh nhỏ nhất).</summary>
        private static List<string> Triangles(IList<Vec3> pos, IList<Vec3> nrm, IList<int> idx)
        {
            var list = new List<string>();
            for (int t = 0; t + 2 < idx.Count; t += 3)
            {
                string a = Key(pos[idx[t]], nrm[idx[t]]);
                string b = Key(pos[idx[t + 1]], nrm[idx[t + 1]]);
                string c = Key(pos[idx[t + 2]], nrm[idx[t + 2]]);
                if (a == b || b == c || a == c) { continue; }
                string[] rot = { a + "|" + b + "|" + c, b + "|" + c + "|" + a, c + "|" + a + "|" + b };
                Array.Sort(rot, StringComparer.Ordinal);
                list.Add(rot[0]);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static void CollectMeshes(ExtractedNode node, List<ExtractedMesh> into)
        {
            into.AddRange(node.Meshes.Where(m => m.VertexCount > 0 && m.TriangleCount > 0));
            foreach (var c in node.Children) { CollectMeshes(c, into); }
        }

        private static void CheckGeometryPreserved(ExtractedScene scene, GlbFile g)
        {
            var source = new List<ExtractedMesh>();
            CollectMeshes(scene.Root, source);
            var expected = source.Select(m => new KeyValuePair<string, List<string>>(m.Name ?? "Mesh", Triangles(m.Positions, m.Normals, m.Indices)))
                .Where(p => p.Value.Count > 0).ToList();
            var accessors = g.Array("accessors");
            var views = g.Array("bufferViews");
            var meshes = g.Array("meshes");
            Assert.Equal(expected.Count, meshes.Count, "mesh count");
            foreach (var mesh in meshes)
            {
                var prim = J.Arr(J.Get(mesh, "primitives"))[0];
                var attrs = J.Obj(J.Get(prim, "attributes"));
                float[] p = ReadVec3(g, accessors[(int)(double)attrs["POSITION"]], views);
                float[] n = ReadVec3(g, accessors[(int)(double)attrs["NORMAL"]], views);
                int[] idx = ReadIndices(g, accessors[J.Int(prim, "indices")], views);
                var pos = new List<Vec3>();
                var nrm = new List<Vec3>();
                for (int i = 0; i < p.Length; i += 3)
                {
                    pos.Add(new Vec3(p[i], p[i + 1], p[i + 2]));
                    nrm.Add(new Vec3(n[i], n[i + 1], n[i + 2]));
                }
                List<string> actual = Triangles(pos, nrm, idx);
                string name = J.Str(mesh, "name");
                int match = expected.FindIndex(e => e.Key == name && e.Value.SequenceEqual(actual));
                Assert.True(match >= 0, "mesh '" + name + "' triangles differ from source");
                expected.RemoveAt(match);
            }
        }

        private static void CheckHierarchy(ExtractedScene scene, GlbFile g)
        {
            var nodes = g.Array("nodes");
            CheckNode(scene.Root, nodes, 0, "Root");
        }

        private static void CheckNode(ExtractedNode src, List<object> nodes, int index, string defaultName)
        {
            var n = nodes[index];
            Assert.Equal(src.Name ?? defaultName, J.Str(n, "name"), "node name");
            if (Mat4.IsIdentity(src.LocalTransform))
            {
                Assert.True(!J.Has(n, "matrix"), "identity omitted for " + src.Name);
            }
            else
            {
                var m = J.Arr(J.Get(n, "matrix")).Select(v => (float)(double)v).ToArray();
                for (int i = 0; i < 16; i++) { Assert.Equal(src.LocalTransform[i], m[i], src.Name + " matrix[" + i + "]"); }
            }
            object children;
            var named = new List<int>();
            if (J.Obj(n).TryGetValue("children", out children))
            {
                named = J.Arr(children).Select(c => (int)(double)c).Where(c => J.Has(nodes[c], "name")).ToList();
            }
            Assert.Equal(src.Children.Count, named.Count, "child count of " + src.Name);
            for (int i = 0; i < src.Children.Count; i++)
            {
                CheckNode(src.Children[i], nodes, named[i], "Component");
            }
        }
    }
}
