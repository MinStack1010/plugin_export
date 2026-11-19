using System;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using PluginSynode.Export;
using System.IO;

namespace PluginSynode
{
    public static class ExportGlbCommand
    {
        public static void Execute(SldWorks swApp)
        {
            try
            {
                if (swApp == null)
                {
                    return;
                }

                string outputPath = AskOutputPath(true);

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return;
                }

                ExportOptions options = new ExportOptions
                {
                    // 1.0 = already meters
                    // 0.001 = millimeters to meters
                    // 0.0254 = inches to meters
                    // 0.3048 = feet to meters

                    UnitScale = 1f,
                    UploadUrl = "", // Change it to where you want to send the GLB file
                    ApiKey = "API_KEY_DEV"
                };

                var extractor = new SolidWorksGeometryExtractor(swApp);
                ExtractedScene solidWorksScene = extractor.ExtractActiveDocument();
                ExtractedScene gltfScene = CoordinateConverter.ConvertSolidWorksSceneToGltf(solidWorksScene, options);

                if (outputPath == null)
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), $"solidworks-export-{Guid.NewGuid()}.glb");

                    GltfSceneWriter.WriteGlb(gltfScene, tempPath);

                    GlbUploader.UploadGlb(
                        tempPath,
                        options.UploadUrl,
                        options.ApiKey
                    );

                    File.Delete(tempPath);

                    swApp.SendMsgToUser2(
                        "GLB uploaded successfully.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk
                    );
                }
                else
                {
                    GltfSceneWriter.WriteGlb(gltfScene, outputPath);

                    swApp.SendMsgToUser2(
                        $"GLB exported successfully:\n{outputPath}",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk
                    );
                }
            }
            catch (Exception ex)
            {
                swApp.SendMsgToUser2(
                    ex.Message,
                    (int)swMessageBoxIcon_e.swMbStop,
                    (int)swMessageBoxBtn_e.swMbOk
                );
            }
        }

        private static string AskOutputPath(bool isSavedPhyscially)
        {
            if (isSavedPhyscially)
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Export to GLB";
                    dialog.Filter = "GLB files (*.glb)|*.glb";
                    dialog.DefaultExt = "glb";
                    dialog.AddExtension = true;
                    dialog.OverwritePrompt = true;
                    dialog.FileName = "solidworks-export.glb";

                    DialogResult result = dialog.ShowDialog();

                    if (result != DialogResult.OK)
                    {
                        return null;
                    }

                    return dialog.FileName;
                }
            }
            else
            {
                return null;
            }
        }
    }
}
