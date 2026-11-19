using System;
using System.Numerics;

namespace PluginSynode.Export
{
    public static class CoordinateConverter
    {   
        public static ExtractedScene ConvertSolidWorksSceneToGltf(ExtractedScene source, ExportOptions options)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (source.Root == null)
            {
                throw new InvalidOperationException("The extracted scene has not root node.");
            }

            if (options.UnitScale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options.UnitScale));
            }

            return new ExtractedScene
            {
                Root = ConvertNode(source.Root, options.UnitScale)
            };
        }

        private static ExtractedNode ConvertNode(ExtractedNode source, float scale)
        {
            Matrix4x4 transform = source.LocalTransform;

            ExtractedNode convertedNode = new ExtractedNode
            {
                Name = source.Name,
                LocalTransform = transform
            };

            foreach (ExtractedMesh mesh in source.Meshes)
            {
                convertedNode.Meshes.Add(ConvertMesh(mesh, scale));
            }

            foreach (ExtractedNode child in source.Children)
            {
                convertedNode.Children.Add(ConvertNode(child, scale));
            }

            return convertedNode;
        }

        private static ExtractedMesh ConvertMesh(ExtractedMesh source, float scale)
        {
            ExtractedMesh convertedMesh = new ExtractedMesh
            {
                Name = source.Name,
                Material = source.Material
            };

            for (int i = 0; i < source.Positions.Count; i++)
            {
                Vector3 position = source.Positions[i];
                convertedMesh.Positions.Add(
                    new Vector3(
                        position.X * scale,
                        position.Y * scale,
                        position.Z * scale
                    )
                );
            }

            for (int i = 0; i < source.Normals.Count; i++)
            {
                Vector3 normal = source.Normals[i];
                convertedMesh.Normals.Add(Vector3.Normalize(normal));
            }

            for (int i = 0; i < source.Indices.Count; i++)
            {
                convertedMesh.Indices.Add(source.Indices[i]);
            }

            return convertedMesh;
        }
    }
}
