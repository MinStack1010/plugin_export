using System.Numerics;

namespace PluginSynode.Export
{
    public class ExtractedMaterial
    {
        public enum AlphaType
        {
            Blend,
            Mask,
            Opaque
        }
        
        public string Name { get; set; }
        public Vector4 BaseColour { get; set; } = new Vector4(0.8f, 0.8f, 0.8f, 1.0f);
        public float Metallic { get; set; }
        public float Roughness { get; set; }
        public AlphaType Alpha { get; set; } = AlphaType.Opaque;
    }
}
