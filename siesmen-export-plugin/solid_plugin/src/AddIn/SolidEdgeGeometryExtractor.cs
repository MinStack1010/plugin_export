using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SynodeExporter.Core;
using SEA = global::SolidEdge.Assembly.Interop;
using SEF = global::SolidEdge.Framework.Interop;
using SEG = global::SolidEdge.Geometry.Interop;
using SEP = global::SolidEdge.Part.Interop;

namespace SynodeExporter.AddIn
{
    /// <summary>
    /// Đọc mô hình đang mở trong Solid Edge thành ExtractedScene, tương đương
    /// SolidWorksGeometryExtractor của plugin SW:
    /// * Part / Sheet Metal: node gốc (tên tài liệu) chứa mesh của các body hiển thị;
    /// * Assembly: node gốc (tên tài liệu) + cây Occurrence, transform cục bộ = ma trận
    ///   của Occurrence trong tài liệu cha; sub-assembly được duyệt đệ quy;
    /// * mỗi body chia thành các nhóm vật liệu (face -> body -> part), mesh đặt tên
    ///   "&lt;node&gt;_Material_&lt;i&gt;" như SW.
    /// Mọi tọa độ API Solid Edge tính bằng mét.
    /// </summary>
    public sealed class SolidEdgeGeometryExtractor
    {
        private const int MaxDepth = 64;

        private readonly SEF.Application _app;
        private readonly ExportOptions _options;
        private SolidEdgeMatrixLayout _layoutHint = SolidEdgeMatrixLayout.TranslationLast;
        private bool _layoutKnown;

        public SolidEdgeGeometryExtractor(SEF.Application app, ExportOptions options)
        {
            if (app == null)
            {
                throw new ArgumentNullException("app");
            }
            _app = app;
            _options = options ?? new ExportOptions();
        }

        public int SkippedFaces { get; private set; }

        public int SkippedOccurrences { get; private set; }

        public int TriangleCount { get; private set; }

        public ExtractedScene ExtractActiveDocument()
        {
            object document = GetActiveDocument();
            if (document == null)
            {
                throw new InvalidOperationException("No active Solid Edge document.");
            }

            var part = document as SEP.PartDocument;
            if (part != null)
            {
                Logger.Info("Extracting part: " + part.FullName);
                return ExtractPart(part.Name, document);
            }
            var sheetMetal = document as SEP.SheetMetalDocument;
            if (sheetMetal != null)
            {
                Logger.Info("Extracting sheet metal part: " + sheetMetal.FullName);
                return ExtractPart(sheetMetal.Name, document);
            }
            var assembly = document as SEA.AssemblyDocument;
            if (assembly != null)
            {
                Logger.Info("Extracting assembly: " + assembly.FullName);
                return ExtractAssembly(assembly);
            }
            throw new NotSupportedException("Only parts and assemblies are supported.");
        }

        private object GetActiveDocument()
        {
            try
            {
                return _app.ActiveDocument;
            }
            catch (COMException ex)
            {
                // Solid Edge báo lỗi khi không có tài liệu nào đang mở.
                Logger.Warn("Application.ActiveDocument failed: " + ex.Message);
                return null;
            }
        }

        private ExtractedScene ExtractPart(string title, object document)
        {
            var scene = new ExtractedScene
            {
                Root = new ExtractedNode { Name = title, LocalTransform = Mat4.Identity() }
            };
            ExtractPartMeshesIntoNode(document, scene.Root);
            return scene;
        }

        private ExtractedScene ExtractAssembly(SEA.AssemblyDocument assembly)
        {
            var scene = new ExtractedScene
            {
                Root = new ExtractedNode { Name = assembly.Name, LocalTransform = Mat4.Identity() }
            };
            int budget = 1000;
            DetectLayoutHint(assembly.Occurrences, 0, ref budget);
            Logger.Info("Occurrence matrix layout: " + _layoutHint + (_layoutKnown ? " (detected)" : " (default)"));
            AddOccurrences(assembly.Occurrences, scene.Root, 1);
            return scene;
        }

        /// <summary>Tìm một Occurrence có tịnh tiến khác 0 để xác định bố cục ma trận của GetMatrix.</summary>
        private void DetectLayoutHint(SEA.Occurrences occurrences, int depth, ref int budget)
        {
            if (occurrences == null || _layoutKnown || depth > MaxDepth)
            {
                return;
            }
            int count = occurrences.Count;
            for (int i = 1; i <= count && !_layoutKnown && budget > 0; i++)
            {
                budget--;
                SEA.Occurrence occurrence;
                try
                {
                    occurrence = occurrences.Item(i);
                }
                catch (COMException)
                {
                    continue;
                }
                if (occurrence == null)
                {
                    continue;
                }
                try
                {
                    double[] origin;
                    double[] raw = ReadMatrix(occurrence, out origin);
                    SolidEdgeMatrixLayout layout;
                    if (SolidEdgeMatrix.TryDetectLayout(raw, origin, out layout))
                    {
                        _layoutHint = layout;
                        _layoutKnown = true;
                        return;
                    }
                    var sub = occurrence.OccurrenceDocument as SEA.AssemblyDocument;
                    if (sub != null)
                    {
                        DetectLayoutHint(sub.Occurrences, depth + 1, ref budget);
                    }
                }
                catch (COMException)
                {
                }
            }
        }

