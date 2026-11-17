using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Synode.Inventor.Core
{
    /// <summary>Kết quả kiểm tra một file GLB.</summary>
    public sealed class GlbValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        public long FileLength { get; internal set; }
        public uint Version { get; internal set; }
        public uint DeclaredLength { get; internal set; }
        public int JsonChunkLength { get; internal set; }
        public int BinChunkLength { get; internal set; }
        public string? Generator { get; internal set; }
        public int NodeCount { get; internal set; }
        public int MeshCount { get; internal set; }
        public int PrimitiveCount { get; internal set; }
        public int MaterialCount { get; internal set; }
        public int TextureCount { get; internal set; }
        public int AccessorCount { get; internal set; }
        /// <summary>Tổng số tam giác (ước tính từ indices hoặc POSITION với mode TRIANGLES).</summary>
        public long TriangleCount { get; internal set; }
        public long VertexCount { get; internal set; }
        public List<string> ExtensionsUsed { get; } = new List<string>();
        public List<string> ExtensionsRequired { get; } = new List<string>();
        /// <summary>Bao (min/max) của POSITION theo hệ tọa độ local của từng mesh (chỉ để tham khảo).</summary>
        public double[]? PositionMin { get; internal set; }
        public double[]? PositionMax { get; internal set; }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append("GLB v").Append(Version).Append(", ").Append(FileLength).Append(" bytes, ")
              .Append(NodeCount).Append(" nodes, ").Append(MeshCount).Append(" meshes, ")
              .Append(PrimitiveCount).Append(" primitives, ").Append(TriangleCount).Append(" triangles, ")
              .Append(VertexCount).Append(" vertices, ").Append(MaterialCount).Append(" materials, ")
              .Append(TextureCount).Append(" textures");
            if (!string.IsNullOrEmpty(Generator))
            {
                sb.Append(", generator=\"").Append(Generator).Append('"');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Kiểm tra cấu trúc GLB (glTF 2.0 binary) theo đặc tả Khronos:
    /// header 12 byte (magic "glTF", version 2, length), chunk JSON đầu tiên, chunk BIN tùy chọn,
    /// căn lề 4 byte, JSON hợp lệ với asset.version = "2.0", và các tham chiếu
    /// buffer/bufferView/accessor/mesh/node/material nằm trong phạm vi.
    /// Đây KHÔNG phải bản thay thế đầy đủ cho Khronos glTF-Validator.
    /// </summary>
    public static class GlbValidator
    {
        public const uint Magic = 0x46546C67;     // "glTF"
        public const uint ChunkJson = 0x4E4F534A; // "JSON"
        public const uint ChunkBin = 0x004E4942;  // "BIN\0"

        private static readonly Dictionary<string, int> ComponentCounts = new Dictionary<string, int>
        {
            { "SCALAR", 1 }, { "VEC2", 2 }, { "VEC3", 3 }, { "VEC4", 4 }, { "MAT2", 4 }, { "MAT3", 9 }, { "MAT4", 16 }
        };

        public static GlbValidationResult ValidateFile(string path)
        {
            var result = new GlbValidationResult();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                result.Errors.Add("File does not exist: " + path);
                return result;
            }
            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Errors.Add("Cannot read file: " + ex.Message);
                return result;
            }
            return Validate(data, result);
        }

        public static GlbValidationResult Validate(byte[] data)
        {
            return Validate(data, new GlbValidationResult());
        }

        private static GlbValidationResult Validate(byte[] data, GlbValidationResult r)
        {
            r.FileLength = data.LongLength;
            if (data.Length == 0)
            {
                r.Errors.Add("File is empty (0 bytes).");
                return r;
            }
            if (data.Length < 12)
            {
                r.Errors.Add("File is too small for a GLB header (" + data.Length + " bytes).");
                return r;
            }

            uint magic = BitConverter.ToUInt32(data, 0);
            if (magic != Magic)
            {
                string start = Encoding.ASCII.GetString(data, 0, Math.Min(16, data.Length));
                r.Errors.Add(start.TrimStart().StartsWith("{", StringComparison.Ordinal)
                    ? "File is a JSON .gltf, not a binary GLB (magic bytes are not 'glTF')."
                    : "Invalid magic bytes: 0x" + magic.ToString("X8") + " (expected 'glTF').");
                return r;
            }
            r.Version = BitConverter.ToUInt32(data, 4);
            r.DeclaredLength = BitConverter.ToUInt32(data, 8);
            if (r.Version != 2)
            {
                r.Errors.Add("Unsupported GLB version " + r.Version + " (expected 2).");
                return r;
            }
            if (r.DeclaredLength != (ulong)data.LongLength)
            {
                r.Errors.Add("Declared length " + r.DeclaredLength + " does not match file size " + data.LongLength + ".");
                return r;
            }

            // ---- Chunks ----
            int offset = 12;
            byte[]? json = null;
            int binLength = -1;
            int chunkIndex = 0;
            while (offset < data.Length)
            {
                if (data.Length - offset < 8)
                {
                    r.Errors.Add("Truncated chunk header at offset " + offset + ".");
                    return r;
                }
                uint chunkLength = BitConverter.ToUInt32(data, offset);
                uint chunkType = BitConverter.ToUInt32(data, offset + 4);
                long end = (long)offset + 8 + chunkLength;
                if (end > data.Length)
                {
                    r.Errors.Add("Chunk " + chunkIndex + " (length " + chunkLength + ") exceeds file size.");
                    return r;
                }
                if (chunkLength % 4 != 0)
                {
                    r.Errors.Add("Chunk " + chunkIndex + " length " + chunkLength + " is not 4-byte aligned.");
                }
                if (chunkIndex == 0)
                {
                    if (chunkType != ChunkJson)
                    {
                        r.Errors.Add("First chunk is not JSON (type 0x" + chunkType.ToString("X8") + ").");
                        return r;
                    }
                    json = new byte[chunkLength];
                    Buffer.BlockCopy(data, offset + 8, json, 0, (int)chunkLength);
                    r.JsonChunkLength = (int)chunkLength;
                }
                else if (chunkIndex == 1 && chunkType == ChunkBin)
                {
                    binLength = (int)chunkLength;
                    r.BinChunkLength = binLength;
                }
                else if (chunkType == ChunkJson || chunkType == ChunkBin)
                {
                    r.Errors.Add("Unexpected " + (chunkType == ChunkJson ? "JSON" : "BIN") + " chunk at index " + chunkIndex + ".");
                }
                // Các chunk khác: bỏ qua theo đặc tả.
                offset = (int)end;
                chunkIndex++;
            }
            if (json == null)
            {
                r.Errors.Add("Missing JSON chunk.");
                return r;
            }

            // ---- JSON ----
            JsonDocument doc;
            try
            {
                // JSON chunk được đệm bằng khoảng trắng (0x20) - hợp lệ với parser.
                doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false });
            }
            catch (JsonException ex)
            {
                r.Errors.Add("JSON chunk is not valid JSON: " + ex.Message);
                return r;
            }

            using (doc)
            {
                ValidateJson(doc.RootElement, binLength, r);
            }
            return r;
        }

        private static void ValidateJson(JsonElement root, int binLength, GlbValidationResult r)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                r.Errors.Add("JSON root is not an object.");
                return;
            }
            if (!root.TryGetProperty("asset", out JsonElement asset) || asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("version", out JsonElement ver) || ver.ValueKind != JsonValueKind.String)
            {
                r.Errors.Add("Missing asset.version.");
                return;
            }
            if (ver.GetString() != "2.0")
            {
                r.Errors.Add("asset.version is '" + ver.GetString() + "' (expected '2.0').");
            }
            if (asset.TryGetProperty("generator", out JsonElement gen) && gen.ValueKind == JsonValueKind.String)
            {
                r.Generator = gen.GetString();
            }
            ReadStringArray(root, "extensionsUsed", r.ExtensionsUsed);
            ReadStringArray(root, "extensionsRequired", r.ExtensionsRequired);
            foreach (string ext in r.ExtensionsRequired)
            {
                if (!r.ExtensionsUsed.Contains(ext))
                {
                    r.Errors.Add("extensionsRequired contains '" + ext + "' which is not in extensionsUsed.");
                }
                r.Warnings.Add("Required extension '" + ext + "': viewers must support it to display the model.");
            }

            // buffers
            List<long> bufferLengths = new List<long>();
            foreach (JsonElement buffer in Array(root, "buffers"))
            {
                long len = GetLong(buffer, "byteLength", -1);
                bool hasUri = buffer.TryGetProperty("uri", out JsonElement uri);
                int i = bufferLengths.Count;
                if (len < 1)
                {
                    r.Errors.Add("buffers[" + i + "].byteLength is missing or < 1.");
                }
                if (hasUri)
                {
                    string u = uri.GetString() ?? string.Empty;
                    if (!u.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        r.Warnings.Add("buffers[" + i + "] references an external file '" + u + "' (GLB is not self-contained).");
                    }
                }
                else if (i == 0)
                {
                    if (binLength < 0)
                    {
                        r.Errors.Add("buffers[0] has no uri but the GLB has no BIN chunk.");
                    }
                    else if (len > binLength || len < binLength - 3)
                    {
                        r.Errors.Add("buffers[0].byteLength " + len + " does not match BIN chunk length " + binLength + ".");
                    }
                }
                else
                {
                    r.Errors.Add("buffers[" + i + "] has no uri (only buffers[0] may use the BIN chunk).");
                }
                bufferLengths.Add(len);
            }

            // bufferViews
            var viewInfo = new List<(long length, long stride)>();
            int vi = 0;
            foreach (JsonElement view in Array(root, "bufferViews"))
            {
                int buffer = (int)GetLong(view, "buffer", -1);
                long off = GetLong(view, "byteOffset", 0);
                long len = GetLong(view, "byteLength", -1);
                long stride = GetLong(view, "byteStride", 0);
                if (buffer < 0 || buffer >= bufferLengths.Count)
                {
                    r.Errors.Add("bufferViews[" + vi + "].buffer " + buffer + " is out of range.");
                }
                else if (len < 1 || off < 0 || off + len > bufferLengths[buffer])
                {
                    r.Errors.Add("bufferViews[" + vi + "] range [" + off + ", " + (off + len) + ") exceeds buffer " + buffer + ".");
                }
                viewInfo.Add((len, stride));
                vi++;
            }

            // accessors
            var accessorCounts = new List<long>();
            int ai = 0;
            foreach (JsonElement acc in Array(root, "accessors"))
            {
                long count = GetLong(acc, "count", -1);
                int componentType = (int)GetLong(acc, "componentType", -1);
                string type = acc.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
                int componentSize = ComponentSize(componentType);
                if (count < 1)
                {
                    r.Errors.Add("accessors[" + ai + "].count is missing or < 1.");
                }
                if (componentSize == 0)
                {
                    r.Errors.Add("accessors[" + ai + "].componentType " + componentType + " is invalid.");
                }
                if (!ComponentCounts.TryGetValue(type, out int components))
                {
                    r.Errors.Add("accessors[" + ai + "].type '" + type + "' is invalid.");
                }
                if (acc.TryGetProperty("bufferView", out JsonElement bvEl))
                {
                    int bv = bvEl.GetInt32();
                    if (bv < 0 || bv >= viewInfo.Count)
                    {
                        r.Errors.Add("accessors[" + ai + "].bufferView " + bv + " is out of range.");
                    }
                    else if (count > 0 && componentSize > 0 && components > 0)
                    {
                        long elementSize = componentSize * components;
                        long stride = viewInfo[bv].stride > 0 ? viewInfo[bv].stride : elementSize;
                        long needed = GetLong(acc, "byteOffset", 0) + stride * (count - 1) + elementSize;
                        if (needed > viewInfo[bv].length)
                        {
                            r.Errors.Add("accessors[" + ai + "] needs " + needed + " bytes but bufferViews[" + bv + "] has " + viewInfo[bv].length + ".");
                        }
                    }
                }
                accessorCounts.Add(count);
                ai++;
            }
            r.AccessorCount = accessorCounts.Count;

            int materialCount = Count(root, "materials");
            r.MaterialCount = materialCount;
            r.TextureCount = Count(root, "textures");

            // meshes
            int meshCount = 0;
            double[] min = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] max = { double.MinValue, double.MinValue, double.MinValue };
            bool anyBounds = false;
            foreach (JsonElement mesh in Array(root, "meshes"))
            {
                int pi = 0;
                foreach (JsonElement prim in Array(mesh, "primitives"))
                {
                    r.PrimitiveCount++;
                    string where = "meshes[" + meshCount + "].primitives[" + pi + "]";
                    if (!prim.TryGetProperty("attributes", out JsonElement attrs) || attrs.ValueKind != JsonValueKind.Object)
                    {
                        r.Errors.Add(where + " has no attributes.");
                        pi++;
                        continue;
                    }
                    long positionCount = 0;
                    if (attrs.TryGetProperty("POSITION", out JsonElement posEl))
                    {
                        int pos = posEl.GetInt32();
                        if (pos < 0 || pos >= accessorCounts.Count)
                        {
                            r.Errors.Add(where + ".attributes.POSITION is out of range.");
                        }
                        else
                        {
                            positionCount = accessorCounts[pos];
                            r.VertexCount += positionCount;
                            if (ReadBounds(root, pos, out double[]? pmin, out double[]? pmax) && pmin != null && pmax != null)
                            {
                                anyBounds = true;
                                for (int k = 0; k < 3; k++)
                                {
                                    min[k] = Math.Min(min[k], pmin[k]);
                                    max[k] = Math.Max(max[k], pmax[k]);
                                }
                            }
                        }
                    }
                    else if (!HasExtension(prim, "KHR_draco_mesh_compression"))
                    {
                        r.Errors.Add(where + " has no POSITION attribute.");
                    }
                    foreach (JsonProperty attr in attrs.EnumerateObject())
                    {
                        int idx = attr.Value.GetInt32();
                        if (idx < 0 || idx >= accessorCounts.Count)
                        {
                            r.Errors.Add(where + ".attributes." + attr.Name + " is out of range.");
                        }
                    }
                    int mode = (int)GetLong(prim, "mode", 4);
                    long indexCount = positionCount;
                    if (prim.TryGetProperty("indices", out JsonElement idxEl))
                    {
                        int ind = idxEl.GetInt32();
                        if (ind < 0 || ind >= accessorCounts.Count)
                        {
                            r.Errors.Add(where + ".indices is out of range.");
                            indexCount = 0;
                        }
                        else
                        {
                            indexCount = accessorCounts[ind];
                        }
                    }
                    if (mode == 4)
                    {
                        r.TriangleCount += indexCount / 3;
                    }
                    if (prim.TryGetProperty("material", out JsonElement matEl))
                    {
                        int mat = matEl.GetInt32();
                        if (mat < 0 || mat >= materialCount)
                        {
                            r.Errors.Add(where + ".material " + mat + " is out of range.");
                        }
                    }
                    pi++;
                }
                if (pi == 0)
                {
                    r.Errors.Add("meshes[" + meshCount + "] has no primitives.");
                }
                meshCount++;
            }
            r.MeshCount = meshCount;
            if (anyBounds)
            {
                r.PositionMin = min;
                r.PositionMax = max;
            }

            // nodes
            int nodeCount = Count(root, "nodes");
            r.NodeCount = nodeCount;
            int ni = 0;
            var parentOf = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++) parentOf[i] = -1;
            foreach (JsonElement node in Array(root, "nodes"))
            {
                if (node.TryGetProperty("mesh", out JsonElement m))
                {
                    int mi = m.GetInt32();
                    if (mi < 0 || mi >= meshCount)
                    {
                        r.Errors.Add("nodes[" + ni + "].mesh " + mi + " is out of range.");
                    }
                }
                foreach (JsonElement c in Array(node, "children"))
                {
                    int ci = c.GetInt32();
                    if (ci < 0 || ci >= nodeCount)
                    {
                        r.Errors.Add("nodes[" + ni + "].children contains invalid index " + ci + ".");
                    }
                    else if (parentOf[ci] >= 0 || ci == ni)
                    {
                        r.Errors.Add("nodes[" + ci + "] has more than one parent (or is its own child).");
                    }
                    else
                    {
                        parentOf[ci] = ni;
                    }
                }
                if (node.TryGetProperty("matrix", out JsonElement mat) && mat.GetArrayLength() != 16)
                {
                    r.Errors.Add("nodes[" + ni + "].matrix does not have 16 elements.");
                }
                ni++;
            }
            foreach (JsonElement scene in Array(root, "scenes"))
            {
                foreach (JsonElement n in Array(scene, "nodes"))
                {
                    int idx = n.GetInt32();
                    if (idx < 0 || idx >= nodeCount)
                    {
                        r.Errors.Add("scene references invalid node " + idx + ".");
                    }
                }
            }

            if (meshCount == 0 || r.PrimitiveCount == 0)
            {
                r.Errors.Add("GLB contains no mesh geometry.");
            }
            else if (r.TriangleCount == 0 && r.VertexCount == 0 && r.ExtensionsUsed.Count == 0)
            {
                r.Errors.Add("GLB meshes contain no vertices.");
            }
        }

        private static bool ReadBounds(JsonElement root, int accessor, out double[]? min, out double[]? max)
        {
            min = null;
            max = null;
            int i = 0;
            foreach (JsonElement acc in Array(root, "accessors"))
            {
                if (i++ != accessor)
                {
                    continue;
                }
                if (acc.TryGetProperty("min", out JsonElement mn) && acc.TryGetProperty("max", out JsonElement mx)
                    && mn.GetArrayLength() >= 3 && mx.GetArrayLength() >= 3)
                {
                    min = new[] { mn[0].GetDouble(), mn[1].GetDouble(), mn[2].GetDouble() };
                    max = new[] { mx[0].GetDouble(), mx[1].GetDouble(), mx[2].GetDouble() };
                    return true;
                }
                return false;
            }
            return false;
        }

        private static bool HasExtension(JsonElement el, string name)
        {
            return el.TryGetProperty("extensions", out JsonElement ext) && ext.ValueKind == JsonValueKind.Object
                && ext.TryGetProperty(name, out _);
        }

        private static int ComponentSize(int componentType)
        {
            switch (componentType)
            {
                case 5120: case 5121: return 1;
                case 5122: case 5123: return 2;
                case 5125: case 5126: return 4;
                default: return 0;
            }
        }

        private static IEnumerable<JsonElement> Array(JsonElement parent, string name)
        {
            if (parent.TryGetProperty(name, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in arr.EnumerateArray())
                {
                    yield return e;
                }
            }
        }

        private static int Count(JsonElement parent, string name)
        {
            return parent.TryGetProperty(name, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array ? arr.GetArrayLength() : 0;
        }

        private static long GetLong(JsonElement el, string name, long fallback)
        {
            return el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long x) ? x : fallback;
        }

        private static void ReadStringArray(JsonElement root, string name, List<string> target)
        {
            foreach (JsonElement e in Array(root, name))
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    target.Add(e.GetString() ?? string.Empty);
                }
            }
        }
    }
}
