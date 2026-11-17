using System;
using System.Runtime.InteropServices;
using Synode.Inventor.AddIn.Commands;
using Synode.Inventor.AddIn.Services;
using Synode.Inventor.AddIn.Ui;
using Synode.Inventor.Core;
using Inv = global::Inventor;

namespace Synode.Inventor.AddIn
{
    /// <summary>
    /// Điểm vào add-in. Inventor tạo lớp này theo ClassId trong file Synode.InventorExporter.addin
    /// (không cần đăng ký COM/RegAsm). Tương đương SwAddin (ConnectToSW/DisconnectFromSW) của plugin SolidWorks.
    /// </summary>
    [Guid(AddInIds.AddInGuid)]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class StandardAddInServer : Inv.ApplicationAddInServer
    {
        private Inv.Application? _app;
        private ISynodeLogger? _log;
        private RibbonUi? _ui;

        public void Activate(Inv.ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            _log = FileLogger.CreateDefault();
            try
            {
                _app = addInSiteObject.Application;
                Inv.SoftwareVersion version = _app.SoftwareVersion;
                _log.Info("Activate: " + AddInIds.DisplayName + " " + typeof(StandardAddInServer).Assembly.GetName().Version +
                          " | Inventor " + version.DisplayVersion + " (major " + version.Major + ")" +
                          " | .NET " + Environment.Version + " | firstTime=" + firstTime);

                var messenger = new NativeMessageBox(MainWindowHandle);
                var command = new ExportGlbCommand(
                    _app,
                    new InventorDocumentService(_app, _log),
                    new InventorSaveFileDialogService(_app, _log),
                    new GlbExportService(_app, _log),
                    messenger,
                    _log);

                _ui = new RibbonUi(_app, _log, command.Execute);
                _ui.Create();
                _log.Info("Activate completed.");
            }
            catch (Exception ex)
            {
                // Ghi log rồi ném lại để Inventor biết add-in nạp thất bại (không nuốt lỗi).
                _log.Error("Activate failed.", ex);
                throw;
            }
        }

        public void Deactivate()
        {
            try
            {
                _ui?.Dispose();
                _log?.Info("Deactivate completed.");
            }
            catch (COMException ex)
            {
                _log?.Error("Error while removing UI during Deactivate.", ex);
            }
            finally
            {
                _ui = null;
                ComHelper.Release(_app);
                _app = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>Không dùng (API cũ, Inventor dùng ControlDefinitions thay thế).</summary>
        public void ExecuteCommand(int commandID)
        {
        }

        /// <summary>Không cung cấp API automation.</summary>
        public object? Automation => null;

        private IntPtr MainWindowHandle()
        {
            try
            {
                return _app == null ? IntPtr.Zero : new IntPtr(_app.MainFrameHWND);
            }
            catch (COMException)
            {
                return IntPtr.Zero;
            }
        }
    }
}
