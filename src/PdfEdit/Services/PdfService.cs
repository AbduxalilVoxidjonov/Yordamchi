using System.IO;
using System.Windows.Media.Imaging;
using PdfEdit.Helpers;
using PdfEdit.Models;
using PdfEdit.Services.Abstractions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PDFtoImage;
using SkiaSharp;

// PdfSharp also defines a PageRotation/PageSize pair; alias them so PdfEdit.Models wins.
using PdfPageOrientation = PdfSharp.PageOrientation;
using PdfPageSize = PdfSharp.PageSize;

namespace PdfEdit.Services;

/// <summary>
/// Default <see cref="IPdfService"/>: pdfium (via PDFtoImage) rasterizes, PDFsharp writes.
/// <para>
/// Two invariants run through the whole class. Sources are always read into a byte buffer before
/// PDFsharp touches them, so no file handle is held on the input while the output is written; and
/// the output is always written to a sibling temporary file that is then moved over the target, so
/// "save over the document I just opened" can never leave a half written PDF behind.
/// </para>
/// </summary>
public sealed class PdfService : IPdfService
{
    /// <inheritdoc />
    public async Task<List<PageModel>> RenderPdfPagesAsync(
        string filePath,
        int thumbnailWidth = 220,
        string? password = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(filePath);
            var pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            return await Task.Run(async () =>
            {
                // Points, and pdfium has already folded the page's own /Rotate into them.
                var sizes = Conversion.GetPageSizes(pdfBytes, password);
                if (sizes.Count == 0)
                    throw new PdfServiceException(PdfErrorKind.EmptySelection, $"'{Path.GetFileName(filePath)}' contains no pages.", filePath);

                var options = CreateRenderOptions(thumbnailWidth);
                var pages = new List<PageModel>(sizes.Count);
                var index = 0;

                await foreach (var bitmap in Conversion
                    .ToImagesAsync(pdfBytes, password, options, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (bitmap)
                    {
                        var size = index < sizes.Count ? sizes[index] : default;
                        pages.Add(new PageModel
                        {
                            SourceFilePath = filePath,
                            SourcePageIndex = index,
                            Thumbnail = SkiaImageHelper.ToFrozenBitmapImage(bitmap),
                            WidthPoints = size.Width,
                            HeightPoints = size.Height
                        });
                    }

                    index++;
                    progress?.Report(new PdfProgress(index, sizes.Count, $"Rendering page {index} of {sizes.Count}"));
                }

                return pages;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, filePath);
        }
    }

    /// <inheritdoc />
    public async Task MergePdfFilesAsync(
        List<string> inputFiles,
        string outputPath,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (inputFiles is null || inputFiles.Count == 0)
                throw new PdfServiceException(PdfErrorKind.EmptySelection, "No files were selected to merge.", outputPath);

            ValidateOutputPath(outputPath);
            foreach (var file in inputFiles)
                EnsureFileExists(file);

            var buffers = new List<(string Path, byte[] Bytes)>(inputFiles.Count);
            foreach (var file in inputFiles)
                buffers.Add((file, await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false)));

            await Task.Run(() => MergeCore(buffers, outputPath, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, outputPath);
        }
    }

