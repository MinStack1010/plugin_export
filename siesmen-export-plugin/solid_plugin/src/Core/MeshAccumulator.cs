using System;
using System.Collections.Generic;

namespace SynodeExporter.Core
{
    /// <summary>
    /// So sánh đỉnh (position, normal) giống VertexPositionNormal của SharpGLTF:
    /// so sánh float ==, nên -0 == +0 (hash được chuẩn hóa tương ứng).
    /// </summary>
    public sealed class VertexKeyComparer : IEqualityComparer<VertexKey>
    {
        public static readonly VertexKeyComparer Instance = new VertexKeyComparer();

        public bool Equals(VertexKey a, VertexKey b)
        {
            return a.P.X == b.P.X && a.P.Y == b.P.Y && a.P.Z == b.P.Z
                && a.N.X == b.N.X && a.N.Y == b.N.Y && a.N.Z == b.N.Z;
        }

        public int GetHashCode(VertexKey k)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + H(k.P.X);
                h = h * 31 + H(k.P.Y);
                h = h * 31 + H(k.P.Z);
                h = h * 31 + H(k.N.X);
                h = h * 31 + H(k.N.Y);
                h = h * 31 + H(k.N.Z);
                return h;
            }
        }

        private static int H(float v)
        {
            return v == 0f ? 0 : v.GetHashCode();
        }
    }

    public struct VertexKey
    {
        public Vec3 P;
        public Vec3 N;

        public VertexKey(Vec3 p, Vec3 n)
        {
            P = p;
            N = n;
        }
    }

    /// <summary>
    /// Gom tam giác thành mesh. Tái hiện SolidWorksGeometryExtractor.AddTessellationFacet
    /// + GetTessellationNormal và PrimitiveBuilder.AddTriangle của SharpGLTF:
    /// * normal được chuẩn hóa (float32);
    /// * bỏ tam giác suy biến (|cross|^2 &lt; 1e-20);
    /// * đảo chiều quấn khi normal hình học ngược hướng normal trung bình;
    /// * đỉnh trùng (position, normal) được dùng chung; tam giác bị suy biến sau khi gộp đỉnh thì bỏ.
    /// </summary>
    public sealed class MeshAccumulator
    {
        private const float DegenerateEpsilon = 1e-20f;
        private readonly Dictionary<VertexKey, int> _vertexMap = new Dictionary<VertexKey, int>(VertexKeyComparer.Instance);

        public MeshAccumulator(string name, ExtractedMaterial material)
        {
            Mesh = new ExtractedMesh { Name = name, Material = material };
        }

        public ExtractedMesh Mesh { get; private set; }

        public int DegenerateCount { get; private set; }

        public int ZeroNormalCount { get; private set; }

        /// <summary>Thêm một tam giác. Vị trí tính bằng mét; normal có thể null hoặc chưa chuẩn hóa.</summary>
        public bool AddTriangle(Vec3 p0, Vec3 p1, Vec3 p2, Vec3? n0, Vec3? n1, Vec3? n2)
        {
            if (!p0.IsFinite() || !p1.IsFinite() || !p2.IsFinite())
            {
                DegenerateCount++;
                return false;
            }

            Vec3 geometric = Vec3.Cross(p1 - p0, p2 - p0);
            if (geometric.LengthSquared() < DegenerateEpsilon)
            {
                DegenerateCount++;
                return false;
            }

            Vec3 unitGeometric;
            Vec3.TryNormalize(geometric, out unitGeometric);

            Vec3 u0 = NormalOrFallback(n0, unitGeometric);
            Vec3 u1 = NormalOrFallback(n1, unitGeometric);
            Vec3 u2 = NormalOrFallback(n2, unitGeometric);

            Vec3 average = u0 + u1 + u2;
            if (Vec3.Dot(geometric, average) < 0)
            {
                Vec3 tp = p1;
                p1 = p2;
                p2 = tp;
                Vec3 tn = u1;
                u1 = u2;
                u2 = tn;
            }

            int i0 = GetOrAddVertex(p0, u0);
            int i1 = GetOrAddVertex(p1, u1);
            int i2 = GetOrAddVertex(p2, u2);
            if (i0 == i1 || i1 == i2 || i0 == i2)
            {
                DegenerateCount++;
                return false;
            }

            Mesh.Indices.Add(i0);
            Mesh.Indices.Add(i1);
            Mesh.Indices.Add(i2);
            return true;
        }

        private Vec3 NormalOrFallback(Vec3? normal, Vec3 fallback)
        {
            Vec3 unit;
            if (normal.HasValue && normal.Value.IsFinite() && normal.Value.LengthSquared() >= DegenerateEpsilon
                && Vec3.TryNormalize(normal.Value, out unit))
            {
                return unit;
            }
            // SW ném lỗi khi gặp normal bằng 0; facet của Solid Edge có thể không có
            // normal (hoặc normal suy biến tại điểm kỳ dị) nên dùng normal của facet.
            ZeroNormalCount++;
            return fallback;
        }

        private int GetOrAddVertex(Vec3 p, Vec3 n)
        {
            var key = new VertexKey(p, n);
            int index;
            if (_vertexMap.TryGetValue(key, out index))
            {
                return index;
            }
            index = Mesh.Positions.Count;
            Mesh.Positions.Add(p);
            Mesh.Normals.Add(n);
            _vertexMap.Add(key, index);
            return index;
        }
    }

    public static class MeshValidator
    {
        /// <summary>Port của ValidateExtractedMesh (SW) + kiểm tra số hữu hạn.</summary>
        public static void Validate(ExtractedMesh mesh)
        {
            if (mesh.VertexCount == 0)
            {
                throw new ExportException("Mesh '" + mesh.Name + "' has no vertices.");
            }
            if (mesh.TriangleCount == 0)
            {
                throw new ExportException("Mesh '" + mesh.Name + "' has no triangles.");
            }
            if (mesh.Normals.Count != mesh.Positions.Count)
            {
                throw new ExportException("Mesh '" + mesh.Name + "' has mismatched normal count.");
            }
            if (mesh.Indices.Count % 3 != 0)
            {
                throw new ExportException("Mesh '" + mesh.Name + "' has an invalid index count.");
            }
            int count = mesh.VertexCount;
            foreach (int index in mesh.Indices)
            {
                if (index < 0 || index >= count)
                {
                    throw new ExportException("Mesh '" + mesh.Name + "' has an out of range index " + index + ".");
                }
            }
            foreach (Vec3 p in mesh.Positions)
            {
                if (!p.IsFinite())
                {
                    throw new ExportException("Mesh '" + mesh.Name + "' has a non finite position.");
                }
            }
            foreach (Vec3 n in mesh.Normals)
            {
                if (!n.IsFinite())
                {
                    throw new ExportException("Mesh '" + mesh.Name + "' has a non finite normal.");
                }
            }
        }
    }

    /// <summary>
    /// Đọc dữ liệu facet trả về bởi Face.GetFacetData của Solid Edge:
    /// Points = FacetCount*3 đỉnh * 3 tọa độ (mét); Normals = mỗi đỉnh (FacetCount*9)
    /// hoặc mỗi facet (FacetCount*3) hoặc rỗng. SAFEARRAY có thể có chỉ số
    /// bắt đầu khác 0 nên được đọc qua GetValue theo chỉ số tuyến tính.
    /// </summary>
    public static class FacetReader
    {
        public static int AddFacets(MeshAccumulator accumulator, int facetCount, object points, object normals)
        {
            if (facetCount <= 0)
            {
                return 0;
            }
            double[] p = ToDoubles(points);
            if (p == null || p.Length < facetCount * 9)
            {
                throw new ExportException("Invalid facet data: expected " + (facetCount * 9) + " coordinates, got "
                    + (p == null ? 0 : p.Length) + ".");
            }
            double[] n = ToDoubles(normals);
            bool perVertex = n != null && n.Length >= facetCount * 9;
            bool perFacet = !perVertex && n != null && n.Length >= facetCount * 3;

            int added = 0;
            for (int i = 0; i < facetCount; i++)
            {
                int b = i * 9;
                Vec3 p0 = new Vec3((float)p[b], (float)p[b + 1], (float)p[b + 2]);
                Vec3 p1 = new Vec3((float)p[b + 3], (float)p[b + 4], (float)p[b + 5]);
                Vec3 p2 = new Vec3((float)p[b + 6], (float)p[b + 7], (float)p[b + 8]);
                Vec3? n0 = null, n1 = null, n2 = null;
                if (perVertex)
                {
                    n0 = new Vec3((float)n[b], (float)n[b + 1], (float)n[b + 2]);
                    n1 = new Vec3((float)n[b + 3], (float)n[b + 4], (float)n[b + 5]);
                    n2 = new Vec3((float)n[b + 6], (float)n[b + 7], (float)n[b + 8]);
                }
                else if (perFacet)
                {
                    int f = i * 3;
                    n0 = n1 = n2 = new Vec3((float)n[f], (float)n[f + 1], (float)n[f + 2]);
                }
                if (accumulator.AddTriangle(p0, p1, p2, n0, n1, n2))
                {
                    added++;
                }
            }
            return added;
        }

        /// <summary>Chuyển mảng COM (double/float/object, mọi chỉ số gốc, 1 hoặc nhiều chiều) thành double[].</summary>
        public static double[] ToDoubles(object value)
        {
            var array = value as Array;
            if (array == null)
            {
                return null;
            }
            var result = new double[array.Length];
            int i = 0;
            foreach (object item in array)
            {
                result[i++] = Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture);
            }
            return result;
        }
    }
}
