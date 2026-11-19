using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Synode.Inventor.Core;
using Inv = global::Inventor;

namespace Synode.Inventor.AddIn.Services
{
    internal sealed class GlbExportResult
    {
        public GlbExportResult(string outputPath, GlbValidationResult validation, TimeSpan elapsed)
        {
            OutputPath = outputPath;
            Validation = validation;
            Elapsed = elapsed;
        }

        public string OutputPath { get; }
        public GlbValidationResult Validation { get; }
        public TimeSpan Elapsed { get; }
    }

    /// <summary>
    /// Exports GLB using the NATIVE glTF translator of Inventor 2027 (TranslatorAddIn.SaveCopyAs).
    /// Flow: check path -> get translator -> SaveCopyAs to a temporary file (.glb) in the same folder ->
    /// validate the GLB with GlbValidator -> replace the target file. The temporary file is always cleaned up on failure.
    /// </summary>
    internal sealed class GlbExportService
    {
        private readonly Inv.Application _app;
        private readonly ISynodeLogger _log;

        public GlbExportService(Inv.Application app, ISynodeLogger log)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public GlbExportResult Export(Inv.Document document, string requestedPath)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            string finalPath = ExportPathHelper.NormalizeOutputPath(requestedPath);
            string directory = Path.GetDirectoryName(finalPath)!;
            ExportPathHelper.EnsureDirectoryWritable(directory);
            if (ExportPathHelper.IsFileLocked(finalPath))
            {
                throw new ExportException(ExportErrorCode.OutputLocked,
                    "The output file is open in another program or is read-only:" + Environment.NewLine + finalPath +
                    Environment.NewLine + "Close it (or choose another name) and try again.");
            }

            string tempPath = ExportPathHelper.MakeTempPath(finalPath);
            _log.Info("Export start -> '" + finalPath + "' (temp '" + Path.GetFileName(tempPath) + "')");
            var stopwatch = Stopwatch.StartNew();

            Inv.TranslatorAddIn translator = GetGltfTranslator();
            Inv.TransientObjects transient = _app.TransientObjects;
            Inv.TranslationContext? context = null;
            Inv.NameValueMap? options = null;
            Inv.DataMedium? medium = null;
            bool success = false;
            try
            {
                context = transient.CreateTranslationContext();
                context.Type = Inv.IOMechanismEnum.kFileBrowseIOMechanism;
                options = transient.CreateNameValueMap();
                medium = transient.CreateDataMedium();
                medium.FileName = tempPath;

                bool hasOptions = translator.HasSaveCopyAsOptions[document, context, options];
                // Only log the default options; do NOT set options that Autodesk has not documented.
                _log.Info("glTF translator options (" + (hasOptions ? "has options" : "no options") + "): " + DescribeOptions(options));

                try
                {
                    translator.SaveCopyAs(document, context, options, medium);
                }
                catch (COMException ex)
                {
                    throw new ExportException(ExportErrorCode.TranslatorFailed,
                        "Inventor's glTF translator failed to export the model (HRESULT " + ComHelper.DescribeHResult(ex) + ")." +
                        Environment.NewLine + ex.Message, ex);
                }

                if (!File.Exists(tempPath))
                {
                    string? sibling = FindUnexpectedOutput(tempPath);
                    throw new ExportException(ExportErrorCode.OutputMissing,
                        "Inventor's glTF translator finished but did not create the expected GLB file." +
                        (sibling != null ? Environment.NewLine + "Unexpected output found: " + sibling : string.Empty));
                }

                GlbValidationResult validation = GlbValidator.ValidateFile(tempPath);
                _log.Info("Validation: " + validation.Summary());
                if (!validation.IsValid)
                {
                    throw new ExportException(ExportErrorCode.OutputInvalid,
                        "The exported file is not a valid GLB (glTF 2.0 binary):" + Environment.NewLine + " - " +
                        string.Join(Environment.NewLine + " - ", validation.Errors.GetRange(0, Math.Min(5, validation.Errors.Count))));
                }

                ExportPathHelper.ReplaceFile(tempPath, finalPath);
                stopwatch.Stop();
                success = true;
                _log.Info("Export OK in " + stopwatch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) +
                          " s, " + ExportPathHelper.FormatBytes(new FileInfo(finalPath).Length));
                return new GlbExportResult(finalPath, validation, stopwatch.Elapsed);
            }
            finally
            {
                if (!success) ExportPathHelper.TryDelete(tempPath, _log);
                ComHelper.Release(medium);
                ComHelper.Release(options);
                ComHelper.Release(context);
            }
        }

        /// <summary>Gets the glTF translator by the ClassId read from Inventor's manifest; activates it if not loaded yet.</summary>
        private Inv.TranslatorAddIn GetGltfTranslator()
        {
            Inv.ApplicationAddIn addIn;
            try
            {
                addIn = _app.ApplicationAddIns.ItemById[AddInIds.GltfTranslatorId];
            }
            catch (COMException ex)
            {
                throw new ExportException(ExportErrorCode.TranslatorNotFound,
                    "Inventor's glTF translator (" + AddInIds.GltfTranslatorId + ") is not available in this installation.", ex);
            }

            if (!(addIn is Inv.TranslatorAddIn translator))
            {
                throw new ExportException(ExportErrorCode.TranslatorNotFound,
                    "The add-in " + AddInIds.GltfTranslatorId + " is not a translator add-in.");
            }

            try
            {
                if (!translator.Activated)
                {
                    _log.Info("Activating Inventor glTF translator...");
                    translator.Activate();
                }
            }
            catch (COMException ex)
            {
                throw new ExportException(ExportErrorCode.TranslatorNotFound,
                    "Inventor's glTF translator could not be loaded (HRESULT " + ComHelper.DescribeHResult(ex) + "). " +
                    "Check that it is enabled in Tools > Add-Ins.", ex);
            }

            if (!translator.SupportsSaveCopyAs)
            {
                throw new ExportException(ExportErrorCode.TranslatorNotFound, "Inventor's glTF translator does not support Save Copy As.");
            }
            return translator;
        }

        private static string DescribeOptions(Inv.NameValueMap? map)
        {
            if (map == null || map.Count == 0) return "(none)";
            var sb = new StringBuilder();
            for (int i = 1; i <= map.Count; i++)
            {
                string name = map.Name[i];
                object? value;
                try
                {
                    value = map.Value[name];
                }
                catch (COMException)
                {
                    value = "<unreadable>";
                }
                string text = value == null ? "null" : Marshal.IsComObject(value) ? "<object>" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                if (text.Length > 120) text = text.Substring(0, 120) + "...";
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(name).Append('=').Append(text);
            }
            return sb.ToString();
        }

        /// <summary>If the translator wrote a .gltf instead of a .glb, report it clearly (and do not fake it by renaming).</summary>
        private static string? FindUnexpectedOutput(string tempPath)
        {
            string gltf = Path.ChangeExtension(tempPath, ".gltf");
            return File.Exists(gltf) ? gltf : null;
        }
    }
}
