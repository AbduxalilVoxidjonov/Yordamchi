namespace PdfEdit.Models;

/// <summary>Cause of a failed PDF operation, mapped to a user-facing message by the UI layer.</summary>
public enum PdfErrorKind
{
    Unknown,
    FileNotFound,
    /// <summary>The document needs a password (owner or user password).</summary>
    PasswordProtected,
    /// <summary>The file is not a PDF, or its cross reference table is damaged.</summary>
    CorruptedDocument,
    /// <summary>Image format not supported by the PDF image importer.</summary>
    UnsupportedImage,
    /// <summary>Output path is locked by another process, read-only, or access was denied.</summary>
    OutputNotWritable,
    /// <summary>The requested selection produced an empty document.</summary>
    EmptySelection,
    /// <summary>Page index outside the source document.</summary>
    PageIndexOutOfRange
}

/// <summary>
/// Every failure surfaced by <c>IPdfService</c> is wrapped in this type, so view models can
/// show a meaningful message without knowing anything about PDFsharp or pdfium.
/// </summary>
public sealed class PdfServiceException : Exception
{
    public PdfServiceException(PdfErrorKind kind, string message, string? filePath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        FilePath = filePath;
    }

    public PdfErrorKind Kind { get; }

    /// <summary>The file the operation was working on when it failed, when known.</summary>
    public string? FilePath { get; }
}
