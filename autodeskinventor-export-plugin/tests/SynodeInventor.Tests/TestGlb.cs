using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Synode.Inventor.Tests
{
    /// <summary>Dựng file GLB tối thiểu (thật, đúng đặc tả) để kiểm thử validator.</summary>
    internal static class TestGlb
    {
        public const string TriangleJson =
            "{\"asset\":{\"version\":\"2.0\",\"generator\":\"synode-test\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}]," +
            "\"nodes\":[{\"name\":\"root\",\"children\":[1]},{\"name\":\"child\",\"mesh\":0}]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"indices\":1,\"material\":0}]}]," +
            "\"materials\":[{\"name\":\"m\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.8,0.8,0.8,1]}}]," +
            "\"buffers\":[{\"byteLength\":44}]," +
            "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":6}]," +
            "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]}," +
            "{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}]}";

        public static byte[] TriangleBin()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            float[] pos = { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
            foreach (float f in pos) w.Write(f);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2);
            w.Write((ushort)0); // padding to 44 bytes
            w.Flush();
            return ms.ToArray();
        }

        public static byte[] Triangle() => Build(TriangleJson, TriangleBin());

        /// <summary>Ghép header + chunk JSON (đệm 0x20) + chunk BIN (đệm 0x00).</summary>
        public static byte[] Build(string json, byte[]? bin, uint version = 2, bool padJson = true)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            var chunks = new List<byte>();
            byte[] jsonChunk = padJson ? Pad(jsonBytes, 0x20) : jsonBytes;
            chunks.AddRange(BitConverter.GetBytes((uint)jsonChunk.Length));
            chunks.AddRange(BitConverter.GetBytes(0x4E4F534Au));
            chunks.AddRange(jsonChunk);
            if (bin != null)
            {
                byte[] binChunk = Pad(bin, 0);
                chunks.AddRange(BitConverter.GetBytes((uint)binChunk.Length));
                chunks.AddRange(BitConverter.GetBytes(0x004E4942u));
                chunks.AddRange(binChunk);
            }
            var all = new List<byte>();
            all.AddRange(BitConverter.GetBytes(0x46546C67u));
            all.AddRange(BitConverter.GetBytes(version));
            all.AddRange(BitConverter.GetBytes((uint)(12 + chunks.Count)));
            all.AddRange(chunks);
            return all.ToArray();
        }

        private static byte[] Pad(byte[] data, byte fill)
        {
            int padded = (data.Length + 3) & ~3;
            var result = new byte[padded];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            for (int i = data.Length; i < padded; i++) result[i] = fill;
            return result;
        }
    }
}
