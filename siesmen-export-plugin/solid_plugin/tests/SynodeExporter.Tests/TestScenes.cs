using System;
using System.Collections.Generic;
using SynodeExporter.Core;

namespace SynodeExporter.Tests
{
    /// <summary>
    /// Các scene mẫu mô phỏng dữ liệu mà extractor Solid Edge tạo ra (đã qua
    /// MeshAccumulator) và các trường hợp biên của writer. Dùng chung cho
    /// test cấu trúc, so sánh net48/net9 và đối chiếu với writer SharpGLTF của plugin SW.
    /// </summary>
    internal static class TestScenes
    {
        public static IEnumerable<KeyValuePair<string, Func<ExtractedScene>>> All()
        {
            yield return Item("part_cube", PartCube);
            yield return Item("assembly_nested", AssemblyNested);
            yield return Item("multi_mesh_packing", MultiMeshPacking);
            yield return Item("uint16_boundary_65535", () => Strip("strip65535", 65535));
            yield return Item("uint32_65536", () => Strip("strip65536", 65536));
            yield return Item("mixed_small_and_large", MixedSmallAndLarge);
            yield return Item("duplicates_degenerate_unused", DuplicatesDegenerateUnused);
            yield return Item("names_escaping", NamesEscaping);
            yield return Item("materials_variants", MaterialsVariants);
            yield return Item("empty_root", () => new ExtractedScene { Root = new ExtractedNode { Name = "Empty.asm" } });
            yield return Item("null_names", NullNames);
            yield return Item("negative_zero_and_fractions", NegativeZero);
        }

        private static KeyValuePair<string, Func<ExtractedScene>> Item(string name, Func<ExtractedScene> f)
        {
            return new KeyValuePair<string, Func<ExtractedScene>>(name, f);
        }

        public static ExtractedMaterial Grey(string name)
        {
            return MaterialFactory.FromDisplayColour(name, 0.8f, 0.8f, 0.8f, 1f, 0f, 0f);
        }

        /// <summary>Hộp kín (mỗi mặt 2 tam giác, normal phẳng), qua MeshAccumulator như extractor thật.</summary>
        public static ExtractedMesh Box(string name, ExtractedMaterial material, float sx, float sy, float sz, float ox = 0, float oy = 0, float oz = 0)
        {
            var acc = new MeshAccumulator(name, material);
            Vec3[] c = new Vec3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = new Vec3(ox + ((i & 1) != 0 ? sx : 0), oy + ((i & 2) != 0 ? sy : 0), oz + ((i & 4) != 0 ? sz : 0));
            }
            int[][] quads =
            {
                new[] { 0, 2, 3, 1 }, // -Z
                new[] { 4, 5, 7, 6 }, // +Z
                new[] { 0, 1, 5, 4 }, // -Y
                new[] { 2, 6, 7, 3 }, // +Y
                new[] { 0, 4, 6, 2 }, // -X
                new[] { 1, 3, 7, 5 }  // +X
            };
            Vec3[] normals =
            {
                new Vec3(0, 0, -1), new Vec3(0, 0, 1), new Vec3(0, -1, 0), new Vec3(0, 1, 0), new Vec3(-1, 0, 0), new Vec3(1, 0, 0)
            };
            for (int f = 0; f < 6; f++)
            {
                Vec3 n = normals[f];
                int[] q = quads[f];
                acc.AddTriangle(c[q[0]], c[q[1]], c[q[2]], n, n, n);
                acc.AddTriangle(c[q[0]], c[q[2]], c[q[3]], n, n, n);
            }
            return acc.Mesh;
        }

        /// <summary>Lưới phẳng dạng dải với số đỉnh chính xác (mỗi đỉnh một vị trí riêng).</summary>
        public static ExtractedMesh StripMesh(string name, int vertexCount, ExtractedMaterial material)
        {
            var mesh = new ExtractedMesh { Name = name, Material = material };
            for (int i = 0; i < vertexCount; i++)
            {
                int col = i / 2;
                float x = col * 0.001f;
                float y = (i % 2) * 0.01f;
                float z = (float)Math.Round(Math.Sin(col * 0.01), 6) * 0.002f;
                mesh.Positions.Add(new Vec3(x, y, z));
                mesh.Normals.Add(new Vec3(0, 0, 1));
            }
            for (int i = 0; i + 2 < vertexCount; i++)
            {
                if (i % 2 == 0)
                {
                    mesh.Indices.Add(i); mesh.Indices.Add(i + 1); mesh.Indices.Add(i + 2);
                }
                else
                {
                    mesh.Indices.Add(i + 1); mesh.Indices.Add(i); mesh.Indices.Add(i + 2);
                }
            }
            return mesh;
        }

