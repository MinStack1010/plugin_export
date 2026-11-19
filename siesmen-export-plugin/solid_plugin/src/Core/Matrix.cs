using System;

namespace SynodeExporter.Core
{
    /// <summary>
    /// 4x4 matrix stored as an array of 16 floats in System.Numerics.Matrix4x4 order
    /// (row-vector: rows 1..3 = rotated X, Y, Z axes; row 4 = translation).
    /// This memory layout is identical to the glTF "matrix" (column-major).
    /// </summary>
    public static class Mat4
    {
        public static float[] Identity()
        {
            return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        /// <summary>Exact comparison with the identity matrix (SharpGLTF omits "matrix" when LocalTransform == Identity).</summary>
        public static bool IsIdentity(float[] m)
        {
            if (m == null || m.Length != 16)
            {
                return false;
            }
            for (int i = 0; i < 16; i++)
            {
                float expected = (i % 5 == 0) ? 1f : 0f;
                if (m[i] != expected)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsFinite(float[] m)
        {
            if (m == null || m.Length != 16)
            {
                return false;
            }
            foreach (float v in m)
            {
                if (float.IsNaN(v) || float.IsInfinity(v))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Product a*b in the row-vector convention (applies a first, then b).</summary>
        public static double[] Multiply(double[] a, double[] b)
        {
            var r = new double[16];
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        sum += a[row * 4 + k] * b[k * 4 + col];
                    }
                    r[row * 4 + col] = sum;
                }
            }
            return r;
        }

        public static double[] Transpose(double[] m)
        {
            var r = new double[16];
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    r[col * 4 + row] = m[row * 4 + col];
                }
            }
            return r;
        }

        /// <summary>Transforms a point (row-vector * M).</summary>
        public static double[] TransformPoint(double[] m, double x, double y, double z)
        {
            return new[]
            {
                x * m[0] + y * m[4] + z * m[8] + m[12],
                x * m[1] + y * m[5] + z * m[9] + m[13],
                x * m[2] + y * m[6] + z * m[10] + m[14]
            };
        }

        public static float[] ToFloat(double[] m)
        {
            var r = new float[16];
            for (int i = 0; i < 16; i++)
            {
                r[i] = (float)m[i];
            }
            return r;
        }
    }

    /// <summary>Result of normalizing a Solid Edge Occurrence.GetMatrix matrix.</summary>
    public enum SolidEdgeMatrixLayout
    {
        /// <summary>Translation in elements [12..14] (row-vector, like Matrix4x4) - used directly.</summary>
        TranslationLast,
        /// <summary>Translation in elements [3], [7], [11] (column-vector) - must be transposed.</summary>
        TranslationInColumn
    }

    /// <summary>
    /// Converts the 16-double matrix of Occurrence.GetMatrix to Matrix4x4/glTF.
    /// The Solid Edge array layout is not covered by the installed documentation (SESDK.chm
    /// is not shipped), so the layout is determined at run time: the position of the
    /// translation part is compared with the origin returned by Occurrence.GetTransform.
    /// </summary>
    public static class SolidEdgeMatrix
    {
        /// <summary>
        /// Determines the layout. Returns false when the layouts cannot be distinguished (e.g. a
        /// rotation-only matrix without translation) - <paramref name="layout"/> is then TranslationLast.
        /// </summary>
        public static bool TryDetectLayout(double[] m, double[] origin, out SolidEdgeMatrixLayout layout)
        {
            if (m == null || m.Length != 16)
            {
                throw new ArgumentException("A Solid Edge matrix must contain 16 values.");
            }

            layout = SolidEdgeMatrixLayout.TranslationLast;
            const double eps = 1e-12;

            if (origin != null && origin.Length == 3)
            {
                double last = Distance(m[12], m[13], m[14], origin);
                double column = Distance(m[3], m[7], m[11], origin);
                if (Math.Abs(last - column) > eps)
                {
                    layout = column < last ? SolidEdgeMatrixLayout.TranslationInColumn : SolidEdgeMatrixLayout.TranslationLast;
                    return true;
                }
            }

            // An affine matrix has (0,0,0,1) in one of the two layouts.
            bool lastIsAffine = m[3] == 0 && m[7] == 0 && m[11] == 0;
            bool columnIsAffine = m[12] == 0 && m[13] == 0 && m[14] == 0;
            if (lastIsAffine != columnIsAffine)
            {
                layout = lastIsAffine ? SolidEdgeMatrixLayout.TranslationLast : SolidEdgeMatrixLayout.TranslationInColumn;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the double matrix in Matrix4x4 order (translation in [12..14]).
        /// <paramref name="fallback"/> is used when the layout cannot be determined from this matrix itself.
        /// </summary>
        public static double[] Normalize(double[] m, double[] origin, SolidEdgeMatrixLayout fallback, out SolidEdgeMatrixLayout layout, out bool detected)
        {
            detected = TryDetectLayout(m, origin, out layout);
            if (!detected)
            {
                layout = fallback;
            }
            double[] result = layout == SolidEdgeMatrixLayout.TranslationInColumn ? Mat4.Transpose(m) : (double[])m.Clone();
            foreach (double v in result)
            {
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    throw new ExportException("The component transform contains invalid values.");
                }
            }
            return result;
        }

        private static double Distance(double x, double y, double z, double[] o)
        {
            double dx = x - o[0];
            double dy = y - o[1];
            double dz = z - o[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
