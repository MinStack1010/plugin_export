using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PluginSynode.Export
{
    public class SolidWorksGeometryExtractor
    {
        private readonly SldWorks _swApp;

        public SolidWorksGeometryExtractor(SldWorks swApp)
        {
            _swApp = swApp;
        }

        public ExtractedScene ExtractActiveDocument()
        {
            ModelDoc2 model = _swApp.ActiveDoc as ModelDoc2;

            if (model == null)
            {
                throw new InvalidOperationException("No active SOLIDWORKS document.");
            }

            int docType = model.GetType();

            if (docType == (int)swDocumentTypes_e.swDocPART)
            {
                return ExtractPart(model);
            }

            if (docType == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return ExtractAssembly(model);
            }

            throw new NotSupportedException("Only parts and assemblies are supported.");
        }

        private void AddTessellationFacet(
            ITessellation tessellation,
            int facetId,
            Face2 face,
            ExtractedMesh mesh,
            Dictionary<int, int> vertexMap)
        {
            Array fins = tessellation.GetFacetFins(facetId) as Array;

            if (fins == null || fins.Length != 3)
            {
                throw new Exception($"Facet {facetId} does not contain three fins.");
            }

            List<int> vertexIds = new List<int>(3);

            foreach (object finObject in fins)
            {
                int finId = Convert.ToInt32(finObject);

                Array endpoints = tessellation.GetFinVertices(finId) as Array;

                if (endpoints == null || endpoints.Length != 2)
                {
                    throw new Exception($"Invalid fin {finId} in facet {facetId}.");
                }

                foreach (object endpoint in endpoints)
                {
                    int vertexId = Convert.ToInt32(endpoint);

                    if (!vertexIds.Contains(vertexId))
                    {
                        vertexIds.Add(vertexId);
                    }
                }
            }

            if (vertexIds.Count != 3)
            {
                throw new Exception($"Facet {facetId} has {vertexIds.Count} unique vertices.");
            }

            int i0 = GetOrAddTessellationVertex(tessellation, vertexIds[0], mesh, vertexMap);
            int i1 = GetOrAddTessellationVertex(tessellation, vertexIds[1], mesh, vertexMap);
            int i2 = GetOrAddTessellationVertex(tessellation, vertexIds[2], mesh, vertexMap);

            Vector3 p0 = mesh.Positions[i0];
            Vector3 p1 = mesh.Positions[i1];
            Vector3 p2 = mesh.Positions[i2];

            Vector3 geometricNormal = Vector3.Cross(p1 - p0, p2 - p0);

            if (geometricNormal.LengthSquared() < 1e-20f)
            {
                return;
            }

            Vector3 averageNormal = mesh.Normals[i0] + mesh.Normals[i1] + mesh.Normals[i2];

            if (Vector3.Dot(geometricNormal, averageNormal) < 0)
            {
                int temp = i1;
                i1 = i2;
                i2 = temp;
            }

            mesh.Indices.Add(i0);
            mesh.Indices.Add(i1);
            mesh.Indices.Add(i2);
        }

        private bool AreMaterialsEquivalent(ExtractedMaterial a, ExtractedMaterial b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            const float tolerance = 0.0001f;

            return Vector4.Distance(a.BaseColour, b.BaseColour) < tolerance
                && Math.Abs(a.Metallic - b.Metallic) < tolerance
                && Math.Abs(a.Roughness - b.Roughness) < tolerance
                && a.Alpha == b.Alpha;
        }

        private float Clamp(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }

            if (value > 1.0f)
            {
                return 1.0f;
            }

            return value;
        }

        private ExtractedMaterial ConvertDspbrMaterial(DSPBRMaterial dspbr, string name)
        {
            if (dspbr == null)
            {
                return null;
            }

            Vector4 baseColour = ConvertPackedColour(Convert.ToInt32(dspbr.Albedo));
            float transparency = Clamp((float)dspbr.Transparency);
            ExtractedMaterial.AlphaType alpha = ExtractedMaterial.AlphaType.Opaque;

            if (transparency != 0)
            {
                baseColour.W = 1f - transparency;
                alpha = ExtractedMaterial.AlphaType.Blend;
            }

            return new ExtractedMaterial
            {
                Name = name + "_Material",
                BaseColour = baseColour,
                Metallic = Clamp((float)dspbr.Metallic),
                Roughness = Clamp((float)dspbr.Roughness),
                Alpha = alpha
            };
        }

        private Vector4 ConvertPackedColour(int colour)
        {
            float r = (colour & 0xFF) / 255f;
            float g = ((colour >> 8) & 0xFF) / 255f;
            float b = ((colour >> 16) & 0xFF) / 255f;

            return new Vector4(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b), 1f);
        }

        private ExtractedScene ExtractAssembly(ModelDoc2 model)
        {
            var scene = new ExtractedScene();
            AssemblyDoc assembly = model as AssemblyDoc;

            if (assembly == null)
            {
                return scene;
            }

            Configuration configuration = model.ConfigurationManager.ActiveConfiguration;
            Component2 rootComponent = configuration.GetRootComponent3(true);

            if (rootComponent == null)
            {
                return scene;
            }

            scene.Root = new ExtractedNode
            {
                Name = model.GetTitle(),
                LocalTransform = Matrix4x4.Identity
            };

            object[] children = rootComponent.GetChildren() as object[];

            if (children != null)
            {
                foreach (object childObj in children)
                {
                    Component2 child = childObj as Component2;

                    if (child == null || child.IsSuppressed())
                    {
                        continue;
                    }

                    ExtractedNode childNode = ExtractComponentRecursive(child, Matrix4x4.Identity);

                    if (childNode != null)
                    {
                        scene.Root.Children.Add(childNode);
                    }
                }
            }

            return scene;
        }

        private List<ExtractedMesh> ExtractBodyMesh(
            ModelDoc2 model,
            Body2 body,
            string name,
            RenderMaterial bodyRenderMaterial)
        {
            List<ExtractedMesh> result = new List<ExtractedMesh>();

            ITessellation tessellation = body.GetTessellation(null);

            if (tessellation == null)
            {
                throw new Exception($"Unable to create tessellation for the body: {name}");
            }

            tessellation.NeedVertexNormal = true;
            tessellation.NeedFaceFacetMap = true;
            tessellation.NeedEdgeFinMap = false;
            tessellation.ImprovedQuality = true;

            if (!tessellation.Tessellate())
            {
                throw new Exception($"Body tessellation failed: {name}");
            }

            ExtractedMaterial bodyMaterial = ExtractDspbrMaterial(bodyRenderMaterial, body.Name);

            var materialGroups = new List<(
                ExtractedMaterial Material,
                ExtractedMesh Mesh,
                Dictionary<int, int> VertexMap
            )>();

            var faceCache = new List<(Face2 Face, int GroupIndex)>();

            int facetCount = tessellation.GetFacetCount();

            for (int i = 0; i < facetCount; i++)
            {
                Face2 face = tessellation.GetFacetFace(i) as Face2;

                if (face == null)
                {
                    throw new Exception($"Unable to identify face for facet {i}");
                }

                int groupIndex = -1;

                foreach (var cachedFace in faceCache)
                {
                    if (IsSameEntity(face, cachedFace.Face))
                    {
                        groupIndex = cachedFace.GroupIndex;
                        break;
                    }
                }

                if (groupIndex == -1)
                {
                    ExtractedMaterial effectiveMaterial = null;
                    RenderMaterial faceRenderMaterial = FindFaceRenderMaterial(model, face);

                    if (faceRenderMaterial != null)
                    {
                        effectiveMaterial = ExtractDspbrMaterial(faceRenderMaterial, body.Name + "_Face");
                    }
                    
                    if (effectiveMaterial == null)
                    {
                        RenderMaterial featureRenderMaterial = FindFaceFeatureRenderMaterial(model, face);

                        if (featureRenderMaterial != null)
                        {
                            effectiveMaterial = ExtractDspbrMaterial(featureRenderMaterial, body.Name + "_Feature");
                        }
                    }

                    if (effectiveMaterial == null)
                    {
                        effectiveMaterial = bodyMaterial;
                    }

                    for (int j = 0; j < materialGroups.Count; j++)
                    {
                        if (AreMaterialsEquivalent(effectiveMaterial, materialGroups[j].Material))
                        {
                            groupIndex = j;
                            break;
                        }
                    }

                    if (groupIndex == -1)
                    {
                        groupIndex = materialGroups.Count;

                        ExtractedMesh mesh = new ExtractedMesh
                        {
                            Name = name + "_Material_" + groupIndex,
                            Material = effectiveMaterial
                        };

                        materialGroups.Add((effectiveMaterial, mesh, new Dictionary<int, int>()));
                    }

                    faceCache.Add((face, groupIndex));
                }

                var group = materialGroups[groupIndex];
                AddTessellationFacet(tessellation, i, face, group.Mesh, group.VertexMap);
            }

            foreach (var group in materialGroups)
            {
                if (group.Mesh.Indices.Count > 0)
                {
                    result.Add(group.Mesh);
                }
            }

            return result;
        }

        private ExtractedNode ExtractComponentRecursive(Component2 component, Matrix4x4 parentWorldTransform)
        {
            Matrix4x4 componentWorldTransform = GetComponentTransform(component);
            Matrix4x4 localTransform;

            if (!Matrix4x4.Invert(parentWorldTransform, out Matrix4x4 inverseParent))
            {
                throw new InvalidOperationException($"Unable to invert parent transform for '{component.Name2}'");
            }

            localTransform = componentWorldTransform * inverseParent;

            var node = new ExtractedNode
            {
                Name = component.Name2,
                LocalTransform = localTransform
            };

            ModelDoc2 componentModel = component.GetModelDoc2() as ModelDoc2;

            if (componentModel != null && componentModel.GetType() == (int)swDocumentTypes_e.swDocPART)
            {
                ExtractPartMeshesIntoNode(componentModel, node);
            }

            object[] children = component.GetChildren() as object[];

            if (children != null)
            {
                foreach (object childObj in children)
                {
                    Component2 child = childObj as Component2;

                    if (child == null || child.IsSuppressed())
                    {
                        continue;
                    }

                    ExtractedNode childNode = ExtractComponentRecursive(child, componentWorldTransform);

                    if (childNode != null)
                    {
                        node.Children.Add(childNode);
                    }
                }
            }

            return node;
        }

        private ExtractedMaterial ExtractDspbrMaterial(RenderMaterial renderMaterial, string name)
        {
            if (renderMaterial == null)
            {
                return null;
            }

            try
            {
                if (!renderMaterial.HasDSPBRProperties)
                {
                    return null;
                }

                DSPBRMaterial dspbr = renderMaterial.GetDSPBRMaterial();
                return ConvertDspbrMaterial(dspbr, name);
            }
            catch (Exception e)
            {
                // Logger.Log($"DSPBR extraction failed for '{name}': {e}");
                return null;
            }
        }

        private ExtractedScene ExtractPart(ModelDoc2 model)
        {
            var scene = new ExtractedScene();

            scene.Root = new ExtractedNode
            {
                Name = model.GetTitle(),
                LocalTransform = Matrix4x4.Identity
            };

            ExtractPartMeshesIntoNode(model, scene.Root);

            return scene;
        }

        private void ExtractPartMeshesIntoNode(ModelDoc2 model, ExtractedNode node)
        {
            if (model == null || node == null)
            {
                return;
            }

            PartDoc part = model as PartDoc;

            if (part == null)
            {
                return;
            }

            object bodiesObj = part.GetBodies2((int)swBodyType_e.swSolidBody, true);

            if (bodiesObj == null)
            {
                return;
            }

            object[] bodies = bodiesObj as object[];

            if (bodies == null)
            {
                return;
            }

            foreach (object bodyObj in bodies)
            {
                Body2 body = bodyObj as Body2;

                if (body == null)
                {
                    continue;
                }

                RenderMaterial renderMaterial = FindBodyOrPartRenderMaterial(model, body);
                List<ExtractedMesh> meshes = ExtractBodyMesh(model, body, node.Name, renderMaterial);

                foreach (ExtractedMesh mesh in meshes)
                {
                    if (mesh.VertexCount > 0 && mesh.TriangleCount > 0)
                    {
                        ValidateExtractedMesh(mesh);
                        node.Meshes.Add(mesh);
                    }
                }
            }
        }

        private RenderMaterial FindFaceFeatureRenderMaterial(ModelDoc2 model, Face2 face)
        {
            if (model == null || face == null)
            {
                return null;
            }

            Feature feature = face.GetFeature() as Feature;

            if (feature == null)
            {
                return null;
            }

            return FindFeatureRenderMaterial(model, feature);
        }

        private RenderMaterial FindFaceRenderMaterial(ModelDoc2 model, Face2 face)
        {
            if (model == null || face == null)
            {
                return null;
            }

            object[] materials = model.Extension.GetRenderMaterials2(
                (int)swDisplayStateOpts_e.swThisDisplayState,
                null
            ) as object[];

            if (materials == null)
            {
                return null;
            }

            foreach (object obj in materials)
            {
                RenderMaterial material = obj as RenderMaterial;

                if (material == null || !material.HasDSPBRProperties)
                {
                    continue;
                }

                object[] entities = material.GetEntities() as object[];

                if (entities == null)
                {
                    continue;
                }

                foreach (object entity in entities)
                {
                    if (!(entity is Face2 assignedFace))
                    {
                        continue;
                    }

                    if (_swApp.IsSame(face, assignedFace) == (int)swObjectEquality.swObjectSame)
                    {
                        return material;
                    }
                }
            }

            return null;
        }

        private RenderMaterial FindFeatureRenderMaterial(ModelDoc2 model, Feature feature)
        {
            if (model == null || feature == null)
            {
                return null;
            }

            object[] materials = model.Extension.GetRenderMaterials2(
                (int)swDisplayStateOpts_e.swThisDisplayState,
                null
            ) as object[];

            if (materials == null)
            {
                return null;
            }

            foreach (object obj in materials)
            {
                RenderMaterial material = obj as RenderMaterial;

                if (material == null || !material.HasDSPBRProperties)
                {
                    continue;
                }

                object[] entities = material.GetEntities() as object[];

                if (entities == null)
                {
                    continue;
                }

                foreach (object entity in entities)
                {
                    if (!(entity is Feature assignedFeature))
                    {
                        continue;
                    }

                    if (IsSameEntity(feature, assignedFeature))
                    {
                        return material;
                    }
                }
            }

            return null;
        }

        private Matrix4x4 GetComponentTransform(Component2 component)
        {
            MathTransform swTransform = component.Transform2;

            if (swTransform == null)
            {
                return Matrix4x4.Identity;
            }

            double[] data = swTransform.ArrayData as double[];

            if (data == null || data.Length < 16)
            {
                return Matrix4x4.Identity;
            }

            float scale = (float)data[12];

            return new Matrix4x4(
                (float)data[0] * scale,
                (float)data[1] * scale,
                (float)data[2] * scale,
                0.0f,

                (float)data[3] * scale,
                (float)data[4] * scale,
                (float)data[5] * scale,
                0.0f,

                (float)data[6] * scale,
                (float)data[7] * scale,
                (float)data[8] * scale,
                0.0f,

                (float)data[9],
                (float)data[10],
                (float)data[11],
                1.0f
            );
        }

        private int GetOrAddTessellationVertex(
            ITessellation tessellation,
            int vertexId,
            ExtractedMesh mesh,
            Dictionary<int, int> vertexMap)
        {
            if (vertexMap.TryGetValue(vertexId, out int existingIndex))
            {
                return existingIndex;
            }

            Vector3 position = GetTessellationPosition(tessellation, vertexId);
            Vector3 normal = GetTessellationNormal(tessellation, vertexId);

            int newIndex = mesh.Positions.Count;

            mesh.Positions.Add(position);
            mesh.Normals.Add(normal);
            vertexMap.Add(vertexId, newIndex);

            return newIndex;
        }

        private Vector3 GetTessellationPosition(ITessellation tessellation, int vertexId)
        {
            double[] values = tessellation.GetVertexPoint(vertexId) as double[];

            if (values == null || values.Length < 3)
            {
                throw new Exception($"Invalid position for vertex {vertexId}.");
            }

            return new Vector3((float)values[0], (float)values[1], (float)values[2]);
        }

        private Vector3 GetTessellationNormal(ITessellation tessellation, int vertexId)
        {
            double[] values = tessellation.GetVertexNormal(vertexId) as double[];

            if (values == null || values.Length < 3)
            {
                throw new Exception($"Invalid normal for vertex {vertexId}.");
            }

            Vector3 normal = new Vector3((float)values[0], (float)values[1], (float)values[2]);

            if (normal.LengthSquared() < 1e-20f)
            {
                throw new Exception($"Zero normal for vertex {vertexId}.");
            }

            return Vector3.Normalize(normal);
        }

        private bool IsSameEntity(object first, object second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            try
            {
                int result = _swApp.IsSame(first, second);
                return result == 1;
            }
            catch (Exception e)
            {
                // Logger.Log($"Entity comparison failed: {e.Message}");
                return false;
            }
        }

        private RenderMaterial FindBodyOrPartRenderMaterial(ModelDoc2 model, Body2 body)
        {
            object[] materials = model.Extension.GetRenderMaterials2(
                (int)swDisplayStateOpts_e.swThisDisplayState,
                null
            ) as object[];

            if (materials == null)
            {
                return null;
            }

            RenderMaterial partMaterial = null;
            RenderMaterial bodyMaterial = null;

            foreach (object obj in materials)
            {
                RenderMaterial material = obj as RenderMaterial;

                if (material == null)
                {
                    continue;
                }

                object[] entities = material.GetEntities() as object[];

                if (entities == null)
                {
                    continue;
                }

                foreach (object entity in entities)
                {
                    if (entity is Body2 assignedBody)
                    {
                        if (IsSameEntity(body, assignedBody))
                        {
                            bodyMaterial = material;
                        }
                    }
                    else if (entity is ModelDoc2)
                    {
                        partMaterial = material;
                    }
                }
            }

            return bodyMaterial ?? partMaterial;
        }

        private float SrgbToLinear(float value)
        {
            value = Clamp(value);

            if (value <= 0.04045f)
            {
                return value / 12.92f;
            }

            return (float)Math.Pow((value + 0.055f) / 1.055f, 2.4);
        }

        private void ValidateExtractedMesh(ExtractedMesh mesh)
        {
            if (mesh.VertexCount == 0)
            {
                throw new Exception($"Mesh '{mesh.Name}' has no vertices.");
            }

            if (mesh.TriangleCount == 0)
            {
                throw new Exception($"Mesh '{mesh.Name}' has no triangles.");
            }

            if (mesh.Normals.Count != mesh.VertexCount)
            {
                throw new Exception($"Mesh '{mesh.Name}' has mismatched position/normal count.");
            }

            foreach (int index in mesh.Indices)
            {
                if (index < 0 || index >= mesh.VertexCount)
                {
                    throw new Exception($"Mesh '{mesh.Name}' has invalid triangle index: {index}.");
                }
            }
        }
    }
}
