using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Synode.Inventor.Core
{
    /// <summary>Simple logger injected into the services (no global static state).</summary>
    public interface ISynodeLogger
    {
        string? LogFilePath { get; }
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? exception = null);
    }

    /// <summary>
    /// Writes the log to a UTF-8 text file. Does not log sensitive information (user names, emails, tokens).
    /// Logging failures must NOT break the export operation: they are redirected to Trace.
    /// </summary>
    public sealed class FileLogger : ISynodeLogger
    {
        private const long MaxBytes = 5L * 1024 * 1024;
        private readonly object _sync = new object();

        public FileLogger(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath)) throw new ArgumentException("Log path is empty.", nameof(logFilePath));
            LogFilePath = logFilePath;
        }

        public string? LogFilePath { get; }

        /// <summary>%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log</summary>
        public static string DefaultLogPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            return Path.Combine(root, "Synode", "InventorExporter", "logs", "SynodeInventor.log");
        }

        public static FileLogger CreateDefault() => new FileLogger(DefaultLogPath());

        public void Info(string message) => Write("INFO ", message, null);
        public void Warn(string message) => Write("WARN ", message, null);
        public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

        private void Write(string level, string message, Exception? exception)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
              .Append(" [").Append(level).Append("] ").Append(message);
            if (exception != null)
            {
                sb.AppendLine().Append("    ").Append(exception.GetType().FullName)
                  .Append(" (HResult 0x").Append(exception.HResult.ToString("X8", CultureInfo.InvariantCulture)).Append("): ")
                  .Append(exception.Message);
                if (exception.StackTrace != null) sb.AppendLine().Append(exception.StackTrace);
                if (exception.InnerException != null)
                {
                    sb.AppendLine().Append("    Inner: ").Append(exception.InnerException.GetType().FullName)
                      .Append(": ").Append(exception.InnerException.Message);
                }
            }
            sb.AppendLine();
            string line = sb.ToString();

            lock (_sync)
            {
                try
                {
                    string path = LogFilePath!;
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = Path.ChangeExtension(path, ".old.log");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
                {
                    Trace.WriteLine("SynodeInventor log write failed: " + ex.Message);
                    Trace.Write(line);
                }
            }
        }
    }
}
