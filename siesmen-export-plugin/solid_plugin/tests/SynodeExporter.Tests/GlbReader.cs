using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SynodeExporter.Tests
{
    /// <summary>Đọc GLB và JSON tối giản để kiểm tra cấu trúc file do writer tạo ra (độc lập với writer).</summary>
    internal sealed class GlbFile
    {
        public string JsonText;
        public Dictionary<string, object> Json;
        public byte[] Bin;
        public uint DeclaredLength;
        public int FileLength;

        public static GlbFile Parse(byte[] data)
        {
            Assert.True(data.Length >= 20, "GLB too short");
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                Assert.Equal(0x46546C67u, r.ReadUInt32(), "magic");
                Assert.Equal(2u, r.ReadUInt32(), "version");
                var glb = new GlbFile { DeclaredLength = r.ReadUInt32(), FileLength = data.Length };
                Assert.Equal((uint)data.Length, glb.DeclaredLength, "declared length");
                uint jsonLength = r.ReadUInt32();
                Assert.Equal(0x4E4F534Au, r.ReadUInt32(), "JSON chunk type");
                Assert.True(jsonLength % 4 == 0, "JSON chunk not 4-byte aligned");
                byte[] json = r.ReadBytes((int)jsonLength);
                glb.JsonText = new UTF8Encoding(false, true).GetString(json);
                glb.Json = (Dictionary<string, object>)MiniJson.Parse(glb.JsonText);
                if (r.BaseStream.Position < data.Length)
                {
                    uint binLength = r.ReadUInt32();
                    Assert.Equal(0x004E4942u, r.ReadUInt32(), "BIN chunk type");
                    Assert.True(binLength % 4 == 0, "BIN chunk not 4-byte aligned");
                    glb.Bin = r.ReadBytes((int)binLength);
                    Assert.Equal((int)binLength, glb.Bin.Length, "BIN chunk length");
                }
                Assert.Equal((long)data.Length, r.BaseStream.Position, "trailing data");
                return glb;
            }
        }

        public List<object> Array(string key)
        {
            object v;
            return Json.TryGetValue(key, out v) ? (List<object>)v : new List<object>();
        }
    }

    internal static class J
    {
        public static Dictionary<string, object> Obj(object o) { return (Dictionary<string, object>)o; }
        public static List<object> Arr(object o) { return (List<object>)o; }
        public static bool Has(object o, string key) { return Obj(o).ContainsKey(key); }
        public static object Get(object o, string key) { return Obj(o)[key]; }
        public static int Int(object o, string key) { return (int)(double)Obj(o)[key]; }
        public static int IntOr(object o, string key, int fallback) { object v; return Obj(o).TryGetValue(key, out v) ? (int)(double)v : fallback; }
        public static string Str(object o, string key) { return (string)Obj(o)[key]; }
    }

    /// <summary>Parser JSON nhỏ (đúng chuẩn RFC 8259 cho các phần cần dùng).</summary>
    internal sealed class MiniJson
    {
        private readonly string _s;
        private int _i;

        private MiniJson(string s) { _s = s; }

        public static object Parse(string s)
        {
            var p = new MiniJson(s);
            p.Ws();
            object v = p.Value();
            p.Ws();
            if (p._i != s.Length) { throw new FormatException("Trailing JSON at " + p._i); }
            return v;
        }

        private void Ws()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) { _i++; }
        }

        private object Value()
        {
            char c = _s[_i];
            if (c == '{') { return ObjectValue(); }
            if (c == '[') { return ArrayValue(); }
            if (c == '"') { return StringValue(); }
            if (Match("true")) { return true; }
            if (Match("false")) { return false; }
            if (Match("null")) { return null; }
            return NumberValue();
        }

        private bool Match(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) == 0) { _i += word.Length; return true; }
            return false;
        }

        private Dictionary<string, object> ObjectValue()
        {
            var d = new Dictionary<string, object>();
            _i++;
            Ws();
            if (_s[_i] == '}') { _i++; return d; }
            while (true)
            {
                Ws();
                string key = StringValue();
                Ws();
                if (_s[_i++] != ':') { throw new FormatException("':' expected at " + (_i - 1)); }
                Ws();
                if (d.ContainsKey(key)) { throw new FormatException("Duplicate key " + key); }
                d[key] = Value();
                Ws();
                char c = _s[_i++];
                if (c == '}') { return d; }
                if (c != ',') { throw new FormatException("',' expected at " + (_i - 1)); }
            }
        }

        private List<object> ArrayValue()
        {
            var list = new List<object>();
            _i++;
            Ws();
            if (_s[_i] == ']') { _i++; return list; }
            while (true)
            {
                Ws();
                list.Add(Value());
                Ws();
                char c = _s[_i++];
                if (c == ']') { return list; }
                if (c != ',') { throw new FormatException("',' expected at " + (_i - 1)); }
            }
        }

        private string StringValue()
        {
            if (_s[_i] != '"') { throw new FormatException("string expected at " + _i); }
            _i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = _s[_i++];
                if (c == '"') { return sb.ToString(); }
                if (c < 0x20) { throw new FormatException("Unescaped control char at " + (_i - 1)); }
                if (c != '\\') { sb.Append(c); continue; }
                char e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                        break;
                    default: throw new FormatException("Bad escape at " + (_i - 1));
                }
            }
        }

        private double NumberValue()
        {
            int start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) { _i++; }
            if (_i == start) { throw new FormatException("Unexpected char '" + _s[start] + "' at " + start); }
            return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
