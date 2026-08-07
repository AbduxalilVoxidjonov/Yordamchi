using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using PDFtoImage;
using SkiaSharp;
using Tesseract;

// Tesseract ham, Yordamchi.Models ham "Page" so'zini ishlatadi; chalkashmaslik uchun taxallus.
using TesseractPage = Tesseract.Page;

namespace Yordamchi.Services;

/// <summary>
/// Tesseract 5 ustidagi <see cref="IOcrService"/> amalga oshiruvi.
/// <para>
/// Sinf ikkita muhim cheklovni hisobga oladi. Birinchisi — <see cref="TesseractEngine"/> ni
/// yaratish qimmat (til modellari diskdan o'qiladi), shuning uchun dvigatellar til ifodasi
/// bo'yicha keshlanadi. Ikkinchisi — bitta dvigatel bir vaqtning o'zida faqat bitta oqimda
/// ishlashi mumkin, shuning uchun har bir dvigatel o'z <see cref="SemaphoreSlim"/> "eshigi"
/// bilan himoyalangan va PDF sahifalari ketma-ket qayta ishlanadi.
/// </para>
/// <para>
/// Til fayllari (<c>*.traineddata</c>) dasturga qo'shib yuborilmaydi — ular kerak bo'lganda
/// rasmiy <c>tessdata_fast</c> omboridan yuklab olinadi va <see cref="TessDataPath"/> ga
/// saqlanadi.
/// </para>
/// </summary>
public sealed class OcrService : IOcrService, IDisposable
{
    /// <summary>Til fayllari yuklab olinadigan manzil (tessdata_fast — tez va aniqligi yetarli).</summary>
    private const string DownloadUrlFormat =
        "https://github.com/tesseract-ocr/tessdata_fast/raw/main/{0}.traineddata";

    private const string TrainedDataExtension = ".traineddata";

    /// <summary>Shundan tor rasmlar OCR uchun ikki barobar kattalashtiriladi.</summary>
    private const int SmallImageWidthThreshold = 1000;