        private static ExtractedScene Strip(string name, int count)
        {
            var root = new ExtractedNode { Name = name + ".par" };
            root.Meshes.Add(StripMesh(name + ".par_Material_0", count, Grey("Strip_Material")));
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene PartCube()
        {
            var root = new ExtractedNode { Name = "Block.par" };
            root.Meshes.Add(Box("Block.par_Material_0", MaterialFactory.FromDisplayColour("Steel_Material", 0.6f, 0.62f, 0.65f, 1f, 0f, 0f), 0.1f, 0.05f, 0.02f));
            return new ExtractedScene { Root = root };
        }

        public static float[] Transform(double angleDegZ, double angleDegX, float tx, float ty, float tz)
        {
            double a = angleDegZ * Math.PI / 180.0;
            double b = angleDegX * Math.PI / 180.0;
            // Làm tròn sin/cos: Math.Cos của .NET Framework (CRT cũ) và .NET 9 có thể khác
            // vài ULP (vd. cos 90° = 6.123031E-17 so với 6.123234E-17) -> dữ liệu test phải giống nhau trên 2 runtime.
            double ca = Math.Round(Math.Cos(a), 7), sa = Math.Round(Math.Sin(a), 7);
            double cb = Math.Round(Math.Cos(b), 7), sb = Math.Round(Math.Sin(b), 7);
            double[] rz = { ca, sa, 0, 0, -sa, ca, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            double[] rx = { 1, 0, 0, 0, 0, cb, sb, 0, 0, -sb, cb, 0, 0, 0, 0, 1 };
            double[] m = Mat4.Multiply(rx, rz);
            m[12] = tx; m[13] = ty; m[14] = tz;
            return Mat4.ToFloat(m);
        }

        public static ExtractedScene AssemblyNested()
        {
            var steel = MaterialFactory.FromDisplayColour("Steel_Material", 0.6f, 0.62f, 0.65f, 1f, 0f, 0f);
            var red = MaterialFactory.FromDisplayColour("Red_Face", 0.9f, 0.1f, 0.05f, 1f, 0f, 0f);
            var glass = MaterialFactory.FromDisplayColour("Glass_Material", 0.7f, 0.85f, 0.95f, 0.35f, 0f, 0f);

            var root = new ExtractedNode { Name = "Gearbox.asm" };

            var housing = new ExtractedNode { Name = "Housing.par:1" };
            housing.Meshes.Add(Box("Housing.par:1_Material_0", steel, 0.2f, 0.1f, 0.1f));
            housing.Meshes.Add(Box("Housing.par:1_Material_1", red, 0.02f, 0.02f, 0.02f, 0.2f, 0f, 0f));
            root.Children.Add(housing);

            var sub = new ExtractedNode { Name = "Shaft_Sub.asm:1", LocalTransform = Transform(90, 0, 0.05f, 0.05f, 0.1f) };
            for (int i = 1; i <= 3; i++)
            {
                // Cùng một part dùng nhiều lần với transform khác nhau.
                var gear = new ExtractedNode { Name = "Gear.par:" + i, LocalTransform = Transform(30 * i, 15, 0.01f * i, 0f, -0.002f) };
                gear.Meshes.Add(Box("Gear.par:" + i + "_Material_0", steel, 0.03f, 0.03f, 0.01f));
                sub.Children.Add(gear);
            }
            var cover = new ExtractedNode { Name = "Cover.par:1", LocalTransform = Transform(0, 180, 0f, 0.1f, 0.1f) };
            cover.Meshes.Add(Box("Cover.par:1_Material_0", glass, 0.2f, 0.1f, 0.003f));
            sub.Children.Add(cover);
            root.Children.Add(sub);

            // Occurrence không có hình học (ví dụ assembly rỗng / tài liệu không phải part).
            root.Children.Add(new ExtractedNode { Name = "Empty.asm:1", LocalTransform = Transform(0, 0, 1f, 2f, 3f) });
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene MultiMeshPacking()
        {
            var root = new ExtractedNode { Name = "Multi.par" };
            float[] sizes = { 0.01f, 0.5f, 0.2f, 0.05f };
            for (int i = 0; i < sizes.Length; i++)
            {
                var mat = MaterialFactory.FromDisplayColour("M" + i + "_Material", 0.1f * (i + 1), 0.2f, 0.3f, 1f, 0f, 0f);
                root.Meshes.Add(Box("Multi.par_Material_" + i, mat, sizes[i], sizes[i], sizes[i], i, 0, 0));
            }
            root.Meshes.Add(StripMesh("Multi.par_Material_4", 300, Grey("Default_Material")));
            root.Meshes.Add(StripMesh("Multi.par_Material_5", 37, Grey("Default_Material")));
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene MixedSmallAndLarge()
        {
            var root = new ExtractedNode { Name = "Mixed.asm" };
            var a = new ExtractedNode { Name = "Small.par:1" };
            a.Meshes.Add(Box("Small.par:1_Material_0", Grey("Grey_Material"), 0.01f, 0.01f, 0.01f));
            var b = new ExtractedNode { Name = "Large.par:1", LocalTransform = Transform(45, 0, 0.3f, 0, 0) };
            b.Meshes.Add(StripMesh("Large.par:1_Material_0", 70000, Grey("Grey_Material")));
            root.Children.Add(a);
            root.Children.Add(b);
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene DuplicatesDegenerateUnused()
        {
            // Dữ liệu "thô" (không qua MeshAccumulator): đỉnh trùng, tam giác suy biến, đỉnh không dùng.
            var mesh = new ExtractedMesh { Name = "Raw.par_Material_0", Material = Grey("Raw_Material") };
            Vec3 n = new Vec3(0, 0, 1);
            Vec3[] p =
            {
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 1, 0),
                new Vec3(5, 5, 5),               // không dùng
                new Vec3(0, 0, 0),               // trùng đỉnh 0
                new Vec3(1, 1, 0),               // trùng đỉnh 2
                new Vec3(-0f, 0, 0)              // -0 so với +0
            };
            foreach (var v in p) { mesh.Positions.Add(v); mesh.Normals.Add(n); }
            int[] idx =
            {
                0, 1, 2,
                5, 6, 3,   // dùng đỉnh trùng
                0, 0, 1,   // suy biến (2 chỉ số giống nhau)
                7, 1, 6,
                2, 2, 2    // suy biến hoàn toàn
            };
            mesh.Indices.AddRange(idx);
            var root = new ExtractedNode { Name = "Raw.par" };
            root.Meshes.Add(mesh);
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene NamesEscaping()
        {
            var root = new ExtractedNode { Name = "Bánh răng ống.asm" };
            var c1 = new ExtractedNode { Name = "<a&b> 'q' \"dq\" \\slash/ +plus `tick`" };
            c1.Meshes.Add(Box("Mesh <1> & \"x\"", MaterialFactory.FromDisplayColour("Thép không gỉ_Material", 0.5f, 0.5f, 0.5f, 1f, 0f, 0f), 0.01f, 0.01f, 0.01f));
            var c2 = new ExtractedNode { Name = "Tab\tNewline\nCR\rCtl\u0001 € 日本 😀" };
            c2.Meshes.Add(Box("Đai ốc_Material_0", Grey("Đai ốc_Material"), 0.02f, 0.01f, 0.01f));
            root.Children.Add(c1);
            root.Children.Add(c2);
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene MaterialsVariants()
        {
            var root = new ExtractedNode { Name = "Materials.par" };
            var mats = new List<ExtractedMaterial>
            {
                new ExtractedMaterial { Name = "Metal_1_Rough_1", BaseColour = new Vec4(1, 1, 1, 1), Metallic = 1f, Roughness = 1f },
                new ExtractedMaterial { Name = "White_Default" , BaseColour = new Vec4(1, 1, 1, 1) },
                new ExtractedMaterial { Name = "Mask", BaseColour = new Vec4(0.2f, 0.4f, 0.6f, 0.5f), Alpha = AlphaType.Mask },
                new ExtractedMaterial { Name = "Blend", BaseColour = new Vec4(0.2f, 0.4f, 0.6f, 0.25f), Alpha = AlphaType.Blend },
                new ExtractedMaterial { Name = "Half", BaseColour = new Vec4(0.5f, 0.5f, 0.5f, 1f), Metallic = 0.5f, Roughness = 0.25f },
                null,
                new ExtractedMaterial { Name = "Metal_1_Rough_1", BaseColour = new Vec4(1, 1, 1, 1), Metallic = 1f, Roughness = 1f }, // trùng
                new ExtractedMaterial { Name = null }
            };
            for (int i = 0; i < mats.Count; i++)
            {
                root.Meshes.Add(Box("Materials.par_Material_" + i, mats[i], 0.01f, 0.01f, 0.01f, 0.02f * i, 0, 0));
            }
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene NullNames()
        {
            var root = new ExtractedNode { Name = null };
            var child = new ExtractedNode { Name = null, LocalTransform = Transform(10, 20, 0.1f, 0.2f, 0.3f) };
            child.Meshes.Add(Box(null, null, 0.01f, 0.02f, 0.03f));
            child.Meshes[0].Name = null;
            root.Children.Add(child);
            // Mesh rỗng phải bị bỏ qua.
            root.Meshes.Add(new ExtractedMesh { Name = "Empty", Material = Grey("E") });
            return new ExtractedScene { Root = root };
        }

        public static ExtractedScene NegativeZero()
        {
            var root = new ExtractedNode { Name = "NegZero.asm" };
            var child = new ExtractedNode { Name = "Rot180.par:1", LocalTransform = Transform(180, 90, -0f, 0.1f, 1e-7f) };
            var mesh = new ExtractedMesh { Name = "Rot180.par:1_Material_0", Material = MaterialFactory.FromDisplayColour("Odd_Material", 1f / 3f, 0.1f, 1e-5f, 0.999f, 0f, 0f) };
            Vec3[] pts = { new Vec3(-0f, 0f, 1e-8f), new Vec3(1f / 3f, -0.1f, 12345.678f), new Vec3(-1e-6f, 2.5e7f, 0.3f) };
            Vec3 normal;
            Vec3.TryNormalize(Vec3.Cross(pts[1] - pts[0], pts[2] - pts[0]), out normal);
            foreach (var v in pts) { mesh.Positions.Add(v); mesh.Normals.Add(normal); }
            mesh.Indices.AddRange(new[] { 0, 1, 2 });
            child.Meshes.Add(mesh);
            root.Children.Add(child);
            return new ExtractedScene { Root = root };
        }
    }
}
