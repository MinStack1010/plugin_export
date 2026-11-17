using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PyHost
{
    /// <summary>
    /// Usage: PyHost [--python-dir DIR] [python arguments...]
    /// DIR defaults to %NX_PYTHON_DIR%, then %UGII_BASE_DIR%\NXBIN\python, then
    /// the first "%ProgramFiles%\Siemens\*\NXBIN\python" containing python312.dll.
    /// All remaining arguments are passed to Py_Main (like python.exe).
    /// </summary>
    internal static class Program
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int PyMainDelegate(int argc, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] argv);

        private static int Main(string[] args)
        {
            string pythonDir = null;
            var rest = args.ToList();
            int idx = rest.IndexOf("--python-dir");
            if (idx >= 0 && idx + 1 < rest.Count)
            {
                pythonDir = rest[idx + 1];
                rest.RemoveRange(idx, 2);
            }

            pythonDir ??= FindPythonDir();
            if (pythonDir == null)
            {
                Console.Error.WriteLine("python312.dll of Siemens NX not found. Use --python-dir or set NX_PYTHON_DIR.");
                return 3;
            }

            string dll = Path.Combine(pythonDir, "python312.dll");
            // sitecustomize.py of NX needs UGII_BASE_DIR (NX root = ...\NXBIN\python\..\..).
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UGII_BASE_DIR")))
            {
                Environment.SetEnvironmentVariable("UGII_BASE_DIR", Path.GetFullPath(Path.Combine(pythonDir, "..", "..")));
            }

            NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (n, a, p) => IntPtr.Zero);
            IntPtr handle = NativeLibrary.Load(dll);
            var pyMain = Marshal.GetDelegateForFunctionPointer<PyMainDelegate>(NativeLibrary.GetExport(handle, "Py_Main"));

            var argv = new[] { Path.Combine(pythonDir, "python.exe") }.Concat(rest).ToArray();
            return pyMain(argv.Length, argv);
        }

        private static string FindPythonDir()
        {
            string env = Environment.GetEnvironmentVariable("NX_PYTHON_DIR");
            if (IsPythonDir(env)) return env;
            string ugii = Environment.GetEnvironmentVariable("UGII_BASE_DIR");
            if (!string.IsNullOrEmpty(ugii) && IsPythonDir(Path.Combine(ugii, "NXBIN", "python")))
                return Path.Combine(ugii, "NXBIN", "python");
            string siemens = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Siemens");
            if (!Directory.Exists(siemens)) return null;
            return Directory.GetDirectories(siemens)
                .Select(d => Path.Combine(d, "NXBIN", "python"))
                .FirstOrDefault(IsPythonDir);
        }

        private static bool IsPythonDir(string dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "python312.dll"));
    }
}
