using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SynodeExporter.Core;
using SEF = global::SolidEdge.Framework.Interop;

namespace SynodeExporter.AddIn
{
    /// <summary>Bọc HWND cửa sổ chính Solid Edge làm owner cho hộp thoại.</summary>
    internal sealed class Win32Window : IWin32Window
    {
        public Win32Window(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; private set; }

        public static IWin32Window FromApplication(SEF.Application app)
        {
            try
            {
                return new Win32Window(new IntPtr(app.hWnd));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Lệnh "Export to GLB" - tương đương PluginSynode.ExportGlbCommand (SW):
    /// hộp thoại lưu file -> đọc mô hình -> chuyển tọa độ -> ghi GLB -> thông báo.
    /// </summary>
    public static class ExportGlbCommand
    {
        public const string Caption = "Synode Exporter";

        public static void Execute(SEF.Application app)
        {
            if (app == null)
            {
                return;
            }
            IWin32Window owner = Win32Window.FromApplication(app);
            try
            {
                string outputPath = AskOutputPath(owner);
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    return;
                }

                var options = new ExportOptions
                {
                    // API Solid Edge trả về mét = đơn vị của glTF (giống UnitScale = 1 của plugin SW).
                    UnitScale = 1f
                };

                var watch = Stopwatch.StartNew();
                SetStatus(app, "Synode Exporter: exporting " + Path.GetFileName(outputPath) + "...");
                Cursor previous = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                SolidEdgeGeometryExtractor extractor;
                try
                {
                    extractor = new SolidEdgeGeometryExtractor(app, options);
                    ExtractedScene solidEdgeScene = extractor.ExtractActiveDocument();
                    ExtractedScene gltfScene = CoordinateConverter.ConvertToGltf(solidEdgeScene, options);
                    GlbWriter.WriteGlb(gltfScene, outputPath);
                }
                finally
                {
                    Cursor.Current = previous;
                    SetStatus(app, string.Empty);
                }

                Logger.Info("GLB exported: " + outputPath + " (" + extractor.TriangleCount + " triangles, "
                    + extractor.SkippedFaces + " skipped faces, " + extractor.SkippedOccurrences + " skipped occurrences, "
                    + watch.ElapsedMilliseconds + " ms)");

                string message = "GLB exported successfully:\n" + outputPath;
                if (extractor.SkippedFaces > 0 || extractor.SkippedOccurrences > 0)
                {
                    message += "\n\nWarning: " + extractor.SkippedFaces + " face(s) and " + extractor.SkippedOccurrences
                        + " component(s) could not be exported. See the log:\n" + Logger.LogPath;
                }
                Show(owner, message, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("Export to GLB failed", ex);
                Show(owner, ex.Message, MessageBoxIcon.Stop);
            }
        }

        private static string AskOutputPath(IWin32Window owner)
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Export to GLB";
                dialog.Filter = "GLB files (*.glb)|*.glb";
                dialog.DefaultExt = "glb";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = "solidedge-export.glb";

                DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (result != DialogResult.OK)
                {
                    return null;
                }
                return dialog.FileName;
            }
        }

        private static void Show(IWin32Window owner, string message, MessageBoxIcon icon)
        {
            if (owner != null)
            {
                MessageBox.Show(owner, message, Caption, MessageBoxButtons.OK, icon);
            }
            else
            {
                MessageBox.Show(message, Caption, MessageBoxButtons.OK, icon);
            }
        }

        private static void SetStatus(SEF.Application app, string text)
        {
            try
            {
                app.StatusBar = text;
            }
            catch
            {
                // Thanh trạng thái chỉ để hiển thị tiến trình.
            }
        }
    }
}