        private void AddOccurrences(SEA.Occurrences occurrences, ExtractedNode parent, int depth)
        {
            if (occurrences == null)
            {
                return;
            }
            if (depth > MaxDepth)
            {
                throw new ExportException("The assembly structure is nested too deeply (more than " + MaxDepth + " levels).");
            }
            int count = occurrences.Count;
            for (int i = 1; i <= count; i++)
            {
                SEA.Occurrence occurrence = occurrences.Item(i);
                ExtractedNode child = ExtractOccurrence(occurrence, depth);
                if (child != null)
                {
                    parent.Children.Add(child);
                }
            }
        }

        private ExtractedNode ExtractOccurrence(SEA.Occurrence occurrence, int depth)
        {
            if (occurrence == null)
            {
                return null;
            }
            string name = SafeName(occurrence);

            object document;
            try
            {
                if (occurrence.IsNongraphic)
                {
                    Logger.Info("Skipping non-graphic occurrence: " + name);
                    SkippedOccurrences++;
                    return null;
                }
                document = occurrence.OccurrenceDocument;
            }
            catch (COMException ex)
            {
                Logger.Warn("Skipping occurrence '" + name + "': " + ex.Message);
                SkippedOccurrences++;
                return null;
            }
            if (document == null)
            {
                // Linh kiện bị suppress, chưa được nạp hoặc mất file: không có hình học.
                Logger.Warn("Skipping occurrence without document (suppressed, unloaded or missing): " + name);
                SkippedOccurrences++;
                return null;
            }

            double[] origin;
            double[] raw = ReadMatrix(occurrence, out origin);
            SolidEdgeMatrixLayout layout;
            bool detected;
            double[] matrix = SolidEdgeMatrix.Normalize(raw, origin, _layoutHint, out layout, out detected);
            if (detected && layout != _layoutHint)
            {
                Logger.Warn("Occurrence '" + name + "' uses matrix layout " + layout + " (expected " + _layoutHint + ").");
            }

            var node = new ExtractedNode { Name = name, LocalTransform = Mat4.ToFloat(matrix) };

            if (document is SEP.PartDocument || document is SEP.SheetMetalDocument)
            {
                ExtractPartMeshesIntoNode(document, node);
            }
            else
            {
                var sub = document as SEA.AssemblyDocument;
                if (sub != null)
                {
                    AddOccurrences(sub.Occurrences, node, depth + 1);
                }
                else
                {
                    Logger.Warn("Occurrence '" + name + "' references an unsupported document type; exported as an empty node.");
                }
            }
            return node;
        }

        private static string SafeName(SEA.Occurrence occurrence)
        {
            try
            {
                return occurrence.Name;
            }
            catch (COMException)
            {
                return null;
            }
        }

        private static double[] ReadMatrix(SEA.Occurrence occurrence, out double[] origin)
        {
            Array values = new double[16];
            occurrence.GetMatrix(ref values);
            double[] matrix = FacetReader.ToDoubles(values);
            if (matrix == null || matrix.Length != 16)
            {
                throw new ExportException("Unable to read the transform of '" + SafeName(occurrence) + "'.");
            }
            origin = null;
            try
            {
                double ox = 0, oy = 0, oz = 0, ax = 0, ay = 0, az = 0;
                occurrence.GetTransform(out ox, out oy, out oz, out ax, out ay, out az);
                origin = new[] { ox, oy, oz };
            }
            catch (COMException)
            {
            }
            return matrix;
        }

        // ------------------------------------------------------------------ part

        private void ExtractPartMeshesIntoNode(object document, ExtractedNode node)
        {
            SEP.Models models;
            SEF.FaceStyle baseStyle = null;
            var part = document as SEP.PartDocument;
            var sheetMetal = document as SEP.SheetMetalDocument;
            if (part != null)
            {
                models = part.Models;
                try { part.GetBaseStyle(SEP.PartBaseStylesConstants.sePartBaseStyle, out baseStyle); } catch (COMException) { }
            }
            else if (sheetMetal != null)
            {
                models = sheetMetal.Models;
                try { sheetMetal.GetBaseStyle(SEP.PartBaseStylesConstants.sePartBaseStyle, out baseStyle); } catch (COMException) { }
            }
            else
            {
                return;
            }
            if (models == null)
            {
                return;
            }

            int count = models.Count;
            for (int i = 1; i <= count; i++)
            {
                SEP.Model model = models.Item(i);
                if (model == null)
                {
                    continue;
                }
                var body = model.Body as SEG.Body;
                if (body == null)
                {
                    continue;
                }
                bool visible = true;
                try { visible = body.Visible; } catch (COMException) { }
                if (!visible)
                {
                    continue; // SW: GetBodies2(swSolidBody, visibleOnly: true)
                }
                string bodyName = SafeModelName(model, i);
                foreach (ExtractedMesh mesh in ExtractBodyMesh(body, bodyName, node.Name, baseStyle))
                {
                    if (mesh.VertexCount > 0 && mesh.TriangleCount > 0)
                    {
                        MeshValidator.Validate(mesh);
                        node.Meshes.Add(mesh);
                        TriangleCount += mesh.TriangleCount;
                    }
                }
            }
        }

