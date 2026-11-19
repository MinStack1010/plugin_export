using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SEC = global::SolidEdge.Constant.Interop;
using SEF = global::SolidEdge.Framework.Interop;

namespace SynodeExporter.AddIn
{
    /// <summary>
    /// Solid Edge COM add-in "Synode Exporter".
    ///
    /// Solid Edge creates this object through the CLSID registered in the registry (Implemented
    /// Category {26B1D2D1-2B03-11d2-B589-080036E8B802}), calls OnConnection when the add-in
    /// is enabled in the Add-In Manager, OnConnectToEnvironment for each registered
    /// environment (Part, Sheet Metal, Assembly) and OnDisconnection when it is disabled or Solid Edge exits.
    /// </summary>
    [ComVisible(true)]
    [Guid(SolidEdgeIds.AddInClsid)]
    [ProgId(SolidEdgeIds.AddInProgId)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class SynodeAddIn : SEF.ISolidEdgeAddIn
    {
        /// <summary>Internal command ID of "Export to GLB".</summary>
        public const int ExportGlbCommandId = 1;

        /// <summary>Increment this value when the UI changes so that Solid Edge recreates the button (bFirstTime = true).</summary>
        public const int GuiVersion = 1;

        /// <summary>RT_BITMAP resource IDs in the DLL (scripts\New-IconResource.ps1).</summary>
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

                // Command string: "<internal name>\n<caption>\n<tooltip>\n<description>".
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

                // Solid Edge returns the runtime command IDs in the array; accept both the internal and the runtime IDs.
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
                // Never crash Solid Edge if the UI cannot be created.
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
            // [out] BSTR per the type library: return the command caption.
            MenuItemText = CommandCaption;
            if (_exportCommandIds.Contains(CommandID))
            {
                CommandFlags = (int)SEC.SECommandActivation.seCmdActive_Enabled;
            }
        }
    }
}
