using System;

namespace Synode.Inventor.Core
{
    /// <summary>Export error categories, used to show clear messages and to keep errors testable.</summary>
    public enum ExportErrorCode
    {
        None = 0,
        NoApplication,
        NoActiveDocument,
        UnsupportedDocument,
        NoGeometry,
        InvalidOutputPath,
        OutputNotWritable,
        OutputLocked,
        TranslatorNotFound,
        TranslatorFailed,
        OutputMissing,
        OutputInvalid,
        Cancelled,
        Unexpected
    }

    /// <summary>Export error with a category code; Message is the user-facing message (in English, as in the SolidWorks plugin).</summary>
    public sealed class ExportException : Exception
    {
        public ExportException(ExportErrorCode code, string message, Exception? inner = null)
            : base(message, inner)
        {
            Code = code;
        }

        public ExportErrorCode Code { get; }
    }
}
