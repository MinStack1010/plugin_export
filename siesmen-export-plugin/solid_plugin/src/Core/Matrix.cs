using System;

namespace SynodeExporter.Core
{
    /// <summary>
    /// Ma trận 4x4 dạng mảng 16 float theo thứ tự System.Numerics.Matrix4x4
    /// (row-vector: hàng 1..3 = trục X, Y, Z đã xoay; hàng 4 = tịnh tiến).
    /// Bố cục bộ nhớ này trùng với "matrix" (column-major) của glTF.
    /// </summary>
    public static class Mat4
    {
        public static float[] Identity()
        {
            return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        /// <summary>So sánh chính xác với ma trận đơn vị (SharpGLTF bỏ "matrix" khi LocalTransform == Identity).</summary>
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

        /// <summary>Tích a*b theo quy ước row-vector (áp dụng a trước rồi b).</summary>
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

        /// <summary>Biến đổi điểm (row-vector * M).</summary>
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

    /// <summary>Kết quả chuẩn hóa ma trận Occurrence.GetMatrix của Solid Edge.</summary>
    public enum SolidEdgeMatrixLayout
    {
        /// <summary>Tịnh tiến ở phần tử [12..14] (row-vector, giống Matrix4x4) - dùng trực tiếp.</summary>
        TranslationLast,
        /// <summary>Tịnh tiến ở phần tử [3], [7], [11] (column-vector) - cần chuyển vị.</summary>
        TranslationInColumn
    }

    /// <summary>
    /// Chuyển ma trận 16 double của Occurrence.GetMatrix sang Matrix4x4/glTF.
    /// Bố cục mảng của Solid Edge không có trong tài liệu cài đặt (SESDK.chm
    /// không đi kèm), nên bố cục được xác định lúc chạy: vị trí của phần tịnh
    /// tiến được đối chiếu với gốc tọa độ trả về bởi Occurrence.GetTransform.
    /// </summary>
    public static class SolidEdgeMatrix
    {
        /// <summary>
        /// Xác định bố cục. Trả về false khi không thể phân biệt (ví dụ ma trận
        /// chỉ xoay, không tịnh tiến) - khi đó <paramref name="layout"/> = TranslationLast.
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

            // Ma trận affine có (0,0,0,1) theo một trong hai cách bố trí.
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
        /// Trả về ma trận double theo thứ tự Matrix4x4 (tịnh tiến ở [12..14]).
        /// <paramref name="fallback"/> được dùng khi bố cục không thể xác định từ chính ma trận này.
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
