using System;
using System.Runtime.InteropServices;
using Synode.Inventor.Core;
using Inv = global::Inventor;

namespace Synode.Inventor.AddIn.Services
{
    internal enum ExportDocumentKind
    {
        Part,
        Assembly
    }

    /// <summary>Tài liệu Inventor đủ điều kiện export.</summary>
    internal sealed class ExportableDocument
    {
        public ExportableDocument(Inv.Document document, ExportDocumentKind kind, string fullFileName, string displayName, int itemCount)
        {
            Document = document;
            Kind = kind;
            FullFileName = fullFileName;
            DisplayName = displayName;
            ItemCount = itemCount;
        }

        public Inv.Document Document { get; }
        public ExportDocumentKind Kind { get; }
        public string FullFileName { get; }
        public string DisplayName { get; }

        /// <summary>Part: số solid/surface body (+ mesh feature set); Assembly: số occurrence cấp cao nhất.</summary>
        public int ItemCount { get; }

        public string Describe() =>
            Kind + " '" + DisplayName + "' (" + ItemCount + (Kind == ExportDocumentKind.Part ? " bodies)" : " occurrences)") +
            (FullFileName.Length == 0 ? " [unsaved]" : string.Empty);
    }

    /// <summary>Đọc tài liệu đang mở trong Inventor và kiểm tra điều kiện export.</summary>
    internal sealed class InventorDocumentService
    {
        private readonly Inv.Application _app;
        private readonly ISynodeLogger _log;

        public InventorDocumentService(Inv.Application app, ISynodeLogger log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public ExportableDocument GetActiveExportableDocument()
        {
            Inv.Document? doc = _app.ActiveDocument as Inv.Document;
            if (doc == null)
            {
                throw new ExportException(ExportErrorCode.NoActiveDocument,
                    "No document is open. Open a part (.ipt) or an assembly (.iam) and try again.");
            }

            string fullFileName = SafeString(() => doc.FullFileName);
            string displayName = SafeString(() => doc.DisplayName);
            Inv.DocumentTypeEnum type = doc.DocumentType;
            _log.Info("Active document: type=" + type + ", name='" + displayName + "'");

            switch (type)
            {
                case Inv.DocumentTypeEnum.kPartDocumentObject:
                {
                    var part = (Inv.PartDocument)doc;
                    Inv.PartComponentDefinition def = part.ComponentDefinition;
                    int bodies = def.SurfaceBodies.Count;
                    int meshSets = def.MeshFeatureSets.Count;
                    if (bodies == 0 && meshSets == 0)
                    {
                        throw new ExportException(ExportErrorCode.NoGeometry,
                            "The active part '" + displayName + "' has no solid, surface or mesh bodies to export.");
                    }
                    return new ExportableDocument(doc, ExportDocumentKind.Part, fullFileName, displayName, bodies + meshSets);
                }
                case Inv.DocumentTypeEnum.kAssemblyDocumentObject:
                {
                    var asm = (Inv.AssemblyDocument)doc;
                    int occurrences = asm.ComponentDefinition.Occurrences.Count;
                    if (occurrences == 0)
                    {
                        throw new ExportException(ExportErrorCode.NoGeometry,
                            "The active assembly '" + displayName + "' has no components to export.");
                    }
                    return new ExportableDocument(doc, ExportDocumentKind.Assembly, fullFileName, displayName, occurrences);
                }
                default:
                    throw new ExportException(ExportErrorCode.UnsupportedDocument,
                        "The active document type (" + DescribeType(type) + ") is not supported." + Environment.NewLine +
                        "Only parts (.ipt) and assemblies (.iam) can be exported to GLB.");
            }
        }

        private static string DescribeType(Inv.DocumentTypeEnum type)
        {
            switch (type)
            {
                case Inv.DocumentTypeEnum.kDrawingDocumentObject: return "drawing .idw/.dwg";
                case Inv.DocumentTypeEnum.kPresentationDocumentObject: return "presentation .ipn";
                default: return type.ToString();
            }
        }

        private string SafeString(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch (COMException ex)
            {
                _log.Warn("Could not read document property: " + ex.Message);
                return string.Empty;
            }
        }
    }
}
