using System;
using System.Runtime.InteropServices;

namespace Synode.Inventor.AddIn.Ui
{
    /// <summary>Win32 message box (MessageBoxW) owned by the Inventor main window; no WinForms needed.</summary>
    internal interface IUserMessenger
    {
        void Info(string title, string message);
        void Warning(string title, string message);
        void Error(string title, string message);
    }

    internal sealed class NativeMessageBox : IUserMessenger
    {
        private const uint MB_OK = 0x0;
        private const uint MB_ICONERROR = 0x10;
        private const uint MB_ICONWARNING = 0x30;
        private const uint MB_ICONINFORMATION = 0x40;
        private const uint MB_SETFOREGROUND = 0x10000;

        private readonly Func<IntPtr> _owner;

        public NativeMessageBox(Func<IntPtr> owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void Info(string title, string message) => Show(title, message, MB_ICONINFORMATION);
        public void Warning(string title, string message) => Show(title, message, MB_ICONWARNING);
        public void Error(string title, string message) => Show(title, message, MB_ICONERROR);

        private void Show(string title, string message, uint icon)
        {
            MessageBoxW(_owner(), message, title, MB_OK | icon | MB_SETFOREGROUND);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}
