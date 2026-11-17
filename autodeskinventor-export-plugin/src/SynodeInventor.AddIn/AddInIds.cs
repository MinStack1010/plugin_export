namespace Synode.Inventor.AddIn
{
    /// <summary>Các định danh cố định của add-in.</summary>
    internal static class AddInIds
    {
        /// <summary>GUID mới, duy nhất của Synode Inventor Exporter (khớp ClassId/ClientId trong .addin).</summary>
        public const string AddInGuid = "7001FEF0-B792-4C1D-9FB4-0515C8F2F90B";
        public const string ClientId = "{" + AddInGuid + "}";

        public const string DisplayName = "Synode Inventor Exporter";

        /// <summary>
        /// Translator glTF gốc của Inventor 2027 - lấy từ
        /// Bin\Addins\Autodesk.TranslatorglTFExport.Inventor.addin (ClassId, "Translator: glTF", .glb;*.gltf).
        /// </summary>
        public const string GltfTranslatorId = "{856E3C5D-A1F4-4B86-A6C0-8B849C0C8BE0}";

        public const string ExportButtonInternalName = "Synode.InventorExporter.ExportGlb";
        public const string ExportButtonText = "Export to GLB";
        public const string TabInternalName = "id_TabSynode";
        public const string TabDisplayName = "Synode";
        public const string PanelInternalName = "id_PanelSynodeExport";
        public const string PanelDisplayName = "Synode";

        /// <summary>Môi trường ribbon có nút Export (chỉ Part và Assembly).</summary>
        public static readonly string[] RibbonNames = { "Part", "Assembly" };
    }
}
