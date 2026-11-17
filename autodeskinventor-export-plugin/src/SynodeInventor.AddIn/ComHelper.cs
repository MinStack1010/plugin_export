using System;
using System.Runtime.InteropServices;

namespace Synode.Inventor.AddIn
{
    /// <summary>Giải phóng tham chiếu COM do chính add-in tạo ra (transient objects, UI objects).</summary>
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
