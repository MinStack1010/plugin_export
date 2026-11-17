using System;

namespace SynodeExporter.Core
{
    /// <summary>Các hàm vật liệu, port từ SolidWorksGeometryExtractor (plugin SW).</summary>
    public static class MaterialFactory
    {
        /// <summary>Dung sai của AreMaterialsEquivalent (SW).</summary>
        public const float EquivalenceTolerance = 0.0001f;

        public static float Clamp(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }
            if (value > 1.0f)
            {
                return 1.0f;
            }
            return value;
        }

        /// <summary>Cùng công thức với SolidWorksGeometryExtractor.SrgbToLinear.</summary>
        public static float SrgbToLinear(float value)
        {
            value = Clamp(value);
            if (value <= 0.04045f)
            {
                return value / 12.92f;
            }
            return (float)Math.Pow((value + 0.055f) / 1.055f, 2.4);
        }

        /// <summary>Port của AreMaterialsEquivalent (bỏ qua tên, giống SW).</summary>
        public static bool AreEquivalent(ExtractedMaterial a, ExtractedMaterial b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null)
            {
                return false;
            }
            return Vec4.Distance(a.BaseColour, b.BaseColour) < EquivalenceTolerance
                && Math.Abs(a.Metallic - b.Metallic) < EquivalenceTolerance
                && Math.Abs(a.Roughness - b.Roughness) < EquivalenceTolerance
                && a.Alpha == b.Alpha;
        }

        /// <summary>
        /// Tạo vật liệu từ màu hiển thị của Solid Edge (FaceStyle diffuse, 0..1,
        /// hệ sRGB như trên màn hình) và độ đục (Opacity, 1 = đục hoàn toàn).
        /// Quy đổi giống ConvertDspbrMaterial của SW: màu sRGB -> linear,
        /// transparency = 1 - opacity, alpha = 1 - transparency, BLEND khi có trong suốt.
        /// </summary>
        public static ExtractedMaterial FromDisplayColour(string name, float red, float green, float blue, float opacity,
            float metallic, float roughness)
        {
            var colour = new Vec4(SrgbToLinear(red), SrgbToLinear(green), SrgbToLinear(blue), 1f);
            float transparency = Clamp(1f - opacity);
            AlphaType alpha = AlphaType.Opaque;
            if (transparency != 0)
            {
                colour.W = 1f - transparency;
                alpha = AlphaType.Blend;
            }
            return new ExtractedMaterial
            {
                Name = name + "_Material",
                BaseColour = colour,
                Metallic = Clamp(metallic),
                Roughness = Clamp(roughness),
                Alpha = alpha
            };
        }
    }

    /// <summary>
    /// Port của CoordinateConverter.ConvertSolidWorksSceneToGltf: nhân vị trí với
    /// UnitScale, chuẩn hóa lại normal, giữ nguyên transform (không đổi trục).
    /// </summary>
    public static class CoordinateConverter
    {
        public static ExtractedScene ConvertToGltf(ExtractedScene source, ExportOptions options)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (source.Root == null)
            {
                throw new ExportException("The extracted scene has no root node.");
            }
            if (!(options.UnitScale > 0))
            {
                throw new ArgumentOutOfRangeException("options", "UnitScale must be greater than zero.");
            }
            return new ExtractedScene { Root = ConvertNode(source.Root, options.UnitScale) };
        }

        private static ExtractedNode ConvertNode(ExtractedNode source, float scale)
        {
            var node = new ExtractedNode
            {
                Name = source.Name,
                LocalTransform = (float[])source.LocalTransform.Clone()
            };
            if (scale != 1f)
            {
                // SW giữ nguyên tịnh tiến (chỉ đúng vì UnitScale luôn = 1); ở đây
                // tịnh tiến cũng được đổi đơn vị để hình học và vị trí luôn nhất quán.
                node.LocalTransform[12] *= scale;
                node.LocalTransform[13] *= scale;
                node.LocalTransform[14] *= scale;
            }
            foreach (ExtractedMesh mesh in source.Meshes)
            {
                node.Meshes.Add(ConvertMesh(mesh, scale));
            }
            foreach (ExtractedNode child in source.Children)
            {
                node.Children.Add(ConvertNode(child, scale));
            }
            return node;
        }

        private static ExtractedMesh ConvertMesh(ExtractedMesh source, float scale)
        {
            var mesh = new ExtractedMesh { Name = source.Name, Material = source.Material };
            foreach (Vec3 p in source.Positions)
            {
                mesh.Positions.Add(new Vec3(p.X * scale, p.Y * scale, p.Z * scale));
            }
            foreach (Vec3 n in source.Normals)
            {
                Vec3 unit;
                mesh.Normals.Add(Vec3.TryNormalize(n, out unit) ? unit : n);
            }
            mesh.Indices.AddRange(source.Indices);
            return mesh;
        }
    }
}
