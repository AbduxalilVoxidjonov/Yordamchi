using System.IO;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Services.Conversion;
using PDFtoImage;
using SkiaSharp;

// "Conversion" nomi Yordamchi.Services.Conversion nomlar fazosi bilan to'qnashadi,
// shuning uchun PDFtoImage kutubxonasi taxallus (alias) orqali chaqiriladi.
using Rasterizer = PDFtoImage.Conversion;

namespace Yordamchi.Services;

/// <summary>
/// <see cref="IDocumentConversionService"/> ning asosiy amalga oshirilishi.
/// <para>
/// Ish oqimi doim bir xil: <b>o'qish → oraliq model → yozish</b>.
/// O'qish uchun <see cref="PdfTextExtractor"/> (haqiqiy matn qatlami) yoki
/// <see cref="IOcrService"/> (skaner qilingan sahifalar), yozish uchun esa
/// <see cref="DocxWriter"/>, <c>XlsxWriter</c> va <c>PptxWriter</c> javob beradi.
/// Shuning uchun har bir yangi chiquvchi format uchun PDF ni qaytadan tahlil qilish shart emas.
/// </para>
/// </summary>
public sealed class DocumentConversionService : IDocumentConversionService
{
    private readonly IOcrService _ocr;

    public DocumentConversionService(IOcrService ocr)
        => _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));

    /// <inheritdoc />
    public bool IsMicrosoftWordAvailable
    {
        get
        {
            try
            {
                return OfficeWordInterop.IsAvailable;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }
    }

    // =================================================================================
    //  PDF → Word
    // =================================================================================

    /// <inheritdoc />
    public async Task PdfToWordAsync(
        string pdfPath,
        string docxPath,
        PdfToWordOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options ?? PdfToWordOptions.Default;

        try
        {
            EnsureFileExists(pdfPath);
            EnsureOutputFolder(docxPath);

            progress?.Report(new PdfProgress(0, 100, "PDF o'qilmoqda…"));

            // 0–70% — o'qish, 70–100% — Word'ga yozish.
            var content = await ExtractContentAsync(pdfPath, settings, Scale(progress, 0, 70), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(70, 100, "Word hujjati yozilmoqda…"));
            await Task.Run(() => DocxWriter.Write(content, docxPath, settings, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(100, 100, "Word hujjati tayyor"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, docxPath, "PDF ni Word hujjatiga o'girib bo'lmadi");
        }
    }

    /// <inheritdoc />
    public async Task<DocumentContent> ExtractContentAsync(
        string pdfPath,
        PdfToWordOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options ?? PdfToWordOptions.Default;

        try
        {
            EnsureFileExists(pdfPath);

            var content = settings.Recognition switch
            {
                TextRecognitionMode.ForceOcr => await RecognizeWholeDocumentAsync(pdfPath, OcrOptionsFor(settings.OcrLanguage), progress, cancellationToken)
                    .ConfigureAwait(false),

                TextRecognitionMode.TextLayerOnly => await Task
                    .Run(() => PdfTextExtractor.Extract(pdfPath, settings, progress, cancellationToken), cancellationToken)
                    .ConfigureAwait(false),

                _ => await ExtractAutomaticAsync(pdfPath, settings, progress, cancellationToken).ConfigureAwait(false)
            };

            content.SourcePath ??= pdfPath;
            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, pdfPath, "PDF mazmunini o'qib bo'lmadi");
        }
    }

    /// <summary>
    /// Avtomatik rejim: avval matn qatlami o'qiladi, so'ng "bo'sh" (skaner qilingan) sahifalar
    /// OCR natijasi bilan almashtiriladi.
    /// </summary>
    private async Task<DocumentContent> ExtractAutomaticAsync(
        string pdfPath,
        PdfToWordOptions settings,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var content = await Task
            .Run(() => PdfTextExtractor.Extract(pdfPath, settings, Scale(progress, 0, 60), cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        var minimum = Math.Max(0, settings.MinimumCharactersPerPage);
        var weakPages = new List<int>();
        for (var i = 0; i < content.Pages.Count; i++)
        {
            if (content.Pages[i].CharacterCount < minimum)
                weakPages.Add(i);
        }

        if (weakPages.Count == 0)
            return content;

        progress?.Report(new PdfProgress(60, 100, $"{weakPages.Count} ta sahifada matn topilmadi — OCR ishga tushirilmoqda…"));

        // OCR xizmati sahifa-sahifa metod bermaydi, shuning uchun butun hujjatni taniymiz
        // va faqat matn qatlami bo'sh bo'lgan sahifalarni undan olamiz.
        var recognized = await RecognizeWholeDocumentAsync(pdfPath, OcrOptionsFor(settings.OcrLanguage), Scale(progress, 60, 100), cancellationToken)
            .ConfigureAwait(false);

        var byNumber = new Dictionary<int, ContentPage>();
        foreach (var page in recognized.Pages)
            byNumber[page.Number] = page;

        foreach (var index in weakPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var original = content.Pages[index];
            var number = original.Number > 0 ? original.Number : index + 1;

            if (!byNumber.TryGetValue(number, out var scanned) || scanned.CharacterCount <= original.CharacterCount)
                continue;

            scanned.Number = number;
            scanned.WasRecognized = true;
            if (scanned.WidthPoints <= 0d)
                scanned.WidthPoints = original.WidthPoints;
            if (scanned.HeightPoints <= 0d)
                scanned.HeightPoints = original.HeightPoints;

            content.Pages[index] = scanned;
        }

        return content;
    }

    /// <summary>
    /// OCR xizmatini chaqiradi. Til fayllari yetishmasa
    /// <see cref="PdfErrorKind.MissingComponent"/> xatosi yuqoriga uzatiladi — chaqiruvchi
    /// foydalanuvchiga til paketini yuklab olishni taklif qilishi kerak.
    /// </summary>
    private async Task<DocumentContent> RecognizeWholeDocumentAsync(
        string pdfPath,
        OcrOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Sozlama obyekti chaqiruvchidan keladi va o'zgartirilmaydi — faqat til bo'sh bo'lsa
        // standart to'plamga tushamiz, aks holda Tesseract hech qanday til yuklamaydi.
        if (string.IsNullOrWhiteSpace(options.Language))
            options.Language = OcrOptions.DefaultLanguage;

        var content = await _ocr.RecognizePdfAsync(pdfPath, options, progress, cancellationToken).ConfigureAwait(false);
        content.SourcePath ??= pdfPath;
        return content;
    }

    /// <summary>
    /// Konvertorlar (PDF → Word/Excel/PowerPoint) faqat tilni biladi — qolgan OCR sozlamalari
    /// uchun standart qiymatlar ishlatiladi.
    /// </summary>
    private static OcrOptions OcrOptionsFor(string language) => new()
    {
        Language = string.IsNullOrWhiteSpace(language) ? OcrOptions.DefaultLanguage : language
    };

    // =================================================================================
    //  Word → PDF
    // =================================================================================

    /// <inheritdoc />
    public async Task WordToPdfAsync(
        string docxPath,
        string pdfPath,
        WordToPdfOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options ?? WordToPdfOptions.Default;

        try
        {
            EnsureFileExists(docxPath);
            EnsureOutputFolder(pdfPath);

            var name = Path.GetFileName(docxPath);
            var extension = Path.GetExtension(docxPath);
            var isOpenXml = extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase);

            progress?.Report(new PdfProgress(0, 100, "Word hujjati tayyorlanmoqda…"));

            if (!isOpenXml)
            {
                await ConvertLegacyDocumentAsync(docxPath, pdfPath, name, extension, settings, progress, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var useInterop = settings.Engine switch
            {
                WordToPdfEngine.MicrosoftWord => true,
                WordToPdfEngine.Builtin => false,
                _ => IsMicrosoftWordAvailable
            };

            if (settings.Engine == WordToPdfEngine.MicrosoftWord && !IsMicrosoftWordAvailable)
            {
                throw new PdfServiceException(
                    PdfErrorKind.MissingComponent,
                    "Bu kompyuterda Microsoft Word topilmadi. \"Ichki dvigatel\" variantini tanlang yoki Word'ni o'rnating.",
                    docxPath);
            }

            if (useInterop)
            {
                try
                {
                    await Task.Run(
                        () => OfficeWordInterop.ExportToPdf(docxPath, pdfPath, settings.CreateBookmarks, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException && settings.Engine == WordToPdfEngine.Automatic)
                {
                    // Word ochilmadi yoki COM xato berdi — foydalanuvchini to'xtatmaymiz, ichki dvigatelga tushamiz.
                    progress?.Report(new PdfProgress(10, 100, "Microsoft Word javob bermadi — ichki dvigatelga o'tildi…"));
                    await RenderWithBuiltinAsync(docxPath, pdfPath, settings, progress, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                await RenderWithBuiltinAsync(docxPath, pdfPath, settings, progress, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new PdfProgress(100, 100, "PDF tayyor"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, pdfPath, "Word hujjatini PDF ga o'girib bo'lmadi");
        }
    }

    /// <summary>
    /// Eski formatlar (<c>.doc</c>, <c>.rtf</c>, <c>.odt</c>) — ularni faqat Microsoft Word o'qiy oladi.
    /// </summary>
    private async Task ConvertLegacyDocumentAsync(
        string sourcePath,
        string pdfPath,
        string name,
        string extension,
        WordToPdfOptions settings,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var supportedByWord = extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".odt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

        if (!supportedByWord)
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                $"'{name}' Word hujjati emas. Faqat .docx (va Word o'rnatilgan bo'lsa .doc / .rtf) fayllari qo'llab-quvvatlanadi.",
                sourcePath);
        }

        if (!IsMicrosoftWordAvailable)
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                $"'{name}' eski formatda saqlangan va uni o'qish uchun Microsoft Word kerak. "
                + "Faylni Word'da oching va \"Farqli saqlash → Word hujjati (.docx)\" bilan qayta saqlang, so'ng yana urinib ko'ring.",
                sourcePath);
        }

        await Task.Run(
            () => OfficeWordInterop.ExportToPdf(sourcePath, pdfPath, settings.CreateBookmarks, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new PdfProgress(100, 100, "PDF tayyor"));
    }

    private static Task RenderWithBuiltinAsync(
        string docxPath,
        string pdfPath,
        WordToPdfOptions settings,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
        => Task.Run(
            () => WordToPdfRenderer.Render(docxPath, pdfPath, settings, Scale(progress, 10, 100), cancellationToken),
            cancellationToken);

    // =================================================================================
    //  OCR → Word
    // =================================================================================

    /// <inheritdoc />
    public Task OcrPdfToWordAsync(
        string scannedPdfPath,
        string docxPath,
        string language = OcrOptions.DefaultLanguage,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => OcrPdfToWordAsync(scannedPdfPath, docxPath, OcrOptionsFor(language), progress, cancellationToken);

    /// <inheritdoc />
    public async Task OcrPdfToWordAsync(
        string scannedPdfPath,
        string docxPath,
        OcrOptions options,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            EnsureFileExists(scannedPdfPath);
            EnsureOutputFolder(docxPath);

            progress?.Report(new PdfProgress(0, 100, "Sahifalar tanib olinmoqda…"));

            var content = await RecognizeWholeDocumentAsync(scannedPdfPath, options, Scale(progress, 0, 80), cancellationToken)
                .ConfigureAwait(false);

            var wordOptions = new PdfToWordOptions
            {
                Recognition = TextRecognitionMode.ForceOcr,
                OcrLanguage = options.Language
            };

            progress?.Report(new PdfProgress(80, 100, "Word hujjati yozilmoqda…"));
            await Task.Run(() => DocxWriter.Write(content, docxPath, wordOptions, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(100, 100, "Word hujjati tayyor"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, docxPath, "Skaner qilingan PDF ni Word ga o'girib bo'lmadi");
        }
    }

    // =================================================================================
    //  PDF → Excel / PowerPoint
    // =================================================================================

    /// <inheritdoc />
    public async Task PdfToExcelAsync(
        string pdfPath,
        string xlsxPath,
        PdfToExcelOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options ?? PdfToExcelOptions.Default;

        try
        {
            EnsureFileExists(pdfPath);
            EnsureOutputFolder(xlsxPath);

            // Excel uchun jadval aniqlash eng muhim qadam, sahifa uzilishlari esa keraksiz.
            var readOptions = new PdfToWordOptions
            {
                Recognition = settings.Recognition,
                OcrLanguage = settings.OcrLanguage,
                DetectTables = true,
                DetectHeadings = false,
                ExtractImages = false,
                InsertPageBreaks = false
            };

            progress?.Report(new PdfProgress(0, 100, "PDF o'qilmoqda…"));
            var content = await ExtractContentAsync(pdfPath, readOptions, Scale(progress, 0, 70), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(70, 100, "Excel kitobi yozilmoqda…"));
            await Task.Run(() => XlsxWriter.Write(content, xlsxPath, settings, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(100, 100, "Excel kitobi tayyor"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, xlsxPath, "PDF ni Excel ga o'girib bo'lmadi");
        }
    }

    /// <inheritdoc />
    public async Task PdfToPowerPointAsync(
        string pdfPath,
        string pptxPath,
        PdfToPowerPointOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options ?? PdfToPowerPointOptions.Default;

        try
        {
            EnsureFileExists(pdfPath);
            EnsureOutputFolder(pptxPath);

            var readOptions = new PdfToWordOptions
            {
                Recognition = settings.Recognition,
                OcrLanguage = settings.OcrLanguage,
                DetectTables = true,
                DetectHeadings = true,
                ExtractImages = settings.IncludePageImage,
                InsertPageBreaks = false
            };

            progress?.Report(new PdfProgress(0, 100, "PDF o'qilmoqda…"));
            var content = await ExtractContentAsync(pdfPath, readOptions, Scale(progress, 0, 70), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(70, 100, "Taqdimot yozilmoqda…"));
            await Task.Run(() => PptxWriter.Write(content, pptxPath, settings, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new PdfProgress(100, 100, "Taqdimot tayyor"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, pptxPath, "PDF ni PowerPoint ga o'girib bo'lmadi");
        }
    }

    // =================================================================================
    //  PDF → rasmlar
    // =================================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PdfToImagesAsync(
        string pdfPath,
        string outputFolder,
        PdfToImageOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = options ?? PdfToImageOptions.Default;

        try
        {
            EnsureFileExists(pdfPath);
            EnsureFolderExists(outputFolder);

            // Manba buferga o'qiladi: shunda natijani xuddi shu papkaga yozish xavfsiz bo'ladi.
            var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);

            return await Task.Run<IReadOnlyList<string>>(
                () => RenderImages(bytes, pdfPath, outputFolder, settings, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Wrap(ex, pdfPath, "PDF sahifalarini rasmga o'girib bo'lmadi");
        }
    }

    private static List<string> RenderImages(
        byte[] bytes,
        string pdfPath,
        string outputFolder,
        PdfToImageOptions settings,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sizes = Rasterizer.GetPageSizes(bytes, null);
        if (sizes.Count == 0)
        {
            throw new PdfServiceException(
                PdfErrorKind.EmptySelection,
                $"'{Path.GetFileName(pdfPath)}' ichida sahifa yo'q.",
                pdfPath);
        }

        var dpi = Math.Clamp(settings.Dpi <= 0 ? 150 : settings.Dpi, 36, 900);
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(pdfPath));
        var format = settings.Format switch
        {
            ImageOutputFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageOutputFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };

        // PNG shaffoflikni saqlaydi, JPEG esa umuman qo'llab-quvvatlamaydi — unga doim oq fon kerak.
        var needsWhite = settings.WhiteBackground || settings.Format != ImageOutputFormat.Png;
        var quality = settings.Format == ImageOutputFormat.Png ? 100 : Math.Clamp(settings.JpegQuality, 1, 100);

        var results = new List<string>(sizes.Count);
        for (var index = 0; index < sizes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 72 punkt = 1 dyuym, demak kenglik = sahifa kengligi / 72 × dpi.
            var widthPoints = sizes[index].Width > 0f ? sizes[index].Width : 595f;
            var width = Math.Max(1, (int)Math.Round(widthPoints / 72d * dpi));

            var renderOptions = new RenderOptions
            {
                Width = width,
                WithAspectRatio = true,
                WithAnnotations = true,
                WithFormFill = false,
                AntiAliasing = PdfAntiAliasing.All,
                BackgroundColor = needsWhite ? SKColors.White : null
            };

            using var bitmap = Rasterizer.ToImage(bytes, index, null, renderOptions);
            using var data = bitmap.Encode(format, quality)
                ?? throw new PdfServiceException(
                    PdfErrorKind.UnsupportedImage,
                    $"{index + 1}-sahifani {settings.Extension} formatida saqlab bo'lmadi.",
                    pdfPath);

            var target = Path.Combine(outputFolder, $"{baseName}_{index + 1:D3}{settings.Extension}");
            try
            {
                File.WriteAllBytes(target, data.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PdfServiceException(
                    PdfErrorKind.OutputNotWritable,
                    $"'{Path.GetFileName(target)}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                    target,
                    ex);
            }

            results.Add(target);
            progress?.Report(new PdfProgress(index + 1, sizes.Count, $"{index + 1}-sahifa rasmga o'girildi"));
        }

        return results;
    }

    // =================================================================================
    //  Kichik yordamchilar
    // =================================================================================

    /// <summary>
    /// Ichki bosqichning 0–100% ini umumiy shkalaning <paramref name="from"/>…<paramref name="to"/>
    /// oralig'iga joylaydi.
    /// </summary>
    private static IProgress<PdfProgress>? Scale(IProgress<PdfProgress>? target, int from, int to)
        => target is null ? null : new ScaledProgress(target, from, to);

    private sealed class ScaledProgress(IProgress<PdfProgress> target, int from, int to) : IProgress<PdfProgress>
    {
        public void Report(PdfProgress value)
        {
            var fraction = value.Total > 0
                ? Math.Clamp(value.Completed / (double)value.Total, 0d, 1d)
                : 0d;

            var completed = (int)Math.Round(from + ((to - from) * fraction));
            target.Report(new PdfProgress(Math.Clamp(completed, 0, 100), 100, value.Message));
        }
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "sahifa";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return cleaned.Length == 0 ? "sahifa" : cleaned;
    }

    private static void EnsureFileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, "Fayl ko'rsatilmagan.", path);

        if (!File.Exists(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"'{Path.GetFileName(path)}' topilmadi.", path);
    }

    private static void EnsureOutputFolder(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natija fayli ko'rsatilmagan.", outputPath);

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{outputPath}' — yaroqli fayl yo'li emas.",
                outputPath,
                ex);
        }

        if (!string.IsNullOrEmpty(directory))
            EnsureFolderExists(directory);
    }

    private static void EnsureFolderExists(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natija papkasi ko'rsatilmagan.", folder);

        if (Directory.Exists(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{folder}' papkasini yaratib bo'lmadi.",
                folder,
                ex);
        }
    }

    /// <summary>Kutubxona xatolarini foydalanuvchiga tushunarli <see cref="PdfServiceException"/> ga o'raydi.</summary>
    private static PdfServiceException Wrap(Exception exception, string? filePath, string summary)
    {
        if (exception is PdfServiceException already)
            return already;

        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"{summary}: fayl topilmadi.",
                filePath,
                exception);
        }

        if (exception is UnauthorizedAccessException or IOException)
        {
            return new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"{summary}: faylni yozib bo'lmadi, u boshqa dasturda ochiq bo'lishi mumkin.",
                filePath,
                exception);
        }

        if (exception is NotImplementedException or NotSupportedException)
        {
            return new PdfServiceException(
                PdfErrorKind.MissingComponent,
                $"{summary}: kerakli komponent hali mavjud emas.",
                filePath,
                exception);
        }

        return new PdfServiceException(
            PdfErrorKind.OperationFailed,
            $"{summary}: {exception.Message}",
            filePath,
            exception);
    }
}
