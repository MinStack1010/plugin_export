using System;

namespace Synode.Inventor.Core
{
    /// <summary>Phân loại lỗi export để hiển thị thông báo rõ ràng và kiểm thử được.</summary>
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

    /// <summary>Lỗi export có mã phân loại; Message là thông báo cho người dùng (tiếng Anh, như plugin SW).</summary>
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