    /// <summary>Bitta til faylini yuklab olishga beriladigan eng ko'p vaqt.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Butun jarayon uchun bitta <see cref="HttpClient"/>: har safar yangisini yaratish
    /// soketlarni tugatib qo'yadi (socket exhaustion).
    /// </summary>
    private static readonly Lazy<HttpClient> SharedHttpClient =
        new(CreateHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Kulranga o'tkazuvchi luma matritsasi. Faqat koeffitsiyentlardan iborat (siljish ustuni
    /// nol), shuning uchun Skia ranglarni qanday miqyosda saqlashidan qat'i nazar to'g'ri ishlaydi.
    /// </summary>
    private static readonly Lazy<SKColorFilter> PreprocessFilter = new(
        () => SKColorFilter.CreateCompose(
            // Kontrastni oshirish — och kulrang fon oqarib, matn qorayadi.
            SKColorFilter.CreateHighContrast(false, SKHighContrastConfigInvertStyle.NoInvert, 0.35f),
            SKColorFilter.CreateColorMatrix(
            [
                0.299f, 0.587f, 0.114f, 0f, 0f,
                0.299f, 0.587f, 0.114f, 0f, 0f,
                0.299f, 0.587f, 0.114f, 0f, 0f,
                0f,     0f,     0f,     1f, 0f
            ])),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly SKSamplingOptions HighQualitySampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Til ifodasi (<c>uzb+eng</c>) → tayyor dvigatel. <see cref="_sync"/> himoyasida.</summary>
    private readonly Dictionary<string, EngineSlot> _engines = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    private readonly Lazy<string> _tessDataPath;

    private bool _disposed;

    public OcrService()
    {
        // Papkani qidirish diskka murojaat qiladi, shuning uchun faqat birinchi so'ralganda bajariladi.
        _tessDataPath = new Lazy<string>(ResolveTessDataPath, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string TessDataPath => _tessDataPath.Value;

    // =================================================================================
    //  Til fayllari (tessdata)
    // =================================================================================

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledLanguages()
    {
        try
        {
            var folder = TessDataPath;
            if (!Directory.Exists(folder))
                return [];

            return Directory
                .EnumerateFiles(folder, "*" + TrainedDataExtension, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Papka o'qilmasa — hech qanday til o'rnatilmagan deb hisoblaymiz.
            return [];
        }
    }

    /// <inheritdoc />
    public bool AreLanguagesInstalled(string language, out IReadOnlyList<string> missing)
    {
        var requested = SplitLanguages(language);
        if (requested.Count == 0)
        {
            missing = [];
            return true;
        }

        var installed = new HashSet<string>(GetInstalledLanguages(), StringComparer.OrdinalIgnoreCase);
        var notFound = requested.Where(code => !installed.Contains(code)).ToList();

        missing = notFound;
        return notFound.Count == 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Allaqachon mavjud tillar qayta yuklanmaydi, lekin natijaga qo'shiladi — chaqiruvchi uchun
    /// natija "shu tillar endi tayyor" degani.
    /// </remarks>
    public async Task<IReadOnlyList<string>> DownloadLanguagesAsync(
        IEnumerable<string> languages,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(languages);

        var requested = languages
            .SelectMany(entry => SplitLanguages(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requested.Count == 0)
            return [];

        var invalid = requested.Where(code => !IsSafeLanguageCode(code)).ToList();
        if (invalid.Count > 0)
        {
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                $"Til kodi noto'g'ri: {string.Join(", ", invalid)}. Til kodi faqat harf, raqam va pastki chiziqdan iborat bo'lishi kerak (masalan uzb, eng, rus, uzb_cyrl).");
        }

        var folder = TessDataPath;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Til fayllari uchun papka yaratilmadi: {folder}. Papkaga yozish huquqini tekshiring.",
                folder,
                ex);
        }

        var downloaded = new List<string>(requested.Count);
        var total = requested.Count;
        var done = 0;

        foreach (var code in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PdfProgress(done, total, $"{code} tili yuklanmoqda…"));

            var target = Path.Combine(folder, code + TrainedDataExtension);
            if (!File.Exists(target))
                await DownloadTrainedDataAsync(code, target, cancellationToken).ConfigureAwait(false);

            downloaded.Add(code);
            done++;
            progress?.Report(new PdfProgress(done, total, $"{code} tili tayyor"));
        }

        return downloaded;
    }

    // =================================================================================
    //  Tanib olish
    // =================================================================================

    /// <inheritdoc />
    public async Task<ContentPage> RecognizeAsync(
        SKBitmap image,
        OcrOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var effective = options ?? OcrOptions.Default;

        try
        {
            // OCR — sof CPU ishi, shuning uchun UI oqimidan chetga chiqariladi.
            return await Task.Run(
                () => RecognizeCore(image, effective, cancellationToken),
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
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Rasmni tanib olishda kutilmagan xatolik yuz berdi. Rasm sifatini yoki OCR sozlamalarini tekshiring.",
                innerException: ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> RecognizeTextAsync(
        string imagePath,
        string language = OcrOptions.DefaultLanguage,
        CancellationToken cancellationToken = default)
    {
        EnsureFileExists(imagePath);

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = GetEngineSlot(language);

                slot.Gate.Wait(cancellationToken);
                try
                {
                    using var pix = LoadPix(bytes, imagePath);
                    using var page = slot.Engine.Process(pix, PageSegMode.Auto);
                    return (page.GetText() ?? string.Empty).Trim();
                }
                finally
                {
                    slot.Gate.Release();
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
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"'{Path.GetFileName(imagePath)}' faylidan matn ajratib bo'lmadi.",
                imagePath,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<DocumentContent> RecognizePdfAsync(
        string pdfPath,
        OcrOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureFileExists(pdfPath);
        var effective = options ?? OcrOptions.Default;

        try
        {
            var pdfBytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);

            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Til fayli yo'q bo'lsa — sahifalarni rasmga aylantirishdan oldin xato beramiz.
                var slot = GetEngineSlot(effective.Language);

                var sizes = PDFtoImage.Conversion.GetPageSizes(pdfBytes);
                if (sizes.Count == 0)
                {
                    throw new PdfServiceException(
                        PdfErrorKind.EmptySelection,
                        $"'{Path.GetFileName(pdfPath)}' faylida sahifa topilmadi.",
                        pdfPath);
                }

                var document = new DocumentContent
                {
                    SourcePath = pdfPath,
                    Title = Path.GetFileNameWithoutExtension(pdfPath)
                };

                var dpi = NormalizeDpi(effective.Dpi);
                var total = sizes.Count;
                progress?.Report(new PdfProgress(0, total, "Sahifalar tayyorlanmoqda…"));

                for (var index = 0; index < total; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var size = sizes[index];
                    var renderOptions = new RenderOptions
                    {
                        // Kenglikni sahifaning haqiqiy o'lchamidan hisoblaymiz: punkt → piksel.
                        Width = Math.Max(1, (int)Math.Round(size.Width / 72d * dpi)),
                        WithAspectRatio = true,
                        WithAnnotations = true,
                        WithFormFill = false,
                        BackgroundColor = SKColors.White,
                        AntiAliasing = PdfAntiAliasing.All
                    };

                    ContentPage? page = null;

                    // Sahifalar ketma-ket: dvigatel bir vaqtda faqat bitta oqimga xizmat qiladi.
                    await foreach (var bitmap in PDFtoImage.Conversion
                        .ToImagesAsync(pdfBytes, [index], password: null, options: renderOptions, cancellationToken: cancellationToken)
                        .WithCancellation(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        using (bitmap)
                        {
                            page ??= RecognizeCore(bitmap, effective, cancellationToken, slot);
                        }
                    }

                    page ??= new ContentPage { WasRecognized = true, Confidence = 0f };
                    page.Number = index + 1;
                    page.WidthPoints = size.Width > 0 ? size.Width : page.WidthPoints;
                    page.HeightPoints = size.Height > 0 ? size.Height : page.HeightPoints;
                    document.Pages.Add(page);

                    progress?.Report(new PdfProgress(index + 1, total, $"{index + 1}-sahifa tanilmoqda…"));
                }

                return document;
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
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"'{Path.GetFileName(pdfPath)}' faylini OCR orqali o'qib bo'lmadi. Fayl shikastlangan yoki parol bilan himoyalangan bo'lishi mumkin.",
                pdfPath,
                ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        List<EngineSlot> slots;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            slots = [.. _engines.Values];
            _engines.Clear();
        }

        foreach (var slot in slots)
        {
            try
            {
                slot.Dispose();
            }
            catch
            {
                // Dvigatelni bo'shatishdagi xato foydalanuvchiga hech narsa bermaydi.
            }
        }
    }

    // =================================================================================
    //  Ichki mantiq: tanib olish
    // =================================================================================

    /// <summary>Rasmni tayyorlaydi, Tesseract ga uzatadi va natijani hujjat modeliga aylantiradi.</summary>
    private ContentPage RecognizeCore(
        SKBitmap image,
        OcrOptions options,
        CancellationToken cancellationToken,
        EngineSlot? knownSlot = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var slot = knownSlot ?? GetEngineSlot(options.Language);

        var (prepared, ownsPrepared, scale) = Prepare(image, options);
        try
        {
            byte[] png;
            using (var data = prepared.Encode(SKEncodedImageFormat.Png, 100))
            {
                if (data is null)
                {
                    throw new PdfServiceException(
                        PdfErrorKind.UnsupportedImage,
                        "Rasmni OCR uchun PNG ga aylantirib bo'lmadi.");
                }

                png = data.ToArray();
            }

            cancellationToken.ThrowIfCancellationRequested();

            slot.Gate.Wait(cancellationToken);
            try
            {
                using var pix = LoadPix(png, null);
                using var page = ProcessPix(slot.Engine, pix);
                cancellationToken.ThrowIfCancellationRequested();
                return BuildContentPage(page, options, image.Width, image.Height, scale);
            }
            finally
            {
                slot.Gate.Release();
            }
        }
        finally
        {
            if (ownsPrepared)
                prepared.Dispose();
        }
    }

    /// <summary>
    /// OCR dan oldingi ishlov: kulrang + kontrast, kerak bo'lsa 2× kattalashtirish.
    /// </summary>
    /// <returns>
    /// Tayyorlangan rasm, uni chaqiruvchi bo'shatishi kerakmi va asl rasmga nisbatan miqyos
    /// (koordinatalarni qaytarib hisoblash uchun).
    /// </returns>
    private static (SKBitmap Bitmap, bool Owned, double Scale) Prepare(SKBitmap source, OcrOptions options)
    {
        if (!options.Preprocess)
            return (source, false, 1d);

        // Juda tor rasmda harflar piksellarga sig'maydi; ikki barobar kattalashtirish aniqlikni oshiradi.
        var scale = source.Width < SmallImageWidthThreshold ? 2d : 1d;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var target = new SKBitmap(info);
        try
        {
            using var canvas = new SKCanvas(target);
            canvas.Clear(SKColors.White);

            using var paint = new SKPaint
            {
                IsAntialias = true,
                ColorFilter = PreprocessFilter.Value
            };

            canvas.DrawBitmap(
                source,
                new SKRect(0f, 0f, source.Width, source.Height),
                new SKRect(0f, 0f, width, height),
                HighQualitySampling,
                paint);
        }
        catch
        {
            target.Dispose();
            // Ishlov berib bo'lmasa — asl rasm bilan davom etamiz, OCR baribir ishlaydi.
            return (source, false, 1d);
        }

        return (target, true, scale);
    }

    /// <summary>Tanilgan sahifani <see cref="ContentPage"/> ga aylantiradi.</summary>
    /// <param name="sourceWidthPixels">Asl (ishlov berilmagan) rasm kengligi.</param>
    /// <param name="scale">Tayyorlangan rasm asl rasmdan necha barobar katta.</param>
    private static ContentPage BuildContentPage(
        TesseractPage page,
        OcrOptions options,
        int sourceWidthPixels,
        int sourceHeightPixels,
        double scale)
    {
        var dpi = NormalizeDpi(options.Dpi);

        // Tayyorlangan rasm piksellaridan PDF punktiga: avval miqyosni, keyin dpi ni yechamiz.
        var toPoints = 72d / dpi / (scale <= 0d ? 1d : scale);
        var sourceToPoints = 72d / dpi;

        var content = new ContentPage
        {
            Number = 1,
            WidthPoints = Math.Max(1d, sourceWidthPixels * sourceToPoints),
            HeightPoints = Math.Max(1d, sourceHeightPixels * sourceToPoints),
            WasRecognized = true,
            Confidence = Math.Clamp(page.GetMeanConfidence() * 100f, 0f, 100f)
        };

        if (options.DetectParagraphs)
        {
            foreach (var block in BuildParagraphBlocks(page, options, toPoints))
                content.Blocks.Add(block);
        }

        // Abzaslarga bo'lish o'chirilgan yoki iterator hech nima bermagan bo'lsa — butun matn bitta blok.
        if (content.Blocks.Count == 0)
        {
            var text = NormalizeParagraphText(page.GetText());
            if (text.Length > 0)
            {
                var block = ParagraphBlock.FromText(text);
                block.Left = 0d;
                block.Top = 0d;
                block.Width = content.WidthPoints;
                block.Height = content.HeightPoints;
                content.Blocks.Add(block);
            }
        }

        return content;
    }

    /// <summary>Natija iteratori bo'yicha abzaslarni yig'adi va sarlavhalarni belgilaydi.</summary>
    private static List<ParagraphBlock> BuildParagraphBlocks(TesseractPage page, OcrOptions options, double toPoints)
    {
        var candidates = new List<ParagraphCandidate>();

        using (var iterator = page.GetIterator())
        {
            if (iterator is not null)
            {
                iterator.Begin();
                do
                {
                    var raw = iterator.GetText(PageIteratorLevel.Para);
                    var text = NormalizeParagraphText(raw);
                    if (text.Length == 0)
                        continue;

                    if (!iterator.TryGetBoundingBox(PageIteratorLevel.Para, out var box))
                        box = new Rect(0, 0, 0, 0);

                    var lineCount = CountLines(raw);
                    var confidence = iterator.GetConfidence(PageIteratorLevel.Para);

                    candidates.Add(new ParagraphCandidate(
                        text,
                        box.X1 * toPoints,
                        box.Y1 * toPoints,
                        box.Width * toPoints,
                        box.Height * toPoints,
                        lineCount,
                        confidence));
                }
                while (iterator.Next(PageIteratorLevel.Para));
            }
        }

        if (candidates.Count == 0)
            return [];

        // Sarlavha evristikasi uchun sahifadagi odatiy qator balandligi. Har bir abzas matn
        // uzunligiga qarab "ovoz" beradi, shuning uchun mediana asosiy matnga tayanadi va
        // sahifada ikkitagina abzas bo'lsa ham sarlavha ajralib turadi.
        var medianLineHeight = Median(candidates
            .Where(candidate => candidate.LineHeightPoints > 0d)
            .SelectMany(candidate => Enumerable.Repeat(
                candidate.LineHeightPoints,
                Math.Clamp(candidate.Text.Length / 10, 1, 40))));

        var blocks = new List<ParagraphBlock>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var fontSize = Math.Round(Math.Clamp(candidate.LineHeightPoints * 0.8d, 7d, 40d), 1);
            if (double.IsNaN(fontSize) || fontSize <= 0d)
                fontSize = 11d;

            var isHeading = medianLineHeight > 0d
                && candidate.LineHeightPoints > medianLineHeight * 1.35d
                && candidate.LineCount <= 2
                && candidate.Text.Length <= 90
                // Ishonchi juda past bo'lgan bo'lakni sarlavha deb ko'tarish xavfli.
                && candidate.Confidence >= options.MinimumConfidence;

            var block = new ParagraphBlock
            {
                Kind = isHeading ? BlockKind.Heading2 : BlockKind.Paragraph,
                Left = candidate.Left,
                Top = candidate.Top,
                Width = candidate.Width,
                Height = candidate.Height
            };

            // Ishonchi past abzaslar ham qo'shiladi: yo'qolgan matn noto'g'ri matndan yomonroq.
            block.Runs.Add(new TextRun(
                candidate.Text,
                FontSize: fontSize,
                IsBold: isHeading));

            blocks.Add(block);
        }

        return blocks;
    }

    private static TesseractPage ProcessPix(TesseractEngine engine, Pix pix)
    {
        try
        {
            return engine.Process(pix, PageSegMode.Auto);
        }
        catch (Exception ex) when (ex is TesseractException or DllNotFoundException or BadImageFormatException)
        {
            throw NativeLibraryException(ex);
        }
    }

    private static Pix LoadPix(byte[] bytes, string? path)
    {
        try
        {
            return Pix.LoadFromMemory(bytes);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw NativeLibraryException(ex);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedImage,
                path is null
                    ? "Rasm OCR uchun o'qilmadi — format qo'llab-quvvatlanmaydi."
                    : $"'{Path.GetFileName(path)}' rasm sifatida o'qilmadi — format qo'llab-quvvatlanmaydi.",
                path,
                ex);
        }
    }

    // =================================================================================
    //  Ichki mantiq: dvigatel keshi
    // =================================================================================

    /// <summary>Til ifodasi uchun keshlangan dvigatelni qaytaradi, kerak bo'lsa yaratadi.</summary>
    private EngineSlot GetEngineSlot(string language)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var normalized = NormalizeLanguage(language);
        if (!AreLanguagesInstalled(normalized, out var missing))
        {
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                $"OCR uchun til fayllari topilmadi: {string.Join(", ", missing)}. " +
                $"Ularni dastur ichidan (OCR sozlamalarida «Tillarni yuklab olish») bir marta yuklab olish mumkin. " +
                $"Fayllar shu papkaga saqlanadi: {TessDataPath}",
                TessDataPath);
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_engines.TryGetValue(normalized, out var existing))
                return existing;

            var slot = new EngineSlot(CreateEngine(normalized));
            _engines[normalized] = slot;
            return slot;
        }
    }

    private TesseractEngine CreateEngine(string language)
    {
        try
        {
            return new TesseractEngine(TessDataPath, language, EngineMode.Default);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            throw NativeLibraryException(ex);
        }
        catch (TesseractException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                $"OCR dvigatelini ishga tushirib bo'lmadi ({language}). Til fayllari shikastlangan bo'lishi mumkin — " +
                $"«{TessDataPath}» papkasidagi .traineddata fayllarini o'chirib, qaytadan yuklab ko'ring.",
                TessDataPath,
                ex);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                $"OCR dvigatelini ishga tushirib bo'lmadi ({language}). Til fayllari papkasi: {TessDataPath}",
                TessDataPath,
                ex);
        }
    }

    private static PdfServiceException NativeLibraryException(Exception inner) => new(
        PdfErrorKind.MissingComponent,
        "OCR kutubxonasi (tesseract / leptonica) yuklanmadi. Dastur 64 bitli (x64) rejimda ishlashi va " +
        "dastur papkasida x64 native fayllar bo'lishi kerak. Dasturni qayta o'rnatib ko'ring.",
        innerException: inner);

    // =================================================================================
    //  Ichki mantiq: tessdata papkasi va yuklab olish
    // =================================================================================

    /// <summary>
    /// Til fayllari papkasini topadi: TESSDATA_PREFIX → dastur papkasi → %LOCALAPPDATA%.
    /// Hech biri mos kelmasa, yuklab olish uchun mo'ljallangan LOCALAPPDATA papkasini qaytaradi.
    /// </summary>
    private static string ResolveTessDataPath()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            var trimmed = fromEnvironment.Trim().Trim('"');

            // TESSDATA_PREFIX ba'zan tessdata papkasining o'ziga, ba'zan uning otasiga ishora qiladi.
            if (HasTrainedData(trimmed))
                return trimmed;

            var nested = SafeCombine(trimmed, "tessdata");
            if (nested is not null && HasTrainedData(nested))
                return nested;
        }

