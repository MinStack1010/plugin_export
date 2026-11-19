using System;
using System.Diagnostics;
using System.IO;

namespace PluginSynode
{
    public static class Logger
    {
        private static readonly string LogFile =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "SynodeSolidWorks.log");

        public static void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}";

            Debug.WriteLine(line);
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
    }
}