    /// <inheritdoc />
    public Task ReorderAndDeletePagesAsync(
        string sourcePdfPath,
        List<int> keepPageIndicesInOrder,
        string outputPath,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (keepPageIndicesInOrder is null || keepPageIndicesInOrder.Count == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, "The result would have no pages.", outputPath);

        // Reorder + delete is just a page list, so the one write path below does the real work.
        var edits = keepPageIndicesInOrder
            .Select(index => new PageEdit(sourcePdfPath, index))
            .ToList();

        return BuildPdfAsync(edits, outputPath, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ConvertImagesToPdfAsync(
        List<string> imagePaths,
        string outputPath,
        ImageToPdfOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (imagePaths is null || imagePaths.Count == 0)
                throw new PdfServiceException(PdfErrorKind.EmptySelection, "No images were selected.", outputPath);

            ValidateOutputPath(outputPath);
            foreach (var image in imagePaths)
                EnsureFileExists(image);

            var settings = options ?? ImageToPdfOptions.Default;
            var paths = imagePaths.ToList();

            await Task.Run(() => ConvertImagesCore(paths, outputPath, settings, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, outputPath, PdfErrorKind.UnsupportedImage);
        }
    }

    /// <inheritdoc />
    public async Task BuildPdfAsync(
        IReadOnlyList<PageEdit> pages,
        string outputPath,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (pages is null || pages.Count == 0)
                throw new PdfServiceException(PdfErrorKind.EmptySelection, "The result would have no pages.", outputPath);

            ValidateOutputPath(outputPath);

            var sourcePaths = pages
                .Select(page => page.SourceFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var source in sourcePaths)
                EnsureFileExists(source);

            // Buffer every source up front: the handles must be closed before we write, otherwise
            // saving on top of one of the sources would fail.
            var buffers = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in sourcePaths)
                buffers[source] = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);

            var plan = pages.ToList();
            await Task.Run(() => BuildCore(plan, buffers, outputPath, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, outputPath);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetPageCountAsync(string filePath, string? password = null, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(filePath);
            var pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            // The string overloads of Conversion expect base64, not a path - always pass bytes.
            return await Task.Run(() => Conversion.GetPageCount(pdfBytes, password), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, filePath);
        }
    }

    /// <inheritdoc />
    public async Task<BitmapSource> RenderPageAsync(
        string filePath,
        int pageIndex,
        int width = 900,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(filePath);
            var pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

            return await Task.Run<BitmapSource>(() =>
            {
                var pageCount = Conversion.GetPageCount(pdfBytes, password);
                if (pageIndex < 0 || pageIndex >= pageCount)
                    throw new PdfServiceException(
                        PdfErrorKind.PageIndexOutOfRange,
                        $"Page {pageIndex + 1} does not exist in '{Path.GetFileName(filePath)}' ({pageCount} pages).",
                        filePath);

                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = Conversion.ToImage(pdfBytes, pageIndex, password, CreateRenderOptions(width));
                return SkiaImageHelper.ToFrozenBitmapImage(bitmap);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, filePath);
        }
    }

    /// <inheritdoc />
    public async Task<BitmapSource> RenderImageThumbnailAsync(
        string imagePath,
        int thumbnailWidth = 220,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(imagePath);

            return await Task.Run<BitmapSource>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = SkiaImageHelper.DecodeOriented(imagePath);
                try
                {
                    bitmap = SkiaImageHelper.ScaleToWidth(bitmap, thumbnailWidth);
                    cancellationToken.ThrowIfCancellationRequested();
                    return SkiaImageHelper.ToFrozenBitmapImage(bitmap);
                }
                finally
                {
                    bitmap.Dispose();
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, imagePath, PdfErrorKind.UnsupportedImage);
        }
    }

    private static RenderOptions CreateRenderOptions(int width) => new()
    {
        Width = Math.Max(1, width),
        WithAspectRatio = true,
        WithAnnotations = true,
        WithFormFill = false,
        AntiAliasing = PdfAntiAliasing.All,
        // Without this pages come back transparent and read as black on a dark theme.
        BackgroundColor = SKColors.White
    };

    private static void MergeCore(
        List<(string Path, byte[] Bytes)> sources,
        string outputPath,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var output = new PdfDocument();
        var done = 0;

        foreach (var (path, bytes) in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = OpenForImport(path, bytes);
            for (var i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.AddPage(document.Pages[i]);
            }

            done++;
            progress?.Report(new PdfProgress(done, sources.Count, Path.GetFileName(path)));
        }

        if (output.PageCount == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, "The selected files contain no pages.", outputPath);

        SaveAtomically(output, outputPath);
    }

    private static void BuildCore(
        List<PageEdit> pages,
        Dictionary<string, byte[]> buffers,
        string outputPath,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sources = new Dictionary<string, PdfDocument>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (path, bytes) in buffers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sources[path] = OpenForImport(path, bytes);
            }

            // Validate the whole plan before a single byte of output is produced.
            foreach (var edit in pages)
            {
                var document = sources[edit.SourceFilePath];
                if (edit.SourcePageIndex < 0 || edit.SourcePageIndex >= document.PageCount)
                    throw new PdfServiceException(
                        PdfErrorKind.PageIndexOutOfRange,
                        $"Page {edit.SourcePageIndex + 1} does not exist in '{Path.GetFileName(edit.SourceFilePath)}' ({document.PageCount} pages).",
                        edit.SourceFilePath);
            }

