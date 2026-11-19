using System.Collections.Generic;
using System.Numerics;

namespace PluginSynode.Export
{
    public class ExtractedNode
    {
        public string Name { get; set; }
        public Matrix4x4 LocalTransform = Matrix4x4.Identity;
        public List<ExtractedMesh> Meshes = new List<ExtractedMesh>();
        public List<ExtractedNode> Children = new List<ExtractedNode>();
    }
}