        var beside = SafeCombine(AppContext.BaseDirectory, "tessdata");
        if (beside is not null && HasTrainedData(beside))
            return beside;

        var local = SafeCombine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine("Yordamchi", "tessdata"));

        // LOCALAPPDATA ham topilmasa — dastur papkasi yagona ishonchli tayanch nuqta.
        local ??= Path.Combine(AppContext.BaseDirectory, "tessdata");

        if (HasTrainedData(local))
            return local;

        // Dastur papkasida bo'sh tessdata bo'lsa ham, yozish huquqi kafolatlanmagan —
        // yuklab olinadigan fayllar uchun har doim LOCALAPPDATA tanlanadi.
        try
        {
            Directory.CreateDirectory(local);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Papka yaratilmasa ham yo'lni qaytaramiz: xato yuklab olish paytida aniq ko'rinadi.
        }

        return local;
    }

    private static bool HasTrainedData(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                && Directory.EnumerateFiles(folder, "*" + TrainedDataExtension, SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? SafeCombine(string? root, string relative)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        try
        {
            return Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static async Task DownloadTrainedDataAsync(string language, string targetPath, CancellationToken cancellationToken)
    {
        var url = string.Format(CultureInfo.InvariantCulture, DownloadUrlFormat, language);
        var tempPath = targetPath + ".tmp";

        try
        {
            using var response = await SharedHttpClient.Value
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new PdfServiceException(
                    PdfErrorKind.MissingComponent,
                    $"'{language}' tili uchun fayl serverda topilmadi (HTTP {(int)response.StatusCode}). " +
                    "Til kodini tekshiring — kod uch harfli bo'lishi kerak (uzb, eng, rus).",
                    targetPath);
            }

            // Avval vaqtinchalik faylga: yuklash yarmida uzilsa, buzuq .traineddata qolib ketmasin.
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (PdfServiceException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                $"'{language}' til faylini yuklab bo'lmadi — internetga ulanishni tekshiring va qaytadan urinib ko'ring.",
                targetPath,
                ex);
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Vaqtinchalik fayl qolib ketsa ham ish davom etadi.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = DownloadTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Yordamchi/2.0");
        return client;
    }

    // =================================================================================
    //  Kichik yordamchilar
    // =================================================================================

    private static void EnsureFileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, "Fayl yo'li ko'rsatilmagan.");

        if (!File.Exists(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"Fayl topilmadi: {path}", path);
    }

    private static int NormalizeDpi(int dpi) => dpi is >= 72 and <= 1200 ? dpi : 300;

    private static string NormalizeLanguage(string? language)
    {
        var parts = SplitLanguages(language);
        return parts.Count == 0 ? OcrOptions.DefaultLanguage : string.Join('+', parts);
    }

    /// <summary><c>"uzb+eng+rus"</c> ni alohida kodlarga ajratadi.</summary>
    private static List<string> SplitLanguages(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var part in language.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (seen.Add(part))
                result.Add(part);
        }

        return result;
    }

    /// <summary>Til kodi fayl yo'li yoki URL ni buzmasligini tekshiradi.</summary>
    private static bool IsSafeLanguageCode(string code)
        => code.Length is > 0 and <= 32 && code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    /// <summary>
    /// Abzas ichidagi qator uzilishlarini bo'sh joyga almashtiradi va ortiqcha bo'shliqlarni yig'ishtiradi.
    /// </summary>
    private static string NormalizeParagraphText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var builder = new StringBuilder(raw.Length);
        var pendingSpace = false;

        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>Abzasdagi qatorlar soni — shrift o'lchamini taxmin qilish uchun.</summary>
    private static int CountLines(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 1;

        var trimmed = raw.TrimEnd('\r', '\n');
        var count = 1;
        foreach (var ch in trimmed)
        {
            if (ch == '\n')
                count++;
        }

        return count;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(value => value > 0d).OrderBy(value => value).ToList();
        if (sorted.Count == 0)
            return 0d;

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    /// <summary>Bloklarni yasashdan oldin yig'ilgan xom abzas ma'lumoti.</summary>
    private sealed record ParagraphCandidate(
        string Text,
        double Left,
        double Top,
        double Width,
        double Height,
        int LineCount,
        float Confidence)
    {
        /// <summary>Bitta qatorga to'g'ri keladigan balandlik (punkt).</summary>
        public double LineHeightPoints => LineCount <= 0 ? Height : Height / LineCount;
    }

    /// <summary>Keshlangan dvigatel va uni bitta oqim bilan cheklovchi "eshik".</summary>
    private sealed class EngineSlot(TesseractEngine engine) : IDisposable
    {
        public TesseractEngine Engine { get; } = engine;

        /// <summary><see cref="TesseractEngine"/> thread-safe emas — bir vaqtda bitta chaqiruv.</summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Dispose()
        {
            Engine.Dispose();
            Gate.Dispose();
        }
    }
}
