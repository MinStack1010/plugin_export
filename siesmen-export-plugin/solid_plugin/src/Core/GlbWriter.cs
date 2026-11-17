using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SynodeExporter.Core
{
    /// <summary>
    /// Bộ ghi GLB (glTF 2.0 nhị phân) không phụ thuộc thư viện ngoài.
    ///
    /// Plugin SOLIDWORKS dùng SharpGLTF 1.0.6 (SceneBuilder). SharpGLTF kéo theo
    /// System.Text.Json/System.Memory... dễ xung đột binding khi nạp vào Edge.exe,
    /// nên add-in Solid Edge tái hiện cấu trúc tài liệu mà SharpGLTF sinh ra
    /// (đối chiếu từng byte bằng tests\SynodeExporter.Tests với mã nguồn SW gốc):
    /// * 1 scene, node gốc = root; ma trận đơn vị bị bỏ, transform khác đơn vị ghi "matrix";
    /// * tên null -> "Root"/"Component"/"Mesh"/"Material";
    /// * mỗi ExtractedMesh -> 1 mesh glTF, 1 primitive tam giác; mesh đầu tiên gắn vào
    ///   node, các mesh tiếp theo gắn vào node con không tên (thêm sau toàn bộ cây);
    /// * đỉnh được gộp theo (position, normal) như PrimitiveBuilder.AddTriangle;
    /// * POSITION/NORMAL float32 xen kẽ trong 1 bufferView ARRAY_BUFFER (stride 24),
    ///   indices trong 1 bufferView ELEMENT_ARRAY_BUFFER, uint16 cho cả tài liệu hoặc
    ///   uint32 cho tất cả khi có mesh cần; khối dữ liệu xếp theo kích thước giảm dần;
    /// * vật liệu pbrMetallicRoughness, doubleSided, alphaMode khi khác OPAQUE; trùng nội dung thì dùng chung.
    /// </summary>
    public static class GlbWriter
    {
        public const string Generator = "Synode Exporter for Solid Edge 1.0.0";

        private const uint GlbMagic = 0x46546C67;
        private const uint ChunkJson = 0x4E4F534A;
        private const uint ChunkBin = 0x004E4942;
        private const int ArrayBuffer = 34962;
        private const int ElementArrayBuffer = 34963;
        private const int FloatType = 5126;
        private const int UnsignedShort = 5123;
        private const int UnsignedInt = 5125;

        /// <summary>
        /// Cách thoát chuỗi JSON. Đối chiếu với SharpGLTF 1.0.6 (tests): ký tự ngoài ASCII
        /// (tiếng Việt...) được ghi nguyên dạng UTF-8 -> chế độ Relaxed.
        /// </summary>
        public static JsonEscaping Escaping = JsonEscaping.Relaxed;

        public static void WriteGlb(ExtractedScene scene, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("The output path is empty.", "outputPath");
            }
            byte[] data = BuildGlb(scene);
            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                throw new ExportException("The output folder does not exist: " + directory);
            }
            // Ghi ra file tạm rồi thay thế để không để lại file GLB hỏng khi lỗi.
            string temp = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temp, data);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                File.Move(temp, outputPath);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        public static byte[] BuildGlb(ExtractedScene scene)
        {
            byte[] binary;
            JsonObject doc = BuildGltf(scene, out binary);
            return Encode(doc, binary);
        }

        public static JsonObject BuildGltf(ExtractedScene scene, out byte[] binary)
        {
            if (scene == null)
            {
                throw new ArgumentNullException("scene");
            }
            if (scene.Root == null)
            {
                throw new ExportException("The scene contains no root node.");
            }

            var b = new Builder();
            b.AddNode(scene.Root, "Root");
            b.FinishExtraNodes();
            b.PackBuffers();

            int vertexLength = b.VertexBytes.Length;
            binary = new byte[vertexLength + b.IndexBytes.Length];
            Buffer.BlockCopy(b.VertexBytes, 0, binary, 0, vertexLength);
            Buffer.BlockCopy(b.IndexBytes, 0, binary, vertexLength, b.IndexBytes.Length);

            var doc = new JsonObject();
            doc.Set("asset", new JsonObject().Set("copyright", "").Set("generator", Generator).Set("version", "2.0"));
            if (b.Meshes.Count > 0)
            {
                doc.Set("accessors", b.Accessors);
                var views = new List<object>
                {
                    new JsonObject().Set("buffer", 0).Set("byteLength", vertexLength).Set("byteStride", 24).Set("target", ArrayBuffer),
                    new JsonObject().Set("buffer", 0).Set("byteLength", b.IndexBytes.Length).Set("byteOffset", vertexLength).Set("target", ElementArrayBuffer)
                };
                doc.Set("bufferViews", views);
                doc.Set("buffers", new List<object> { new JsonObject().Set("byteLength", Pad4(binary.Length)) });
            }
            if (b.Materials.Count > 0)
            {
                doc.Set("materials", b.Materials);
            }
            if (b.Meshes.Count > 0)
            {
                doc.Set("meshes", b.Meshes);
            }
            doc.Set("nodes", b.Nodes.Select(n => (object)n.ToJson()).ToList());
            doc.Set("scene", 0);
            doc.Set("scenes", new List<object> { new JsonObject().Set("nodes", new List<object> { 0 }) });
            return doc;
        }

        public static byte[] Encode(JsonObject doc, byte[] binary)
        {
            byte[] json = new UTF8Encoding(false).GetBytes(JsonText.Serialize(doc, Escaping));
            int jsonLength = Pad4(json.Length);
            int binLength = Pad4(binary.Length);
            int total = 12 + 8 + jsonLength + (binLength > 0 ? 8 + binLength : 0);
            using (var stream = new MemoryStream(total))
            using (var w = new BinaryWriter(stream))
            {
                w.Write(GlbMagic);
                w.Write(2u);
                w.Write((uint)total);
                w.Write((uint)jsonLength);
                w.Write(ChunkJson);
                w.Write(json);
                for (int i = json.Length; i < jsonLength; i++)
                {
                    w.Write((byte)0x20);
                }
                if (binLength > 0)
                {
                    w.Write((uint)binLength);
                    w.Write(ChunkBin);
                    w.Write(binary);
                    for (int i = binary.Length; i < binLength; i++)
                    {
                        w.Write((byte)0);
                    }
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        private static int Pad4(int length)
        {
            return length + ((4 - length % 4) % 4);
        }

        private sealed class NodeDoc
        {
            public string Name;
            public bool HasName;
            public List<int> Children;
            public float[] Matrix;
            public int? Mesh;

            public JsonObject ToJson()
            {
                // Thứ tự thuộc tính như SharpGLTF: name, children, matrix, mesh.
                var o = new JsonObject();
                if (HasName)
                {
                    o.Set("name", Name);
                }
                if (Children != null && Children.Count > 0)
                {
                    o.Set("children", Children.Cast<object>().ToList());
                }
                if (Matrix != null)
                {
                    o.Set("matrix", Matrix.Select(v => (object)new JsonNumber(JsonText.FormatFloat(v))).ToList());
                }
                if (Mesh.HasValue)
                {
                    o.Set("mesh", Mesh.Value);
                }
                return o;
            }
        }

        private sealed class VertexBlock
        {
            public byte[] Data;
            public JsonObject Position;
            public JsonObject Normal;
        }

        private sealed class IndexBlock
        {
            public List<int> Indices;
            public int MaxIndex;
            public JsonObject Accessor;
        }

        private sealed class Builder
        {
            public readonly List<NodeDoc> Nodes = new List<NodeDoc>();
            public readonly List<object> Meshes = new List<object>();
            public readonly List<object> Materials = new List<object>();
            public readonly List<object> Accessors = new List<object>();
            public byte[] VertexBytes = new byte[0];
            public byte[] IndexBytes = new byte[0];

            private readonly Dictionary<string, int> _materialIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<VertexBlock> _vertexBlocks = new List<VertexBlock>();
            private readonly List<IndexBlock> _indexBlocks = new List<IndexBlock>();
            private readonly List<KeyValuePair<int, int>> _extraMeshNodes = new List<KeyValuePair<int, int>>();

            public int AddNode(ExtractedNode node, string defaultName)
            {
                int index = Nodes.Count;
                var doc = new NodeDoc { Name = node.Name ?? defaultName, HasName = true };
                Nodes.Add(doc);

                // SharpGLTF (AddNodeRecursive) thêm mesh của node trước khi duyệt node con.
                bool first = true;
                foreach (ExtractedMesh mesh in node.Meshes)
                {
                    int? meshIndex = AddMesh(mesh);
                    if (!meshIndex.HasValue)
                    {
                        continue;
                    }
                    if (first)
                    {
                        doc.Mesh = meshIndex.Value;
                        first = false;
                    }
                    else
                    {
                        _extraMeshNodes.Add(new KeyValuePair<int, int>(index, meshIndex.Value));
                    }
                }

                var children = new List<int>();
                foreach (ExtractedNode child in node.Children)
                {
                    children.Add(AddNode(child, "Component"));
                }
                if (children.Count > 0)
                {
                    doc.Children = children;
                }

                float[] transform = node.LocalTransform;
                if (!Mat4.IsFinite(transform))
                {
                    throw new ExportException("Node '" + doc.Name + "' has an invalid transform.");
                }
                if (!Mat4.IsIdentity(transform))
                {
                    doc.Matrix = (float[])transform.Clone();
                }
                return index;
            }

            public void FinishExtraNodes()
            {
                foreach (var pair in _extraMeshNodes)
                {
                    int child = Nodes.Count;
                    Nodes.Add(new NodeDoc { Mesh = pair.Value });
                    NodeDoc parent = Nodes[pair.Key];
                    if (parent.Children == null)
                    {
                        parent.Children = new List<int>();
                    }
                    parent.Children.Add(child);
                }
            }

            private int? AddMesh(ExtractedMesh mesh)
            {
                if (mesh == null || mesh.VertexCount == 0 || mesh.TriangleCount == 0)
                {
                    return null;
                }
                if (mesh.Normals.Count != mesh.Positions.Count)
                {
                    throw new ExportException("Mesh '" + mesh.Name + "' has mismatched normal count.");
                }

                // PrimitiveBuilder.AddTriangle: gộp đỉnh trùng, bỏ tam giác suy biến.
                var positions = new List<Vec3>();
                var normals = new List<Vec3>();
                var indices = new List<int>();
                var map = new Dictionary<VertexKey, int>(VertexKeyComparer.Instance);
                int count = mesh.VertexCount;
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                    if (a < 0 || a >= count || b < 0 || b >= count || c < 0 || c >= count)
                    {
                        throw new ExportException("Mesh '" + mesh.Name + "' has an out of range index.");
                    }
                    int ia = Vertex(map, positions, normals, mesh, a);
                    int ib = Vertex(map, positions, normals, mesh, b);
                    int ic = Vertex(map, positions, normals, mesh, c);
                    if (ia == ib || ib == ic || ia == ic)
                    {
                        continue;
                    }
                    indices.Add(ia);
                    indices.Add(ib);
                    indices.Add(ic);
                }
                if (indices.Count == 0)
                {
                    return null;
                }
                // Đỉnh chỉ được thêm qua tam giác hợp lệ: loại các đỉnh của tam giác bị bỏ.
                Compact(positions, normals, indices);

                int vertexCount = positions.Count;
                var data = new byte[vertexCount * 24];
                for (int i = 0; i < vertexCount; i++)
                {
                    WriteFloat(data, i * 24, positions[i].X);
                    WriteFloat(data, i * 24 + 4, positions[i].Y);
                    WriteFloat(data, i * 24 + 8, positions[i].Z);
                    WriteFloat(data, i * 24 + 12, normals[i].X);
                    WriteFloat(data, i * 24 + 16, normals[i].Y);
                    WriteFloat(data, i * 24 + 20, normals[i].Z);
                }

                JsonObject position = Accessor("POSITION", 0, FloatType, vertexCount, positions);
                JsonObject normal = Accessor("NORMAL", 12, FloatType, vertexCount, normals);
                var indexAccessor = new JsonObject()
                    .Set("bufferView", 1).Set("byteOffset", 0).Set("componentType", UnsignedShort)
                    .Set("count", indices.Count).Set("type", "SCALAR");
                int positionIndex = Add(position);
                int normalIndex = Add(normal);
                int indexIndex = Add(indexAccessor);

                _vertexBlocks.Add(new VertexBlock { Data = data, Position = position, Normal = normal });
                _indexBlocks.Add(new IndexBlock { Indices = indices, MaxIndex = indices.Max(), Accessor = indexAccessor });

                var primitive = new JsonObject()
                    .Set("attributes", new JsonObject().Set("POSITION", positionIndex).Set("NORMAL", normalIndex))
                    .Set("indices", indexIndex)
                    .Set("material", Material(mesh.Material));
                int meshIndex = Meshes.Count;
                Meshes.Add(new JsonObject().Set("name", mesh.Name ?? "Mesh").Set("primitives", new List<object> { primitive }));
                return meshIndex;
            }

            private static int Vertex(Dictionary<VertexKey, int> map, List<Vec3> positions, List<Vec3> normals, ExtractedMesh mesh, int source)
            {
                var key = new VertexKey(mesh.Positions[source], mesh.Normals[source]);
                if (!key.P.IsFinite() || !key.N.IsFinite())
                {
                    throw new ExportException("Mesh '" + mesh.Name + "' has a non finite vertex.");
                }
                int index;
                if (!map.TryGetValue(key, out index))
                {
                    index = positions.Count;
                    map.Add(key, index);
                    positions.Add(key.P);
                    normals.Add(key.N);
                }
                return index;
            }

            private static void Compact(List<Vec3> positions, List<Vec3> normals, List<int> indices)
            {
                var remap = new int[positions.Count];
                for (int i = 0; i < remap.Length; i++)
                {
                    remap[i] = -1;
                }
                var newPositions = new List<Vec3>(positions.Count);
                var newNormals = new List<Vec3>(positions.Count);
                for (int i = 0; i < indices.Count; i++)
                {
                    int old = indices[i];
                    if (remap[old] < 0)
                    {
                        remap[old] = newPositions.Count;
                        newPositions.Add(positions[old]);
                        newNormals.Add(normals[old]);
                    }
                    indices[i] = remap[old];
                }
                if (newPositions.Count == positions.Count)
                {
                    return;
                }
                positions.Clear();
                positions.AddRange(newPositions);
                normals.Clear();
                normals.AddRange(newNormals);
            }

            private static JsonObject Accessor(string name, int offset, int componentType, int count, List<Vec3> values)
            {
                double[] max = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
                double[] min = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
                foreach (Vec3 v in values)
                {
                    Bounds(v.X, 0, max, min);
                    Bounds(v.Y, 1, max, min);
                    Bounds(v.Z, 2, max, min);
                }
                return new JsonObject()
                    .Set("name", name).Set("bufferView", 0).Set("byteOffset", offset).Set("componentType", componentType)
                    .Set("count", count)
                    .Set("max", max.Select(x => (object)new JsonNumber(JsonText.FormatDouble(x))).ToList())
                    .Set("min", min.Select(x => (object)new JsonNumber(JsonText.FormatDouble(x))).ToList())
                    .Set("type", "VEC3");
            }

            private static void Bounds(float value, int axis, double[] max, double[] min)
            {
                if (value > max[axis])
                {
                    max[axis] = value;
                }
                if (value < min[axis])
                {
                    min[axis] = value;
                }
            }

            private int Add(JsonObject accessor)
            {
                Accessors.Add(accessor);
                return Accessors.Count - 1;
            }

            private int Material(ExtractedMaterial material)
            {
                if (material == null)
                {
                    material = new ExtractedMaterial(); // SW: new ExtractedMaterial() -> xám 0.8, tên "Material"
                }
                var doc = new JsonObject();
                doc.Set("name", material.Name ?? "Material");
                if (material.Alpha != AlphaType.Opaque)
                {
                    doc.Set("alphaMode", material.Alpha == AlphaType.Blend ? "BLEND" : "MASK");
                }
                doc.Set("doubleSided", true);
                Vec4 c = material.BaseColour;
                // SharpGLTF bỏ các giá trị bằng mặc định của glTF (màu 1,1,1,1; metallic 1; roughness 1)
                // nhưng vẫn giữ đối tượng pbrMetallicRoughness (có thể rỗng) - đã đối chiếu bằng test.
                var pbr = new JsonObject();
                if (!(c.X == 1f && c.Y == 1f && c.Z == 1f && c.W == 1f))
                {
                    pbr.Set("baseColorFactor", new List<object> { F(c.X), F(c.Y), F(c.Z), F(c.W) });
                }
                if (material.Metallic != 1f)
                {
                    pbr.Set("metallicFactor", F(material.Metallic));
                }
                if (material.Roughness != 1f)
                {
                    pbr.Set("roughnessFactor", F(material.Roughness));
                }
                doc.Set("pbrMetallicRoughness", pbr);

                string key = JsonText.Serialize(doc, JsonEscaping.Relaxed);
                int index;
                if (!_materialIndex.TryGetValue(key, out index))
                {
                    index = Materials.Count;
                    Materials.Add(doc);
                    _materialIndex.Add(key, index);
                }
                return index;
            }

            private static JsonNumber F(float v)
            {
                return new JsonNumber(JsonText.FormatFloat(v));
            }

            /// <summary>
            /// Bố cục buffer giống SharpGLTF 1.0.6: một kiểu index cho cả tài liệu
            /// (uint32 khi có mesh cần), khối dữ liệu của mỗi bufferView xếp theo kích
            /// thước giảm dần (ổn định với kích thước bằng nhau). Thứ tự accessor giữ
            /// theo mesh; chỉ byteOffset theo bố cục đã xếp.
            /// </summary>
            public void PackBuffers()
            {
                bool wide = _indexBlocks.Any(block => block.MaxIndex >= 65535);
                int component = wide ? UnsignedInt : UnsignedShort;
                int indexSize = wide ? 4 : 2;

                var vertexOrder = _vertexBlocks.Select((block, i) => new { block, i })
                    .OrderByDescending(x => x.block.Data.Length).ThenBy(x => x.i).Select(x => x.block).ToList();
                var vertexStream = new MemoryStream();
                foreach (VertexBlock block in vertexOrder)
                {
                    int offset = (int)vertexStream.Length;
                    SetOffset(block.Position, offset);
                    SetOffset(block.Normal, offset + 12);
                    vertexStream.Write(block.Data, 0, block.Data.Length);
                }
                VertexBytes = vertexStream.ToArray();

                var indexOrder = _indexBlocks.Select((block, i) => new { block, i })
                    .OrderByDescending(x => x.block.Indices.Count).ThenBy(x => x.i).Select(x => x.block).ToList();
                var indexStream = new MemoryStream();
                foreach (IndexBlock block in indexOrder)
                {
                    SetOffset(block.Accessor, (int)indexStream.Length);
                    block.Accessor.Set("componentType", component);
                    var bytes = new byte[block.Indices.Count * indexSize];
                    for (int i = 0; i < block.Indices.Count; i++)
                    {
                        int value = block.Indices[i];
                        if (wide)
                        {
                            bytes[i * 4] = (byte)value;
                            bytes[i * 4 + 1] = (byte)(value >> 8);
                            bytes[i * 4 + 2] = (byte)(value >> 16);
                            bytes[i * 4 + 3] = (byte)(value >> 24);
                        }
                        else
                        {
                            bytes[i * 2] = (byte)value;
                            bytes[i * 2 + 1] = (byte)(value >> 8);
                        }
                    }
                    indexStream.Write(bytes, 0, bytes.Length);
                }
                IndexBytes = indexStream.ToArray();
            }

            private static void SetOffset(JsonObject accessor, int offset)
            {
                if (offset != 0)
                {
                    accessor.Set("byteOffset", offset); // khóa giữ nguyên vị trí
                }
                else
                {
                    accessor.Remove("byteOffset");
                }
            }

            private static void WriteFloat(byte[] buffer, int offset, float value)
            {
                byte[] bytes = BitConverter.GetBytes(value);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }
                Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
            }
        }
    }
}
