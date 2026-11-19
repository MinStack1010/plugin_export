using System;
using System.Collections.Generic;

namespace SynodeExporter.Core
{
    /// <summary>Single-precision 3-component vector (like System.Numerics.Vector3 in the SW plugin).</summary>
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        public static float Dot(Vec3 a, Vec3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        public float LengthSquared()
        {
            return X * X + Y * Y + Z * Z;
        }

        public bool IsFinite()
        {
            return !(float.IsNaN(X) || float.IsInfinity(X) || float.IsNaN(Y) || float.IsInfinity(Y) || float.IsNaN(Z) || float.IsInfinity(Z));
        }

        /// <summary>
        /// Like Vector3.Normalize (v / |v|, all arithmetic in float32) so that normals
        /// are bit-identical to the SOLIDWORKS pipeline. Returns false if the vector is zero.
        /// </summary>
        public static bool TryNormalize(Vec3 v, out Vec3 result)
        {
            float lengthSquared = (v.X * v.X + v.Y * v.Y) + v.Z * v.Z;
            float length = (float)Math.Sqrt(lengthSquared);
            if (length == 0f || float.IsNaN(length) || float.IsInfinity(length))
            {
                result = v;
                return false;
            }
            result = new Vec3(v.X / length, v.Y / length, v.Z / length);
            return true;
        }

        public override string ToString()
        {
            return "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>4-component vector (linear RGBA color).</summary>
    public struct Vec4
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vec4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>Like Vector4.Distance (float32).</summary>
        public static float Distance(Vec4 a, Vec4 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            float dw = a.W - b.W;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw);
        }
    }

    public enum AlphaType
    {
        Blend,
        Mask,
        Opaque
    }

    /// <summary>Equivalent to PluginSynode.Export.ExtractedMaterial (SW plugin).</summary>
    public sealed class ExtractedMaterial
    {
        public string Name { get; set; }
        public Vec4 BaseColour { get; set; } = new Vec4(0.8f, 0.8f, 0.8f, 1.0f);
        public float Metallic { get; set; }
        public float Roughness { get; set; }
        public AlphaType Alpha { get; set; } = AlphaType.Opaque;
    }

    /// <summary>Equivalent to ExtractedMesh (SW plugin). Positions are in meters.</summary>
    public sealed class ExtractedMesh
    {
        public string Name { get; set; }
        public List<Vec3> Positions { get; } = new List<Vec3>();
        public List<Vec3> Normals { get; } = new List<Vec3>();
        public List<int> Indices { get; } = new List<int>();
        public ExtractedMaterial Material { get; set; }

        public int VertexCount { get { return Positions.Count; } }
        public int TriangleCount { get { return Indices.Count / 3; } }
    }

    /// <summary>
    /// Equivalent to ExtractedNode (SW plugin). LocalTransform holds 16 floats in
    /// System.Numerics.Matrix4x4 order (M11, M12, ..., M44; translation in
    /// M41..M43), identical to the column-major layout of the glTF "matrix".
    /// </summary>
    public sealed class ExtractedNode
    {
        public string Name { get; set; }
        public float[] LocalTransform = Mat4.Identity();
        public List<ExtractedMesh> Meshes = new List<ExtractedMesh>();
        public List<ExtractedNode> Children = new List<ExtractedNode>();
    }

    public sealed class ExtractedScene
    {
        public ExtractedNode Root;
    }

    /// <summary>Export options.</summary>
    public sealed class ExportOptions
    {
        /// <summary>
        /// Position unit conversion factor. The Solid Edge API always returns meters (like the
        /// SOLIDWORKS API) and glTF uses meters, so the default value is 1, as in the SW plugin.
        /// </summary>
        public float UnitScale { get; set; } = 1f;

        /// <summary>
        /// Chord tolerance (chord height, meters) used for faceting. 0 = automatic, based on the
        /// body size: max(diagonal * RelativeChordTolerance, MinChordTolerance).
        /// </summary>
        public double ChordTolerance { get; set; } = 0.0;

        public double RelativeChordTolerance { get; set; } = 0.0005;

        public double MinChordTolerance { get; set; } = 1e-5;

        /// <summary>Solid Edge FaceStyle has no PBR parameters: use the same defaults as ExtractedMaterial in SW.</summary>
        public float DefaultMetallic { get; set; } = 0f;

        public float DefaultRoughness { get; set; } = 0f;

        public double ResolveChordTolerance(double bodyDiagonal)
        {
            if (ChordTolerance > 0)
            {
                return ChordTolerance;
            }
            double value = bodyDiagonal * RelativeChordTolerance;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < MinChordTolerance)
            {
                value = MinChordTolerance;
            }
            return value;
        }
    }

    /// <summary>Export error whose message is shown to the user.</summary>
    public sealed class ExportException : Exception
    {
        public ExportException(string message) : base(message)
        {
        }

        public ExportException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
