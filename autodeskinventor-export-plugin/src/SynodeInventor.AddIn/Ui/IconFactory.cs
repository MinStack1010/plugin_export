using System;
using System.Runtime.InteropServices;
using Synode.Inventor.Core;

namespace Synode.Inventor.AddIn.Ui
{
    /// <summary>
    /// Creates an icon as stdole.IPictureDisp (the type Inventor requires for a ControlDefinition) from the pixels
    /// drawn by <see cref="IconRenderer"/>: GDI CreateDIBSection (32bpp) + OleCreatePictureIndirect.
    /// On failure => returns null and the button is still displayed as text.
    /// </summary>
    internal static class IconFactory
    {
        public static stdole.IPictureDisp? Create(int size, ISynodeLogger log)
        {
            IntPtr hBitmap = IntPtr.Zero;
            try
            {
                uint[] topDown = IconRenderer.Render(size);

                var bmi = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = size, // positive = bottom-up (best compatibility with OLE pictures)
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                };
                hBitmap = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    log.Warn("Icon: CreateDIBSection failed (Win32 error " + Marshal.GetLastWin32Error() + ").");
                    return null;
                }

                var row = new int[size];
                for (int y = 0; y < size; y++)
                {
                    int srcRow = size - 1 - y; // flip rows for bottom-up
                    for (int x = 0; x < size; x++) row[x] = unchecked((int)topDown[srcRow * size + x]);
                    Marshal.Copy(row, 0, bits + y * size * 4, size);
                }

                var desc = new PICTDESC
                {
                    cbSizeofstruct = (uint)Marshal.SizeOf<PICTDESC>(),
                    picType = 1, // PICTYPE_BITMAP
                    hbitmap = hBitmap,
                    hpal = IntPtr.Zero
                };
                Guid iid = typeof(stdole.IPictureDisp).GUID;
                int hr = OleCreatePictureIndirect(ref desc, ref iid, true, out object picture);
                if (hr < 0 || picture == null)
                {
                    log.Warn("Icon: OleCreatePictureIndirect failed (HRESULT 0x" + hr.ToString("X8") + ").");
                    return null;
                }
                hBitmap = IntPtr.Zero; // the picture owns the HBITMAP (fOwn = true)
                return (stdole.IPictureDisp)picture;
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException || ex is ExternalException || ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                log.Error("Icon creation failed; the button will be shown without an icon.", ex);
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        /// <summary>PICTDESC with the "bmp" union branch (HBITMAP + HPALETTE).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PICTDESC
        {
            public uint cbSizeofstruct;
            public uint picType;
            public IntPtr hbitmap;
            public IntPtr hpal;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("oleaut32.dll", PreserveSig = true)]
        private static extern int OleCreatePictureIndirect(ref PICTDESC pictDesc, ref Guid riid, [MarshalAs(UnmanagedType.Bool)] bool fOwn,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
    }
}
