using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace SynodeExporter.Core
{
    /// <summary>Số JSON với chuỗi biểu diễn cố định.</summary>
    public sealed class JsonNumber
    {
        public JsonNumber(string text)
        {
            Text = text;
        }

        public string Text { get; private set; }

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>Đối tượng JSON giữ nguyên thứ tự khóa.</summary>
    public sealed class JsonObject
    {
        private readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();

        public IList<KeyValuePair<string, object>> Items { get { return _items; } }

        /// <summary>Gán giá trị; khóa đã có thì giữ nguyên vị trí.</summary>
        public JsonObject Set(string key, object value)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    _items[i] = new KeyValuePair<string, object>(key, value);
                    return this;
                }
            }
            _items.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public bool Remove(string key)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    _items.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool TryGet(string key, out object value)
        {
            foreach (var item in _items)
            {
                if (item.Key == key)
                {
                    value = item.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }
    }

    public enum JsonEscaping
    {
        /// <summary>Như JavaScriptEncoder.Default của System.Text.Json: thoát ký tự ngoài ASCII và ký tự HTML.</summary>
        Default,
        /// <summary>Chỉ thoát ký tự điều khiển, dấu nháy kép và gạch chéo ngược; UTF-8 giữ nguyên.</summary>
        Relaxed
    }

    /// <summary>Bộ ghi JSON gọn (không khoảng trắng) và định dạng số kiểu .NET (System.Text.Json).</summary>
    public static class JsonText
    {
        public static string Serialize(object value, JsonEscaping escaping)
        {
            var sb = new StringBuilder();
            Write(sb, value, escaping);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, JsonEscaping escaping)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            var obj = value as JsonObject;
            if (obj != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var item in obj.Items)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    WriteString(sb, item.Key, escaping);
                    sb.Append(':');
                    Write(sb, item.Value, escaping);
                }
                sb.Append('}');
                return;
            }
            var s = value as string;
            if (s != null)
            {
                WriteString(sb, s, escaping);
                return;
            }
            var number = value as JsonNumber;
            if (number != null)
            {
                sb.Append(number.Text);
                return;
            }
            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }
            if (value is int || value is long)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is float)
            {
                sb.Append(FormatFloat((float)value));
                return;
            }
            if (value is double)
            {
                sb.Append(FormatDouble((double)value));
                return;
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    Write(sb, item, escaping);
                }
                sb.Append(']');
                return;
            }
            throw new ArgumentException("Unsupported JSON value: " + value.GetType().FullName);
        }

        public static void WriteString(StringBuilder sb, string s, JsonEscaping escaping)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        continue;
                    case '\n':
                        sb.Append("\\n");
                        continue;
                    case '\r':
                        sb.Append("\\r");
                        continue;
                    case '\t':
                        sb.Append("\\t");
                        continue;
                    case '\b':
                        sb.Append("\\b");
                        continue;
                    case '\f':
                        sb.Append("\\f");
                        continue;
                }
                if (c == '"')
                {
                    sb.Append(escaping == JsonEscaping.Relaxed ? "\\\"" : "\\u0022");
                    continue;
                }
                bool escape = c < 0x20 || char.IsSurrogate(c); // ký tự ngoài BMP luôn được thoát (giống System.Text.Json)
                if (!escape && escaping == JsonEscaping.Default)
                {
                    escape = c > 0x7E || c == '&' || c == '\'' || c == '+' || c == '<' || c == '>' || c == '`';
                }
                if (escape)
                {
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }

        /// <summary>Chuỗi ngắn nhất khôi phục đúng giá trị float32 (giống float.ToString() của .NET Core).</summary>
        public static string FormatFloat(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                throw new ExportException("Non finite number in glTF document.");
            }
            if (v == 0f)
            {
                return IsNegativeZero(v) ? "-0" : "0";
            }
            if (Math.Abs(v) < 1e7f && v == (float)Math.Floor(v))
            {
                return ((long)v).ToString(CultureInfo.InvariantCulture);
            }
            return ShortestRoundTrip(v, 24, -149, 9);
        }

        /// <summary>Chuỗi ngắn nhất khôi phục đúng giá trị double (giống double.ToString() của .NET Core).</summary>
        public static string FormatDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                throw new ExportException("Non finite number in glTF document.");
            }
            if (v == 0d)
            {
                return IsNegativeZero(v) ? "-0" : "0";
            }
            if (Math.Abs(v) < 1e15 && v == Math.Floor(v))
            {
                return ((long)v).ToString(CultureInfo.InvariantCulture);
            }
            return ShortestRoundTrip(v, 53, -1074, 17);
        }

        /// <summary>
        /// Chuỗi thập phân ngắn nhất (gần giá trị nhất) khôi phục đúng số float/double,
        /// định dạng như "G{số chữ số}" của .NET. Tính chính xác bằng BigInteger vì
        /// float/double.ToString("G n") của .NET Framework 4.8 (runtime của Edge.exe) làm
        /// tròn hai lần (qua 15 chữ số) nên có thể khác kết quả của .NET Core/SharpGLTF.
        /// </summary>
        internal static string ShortestRoundTrip(double v, int mantissaBits, int minExponent, int maxDigits)
        {
            bool negative = v < 0;
            double a = Math.Abs(v);
            long bits = BitConverter.DoubleToInt64Bits(a);
            int rawExp = (int)((bits >> 52) & 0x7FF);
            long frac = bits & 0xFFFFFFFFFFFFFL;
            BigInteger mant = rawExp == 0 ? frac : (frac | (1L << 52));
            int exp2 = rawExp == 0 ? -1074 : rawExp - 1075;
            // Chuẩn hóa: bỏ bit 0 cuối để mant lẻ (không đổi giá trị).
            while (!mant.IsZero && mant.IsEven) { mant >>= 1; exp2++; }

            int topBit = BitLength(mant) - 1 + exp2;
            int ulpExp = Math.Max(topBit - (mantissaBits - 1), minExponent);
            bool powerOfTwo = (mant & (mant - 1)).IsZero;
            int lowUlpExp = (powerOfTwo && topBit - (mantissaBits - 1) > minExponent) ? ulpExp - 1 : ulpExp;
            // Phần định trị trong kiểu đích chẵn -> điểm giữa được làm tròn về chính nó.
            bool evenMantissa = exp2 > ulpExp || (exp2 == ulpExp && mant.IsEven);

            int shift = Math.Max(0, -Math.Min(exp2, lowUlpExp - 1));
            BigInteger V = mant << (exp2 + shift);
            BigInteger halfUp = BigInteger.One << (ulpExp - 1 + shift);
            BigInteger halfLow = BigInteger.One << (lowUlpExp - 1 + shift);
            BigInteger scale = BigInteger.One << shift; // giá trị thực = X / scale

            int k = (int)Math.Floor(Math.Log10(a));
            while (Compare(V, scale, k) < 0) { k--; }
            while (Compare(V, scale, k + 1) >= 0) { k++; }

            for (int p = 1; p <= maxDigits; p++)
            {
                int q = k - p + 1;
                BigInteger num = q >= 0 ? V : V * BigInteger.Pow(10, -q);
                BigInteger den = q >= 0 ? scale * BigInteger.Pow(10, q) : scale;
                BigInteger rem;
                BigInteger n = BigInteger.DivRem(num, den, out rem);
                BigInteger twice = rem * 2;
                if (twice > den || (twice == den && !n.IsEven)) { n += 1; }

                // Kiểm tra D = n*10^q nằm trong khoảng làm tròn về v (đơn vị chung).
                BigInteger dUnits, vUnits, up, low;
                if (q >= 0)
                {
                    dUnits = n * BigInteger.Pow(10, q) * scale; // theo đơn vị 1/scale
                    vUnits = V;
                    up = halfUp;
                    low = halfLow;
                }
                else
                {
                    BigInteger p10 = BigInteger.Pow(10, -q); // nhân cả hai vế với 10^-q
                    dUnits = n * scale;
                    vUnits = num;
                    up = halfUp * p10;
                    low = halfLow * p10;
                }
                BigInteger diff = dUnits - vUnits;
                bool ok = diff.Sign >= 0 ? (diff < up || (diff == up && evenMantissa))
                                         : (-diff < low || (-diff == low && evenMantissa));
                if (!ok)
                {
                    continue;
                }
                string digits = n.ToString(CultureInfo.InvariantCulture);
                int exponent = k;
                if (digits.Length > p)
                {
                    // Làm tròn lên thành 10^p.
                    digits = digits.Substring(0, p);
                    exponent++;
                }
                // .NET Core / System.Text.Json: dạng cố định khi -5 < số mũ < số chữ số round-trip tối đa (9 float, 17 double).
                return (negative ? "-" : "") + FormatG(digits.TrimEnd('0'), exponent, maxDigits);
            }
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Dạng cố định khi -5 &lt; exp &lt; precision, ngược lại dạng d.dddE±XX (như .NET Core).</summary>
        private static string FormatG(string digits, int exponent, int precision)
        {
            if (digits.Length == 0)
            {
                digits = "0";
            }
            if (exponent > -5 && exponent < precision)
            {
                if (exponent < 0)
                {
                    return "0." + new string('0', -exponent - 1) + digits;
                }
                if (digits.Length <= exponent + 1)
                {
                    return digits + new string('0', exponent + 1 - digits.Length);
                }
                return digits.Substring(0, exponent + 1) + "." + digits.Substring(exponent + 1);
            }
            string mantissa = digits.Length == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1);
            return mantissa + "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>So sánh V/scale với 10^k.</summary>
        private static int Compare(BigInteger V, BigInteger scale, int k)
        {
            return k >= 0 ? V.CompareTo(scale * BigInteger.Pow(10, k)) : (V * BigInteger.Pow(10, -k)).CompareTo(scale);
        }

        private static int BitLength(BigInteger value)
        {
            int length = 0;
            while (!value.IsZero) { value >>= 1; length++; }
            return length;
        }

        private static bool IsNegativeZero(double v)
        {
            return v == 0d && BitConverter.DoubleToInt64Bits(v) < 0;
        }
    }
}
