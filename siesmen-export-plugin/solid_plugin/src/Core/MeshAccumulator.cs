using System;
using System.Collections.Generic;

namespace SynodeExporter.Core
{
    /// <summary>
    /// Compares vertices (position, normal) like VertexPositionNormal in SharpGLTF:
    /// float == comparison, so -0 == +0 (the hash is normalized accordingly).
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
    /// Collects triangles into a mesh. Reproduces SolidWorksGeometryExtractor.AddTessellationFacet
    /// + GetTessellationNormal and PrimitiveBuilder.AddTriangle of SharpGLTF:
    /// * normals are normalized (float32);
    /// * degenerate triangles are dropped (|cross|^2 &lt; 1e-20);
    /// * the winding is flipped when the geometric normal points away from the average normal;
    /// * duplicate vertices (position, normal) are shared; triangles that become degenerate after merging are dropped.
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

        /// <summary>Adds a triangle. Positions are in meters; normals may be null or not normalized.</summary>
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
            // SW throws on zero normals; Solid Edge facets may have no normals
            // (or degenerate normals at singular points), so the facet normal is used instead.
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
        /// <summary>Port of ValidateExtractedMesh (SW) + finite number checks.</summary>
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
    /// Reads the facet data returned by Solid Edge Face.GetFacetData:
    /// Points = FacetCount*3 vertices * 3 coordinates (meters); Normals = per vertex (FacetCount*9)
    /// or per facet (FacetCount*3) or empty. The SAFEARRAY may have a non-zero lower
    /// bound, so it is read through enumeration in linear order.
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

        /// <summary>Converts a COM array (double/float/object, any lower bound, 1 or more dimensions) to double[].</summary>
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
