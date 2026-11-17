using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SEC = global::SolidEdge.Constant.Interop;
using SEF = global::SolidEdge.Framework.Interop;

namespace SynodeExporter.AddIn
{
    /// <summary>
    /// COM add-in Solid Edge "Synode Exporter".
    ///
    /// Solid Edge tạo đối tượng này qua CLSID đăng ký trong registry (Implemented
    /// Category {26B1D2D1-2B03-11d2-B589-080036E8B802}), gọi OnConnection khi add-in
    /// được bật trong Add-In Manager, OnConnectToEnvironment cho từng môi trường
    /// đăng ký (Part, Sheet Metal, Assembly) và OnDisconnection khi tắt/thoát.
    /// </summary>
    [ComVisible(true)]
    [Guid(SolidEdgeIds.AddInClsid)]
    [ProgId(SolidEdgeIds.AddInProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class SynodeAddIn : SEF.ISolidEdgeAddIn
    {
        /// <summary>ID lệnh nội bộ của "Export to GLB".</summary>
        public const int ExportGlbCommandId = 1;

        /// <summary>Tăng giá trị này khi thay đổi giao diện để Solid Edge tạo lại nút (bFirstTime = true).</summary>
        public const int GuiVersion = 1;

        /// <summary>ID tài nguyên RT_BITMAP trong DLL (scripts\New-IconResource.ps1).</summary>
        public const int BitmapMedium = 101;
        public const int BitmapLarge = 102;

        public const string CategoryName = "Synode";
        public const string CommandBarName = "Synode";
        public const string CommandCaption = "Export to GLB";
        public const string CommandTooltip = "Export the active part or assembly to a GLB (glTF 2.0) file";

        private SEF.Application _application;
        private SEF.AddIn _addIn;
        private SEF.ISEAddInEvents_Event _addInEvents;
        private readonly HashSet<int> _exportCommandIds = new HashSet<int> { ExportGlbCommandId };
        private bool _busy;

        public void OnConnection(object Application, SEF.SeConnectMode ConnectMode, SEF.AddIn AddInInstance)
        {
            try
            {
                Logger.Info("OnConnection (mode " + ConnectMode + ", add-in " + typeof(SynodeAddIn).Assembly.Location + ")");
                _application = (SEF.Application)Application;
                _addIn = AddInInstance;
                _addIn.GuiVersion = GuiVersion;

                _addInEvents = (SEF.ISEAddInEvents_Event)_addIn.AddInEvents;
                _addInEvents.OnCommand += OnCommand;
                _addInEvents.OnCommandUpdateUI += OnCommandUpdateUI;
            }
            catch (Exception ex)
            {
                Logger.Error("OnConnection failed", ex);
                throw;
            }
        }

        public void OnConnectToEnvironment(string EnvCatID, object pEnvironmentDispatch, bool bFirstTime)
        {
            try
            {
                if (!SolidEdgeIds.IsSupportedEnvironment(EnvCatID))
                {
                    return;
                }
                Logger.Info("OnConnectToEnvironment " + EnvCatID + " (first time: " + bFirstTime + ")");

                // Chuỗi lệnh: "<tên nội bộ>\n<nhãn>\n<tooltip>\n<mô tả>".
                Array commandNames = new string[]
                {
                    "SynodeExporter_ExportGlb\n" + CommandCaption + "\n" + CommandTooltip + "\n" + CommandCaption
                };
                Array commandIds = new int[] { ExportGlbCommandId };

                var addInEx = (SEF.ISEAddInEx)_addIn;
                addInEx.SetAddInInfoEx(
                    typeof(SynodeAddIn).Assembly.Location,
                    EnvCatID,
                    CategoryName,
                    BitmapMedium,
                    BitmapLarge,
                    BitmapMedium,
                    BitmapLarge,
                    commandIds.Length,
                    ref commandNames,
                    ref commandIds);

                // Solid Edge trả về ID lệnh lúc chạy trong mảng; chấp nhận cả ID nội bộ lẫn ID lúc chạy.
                foreach (object id in commandIds)
                {
                    _exportCommandIds.Add(Convert.ToInt32(id));
                }

                if (bFirstTime)
                {
                    SEF.CommandBarButton button = _addIn.AddCommandBarButton(EnvCatID, CommandBarName, ExportGlbCommandId);
                    if (button != null)
                    {
                        button.Style = SEF.SeButtonStyle.seButtonIconAndCaptionBelow;
                        button.Caption = CommandCaption;
                        button.TooltipText = CommandTooltip;
                        button.DescriptionText = CommandTooltip;
                    }
                }
            }
            catch (Exception ex)
            {
                // Không làm sập Solid Edge nếu không tạo được giao diện.
                Logger.Error("OnConnectToEnvironment failed for " + EnvCatID, ex);
            }
        }

        public void OnDisconnection(SEF.SeDisconnectMode DisconnectMode)
        {
            try
            {
                Logger.Info("OnDisconnection (mode " + DisconnectMode + ")");
                if (_addInEvents != null)
                {
                    _addInEvents.OnCommand -= OnCommand;
                    _addInEvents.OnCommandUpdateUI -= OnCommandUpdateUI;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OnDisconnection failed", ex);
            }
            finally
            {
                _addInEvents = null;
                _addIn = null;
                _application = null;
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void OnCommand(int CommandID)
        {
            if (!_exportCommandIds.Contains(CommandID) || _busy)
            {
                return;
            }
            _busy = true;
            try
            {
                ExportGlbCommand.Execute(_application);
            }
            catch (Exception ex)
            {
                Logger.Error("OnCommand failed", ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnCommandUpdateUI(int CommandID, ref int CommandFlags, out string MenuItemText, ref int BitmapID)
        {
            // [out] BSTR theo type library: trả lại đúng nhãn của lệnh.
            MenuItemText = CommandCaption;
            if (_exportCommandIds.Contains(CommandID))
            {
                CommandFlags = (int)SEC.SECommandActivation.seCmdActive_Enabled;
            }
        }
    }
}
