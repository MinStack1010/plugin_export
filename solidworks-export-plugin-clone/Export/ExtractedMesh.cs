using System.Collections.Generic;
using System.Numerics;

namespace PluginSynode.Export
{
    public class ExtractedMesh
    {
        public string Name { get; set; }

        public List<Vector3> Positions { get; } = new List<Vector3>();
        public List<Vector3> Normals { get; } = new List<Vector3>();
        public List<int> Indices { get; } = new List<int>();

        public ExtractedMaterial Material { get; set; }

        public int VertexCount => Positions.Count;
        public int TriangleCount => Indices.Count / 3;
    }
}
