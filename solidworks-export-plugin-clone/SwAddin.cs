using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PluginSynode
{
    [ComVisible(true)]
    [Guid("d2717311-4a25-4677-877a-fa8d5ec18c71")]
    [ProgId("PluginSolidWorks.SwAddin")]
    public class SwAddin : ISwAddin
    {
        private const int CommandGroupId = 1001;

        private SldWorks swApp;
        private int addinCookie;
        private ICommandManager commandManager;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAddinAssemblies;

            swApp = (SldWorks)ThisSW;
            addinCookie = Cookie;

            swApp.SetAddinCallbackInfo2(0, this, addinCookie);

            AddCommandManager();

            return true;
        }

        public bool DisconnectFromSW()
        {
            try
            {
                if (commandManager != null)
                {
                    commandManager.RemoveCommandGroup(CommandGroupId);
                }
            }
            catch
            {
                // Avoid crashing SOLIDWORKS during unload.
            }
            
            swApp = null;
            commandManager = null;

            return true;
        }

        [ComRegisterFunction]
        public static void Register(Type t)
        {
            using (var hklm = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\SolidWorks\AddIns\" + t.GUID.ToString("B")))
            {
                hklm.SetValue(null, 1);
                hklm.SetValue("Title", "Synode Plugin");
                hklm.SetValue("Description", "SOLIDWORKS add-in for Synode.");
            }

            using (var hkcu = Registry.CurrentUser.CreateSubKey(
                @"Software\SolidWorks\AddInsStartup\" + t.GUID.ToString("B")))
            {
                hkcu.SetValue(null, 1);
            }
        }

        [ComUnregisterFunction]
        public static void Unregister(Type t)
        {
            string keyPath = $@"SOFTWARE\SolidWorks\AddIns\{{{t.GUID}}}";
            Registry.LocalMachine.DeleteSubKeyTree(keyPath, false);
        }

        public void OnExportGlbClicked()
        {
            ExportGlbCommand.Execute(swApp);
        }

        public int CanExportGlb()
        {
            return swApp.ActiveDoc != null ? 1 : 0;
        }

        private void AddCommandManager()
        {
            commandManager = swApp.GetCommandManager(addinCookie);

            int errors = 0;

            ICommandGroup cmdGroup = commandManager.CreateCommandGroup2(
                CommandGroupId,
                "Synode GLB Exporter",
                "Export SOLIDWORKS geometry to GLB",
                "Export SOLIDWORKS geometry to GLB",
                -1,
                true,
                ref errors
            );

            int menuToolbarOption =
                (int)swCommandItemType_e.swMenuItem |
                (int)swCommandItemType_e.swToolbarItem;

            cmdGroup.AddCommandItem2(
                "Export to GLB",
                -1,
                "Export active SOLIDWORKS model to GLB",
                "Export to GLB",
                0,
                "OnExportGlbClicked",
                "CanExportGlb",
                0,
                menuToolbarOption
            );

            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu = true;
            cmdGroup.Activate();
        }

        private static Assembly ResolveAddinAssemblies(object sender, ResolveEventArgs args)
        {
            string requestedName = new AssemblyName(args.Name).Name;

            string addinFolder = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location
            );

            string dllPath = Path.Combine(addinFolder, requestedName + ".dll");

            if (File.Exists(dllPath))
            {
                return Assembly.LoadFrom(dllPath);
            }

            return null;
        }
    }
}
