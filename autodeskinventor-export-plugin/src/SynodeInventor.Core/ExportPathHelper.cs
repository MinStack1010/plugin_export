using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Synode.Inventor.Core
{
    /// <summary>
    /// Output file path handling (pure .NET, no dependency on Inventor => unit-testable).
    /// </summary>
    public static class ExportPathHelper
    {
        public const string GlbExtension = ".glb";
        public const string DefaultBaseName = "inventor-export";
        public const string TempMarker = ".synode-tmp-";

        /// <summary>Suggested file name: document name (without extension) + ".glb".</summary>
        public static string SuggestFileName(string? fullFileName, string? displayName)
        {
            string? baseName = null;
            if (!string.IsNullOrWhiteSpace(fullFileName))
            {
                baseName = Path.GetFileNameWithoutExtension(fullFileName.Trim());
            }
            if (string.IsNullOrWhiteSpace(baseName) && !string.IsNullOrWhiteSpace(displayName))
            {
                string d = displayName.Trim();
                string ext = Path.GetExtension(d);
                if (ext.Equals(".ipt", StringComparison.OrdinalIgnoreCase) || ext.Equals(".iam", StringComparison.OrdinalIgnoreCase))
                {
                    d = d.Substring(0, d.Length - ext.Length);
                }
                baseName = d;
            }
            baseName = SanitizeFileName(baseName ?? string.Empty);
            if (baseName.Length == 0) baseName = DefaultBaseName;
            return baseName + GlbExtension;
        }

        /// <summary>Suggested folder: the document's folder if available, otherwise the Documents folder.</summary>
        public static string SuggestDirectory(string? fullFileName)
        {
            if (!string.IsNullOrWhiteSpace(fullFileName))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(fullFileName);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
                catch (ArgumentException)
                {
                    // Invalid document path -> use Documents.
                }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Replaces invalid file name characters with '_' and trims trailing spaces/dots.</summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
            }
            return sb.ToString().Trim().TrimEnd('.', ' ');
        }

        /// <summary>
        /// Normalizes the path chosen by the user: must be absolute, extension ".glb"
        /// (".gltf" or any other extension is replaced with ".glb"), and the folder must exist.
        /// </summary>
        public static string NormalizeOutputPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ExportException(ExportErrorCode.InvalidOutputPath, "No output file was selected.");

            string p = path.Trim().Trim('"');
            string full;
            try
            {
                if (!Path.IsPathRooted(p) || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    throw new ExportException(ExportErrorCode.InvalidOutputPath, "The output path is not a valid absolute path: " + p);
                full = Path.GetFullPath(p);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                throw new ExportException(ExportErrorCode.InvalidOutputPath, "The output path is not valid: " + p, ex);
            }

            string fileName = Path.GetFileName(full);
            if (string.IsNullOrEmpty(fileName))
                throw new ExportException(ExportErrorCode.InvalidOutputPath, "The output path does not contain a file name: " + p);

            string ext = Path.GetExtension(full);
            if (!ext.Equals(GlbExtension, StringComparison.OrdinalIgnoreCase))
            {
                bool knownWrongExt = ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase);
                full = (knownWrongExt ? full.Substring(0, full.Length - ext.Length) : full) + GlbExtension;
            }
            else if (!ext.Equals(GlbExtension, StringComparison.Ordinal))
            {
                full = full.Substring(0, full.Length - ext.Length) + GlbExtension;
            }

            string baseName = Path.GetFileNameWithoutExtension(full);
            if (SanitizeFileName(baseName).Length == 0)
                throw new ExportException(ExportErrorCode.InvalidOutputPath, "The output file name is empty: " + p);

            string? dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                throw new ExportException(ExportErrorCode.InvalidOutputPath, "The output folder does not exist: " + (dir ?? p));

            return full;
        }

        /// <summary>Temporary file path in the same folder as the target file, still with the ".glb" extension so the translator writes GLB.</summary>
        public static string MakeTempPath(string finalPath, Guid? id = null)
        {
            string dir = Path.GetDirectoryName(finalPath) ?? throw new ExportException(ExportErrorCode.InvalidOutputPath, "Invalid output path: " + finalPath);
            string baseName = Path.GetFileNameWithoutExtension(finalPath);
            string token = (id ?? Guid.NewGuid()).ToString("N", CultureInfo.InvariantCulture).Substring(0, 12);
            return Path.Combine(dir, baseName + TempMarker + token + GlbExtension);
        }

        /// <summary>Checks write permission on the folder by creating and deleting a probe file.</summary>
        public static void EnsureDirectoryWritable(string directory)
        {
            string probe = Path.Combine(directory, ".synode-write-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                throw new ExportException(ExportErrorCode.OutputNotWritable,
                    "Cannot write to the output folder: " + directory + Environment.NewLine + ex.Message, ex);
            }
        }

        /// <summary>True if the file is locked by another process (cannot be opened for exclusive write access).</summary>
        public static bool IsFileLocked(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only or insufficient permissions: treat as not overwritable.
                return true;
            }
        }

        /// <summary>Moves the temporary file to the target location (overwrites it if it already exists).</summary>
        public static void ReplaceFile(string tempPath, string finalPath)
        {
            try
            {
                File.Move(tempPath, finalPath, true);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Windows returns "Access denied" even when the target file is open in another program.
                bool readOnly = File.Exists(finalPath) && (File.GetAttributes(finalPath) & FileAttributes.ReadOnly) != 0;
                if (!readOnly && IsFileLocked(finalPath))
                {
                    throw new ExportException(ExportErrorCode.OutputLocked,
                        "The output file is in use by another program: " + finalPath, ex);
                }
                throw new ExportException(ExportErrorCode.OutputNotWritable,
                    "Cannot overwrite the output file (read-only or access denied): " + finalPath, ex);
            }
            catch (IOException ex)
            {
                throw new ExportException(ExportErrorCode.OutputLocked,
                    "The output file is in use by another program: " + finalPath + Environment.NewLine + ex.Message, ex);
            }
        }

        /// <summary>Deletes the file if it exists; returns false (without throwing) if it cannot be deleted.</summary>
        public static bool TryDelete(string? path, ISynodeLogger? log)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return true;
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log?.Warn("Could not delete temporary file '" + path + "': " + ex.Message);
                return false;
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
