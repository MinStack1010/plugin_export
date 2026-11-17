using System;
using System.Runtime.InteropServices;
using Synode.Inventor.Core;
using Inv = global::Inventor;

namespace Synode.Inventor.AddIn.Services
{
    /// <summary>Hỏi người dùng nơi lưu file .glb (hộp thoại Save của chính Inventor).</summary>
    internal interface ISaveFileDialogService
    {
        /// <returns>Đường dẫn được chọn, hoặc null nếu người dùng huỷ.</returns>
        string? AskSavePath(string suggestedFileName, string initialDirectory);
    }

    internal sealed class InventorSaveFileDialogService : ISaveFileDialogService
    {
        private readonly Inv.Application _app;
        private readonly ISynodeLogger _log;

        public InventorSaveFileDialogService(Inv.Application app, ISynodeLogger log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public string? AskSavePath(string suggestedFileName, string initialDirectory)
        {
            _app.CreateFileDialog(out Inv.FileDialog dialog);
            try
            {
                dialog.DialogTitle = "Synode - Export to GLB";
                dialog.Filter = "glTF 2.0 Binary (*.glb)|*.glb";
                dialog.FilterIndex = 1;
                dialog.InitialDirectory = initialDirectory;
                dialog.FileName = suggestedFileName;
                dialog.OptionsEnabled = false;
                dialog.MultiSelectEnabled = false;
                // CancelError = true: khi người dùng bấm Cancel, ShowSave ném COMException.
                dialog.CancelError = true;
                try
                {
                    dialog.ShowSave();
                }
                catch (COMException ex)
                {
                    _log.Info("Save dialog cancelled by user (HRESULT " + ComHelper.DescribeHResult(ex) + ").");
                    return null;
                }

                string fileName = dialog.FileName;
                return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
            }
            finally
            {
                ComHelper.Release(dialog);
            }
        }
    }
}
