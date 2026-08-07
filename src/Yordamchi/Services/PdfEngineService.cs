using System.IO;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.Services;

/// <summary>
/// <see cref="IPdfEngineService"/> ning standart amalga oshirilishi.
/// <para>
/// Bu sinf hech qanday PDF ishini o'zi bajarmaydi — u faqat <em>dispetcher</em>: tanlangan
/// vositani mos modul servisiga yo'naltiradi, natijani foydalanuvchi tilida bitta
/// <see cref="ToolRunResult"/> ga jamlaydi. Shu tufayli UI da 17 ta vosita uchun 17 xil
/// chaqiruv mantiqi saqlanmaydi.
/// </para>
/// </summary>
public sealed class PdfEngineService : IPdfEngineService
{
    public PdfEngineService(
        IPdfService pages,
        IPdfManipulatorService documents,
        IDocumentConversionService conversion,
        IOcrService ocr,
        IImageBackgroundRemover backgroundRemover)
    {
        Pages = pages;
        Documents = documents;
        Conversion = conversion;
        Ocr = ocr;
        BackgroundRemover = backgroundRemover;
    }

    public IPdfService Pages { get; }

    public IPdfManipulatorService Documents { get; }

    public IDocumentConversionService Conversion { get; }

    public IOcrService Ocr { get; }

    public IImageBackgroundRemover BackgroundRemover { get; }

    // =================================================================
    //  Bajarish
    // =================================================================

    /// <inheritdoc />
    public async Task<ToolRunResult> ExecuteAsync(
        ToolRequest request,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var problem = Validate(request);
        if (problem is not null)
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, problem);

        var input = request.InputFiles[0];

