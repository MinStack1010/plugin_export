using System;
using System.Runtime.InteropServices;
using Inventor;
using Synode.Inventor.Core;

namespace Synode.Inventor.AddIn.Ui
{
    /// <summary>
    /// Creates (idempotently) the "Synode" tab > "Synode" panel > "Export to GLB" button in the Part and Assembly ribbons,
    /// and removes them cleanly when the add-in is unloaded.
    /// </summary>
    internal sealed class RibbonUi : IDisposable
    {
        private readonly Application _app;
        private readonly ISynodeLogger _log;
        private readonly Action _onExecute;
        private ButtonDefinition? _button;
        private UserInterfaceEvents? _uiEvents;
        private stdole.IPictureDisp? _smallIcon;
        private stdole.IPictureDisp? _largeIcon;
        private bool _disposed;

        public RibbonUi(Application app, ISynodeLogger log, Action onExecute)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _onExecute = onExecute ?? throw new ArgumentNullException(nameof(onExecute));
        }

        public void Create()
        {
            ControlDefinitions defs = _app.CommandManager.ControlDefinitions;
            _button = FindButton(defs);
            if (_button == null)
            {
                _smallIcon = IconFactory.Create(16, _log);
                _largeIcon = IconFactory.Create(32, _log);
                _button = defs.AddButtonDefinition(
                    AddInIds.ExportButtonText,
                    AddInIds.ExportButtonInternalName,
                    CommandTypesEnum.kNonShapeEditCmdType,
                    AddInIds.ClientId,
                    "Export the active part or assembly to a glTF 2.0 binary file (.glb).",
                    "Synode: Export to GLB",
                    (object?)_smallIcon ?? Type.Missing,
                    (object?)_largeIcon ?? Type.Missing,
                    ButtonDisplayEnum.kAlwaysDisplayText);
                _log.Info("Button definition created: " + AddInIds.ExportButtonInternalName +
                          (_smallIcon == null ? " (no icon)" : " (with icon)"));
            }
            else
            {
                _log.Info("Reusing existing button definition: " + AddInIds.ExportButtonInternalName);
            }

            _button.OnExecute += OnButtonExecute;

            _uiEvents = _app.UserInterfaceManager.UserInterfaceEvents;
            _uiEvents.OnResetRibbonInterface += OnResetRibbonInterface;

            BuildRibbon();
        }

        private void BuildRibbon()
        {
            if (_button == null) return;
            UserInterfaceManager uim = _app.UserInterfaceManager;
            foreach (string ribbonName in AddInIds.RibbonNames)
            {
                try
                {
                    Ribbon ribbon = uim.Ribbons[ribbonName];
                    RibbonTab tab = FindTab(ribbon) ??
                                    ribbon.RibbonTabs.Add(AddInIds.TabDisplayName, AddInIds.TabInternalName, AddInIds.ClientId, "", false, false);
                    RibbonPanel panel = FindPanel(tab) ??
                                        tab.RibbonPanels.Add(AddInIds.PanelDisplayName, AddInIds.PanelInternalName, AddInIds.ClientId, "", false);
                    if (!PanelHasButton(panel))
                    {
                        panel.CommandControls.AddButton(_button, true, true, "", false);
                    }
                    _log.Info("Ribbon '" + ribbonName + "': tab/panel/button ready.");
                }
                catch (COMException ex)
                {
                    // A failure in one ribbon must not break the other ribbons; log full details.
                    _log.Error("Failed to create UI in ribbon '" + ribbonName + "'.", ex);
                }
            }
        }

        private void OnButtonExecute(NameValueMap context)
        {
            _onExecute();
        }

        private void OnResetRibbonInterface(NameValueMap context)
        {
            _log.Info("OnResetRibbonInterface: rebuilding Synode ribbon UI.");
            BuildRibbon();
        }

        private ButtonDefinition? FindButton(ControlDefinitions defs)
        {
            try
            {
                // ControlDefinitions.Item(InternalName) throws a COMException if it does not exist yet.
                return defs[AddInIds.ExportButtonInternalName] as ButtonDefinition;
            }
            catch (COMException)
            {
                return null; // not created yet => create a new one (not an error)
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static RibbonTab? FindTab(Ribbon ribbon)
        {
            foreach (RibbonTab t in ribbon.RibbonTabs)
            {
                if (t.InternalName == AddInIds.TabInternalName) return t;
            }
            return null;
        }

        private static RibbonPanel? FindPanel(RibbonTab tab)
        {
            foreach (RibbonPanel p in tab.RibbonPanels)
            {
                if (p.InternalName == AddInIds.PanelInternalName) return p;
            }
            return null;
        }

        private static bool PanelHasButton(RibbonPanel panel)
        {
            foreach (CommandControl c in panel.CommandControls)
            {
                if (c.InternalName == AddInIds.ExportButtonInternalName) return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_uiEvents != null)
            {
                _uiEvents.OnResetRibbonInterface -= OnResetRibbonInterface;
                ComHelper.Release(_uiEvents);
                _uiEvents = null;
            }

            RemoveRibbonItems();

            if (_button != null)
            {
                _button.OnExecute -= OnButtonExecute;
                try
                {
                    _button.Delete();
                }
                catch (COMException ex)
                {
                    _log.Error("Failed to delete button definition.", ex);
                }
                ComHelper.Release(_button);
                _button = null;
            }
            ComHelper.Release(_smallIcon);
            ComHelper.Release(_largeIcon);
            _smallIcon = null;
            _largeIcon = null;
        }

        private void RemoveRibbonItems()
        {
            UserInterfaceManager uim = _app.UserInterfaceManager;
            foreach (string ribbonName in AddInIds.RibbonNames)
            {
                try
                {
                    Ribbon ribbon = uim.Ribbons[ribbonName];
                    RibbonTab? tab = FindTab(ribbon);
                    if (tab == null) continue;
                    RibbonPanel? panel = FindPanel(tab);
                    panel?.Delete();
                    // Only delete the tab if it belongs to Synode and has no other panels left.
                    if (tab.RibbonPanels.Count == 0) tab.Delete();
                }
                catch (COMException ex)
                {
                    _log.Error("Failed to remove UI from ribbon '" + ribbonName + "'.", ex);
                }
            }
        }
    }
}
