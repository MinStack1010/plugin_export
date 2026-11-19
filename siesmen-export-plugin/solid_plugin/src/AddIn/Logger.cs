using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SynodeExporter.AddIn
{
    /// <summary>
    /// Writes the log to %LOCALAPPDATA%\SynodeExporter\SolidEdge\logs\SynodeSolidEdge.log.
    /// (The SW plugin writes SynodeSolidWorks.log to the Desktop; LocalAppData is used here
    /// to keep the Desktop clean.) Logging failures never break Solid Edge.
    /// </summary>
    internal static class Logger
    {
        private static readonly object Sync = new object();
        private const long MaxBytes = 5 * 1024 * 1024;

        public static string LogDirectory
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SynodeExporter", "SolidEdge", "logs");
            }
        }

        public static string LogPath
        {
            get { return Path.Combine(LogDirectory, "SynodeSolidEdge.log"); }
        }

        public static void Info(string message)
        {
            Write("INFO ", message);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    var info = new FileInfo(LogPath);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = LogPath + ".1";
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }
                        File.Move(LogPath, old);
                    }
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        + " " + level + " " + message + Environment.NewLine;
                    File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Never throw from the logger.
            }
        }
    }
}
