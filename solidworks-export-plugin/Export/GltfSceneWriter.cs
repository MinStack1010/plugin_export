using System;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace PluginSynode.Export
{
    public static class GltfSceneWriter
    {
        public static void WriteGlb(ExtractedScene scene, string outputPath)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            if (scene.Root == null)
            {
                throw new InvalidOperationException("The scene contains no root node.");
            }

            SceneBuilder sceneBuilder = new SceneBuilder();
            NodeBuilder rootNode = new NodeBuilder(scene.Root.Name ?? "Root");

            AddNodeRecursive(sceneBuilder, scene.Root, rootNode);

            sceneBuilder.AddNode(rootNode);

            var model = sceneBuilder.ToGltf2();
            model.SaveGLB(outputPath);
        }

        private static void AddMesh(
            SceneBuilder sceneBuilder,
            NodeBuilder parentNode,
            ExtractedMesh extractedMesh)
        {
            if (extractedMesh.VertexCount == 0 || extractedMesh.TriangleCount == 0)
            {
                return;
            }

            MeshBuilder<VertexPositionNormal> meshBuilder = new MeshBuilder<VertexPositionNormal>(
                extractedMesh.Name ?? "Mesh"
            );

            MaterialBuilder material = CreateMaterial(extractedMesh.Material);
            var primitive = meshBuilder.UsePrimitive(material);

            for (int i = 0; i < extractedMesh.Indices.Count; i += 3)
            {
                int i0 = extractedMesh.Indices[i];
                int i1 = extractedMesh.Indices[i + 1];
                int i2 = extractedMesh.Indices[i + 2];

                VertexPositionNormal v0 = CreateVertex(extractedMesh, i0);
                VertexPositionNormal v1 = CreateVertex(extractedMesh, i1);
                VertexPositionNormal v2 = CreateVertex(extractedMesh, i2);

                primitive.AddTriangle(v0, v1, v2);
            }

            sceneBuilder.AddRigidMesh(meshBuilder, parentNode);
        }

        private static void AddNodeRecursive(
            SceneBuilder sceneBuilder,
            ExtractedNode extractedNode,
            NodeBuilder gltfNode)
        {
            gltfNode.LocalTransform = extractedNode.LocalTransform;

            foreach (ExtractedMesh mesh in extractedNode.Meshes)
            {
                AddMesh(sceneBuilder, gltfNode, mesh);
            }

            foreach (ExtractedNode child in extractedNode.Children)
            {
                NodeBuilder childNode = gltfNode.CreateNode(child.Name ?? "Component");
                AddNodeRecursive(sceneBuilder, child, childNode);
            }
        }

        private static MaterialBuilder CreateMaterial(ExtractedMaterial source)
        {
            if (source == null)
            {
                source = new ExtractedMaterial();
            }

            MaterialBuilder material = new MaterialBuilder(source.Name ?? "Material")
                .WithMetallicRoughnessShader()
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, source.BaseColour)
                .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.MetallicFactor, source.Metallic)
                .WithChannelParam(KnownChannel.MetallicRoughness, KnownProperty.RoughnessFactor, source.Roughness)
                .WithDoubleSide(true);

            if (source.Alpha == ExtractedMaterial.AlphaType.Blend)
            {
                material.WithAlpha(AlphaMode.BLEND);
            }
            else if (source.Alpha == ExtractedMaterial.AlphaType.Mask)
            {
                material.WithAlpha(AlphaMode.MASK);
            }
            else
            {
                material.WithAlpha(AlphaMode.OPAQUE);
            }

            return material;
        }

        private static VertexPositionNormal CreateVertex(ExtractedMesh mesh, int vertexIndex)
        {
            var position = mesh.Positions[vertexIndex];
            var normal = mesh.Normals[vertexIndex];

            return new VertexPositionNormal(position, normal);
        }
    }
}
