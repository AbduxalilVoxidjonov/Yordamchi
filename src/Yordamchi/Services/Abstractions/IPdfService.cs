using System.Windows.Media.Imaging;
using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>
/// All PDF reading, rasterizing and writing the application needs.
/// <para>
/// Every method is fully asynchronous (the CPU bound pdfium / PDFsharp work runs on the thread
/// pool) and every failure is reported as <see cref="PdfServiceException"/>.
/// Returned <see cref="BitmapSource"/> instances are frozen, so they are safe to hand to the UI
/// thread directly.
/// </para>
/// </summary>
public interface IPdfService
{
    /// <summary>
    /// Renders every page of <paramref name="filePath"/> into a thumbnail.
    /// </summary>
    /// <param name="filePath">Absolute path of an existing PDF.</param>
    /// <param name="thumbnailWidth">Target width in pixels; height follows the page aspect ratio.</param>
    /// <param name="password">Password for protected documents, or <c>null</c>.</param>
    /// <param name="progress">Reports one update per rendered page.</param>
    /// <exception cref="PdfServiceException"/>
    Task<List<PageModel>> RenderPdfPagesAsync(
        string filePath,
        int thumbnailWidth = 220,
        string? password = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Concatenates <paramref name="inputFiles"/> in the given order into a new PDF at
    /// <paramref name="outputPath"/>. Existing bookmarks/outlines of the sources are not kept.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task MergePdfFilesAsync(
        List<string> inputFiles,
        string outputPath,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a new PDF containing only the pages listed in
    /// <paramref name="keepPageIndicesInOrder"/>, in exactly that order. Pages that are not
    /// listed are dropped, and a page may be listed more than once to duplicate it.
    /// </summary>
    /// <param name="sourcePdfPath">Absolute path of the source PDF.</param>
    /// <param name="keepPageIndicesInOrder">Zero-based source page indices, in output order.</param>
    /// <param name="outputPath">Destination path. May be the same as the source (written atomically).</param>
    /// <exception cref="PdfServiceException"/>
    Task ReorderAndDeletePagesAsync(
        string sourcePdfPath,
        List<int> keepPageIndicesInOrder,
        string outputPath,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a new PDF where each image in <paramref name="imagePaths"/> becomes one page.
    /// Supports JPG/JPEG, PNG, BMP, GIF, WEBP and TIFF (first frame).
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task ConvertImagesToPdfAsync(
        List<string> imagePaths,
        string outputPath,
        ImageToPdfOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------------
    // Composite primitive used by the page editor: the four operations above
    // are all special cases of "write this exact list of pages".
    // ---------------------------------------------------------------------

    /// <summary>
    /// Writes an arbitrary page list — possibly mixing several source documents and per-page
    /// rotations — into a single PDF. This is what the page editor calls on save, and what
    /// makes reorder + delete + rotate + merge one operation instead of four.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task BuildPdfAsync(
        IReadOnlyList<PageEdit> pages,
        string outputPath,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Page count of a PDF without rasterizing anything.</summary>
    /// <exception cref="PdfServiceException"/>
    Task<int> GetPageCountAsync(string filePath, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>Renders a single page, e.g. for a large preview pane.</summary>
    /// <exception cref="PdfServiceException"/>
    Task<BitmapSource> RenderPageAsync(
        string filePath,
        int pageIndex,
        int width = 900,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a thumbnail for an image file (JPG/PNG/…), used by the "Image to PDF" gallery.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    Task<BitmapSource> RenderImageThumbnailAsync(
        string imagePath,
        int thumbnailWidth = 220,
        CancellationToken cancellationToken = default);
}