            using var output = new PdfDocument();
            var done = 0;
            foreach (var edit in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var added = output.AddPage(sources[edit.SourceFilePath].Pages[edit.SourcePageIndex]);
                if (edit.Rotation != PageRotation.None)
                    added.Rotate = (added.Rotate + (int)edit.Rotation) % 360;

                done++;
                progress?.Report(new PdfProgress(done, pages.Count, $"Writing page {done} of {pages.Count}"));
            }

            SaveAtomically(output, outputPath);
        }
        finally
        {
            foreach (var document in sources.Values)
                document.Dispose();
        }
    }

    private static void ConvertImagesCore(
        List<string> imagePaths,
        string outputPath,
        ImageToPdfOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var document = new PdfDocument();
        var done = 0;

        foreach (var imagePath in imagePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddImagePage(document, imagePath, options);

            done++;
            progress?.Report(new PdfProgress(done, imagePaths.Count, Path.GetFileName(imagePath)));
        }

        SaveAtomically(document, outputPath);
    }

    private static void AddImagePage(PdfDocument document, string imagePath, ImageToPdfOptions options)
    {
        // PDFsharp copies the encoded bytes into the document during DrawImage, so the image and
        // its backing stream can be released per page instead of piling up until the save.
        MemoryStream? encoded = null;
        XImage image;
        try
        {
            if (CanEmbedWithoutReencoding(imagePath, options))
            {
                // Lossless fast path: the original JPEG/PNG bytes go in untouched.
                image = XImage.FromFile(imagePath);
            }
            else
            {
                encoded = ReencodeForPdf(imagePath, options);
                image = XImage.FromStream(encoded);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            encoded?.Dispose();
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedImage,
                $"'{Path.GetFileName(imagePath)}' could not be read as an image.",
                imagePath,
                ex);
        }

        try
        {
            var page = document.AddPage();
            if (options.PageSizeMode == PdfPageSizeMode.FitToImage)
                DrawFitToImage(page, image, options);
            else
                DrawOnFixedPage(page, image, options);
        }
        finally
        {
            image.Dispose();
            encoded?.Dispose();
        }
    }

    private static void DrawFitToImage(PdfPage page, XImage image, ImageToPdfOptions options)
    {
        var fallbackDpi = options.ImageDpi > 0 ? options.ImageDpi : 96d;
        var dpiX = image.HorizontalResolution > 0 ? image.HorizontalResolution : fallbackDpi;
        var dpiY = image.VerticalResolution > 0 ? image.VerticalResolution : fallbackDpi;

        var widthPoints = Math.Max(1d, image.PixelWidth / dpiX * 72d);
        var heightPoints = Math.Max(1d, image.PixelHeight / dpiY * 72d);

        page.Width = XUnit.FromPoint(widthPoints);
        page.Height = XUnit.FromPoint(heightPoints);

        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(image, 0d, 0d, widthPoints, heightPoints);
    }

    private static void DrawOnFixedPage(PdfPage page, XImage image, ImageToPdfOptions options)
    {
        page.Size = options.PageSizeMode == PdfPageSizeMode.Letter ? PdfPageSize.Letter : PdfPageSize.A4;
        if (options.AutoOrientation && image.PixelWidth > image.PixelHeight)
            page.Orientation = PdfPageOrientation.Landscape;

        var pageWidth = page.Width.Point;
        var pageHeight = page.Height.Point;

        // Keep at least a sliver of drawable area even if the caller asks for absurd margins.
        var margin = Math.Max(0d, Math.Min(options.MarginPoints, Math.Min(pageWidth, pageHeight) / 2d - 1d));
        var availableWidth = pageWidth - (2d * margin);
        var availableHeight = pageHeight - (2d * margin);

        var scale = Math.Min(availableWidth / image.PixelWidth, availableHeight / image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(image, (pageWidth - width) / 2d, (pageHeight - height) / 2d, width, height);
    }

    /// <summary>
    /// True when the file can be handed to PDFsharp as-is: a format it embeds natively, small
    /// enough already, and with no EXIF rotation that would have to be baked in.
    /// </summary>
    private static bool CanEmbedWithoutReencoding(string imagePath, ImageToPdfOptions options)
    {
        var extension = Path.GetExtension(imagePath);
        var isNativeFormat = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);

        if (!isNativeFormat)
            return false;

        if (SkiaImageHelper.ReadOrigin(imagePath) != SKEncodedOrigin.TopLeft)
            return false;

        if (options.MaxImageEdgePixels <= 0)
            return true;

        var size = SkiaImageHelper.ReadPixelSize(imagePath);
        return size is null || Math.Max(size.Value.Width, size.Value.Height) <= options.MaxImageEdgePixels;
    }

    private static MemoryStream ReencodeForPdf(string imagePath, ImageToPdfOptions options)
    {
        var bitmap = SkiaImageHelper.DecodeOriented(imagePath);
        try
        {
            bitmap = SkiaImageHelper.LimitMaxEdge(bitmap, options.MaxImageEdgePixels);

            var hasAlpha = SkiaImageHelper.HasTransparency(bitmap);
            var format = hasAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
            var quality = hasAlpha ? 100 : Math.Clamp(options.JpegQuality, 1, 100);

            using var data = bitmap.Encode(format, quality)
                ?? throw new NotSupportedException($"'{Path.GetFileName(imagePath)}' could not be re-encoded.");
            return new MemoryStream(data.ToArray(), writable: false);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static PdfDocument OpenForImport(string path, byte[] bytes)
    {
        try
        {
            // The MemoryStream is owned by the returned document and released with it.
            var stream = new MemoryStream(bytes, writable: false);
            return PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            throw Wrap(ex, path, PdfErrorKind.CorruptedDocument);
        }
    }

    /// <summary>
    /// Writes to a sibling temp file and moves it over the target, so the destination is either the
    /// old file or the complete new one - never a truncated mix. This is what makes saving on top of
    /// the document currently open in the editor safe.
    /// </summary>
    private static void SaveAtomically(PdfDocument document, string outputPath)
    {
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            document.Save(tempPath);
            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(outputPath)}' could not be written. It may be open in another program.",
                outputPath,
                ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file is not worth failing an otherwise successful save.
        }
    }

    private static void EnsureFileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, "No file was specified.", path);

        if (!File.Exists(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"'{Path.GetFileName(path)}' was not found.", path);
    }

    private static void ValidateOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "No output file was specified.", outputPath);

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, $"'{outputPath}' is not a valid file path.", outputPath, ex);
        }

        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, $"The folder '{directory}' could not be created.", outputPath, ex);
        }
    }

    /// <summary>Maps a library exception onto the <see cref="PdfErrorKind"/> the UI understands.</summary>
    private static PdfServiceException Wrap(Exception exception, string? filePath, PdfErrorKind fallback = PdfErrorKind.Unknown)
    {
        var name = string.IsNullOrEmpty(filePath) ? "The file" : $"'{Path.GetFileName(filePath)}'";

        if (exception is PdfServiceException already)
            return already;

        if (exception is FileNotFoundException or DirectoryNotFoundException)
            return new PdfServiceException(PdfErrorKind.FileNotFound, $"{name} was not found.", filePath, exception);

        if (MentionsPassword(exception))
            return new PdfServiceException(PdfErrorKind.PasswordProtected, $"{name} is password protected.", filePath, exception);

        if (exception is PdfReaderException or PdfSharp.PdfSharpException || exception.GetType().Name.Contains("Pdfium", StringComparison.Ordinal))
            return new PdfServiceException(PdfErrorKind.CorruptedDocument, $"{name} is damaged or is not a PDF.", filePath, exception);

        if (exception is UnauthorizedAccessException or IOException)
            return new PdfServiceException(PdfErrorKind.OutputNotWritable, $"{name} could not be written. It may be open in another program.", filePath, exception);

        if (fallback == PdfErrorKind.UnsupportedImage)
            return new PdfServiceException(PdfErrorKind.UnsupportedImage, $"{name} is not a supported image.", filePath, exception);

        if (fallback == PdfErrorKind.CorruptedDocument)
            return new PdfServiceException(PdfErrorKind.CorruptedDocument, $"{name} is damaged or is not a PDF.", filePath, exception);

        return new PdfServiceException(PdfErrorKind.Unknown, $"{name} could not be processed.", filePath, exception);
    }

    private static bool MentionsPassword(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
