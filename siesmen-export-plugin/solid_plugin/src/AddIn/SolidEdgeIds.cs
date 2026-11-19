using System;
using System.Collections.Generic;

namespace SynodeExporter.AddIn
{
    /// <summary>COM identifiers and Solid Edge environment categories.</summary>
    public static class SolidEdgeIds
    {
        /// <summary>CLSID dedicated to Synode Exporter (newly generated, verified not to collide with any registry entry).</summary>
        public const string AddInClsid = "42B484DA-1593-41C3-9623-1BFAA4DA645C";

        public const string AddInProgId = "SynodeExporter.SolidEdge.AddIn";

        public const string AddInTitle = "Synode Exporter";

        /// <summary>CATID "Solid Edge Add-In" (Implemented Categories of the add-ins installed with Solid Edge).</summary>
        public const string CatIdSolidEdgeAddIn = "{26B1D2D1-2B03-11d2-B589-080036E8B802}";

        // Environment categories (read from the registration of the Siemens add-ins installed with Solid Edge).
        public const string EnvAssembly = "{26618395-09D6-11d1-BA07-080036230602}";
        public const string EnvPart = "{26618396-09D6-11d1-BA07-080036230602}";
        public const string EnvSheetMetal = "{26618398-09D6-11d1-BA07-080036230602}";
        public const string EnvSyncPart = "{D9B0BB85-3A6C-4086-A0BB-88A1AAD57A58}";
        public const string EnvSyncSheetMetal = "{9CBF2809-FF80-4dbc-98F2-B82DABF3530F}";
        public const string EnvSyncAssembly = "{2C3C2A72-3A4A-471D-98B5-E3A8CFA4A2BF}";

        /// <summary>Environments that get the "Export to GLB" button (Part, Sheet Metal, Assembly - ordered and synchronous).</summary>
        public static readonly IList<string> SupportedEnvironments = new[]
        {
            EnvPart, EnvSyncPart, EnvSheetMetal, EnvSyncSheetMetal, EnvAssembly, EnvSyncAssembly
        };

        public static bool IsSupportedEnvironment(string envCatId)
        {
            if (string.IsNullOrEmpty(envCatId))
            {
                return false;
            }
            Guid id;
            if (!Guid.TryParse(envCatId, out id))
            {
                return false;
            }
            foreach (string env in SupportedEnvironments)
            {
                if (Guid.Parse(env) == id)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