        return request.Tool switch
        {
            ToolId.Merge => await MergeAsync(request, progress, cancellationToken).ConfigureAwait(false),
            ToolId.Split => await SplitAsync(request, input, progress, cancellationToken).ConfigureAwait(false),
            ToolId.Organize or ToolId.Rotate => await WritePagePlanAsync(request, input, progress, cancellationToken).ConfigureAwait(false),

            ToolId.PdfToWord => await ConvertAsync(request,
                (output, token) => Conversion.PdfToWordAsync(input, output, Option<PdfToWordOptions>(request), progress, token),
                "Word hujjati tayyor", cancellationToken).ConfigureAwait(false),

            ToolId.WordToPdf => await ConvertAsync(request,
                (output, token) => Conversion.WordToPdfAsync(input, output, Option<WordToPdfOptions>(request), progress, token),
                "PDF tayyor", cancellationToken).ConfigureAwait(false),

            ToolId.PdfToExcel => await ConvertAsync(request,
                (output, token) => Conversion.PdfToExcelAsync(input, output, Option<PdfToExcelOptions>(request), progress, token),
                "Excel kitobi tayyor", cancellationToken).ConfigureAwait(false),

            ToolId.PdfToPowerPoint => await ConvertAsync(request,
                (output, token) => Conversion.PdfToPowerPointAsync(input, output, Option<PdfToPowerPointOptions>(request), progress, token),
                "Taqdimot tayyor", cancellationToken).ConfigureAwait(false),

            // Sozlamalar panelidagi to'liq OcrOptions (til + dpi + tayyorlash + abzas) uzatiladi:
            // faqat tilni uzatish foydalanuvchi tanlagan qolgan uchta sozlamani yo'qotib yuborardi.
            ToolId.OcrToWord => await ConvertAsync(request,
                (output, token) => Conversion.OcrPdfToWordAsync(
                    input, output, OcrOptionsOf(request), progress, token),
                "Matn tanib olindi va Word ga yozildi", cancellationToken).ConfigureAwait(false),

            ToolId.PdfToImage => await PdfToImagesAsync(request, input, progress, cancellationToken).ConfigureAwait(false),

            ToolId.ImageToPdf => await ConvertAsync(request,
                (output, token) => Pages.ConvertImagesToPdfAsync(
                    request.InputFiles.ToList(), output, Option<ImageToPdfOptions>(request), progress, token),
                "Rasmlardan PDF yig'ildi", cancellationToken).ConfigureAwait(false),

            ToolId.Compress => await CompressAsync(request, input, progress, cancellationToken).ConfigureAwait(false),

            ToolId.Protect => await ConvertAsync(request,
                (output, token) => Documents.ProtectPdfAsync(
                    input, output, Option<ProtectOptions>(request) ?? new ProtectOptions { UserPassword = request.Password ?? string.Empty },
                    Adapt(progress), token),
                "Hujjat parol bilan himoyalandi", cancellationToken).ConfigureAwait(false),

            ToolId.Unlock => await ConvertAsync(request,
                (output, token) => Documents.UnlockPdfAsync(input, output, request.Password ?? string.Empty, Adapt(progress), token),
                "Himoya olib tashlandi", cancellationToken).ConfigureAwait(false),

            ToolId.Watermark => await ConvertAsync(request,
                (output, token) => Documents.AddWatermarkAsync(
                    input, output, Option<WatermarkOptions>(request) ?? new WatermarkOptions(), Adapt(progress), token),
                "Suv belgisi qo'shildi", cancellationToken).ConfigureAwait(false),

            ToolId.PageNumbers => await ConvertAsync(request,
                (output, token) => Documents.AddPageNumbersAsync(
                    input, output, Option<PageNumberOptions>(request) ?? new PageNumberOptions(), Adapt(progress), token),
                "Sahifalar raqamlandi", cancellationToken).ConfigureAwait(false),

            ToolId.BackgroundRemover => await RemoveBackgroundAsync(request, input, progress, cancellationToken).ConfigureAwait(false),

            _ => throw new PdfServiceException(PdfErrorKind.Unknown, "Bu vosita hali qo'llab-quvvatlanmaydi.")
        };
    }

    // -----------------------------------------------------------------
    //  Alohida vositalar
    // -----------------------------------------------------------------

    private async Task<ToolRunResult> MergeAsync(
        ToolRequest request, IProgress<PdfProgress>? progress, CancellationToken cancellationToken)
    {
        var output = RequireOutput(request);

        // Foydalanuvchi eskizlar orqali sahifalarni qayta tartiblagan bo'lsa — aynan shu reja
        // yoziladi. Aks holda oddiy ketma-ket birlashtirish yetarli va tezroq.
        if (request.PagePlan is { Count: > 0 } plan)
        {
            await Pages.BuildPdfAsync(plan, output, progress, cancellationToken).ConfigureAwait(false);
            return ToolRunResult.Ok($"{plan.Count} ta sahifa birlashtirildi", output);
        }

        await Documents.MergePdfsAsync(request.InputFiles.ToList(), output, Adapt(progress), cancellationToken)
            .ConfigureAwait(false);

        return ToolRunResult.Ok($"{request.InputFiles.Count} ta fayl birlashtirildi", output);
    }

    private async Task<ToolRunResult> SplitAsync(
        ToolRequest request, string input, IProgress<PdfProgress>? progress, CancellationToken cancellationToken)
    {
        var folder = RequireFolder(request);
        var options = Option<SplitOptions>(request) ?? SplitOptions.Default;

        // "Bo'lish" ekranida ham eskizlar ko'rsatiladi, ya'ni foydalanuvchi bo'lishdan oldin
        // sahifani o'chirishi, tartibini almashtirishi yoki burishi mumkin. Oraliqlar
        // (1-3, 5…) esa aynan ekranda ko'rinib turgan tartibda sanaladi — shuning uchun avval
        // rejani vaqtinchalik faylga yozamiz va bo'lishni o'sha "foydalanuvchi ko'rgan" hujjat
        // ustida bajaramiz. Aks holda o'chirilgan sahifa natijaga qaytib kelardi.
        var source = input;
        string? staged = null;

        try
        {
            if (request.PagePlan is { Count: > 0 } plan)
            {
                staged = Path.Combine(Path.GetTempPath(), $"yordamchi-split-{Guid.NewGuid():N}.pdf");
                await Pages.BuildPdfAsync(plan, staged, Scale(progress, 0, 30), cancellationToken).ConfigureAwait(false);
                source = staged;

                // Natija fayllari vaqtinchalik faylning tasodifiy nomini emas, manba hujjat
                // nomini olishi kerak.
                if (string.IsNullOrWhiteSpace(options.FileNamePrefix))
                {
                    options = new SplitOptions
                    {
                        Mode = options.Mode,
                        RangeExpression = options.RangeExpression,
                        PagesPerFile = options.PagesPerFile,
                        FileNamePrefix = Path.GetFileNameWithoutExtension(input)
                    };
                }
            }

            var files = await Documents
                .SplitPdfAsync(source, folder, options, Adapt(progress, staged is null ? 0 : 30), cancellationToken)
                .ConfigureAwait(false);

            return ToolRunResult.Ok($"{files.Count} ta fayl yaratildi", files);
        }
        finally
        {
            if (staged is not null)
                TryDelete(staged);
        }
    }

    private async Task<ToolRunResult> WritePagePlanAsync(
        ToolRequest request, string input, IProgress<PdfProgress>? progress, CancellationToken cancellationToken)
    {
        var output = RequireOutput(request);

        if (request.PagePlan is { Count: > 0 } plan)
        {
            await Pages.BuildPdfAsync(plan, output, progress, cancellationToken).ConfigureAwait(false);
            return ToolRunResult.Ok($"{plan.Count} ta sahifa saqlandi", output);
        }

        // Eskizlar yuklanmagan bo'lsa (masalan juda katta hujjat) — to'g'ridan-to'g'ri burish.
        // Bu yerda tanlangan sahifalar tushunchasi yo'q, shuning uchun Validate "faqat
        // tanlanganlar" rejimini oldindan to'sib qo'yadi va bu yo'l doim barcha sahifani buradi.
        var degrees = (Option<RotateRequest>(request)?.Degrees) ?? 90;
        await Documents.RotatePagesAsync(input, output, degrees, null, Adapt(progress), cancellationToken)
            .ConfigureAwait(false);

        return ToolRunResult.Ok($"Barcha sahifalar {degrees}° ga burildi", output);
    }

    private async Task<ToolRunResult> PdfToImagesAsync(
        ToolRequest request, string input, IProgress<PdfProgress>? progress, CancellationToken cancellationToken)
    {
        var folder = RequireFolder(request);
        var options = Option<PdfToImageOptions>(request) ?? PdfToImageOptions.Default;

        var files = await Conversion.PdfToImagesAsync(input, folder, options, progress, cancellationToken)
            .ConfigureAwait(false);

        return ToolRunResult.Ok($"{files.Count} ta rasm saqlandi", files);
    }

    private async Task<ToolRunResult> CompressAsync(
        ToolRequest request, string input, IProgress<PdfProgress>? progress, CancellationToken cancellationToken)
    {
        var output = RequireOutput(request);
        var level = request.Options as CompressionLevel? ?? CompressionLevel.Medium;

        var result = await Documents.CompressPdfAsync(input, output, level, Adapt(progress), cancellationToken)
            .ConfigureAwait(false);

        var message = result.SavedPercent > 0
            ? $"Hajm {result.SavedPercent}% ga kichraydi ({FormatSize(result.OriginalBytes)} → {FormatSize(result.CompressedBytes)})"
            : $"Hujjat allaqachon optimallashtirilgan ({FormatSize(result.CompressedBytes)})";

        return ToolRunResult.Ok(message, output);
    }

    private async Task<ToolRunResult> RemoveBackgroundAsync(
        ToolRequest request, string input, IProgress<PdfProgress>? progress, CancellationToken cancellationToken)
    {
        var output = RequireOutput(request);
        var options = Option<BackgroundRemovalOptions>(request) ?? BackgroundRemovalOptions.Default;

        var relay = new Progress<int>(percent =>
            progress?.Report(new PdfProgress(percent, 100, "Fon olib tashlanmoqda…")));

        using var bitmap = await BackgroundRemover
            .RemoveBackgroundToBitmapAsync(input, options, relay, cancellationToken)
            .ConfigureAwait(false);

        await BackgroundRemover.SaveAsPngAsync(bitmap, output, cancellationToken).ConfigureAwait(false);

        return ToolRunResult.Ok("Fon olib tashlandi", output);
    }

    /// <summary>Bitta faylga yozadigan vositalar uchun umumiy o'ram.</summary>
    private static async Task<ToolRunResult> ConvertAsync(
        ToolRequest request,
        Func<string, CancellationToken, Task> operation,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var output = RequireOutput(request);
        await operation(output, cancellationToken).ConfigureAwait(false);
        return ToolRunResult.Ok(successMessage, output);
    }

    // =================================================================
    //  Tekshiruvlar
    // =================================================================

    /// <inheritdoc />
    public string? Validate(ToolRequest request)
    {
        if (request.InputFiles.Count == 0)
            return "Avval fayl tanlang.";

        foreach (var file in request.InputFiles)
        {
            if (!File.Exists(file))
                return $"'{Path.GetFileName(file)}' fayli topilmadi.";
        }

        var tool = ToolCatalog.Get(request.Tool);

        if (tool.WritesToFolder)
        {
            if (string.IsNullOrWhiteSpace(request.OutputFolder))
                return "Natija saqlanadigan papkani tanlang.";
        }
        else if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return "Natija fayli uchun joy tanlang.";
        }

        if (request.Tool == ToolId.Merge && request.InputFiles.Count < 2 && request.PagePlan is not { Count: > 0 })
            return "Birlashtirish uchun kamida ikkita PDF tanlang.";

        if (request.Tool == ToolId.Unlock && string.IsNullOrEmpty(request.Password))
            return "Qulfni ochish uchun hujjat parolini kiriting.";

        if (request.Tool == ToolId.Protect)
        {
            var protect = Option<ProtectOptions>(request);

            // Ochish paroli shart emas: egalik paroli bo'lsa hujjat erkin ochiladi, lekin
            // cheklovlar amal qiladi ("faqat cheklovlar" rejimi). Ikkalasi ham bo'sh bo'lsa
            // hech qanday himoya yozib bo'lmaydi — PDF shifrlanmagan qoladi.
            var userPassword = protect?.UserPassword ?? request.Password;
            var ownerPassword = protect?.OwnerPassword;

            if (string.IsNullOrEmpty(userPassword) && string.IsNullOrEmpty(ownerPassword))
                return "Parol kiriting: ochish paroli yoki (faqat cheklovlar uchun) egalik paroli.";
        }

        // "Faqat tanlangan sahifalar" rejimi eskizlar ro'yxatiga tayanadi; u yo'q bo'lsa
        // qaysi sahifa tanlanganini bilishning iloji yo'q, shuning uchun jimgina hammasini
        // burib yubormaymiz.
        if (request.Tool == ToolId.Rotate
            && Option<RotateRequest>(request) is { ApplyToAll: false }
            && request.PagePlan is not { Count: > 0 })
        {
            return "Sahifa eskizlari yuklanmagani uchun tanlangan sahifalarni ajratib bo'lmadi — "
                + "\"Barcha sahifalarga qo'llansin\" ni yoqing.";
        }

        if (request.Tool == ToolId.Split)
        {
            var split = Option<SplitOptions>(request);
            if (split is { Mode: SplitMode.Ranges } && string.IsNullOrWhiteSpace(split.RangeExpression))
                return "Sahifa oraliqlarini kiriting, masalan: 1-3, 5, 8-10";
            if (split is { Mode: SplitMode.FixedChunks, PagesPerFile: < 1 })
                return "Bitta fayldagi sahifalar soni kamida 1 bo'lishi kerak.";
        }

        if (request.Tool == ToolId.Watermark)
        {
            var watermark = Option<WatermarkOptions>(request);
            if (watermark is not null && string.IsNullOrWhiteSpace(watermark.Text))
                return "Suv belgisi matnini kiriting.";
        }

        return CheckPrerequisites(request.Tool, request.Options);
    }

    /// <inheritdoc />
    public string? CheckPrerequisites(ToolId tool, object? options = null)
    {
        // Word — yagona holat, unda yetishmayotgan narsani dastur yuklab bera olmaydi
        // (u foydalanuvchi o'zi o'rnatadigan tashqi dastur), shuning uchun u alohida turadi.
        if (tool == ToolId.WordToPdf)
        {
            return options is WordToPdfOptions { Engine: WordToPdfEngine.MicrosoftWord }
                   && !Conversion.IsMicrosoftWordAvailable
                ? "Bu kompyuterda Microsoft Word topilmadi. \"Ichki dvigatel\" yoki \"Avtomatik\" rejimini tanlang."
                : null;
        }

        // Qolgan hammasi yuklab olinadigan komponentlar — matn ham, tugma ham bitta manbadan
        // kelib chiqadi, shuning uchun tekshiruv mantig'i faqat GetMissingComponent da yashaydi.
        return GetMissingComponent(tool, options) switch
        {
            DownloadableComponent.OcrLanguages => DescribeMissingOcr(OcrLanguageOf(options) ?? OcrOptions.DefaultLanguage),
            DownloadableComponent.AiModel => DescribeMissingAiModel(),
            _ => null
        };
    }

    /// <inheritdoc />
    public DownloadableComponent GetMissingComponent(ToolId tool, object? options = null)
    {
        switch (tool)
        {
            case ToolId.OcrToWord:
                return IsOcrReady(OcrLanguageOf(options) ?? OcrOptions.DefaultLanguage)
                    ? DownloadableComponent.None
                    : DownloadableComponent.OcrLanguages;

            case ToolId.PdfToWord:
                // OCR faqat matn qatlami yo'q sahifalar uchun kerak bo'ladi, shuning uchun
                // "Faqat matn qatlami" rejimida hech qanday tashqi komponent talab qilinmaydi.
                if (options is PdfToWordOptions word && word.Recognition != TextRecognitionMode.TextLayerOnly)
                {
                    return IsOcrReady(word.OcrLanguage)
                        ? DownloadableComponent.None
                        : DownloadableComponent.OcrLanguages;
                }

                return DownloadableComponent.None;

            case ToolId.BackgroundRemover:
                return BackgroundRemover.IsModelAvailable
                    ? DownloadableComponent.None
                    : DownloadableComponent.AiModel;

            default:
                return DownloadableComponent.None;
        }
    }

    /// <inheritdoc />
    public Task DownloadComponentAsync(
        DownloadableComponent component,
        object? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => component switch
        {
            DownloadableComponent.OcrLanguages => Ocr.DownloadLanguagesAsync(
                SplitOcrLanguages(OcrLanguageOf(options) ?? OcrOptions.DefaultLanguage), progress, cancellationToken),

            DownloadableComponent.AiModel => BackgroundRemover.DownloadModelAsync(progress, cancellationToken),

            _ => Task.CompletedTask
        };

    private bool IsOcrReady(string language) => Ocr.AreLanguagesInstalled(language, out _);

    private string DescribeMissingOcr(string language)
    {
        Ocr.AreLanguagesInstalled(language, out var missing);

        return $"OCR til fayllari topilmadi: {string.Join(", ", missing)}. "
            + "Ularni quyidagi \"Yuklab olish\" tugmasi yoki \"Dastur haqida\" sahifasi orqali olishingiz mumkin.";
    }

    private string DescribeMissingAiModel() =>
        $"AI modeli topilmadi ({BackgroundRemover.DownloadableModelSizeText}). "
        + "Uni quyidagi \"Yuklab olish\" tugmasi orqali oling yoki "
        + $"'{BackgroundRemover.DownloadableModelName}' faylini shu papkaga joylashtiring:\n{BackgroundRemover.ModelPath}";

    private static string[] SplitOcrLanguages(string language) =>
        language.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // =================================================================
    //  Yordamchilar
    // =================================================================

    /// <summary>So'rovdagi sozlama obyektini kutilgan turga keltiradi.</summary>
    private static T? Option<T>(ToolRequest request) where T : class => request.Options as T;

    private static T? Option<T>(object? options) where T : class => options as T;

    /// <summary>
    /// OCR vositasi uchun sozlama obyekti. Ishchi oyna doim <see cref="OcrOptions"/> yuboradi;
    /// boshqa turdagi sozlama kelsa (yoki umuman kelmasa) faqat til ajratib olinadi.
    /// </summary>
    private static OcrOptions OcrOptionsOf(ToolRequest request)
        => Option<OcrOptions>(request)
           ?? new OcrOptions { Language = OcrLanguageOf(request) ?? OcrOptions.DefaultLanguage };

    private static string? OcrLanguageOf(ToolRequest request) => OcrLanguageOf(request.Options);

    private static string? OcrLanguageOf(object? options) => options switch
    {
        OcrOptions ocr => ocr.Language,
        PdfToWordOptions word => word.OcrLanguage,
        PdfToExcelOptions excel => excel.OcrLanguage,
        PdfToPowerPointOptions slides => slides.OcrLanguage,
        _ => null
    };

    private static string RequireOutput(ToolRequest request)
        => string.IsNullOrWhiteSpace(request.OutputPath)
            ? throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natija fayli uchun joy tanlanmadi.")
            : request.OutputPath;

    private static string RequireFolder(ToolRequest request)
        => string.IsNullOrWhiteSpace(request.OutputFolder)
            ? throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natija papkasi tanlanmadi.")
            : request.OutputFolder;

    /// <summary>
    /// <see cref="IProgress{T}"/> ning ikki ko'rinishini bog'laydi: modul servislari oddiy foizni
    /// (0..100) xabar qiladi, UI esa matnli <see cref="PdfProgress"/> kutadi.
    /// </summary>
    private static IProgress<int>? Adapt(IProgress<PdfProgress>? progress)
        => progress is null ? null : new Progress<int>(percent => progress.Report(new PdfProgress(percent, 100)));

    /// <summary>
    /// Ikki bosqichli amallar uchun: sub-servis foizi <paramref name="from"/>..100 oralig'iga
    /// siqiladi, shunda progress-bar orqaga sakramaydi.
    /// </summary>
    private static IProgress<int>? Adapt(IProgress<PdfProgress>? progress, int from)
        => progress is null
            ? null
            : new Progress<int>(percent => progress.Report(new PdfProgress(from + percent * (100 - from) / 100, 100)));

    /// <summary><see cref="PdfProgress"/> oqimini <paramref name="from"/>..<paramref name="to"/> oralig'iga joylaydi.</summary>
    private static IProgress<PdfProgress>? Scale(IProgress<PdfProgress>? progress, int from, int to)
        => progress is null
            ? null
            : new Progress<PdfProgress>(report => progress.Report(
                new PdfProgress(from + (int)(report.Percentage * (to - from) / 100d), 100, report.Message)));

    /// <summary>Vaqtinchalik faylni o'chiradi; o'chirilmasa ham amal muvaffaqiyatli hisoblanadi.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Antivirus yoki indekslovchi faylni ushlab turgan bo'lishi mumkin — Windows uni
            // keyinroq o'zi tozalaydi, foydalanuvchini bezovta qilishning hojati yo'q.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