        private static string SafeModelName(SEP.Model model, int index)
        {
            try
            {
                string name = model.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch (COMException)
            {
            }
            return "Body" + index;
        }

        private List<ExtractedMesh> ExtractBodyMesh(SEG.Body body, string bodyName, string meshName, SEF.FaceStyle baseStyle)
        {
            var result = new List<ExtractedMesh>();

            SEF.FaceStyle bodyStyle = null;
            try { bodyStyle = body.Style; } catch (COMException) { }
            ExtractedMaterial bodyMaterial = MaterialFromStyle(bodyStyle ?? baseStyle, bodyName);

            double tolerance = _options.ResolveChordTolerance(BodyDiagonal(body));

            var faces = body.get_Faces(SEG.FeatureTopologyQueryTypeConstants.igQueryAll) as SEG.Faces;
            if (faces == null)
            {
                Logger.Warn("Body '" + bodyName + "' has no faces.");
                return result;
            }

            var groups = new List<KeyValuePair<ExtractedMaterial, MeshAccumulator>>();
            int faceCount = faces.Count;
            for (int f = 1; f <= faceCount; f++)
            {
                var face = faces.Item(f) as SEG.Face;
                if (face == null)
                {
                    continue;
                }

                ExtractedMaterial effective = FaceMaterial(face, bodyName, bodyMaterial);

                int groupIndex = -1;
                for (int g = 0; g < groups.Count; g++)
                {
                    if (MaterialFactory.AreEquivalent(effective, groups[g].Key))
                    {
                        groupIndex = g;
                        break;
                    }
                }
                if (groupIndex == -1)
                {
                    groupIndex = groups.Count;
                    groups.Add(new KeyValuePair<ExtractedMaterial, MeshAccumulator>(
                        effective, new MeshAccumulator(meshName + "_Material_" + groupIndex, effective)));
                }

                try
                {
                    int facetCount = 0;
                    Array points = new double[0];
                    object normals;
                    object textureCoords;
                    face.GetFacetData(tolerance, out facetCount, ref points, out normals, out textureCoords);
                    FacetReader.AddFacets(groups[groupIndex].Value, facetCount, points, normals);
                }
                catch (Exception ex) when (ex is COMException || ex is ExportException || ex is InvalidCastException)
                {
                    SkippedFaces++;
                    Logger.Warn("Face " + f + " of body '" + bodyName + "' could not be tessellated: " + ex.Message);
                }
            }

            foreach (var group in groups)
            {
                if (group.Value.Mesh.Indices.Count > 0)
                {
                    result.Add(group.Value.Mesh);
                }
            }
            return result;
        }

        private ExtractedMaterial FaceMaterial(SEG.Face face, string bodyName, ExtractedMaterial bodyMaterial)
        {
            SEF.FaceStyle faceStyle = null;
            try { faceStyle = face.Style; } catch (COMException) { }
            ExtractedMaterial faceMaterial = MaterialFromStyle(faceStyle, bodyName + "_Face");
            if (faceMaterial != null)
            {
                // Face.Style trả về style hiệu lực; chỉ coi là "override" khi khác vật liệu body.
                return MaterialFactory.AreEquivalent(faceMaterial, bodyMaterial) ? bodyMaterial : faceMaterial;
            }
            if (bodyMaterial != null)
            {
                return bodyMaterial;
            }
            // Không có FaceStyle: dùng màu hiển thị của face.
            try
            {
                double r, g, b, a;
                face.GetRGBAVals(out r, out g, out b, out a);
                return MaterialFactory.FromDisplayColour(bodyName + "_Face", (float)r, (float)g, (float)b, (float)a,
                    _options.DefaultMetallic, _options.DefaultRoughness);
            }
            catch (COMException)
            {
                return null; // GlbWriter dùng vật liệu mặc định (xám 0.8) như SW
            }
        }

        private ExtractedMaterial MaterialFromStyle(SEF.FaceStyle style, string name)
        {
            if (style == null)
            {
                return null;
            }
            try
            {
                float r, g, b;
                style.GetDiffuse(out r, out g, out b);
                float opacity = style.Opacity;
                return MaterialFactory.FromDisplayColour(name, r, g, b, opacity, _options.DefaultMetallic, _options.DefaultRoughness);
            }
            catch (COMException ex)
            {
                Logger.Warn("Unable to read face style for '" + name + "': " + ex.Message);
                return null;
            }
        }

        private static double BodyDiagonal(SEG.Body body)
        {
            try
            {
                Array min = new double[3];
                Array max = new double[3];
                body.GetRange(ref min, ref max);
                double[] a = FacetReader.ToDoubles(min);
                double[] b = FacetReader.ToDoubles(max);
                if (a == null || b == null || a.Length < 3 || b.Length < 3)
                {
                    return 0;
                }
                double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            catch (COMException)
            {
                return 0;
            }
        }
    }
}
