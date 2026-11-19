using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Synode.Inventor.AddIn.Services;
using Synode.Inventor.AddIn.Ui;
using Synode.Inventor.Core;
using Inv = global::Inventor;

namespace Synode.Inventor.AddIn.Commands
{
    /// <summary>
    /// "Export to GLB" command - equivalent to ExportGlbCommand in the SolidWorks plugin:
    /// check document -> save dialog -> export -> success/error message.
    /// </summary>
    internal sealed class ExportGlbCommand
    {
        private const string Title = "Synode - Export to GLB";

        private readonly Inv.Application _app;
        private readonly InventorDocumentService _documents;
        private readonly ISaveFileDialogService _dialog;
        private readonly GlbExportService _exporter;
        private readonly IUserMessenger _messenger;
        private readonly ISynodeLogger _log;
        private bool _running;

        public ExportGlbCommand(Inv.Application app, InventorDocumentService documents, ISaveFileDialogService dialog,
            GlbExportService exporter, IUserMessenger messenger, ISynodeLogger log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
            _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
            _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Called from the button's OnExecute event; never lets an exception escape into Inventor.</summary>
        public void Execute()
        {
            if (_running)
            {
                _log.Warn("Export requested while another export is running; ignored.");
                return;
            }
            _running = true;
            try
            {
                ExportableDocument doc = _documents.GetActiveExportableDocument();
                _log.Info("Export requested for " + doc.Describe());

                string? path = _dialog.AskSavePath(
                    ExportPathHelper.SuggestFileName(doc.FullFileName, doc.DisplayName),
                    ExportPathHelper.SuggestDirectory(doc.FullFileName));
                if (path == null)
                {
                    SetStatus("Synode: export cancelled.");
                    return;
                }

                SetStatus("Synode: exporting '" + doc.DisplayName + "' to GLB...");
                GlbExportResult result = _exporter.Export(doc.Document, path);
                GlbValidationResult v = result.Validation;
                SetStatus("Synode: GLB exported to " + result.OutputPath);

                _messenger.Info(Title,
                    "GLB exported successfully." + Environment.NewLine + Environment.NewLine +
                    "File: " + result.OutputPath + Environment.NewLine +
                    "Size: " + ExportPathHelper.FormatBytes(v.FileLength) + Environment.NewLine +
                    "Meshes: " + v.MeshCount + ", nodes: " + v.NodeCount + ", materials: " + v.MaterialCount + Environment.NewLine +
                    "Triangles: " + v.TriangleCount.ToString("N0", CultureInfo.InvariantCulture) +
                    ", vertices: " + v.VertexCount.ToString("N0", CultureInfo.InvariantCulture) + Environment.NewLine +
                    "Time: " + result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            }
            catch (ExportException ex)
            {
                _log.Error("Export failed [" + ex.Code + "]: " + ex.Message, ex.InnerException);
                SetStatus("Synode: export failed.");
                if (ex.Code == ExportErrorCode.NoActiveDocument || ex.Code == ExportErrorCode.UnsupportedDocument ||
                    ex.Code == ExportErrorCode.NoGeometry)
                {
                    _messenger.Warning(Title, ex.Message);
                }
                else
                {
                    _messenger.Error(Title, "Export to GLB failed." + Environment.NewLine + Environment.NewLine + ex.Message + LogHint());
                }
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException || ex is InvalidOperationException ||
                                       ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                _log.Error("Unexpected error during export.", ex);
                SetStatus("Synode: export failed.");
                _messenger.Error(Title, "Unexpected error during export:" + Environment.NewLine + ex.Message + LogHint());
            }
            finally
            {
                _running = false;
            }
        }

        private string LogHint() =>
            _log.LogFilePath == null ? string.Empty : Environment.NewLine + Environment.NewLine + "Log: " + _log.LogFilePath;

        private void SetStatus(string text)
        {
            try
            {
                _app.StatusBarText = text;
            }
            catch (COMException ex)
            {
                _log.Warn("Could not update status bar: " + ex.Message);
            }
        }
    }
}
