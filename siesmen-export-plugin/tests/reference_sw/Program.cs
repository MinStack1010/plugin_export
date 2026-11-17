using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using PluginSynode.Export;

namespace ReferenceSw
{
    /// <summary>
    /// Usage: ReferenceSw &lt;scene.json&gt; &lt;output.glb&gt;
    /// The JSON scene is produced by tests/tools/scene_json.py from the same
    /// synthetic scenes used by the NX writer tests.  Positions are metres,
    /// matrices are 16 floats in System.Numerics memory order (M11..M44).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: ReferenceSw <scene.json> <output.glb>");
                return 2;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(args[0]));
            var scene = new ExtractedScene { Root = ReadNode(doc.RootElement.GetProperty("root")) };

            // Same pipeline as ExportGlbCommand: converter (UnitScale = 1) then writer.
            var options = new ExportOptions { UnitScale = 1f };
            ExtractedScene converted = CoordinateConverter.ConvertSolidWorksSceneToGltf(scene, options);
            GltfSceneWriter.WriteGlb(converted, args[1]);
            Console.WriteLine("Reference GLB written: " + args[1]);
            return 0;
        }

        private static ExtractedNode ReadNode(JsonElement e)
        {
            var node = new ExtractedNode { Name = ReadString(e, "name") };
            if (e.TryGetProperty("matrix", out JsonElement m))
            {
                float[] v = new float[16];
                int i = 0;
                foreach (JsonElement x in m.EnumerateArray()) v[i++] = x.GetSingle();
                node.LocalTransform = new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7],
                                                    v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
            }
            foreach (JsonElement me in e.GetProperty("meshes").EnumerateArray()) node.Meshes.Add(ReadMesh(me));
            foreach (JsonElement ce in e.GetProperty("children").EnumerateArray()) node.Children.Add(ReadNode(ce));
            return node;
        }

        private static ExtractedMesh ReadMesh(JsonElement e)
        {
            var mesh = new ExtractedMesh { Name = ReadString(e, "name") };
            foreach (JsonElement p in e.GetProperty("positions").EnumerateArray()) mesh.Positions.Add(ReadVec3(p));
            foreach (JsonElement n in e.GetProperty("normals").EnumerateArray()) mesh.Normals.Add(ReadVec3(n));
            foreach (JsonElement i in e.GetProperty("indices").EnumerateArray()) mesh.Indices.Add(i.GetInt32());
            if (e.TryGetProperty("material", out JsonElement mat) && mat.ValueKind == JsonValueKind.Object)
            {
                var c = mat.GetProperty("baseColor");
                mesh.Material = new ExtractedMaterial
                {
                    Name = ReadString(mat, "name"),
                    BaseColour = new Vector4(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle(), c[3].GetSingle()),
                    Metallic = mat.GetProperty("metallic").GetSingle(),
                    Roughness = mat.GetProperty("roughness").GetSingle(),
                    Alpha = mat.GetProperty("alphaMode").GetString() switch
                    {
                        "BLEND" => ExtractedMaterial.AlphaType.Blend,
                        "MASK" => ExtractedMaterial.AlphaType.Mask,
                        _ => ExtractedMaterial.AlphaType.Opaque
                    }
                };
            }
            return mesh;
        }

        private static Vector3 ReadVec3(JsonElement e) => new Vector3(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

        private static string ReadString(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
