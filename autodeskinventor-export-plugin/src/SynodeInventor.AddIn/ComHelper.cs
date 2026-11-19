using System;
using System.Runtime.InteropServices;

namespace Synode.Inventor.AddIn
{
    /// <summary>Releases COM references created by the add-in itself (transient objects, UI objects).</summary>
    internal static class ComHelper
    {
        public static void Release(object? comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }

        public static string DescribeHResult(Exception ex)
        {
            return "0x" + ex.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
