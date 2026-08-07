using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Yordamchi.Helpers;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;
using SkiaSharp;

namespace Yordamchi.Services;

/// <summary>
/// <see cref="IPdfManipulatorService"/> ning standart amalga oshirilishi: butun ish PDFsharp bilan
/// (rasm siqish uchun SkiaSharp bilan) bajariladi.
/// <para>
/// Butun sinf bo'ylab ikkita qoida amal qiladi. Birinchisi — manba fayl PDFsharp ga berilishidan
/// oldin to'liq baytlarga o'qiladi, shuning uchun natija yozilayotganda manba faylga hech qanday
/// handle ushlab turilmaydi (ya'ni "ochilgan faylning ustiga saqlash" ishlaydi). Ikkinchisi —
/// natija avval yonidagi vaqtinchalik faylga yoziladi va keyin nishon ustiga ko'chiriladi, ya'ni
/// nishon fayl har doim yo eskisi, yo to'liq yangisi bo'ladi — hech qachon yarim yozilgan PDF emas.
/// </para>
/// <para>
/// Har qanday nosozlik <see cref="PdfServiceException"/> ko'rinishida chiqadi, xabar matni
/// foydalanuvchiga tayyor holda o'zbek tilida yoziladi.
/// </para>
/// </summary>
public sealed class PdfManipulatorService : IPdfManipulatorService
{
    /// <summary>Suv belgisi va sahifa raqamlari uchun sahifa chekkasidan zaxira masofa (punkt).</summary>
    private const double EdgeMarginPoints = 36d;

    /// <summary><see cref="WatermarkPosition.Tiled"/> rejimidagi to'r qadami (punkt).</summary>
    private const double TileStepPoints = 200d;

    /// <summary>Talab qilingan shrift topilmasa navbat bilan sinab ko'riladigan zaxira shriftlar.</summary>
    private static readonly string[] FallbackFontFamilies = ["Arial", "Segoe UI", "Tahoma", "Verdana", "Times New Roman"];

    /// <summary>Shrift manbasi bir marta sozlanganini bildiradi (<see cref="EnsureFontsAvailable"/>).</summary>
    private static int _fontSetupDone;

    // =================================================================================
    //  Birlashtirish
    // =================================================================================

    /// <inheritdoc />
    public async Task MergePdfsAsync(
        List<string> pdfPaths,
        string outputPath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (pdfPaths is null || pdfPaths.Count == 0)
                throw new PdfServiceException(PdfErrorKind.EmptySelection, "Birlashtirish uchun birorta ham fayl tanlanmadi.", outputPath);

            ValidateOutputPath(outputPath);
            foreach (var path in pdfPaths)
                EnsureFileExists(path);

            // Barcha manbalar oldindan xotiraga o'qiladi: natija manbalardan biri bo'lishi mumkin.
            var buffers = new List<(string Path, byte[] Bytes)>(pdfPaths.Count);
            foreach (var path in pdfPaths)
                buffers.Add((path, await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)));

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

    private static void MergeCore(
        List<(string Path, byte[] Bytes)> sources,
        string outputPath,
        IProgress<int>? progress,
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
            ReportPercent(progress, done, sources.Count, 0, 95);
        }

        if (output.PageCount == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, "Tanlangan fayllarda birorta ham sahifa yo'q.", outputPath);

        SaveAtomically(output, outputPath);
        progress?.Report(100);
    }

    // =================================================================================
    //  Bo'lish
    // =================================================================================

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SplitPdfAsync(
        string pdfPath,
        string outputFolder,
        SplitOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(options);
            EnsureFileExists(pdfPath);
            EnsureFolder(outputFolder);

            var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
            var prefix = SanitizeFileName(
                string.IsNullOrWhiteSpace(options.FileNamePrefix)
                    ? Path.GetFileNameWithoutExtension(pdfPath)
                    : options.FileNamePrefix!);

            return await Task.Run(
                    () => SplitCore(pdfPath, bytes, outputFolder, prefix, pageCount => BuildRanges(options, pageCount), progress, cancellationToken),
                    cancellationToken)
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
            throw Wrap(ex, pdfPath);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> SplitPdfAsync(
        string pdfPath,
        string outputFolder,
        List<(int First, int Last)> pageRanges,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (pageRanges is null || pageRanges.Count == 0)
                throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Birorta ham sahifa oralig'i berilmadi.", pdfPath);

            EnsureFileExists(pdfPath);
            EnsureFolder(outputFolder);

            var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);
            var prefix = SanitizeFileName(Path.GetFileNameWithoutExtension(pdfPath));
            var ranges = pageRanges.ToList();

            return await Task.Run(
                    () => SplitCore(pdfPath, bytes, outputFolder, prefix, pageCount => ValidateRanges(ranges, pageCount), progress, cancellationToken),
                    cancellationToken)
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
            throw Wrap(ex, pdfPath);
        }
    }

    /// <summary>
    /// Bo'lishning umumiy yadrosi: oraliqlar ro'yxati <paramref name="rangeFactory"/> orqali
    /// (sahifalar soni ma'lum bo'lgandan keyin) hosil qilinadi, qolgani ikkala overload uchun bir xil.
    /// </summary>
    private static IReadOnlyList<string> SplitCore(
        string pdfPath,
        byte[] bytes,
        string outputFolder,
        string prefix,
        Func<int, List<(int First, int Last)>> rangeFactory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var source = OpenForImport(pdfPath, bytes);
        if (source.PageCount == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, $"'{Path.GetFileName(pdfPath)}' faylida birorta ham sahifa yo'q.", pdfPath);

        var ranges = rangeFactory(source.PageCount);
        if (ranges.Count == 0)
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Berilgan sozlamalardan birorta ham bo'lak hosil bo'lmadi.", pdfPath);

        var created = new List<string>(ranges.Count);
        var done = 0;

        foreach (var (first, last) in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var part = new PdfDocument();
            for (var page = first; page <= last; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                part.AddPage(source.Pages[page - 1]);
            }

            var name = first == last ? $"{prefix}_{first}.pdf" : $"{prefix}_{first}-{last}.pdf";
            var target = MakeUniquePath(Path.Combine(outputFolder, name));
            SaveAtomically(part, target);
            created.Add(target);

            done++;
            ReportPercent(progress, done, ranges.Count, 0, 100);
        }

        progress?.Report(100);
        return created;
    }

    /// <summary><see cref="SplitOptions"/> ni sahifa oraliqlariga aylantiradi.</summary>
    private static List<(int First, int Last)> BuildRanges(SplitOptions options, int pageCount)
    {
        switch (options.Mode)
        {
            case SplitMode.Ranges:
                return ValidateRanges(ParseRangeExpression(options.RangeExpression), pageCount);

            case SplitMode.FixedChunks:
            {
                if (options.PagesPerFile < 1)
                    throw new PdfServiceException(
                        PdfErrorKind.InvalidOptions,
                        "Bitta fayldagi sahifalar soni kamida 1 bo'lishi kerak.");

                var chunks = new List<(int First, int Last)>();
                for (var first = 1; first <= pageCount; first += options.PagesPerFile)
                    chunks.Add((first, Math.Min(first + options.PagesPerFile - 1, pageCount)));
                return chunks;
            }

            default:
            {
                var pages = new List<(int First, int Last)>(pageCount);
                for (var page = 1; page <= pageCount; page++)
                    pages.Add((page, page));
                return pages;
            }
        }
    }

    /// <summary>
    /// <c>"1-3, 5, 8-10"</c> ko'rinishidagi matnni (birinchi, oxirgi) juftliklariga ajratadi.
    /// Sahifalar 1 dan boshlab sanaladi; vergul, nuqta-vergul va bo'sh joy ajratuvchi bo'la oladi.
    /// </summary>
    private static List<(int First, int Last)> ParseRangeExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                "Sahifa oraliqlari kiritilmadi. Masalan: 1-3, 5, 8-10");

        var parts = expression.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ranges = new List<(int First, int Last)>(parts.Length);

        foreach (var part in parts)
        {
            var pieces = part.Split(['-', '–', '—'], StringSplitOptions.TrimEntries);
            if (pieces.Length is not (1 or 2))
                throw new PdfServiceException(
                    PdfErrorKind.InvalidOptions,
                    $"'{part}' — noto'g'ri oraliq. To'g'ri ko'rinish: 1-3, 5, 8-10");

            if (!TryParsePageNumber(pieces[0], out var first))
                throw new PdfServiceException(
                    PdfErrorKind.InvalidOptions,
                    $"'{part}' — sahifa raqami tushunarsiz. Faqat musbat butun sonlardan foydalaning.");

            var last = first;
            if (pieces.Length == 2 && !TryParsePageNumber(pieces[1], out last))
                throw new PdfServiceException(
                    PdfErrorKind.InvalidOptions,
                    $"'{part}' — oraliqning oxirgi sahifasi tushunarsiz. Faqat musbat butun sonlardan foydalaning.");

            if (first > last)
                throw new PdfServiceException(
                    PdfErrorKind.InvalidOptions,
                    $"'{part}' oralig'ida boshlang'ich sahifa oxirgisidan katta.");

            ranges.Add((first, last));
        }

        if (ranges.Count == 0)
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                "Sahifa oraliqlari tushunarsiz. Masalan: 1-3, 5, 8-10");

        return ranges;
    }

    private static bool TryParsePageNumber(string text, out int value)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1;

    /// <summary>Oraliqlar hujjat chegarasiga sig'ishini tekshiradi.</summary>
    private static List<(int First, int Last)> ValidateRanges(List<(int First, int Last)> ranges, int pageCount)
    {
        foreach (var (first, last) in ranges)
        {
            if (first < 1 || last < first)
                throw new PdfServiceException(
                    PdfErrorKind.InvalidOptions,
                    $"{first}-{last} oralig'i noto'g'ri: sahifalar 1 dan boshlanadi.");

            if (last > pageCount)
                throw new PdfServiceException(
                    PdfErrorKind.PageIndexOutOfRange,
                    $"{first}-{last} oralig'i hujjat chegarasidan chiqib ketdi — hujjatda jami {pageCount} ta sahifa bor.");
        }

        return ranges;
    }

    // =================================================================================
    //  Siqish
    // =================================================================================

    /// <inheritdoc />
    public async Task<CompressionResult> CompressPdfAsync(
        string inputPath,
        string outputPath,
        CompressionLevel level,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(inputPath);
            ValidateOutputPath(outputPath);

            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
            var profile = CompressionProfile.From(level);

            return await Task.Run(() => CompressCore(inputPath, bytes, outputPath, profile, progress, cancellationToken), cancellationToken)
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
            throw Wrap(ex, inputPath);
        }
    }

    private static CompressionResult CompressCore(
        string inputPath,
        byte[] bytes,
        string outputPath,
        CompressionProfile profile,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        long originalBytes = bytes.LongLength;
        var processed = 0;

        var tempPath = MakeTempPath(outputPath);
        try
        {
            using (var document = OpenForModify(inputPath, bytes, password: null))
            {
                var images = CollectImageDictionaries(document);
                for (var i = 0; i < images.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryRecompressImage(images[i], profile))
                        processed++;

                    ReportPercent(progress, i + 1, images.Count, 0, 85);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Obyekt oqimlari va sahifa mazmuni ham qayta siqiladi — rasmsiz hujjatlarda ham yutuq beradi.
                document.Options.NoCompression = false;
                document.Options.CompressContentStreams = true;
                document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;

                if (profile.StripMetadata)
                    StripMetadata(document);

                document.Save(tempPath);
            }

            progress?.Report(95);

            var newBytes = new FileInfo(tempPath).Length;
            if (newBytes >= originalBytes)
            {
                // Foydalanuvchi hech qachon "siqilgan" deb kattaroq fayl olmasin: manbani o'zgarishsiz qaytaramiz.
                File.WriteAllBytes(tempPath, bytes);
                newBytes = originalBytes;
            }

            MoveOverwrite(tempPath, outputPath);
            progress?.Report(100);
            return new CompressionResult(originalBytes, newBytes, processed);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(outputPath)}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                outputPath,
                ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Hujjatdagi barcha rasm XObject larini yig'adi.</summary>
    private static List<PdfDictionary> CollectImageDictionaries(PdfDocument document)
    {
        var images = new List<PdfDictionary>();
        foreach (var item in document.Internals.GetAllObjects())
        {
            if (item is not PdfDictionary dictionary || dictionary.Stream is null)
                continue;

            try
            {
                if (dictionary.Elements.GetName("/Subtype") == "/Image")
                    images.Add(dictionary);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Nosoz obyektni shunchaki o'tkazib yuboramiz — siqish butun hujjatni buzmasligi kerak.
            }
        }

        return images;
    }

    /// <summary>
    /// Bitta rasmni pastroq rezolyutsiya va JPEG bilan qayta kodlaydi.
    /// Natija eskisidan kichik bo'lgandagina almashtiriladi; har qanday shubhali holatda rasm
    /// umuman qo'lga olinmaydi va <c>false</c> qaytadi.
    /// </summary>
    private static bool TryRecompressImage(PdfDictionary dictionary, CompressionProfile profile)
    {
        try
        {
            var stream = dictionary.Stream;
            if (stream is null)
                return false;

            var originalStream = stream.Value;
            if (originalStream is null || originalStream.Length == 0)
                return false;

            var width = dictionary.Elements.GetInteger("/Width");
            var height = dictionary.Elements.GetInteger("/Height");
            if (width <= 0 || height <= 0)
                return false;

            if ((long)width * height < profile.MinimumPixelsToTouch)
                return false;

            if (!IsSafeToRecompress(dictionary, out var filters, out var colorSpace))
                return false;

            using var decoded = DecodeImage(dictionary, filters, colorSpace, width, height);
            if (decoded is null)
                return false;

            var maxEdge = Math.Max(64, profile.TargetDpi * 11); // A4 balandligi ~11 dyuym.
            var bitmap = SkiaImageHelper.LimitMaxEdge(decoded, maxEdge);
            try
            {
                using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(profile.JpegQuality, 1, 100));
                if (data is null)
                    return false;

                var jpeg = data.ToArray();
                if (jpeg.Length == 0 || jpeg.Length >= originalStream.Length)
                    return false;

                stream.Value = jpeg;
                dictionary.Elements.SetInteger("/Length", jpeg.Length);
                dictionary.Elements.SetName("/Filter", "/DCTDecode");
                dictionary.Elements.SetName("/ColorSpace", "/DeviceRGB");
                dictionary.Elements.SetInteger("/BitsPerComponent", 8);
                dictionary.Elements.SetInteger("/Width", bitmap.Width);
                dictionary.Elements.SetInteger("/Height", bitmap.Height);

                // Eski filtr parametrlari yangi JPEG oqimiga to'g'ri kelmaydi.
                dictionary.Elements.Remove("/DecodeParms");
                dictionary.Elements.Remove("/DP");
                dictionary.Elements.Remove("/Decode");
                return true;
            }
            finally
            {
                // LimitMaxEdge o'lchamni o'zgartirsa eski nusxani o'zi bo'shatadi, shuning uchun
                // faqat qaytgan bitmap yopiladi (u `decoded` bilan bir xil bo'lsa `using` ikki marta
                // Dispose qiladi — SKBitmap uchun bu xavfsiz).
                bitmap.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Bitta rasmning siqilmagani butun operatsiyani to'xtatishga arzimaydi.
            return false;
        }
    }

    /// <summary>
    /// Rasmga tegish xavfsizmi. Maskali, indekslangan yoki ekzotik kodlangan rasmlarni qayta
    /// kodlash ko'rinishni buzishi mumkin, shuning uchun ular qo'lga olinmaydi.
    /// </summary>
    private static bool IsSafeToRecompress(PdfDictionary dictionary, out List<string> filters, out string colorSpace)
    {
        filters = GetFilterNames(dictionary);
        colorSpace = GetColorSpaceName(dictionary);

        // Shaffoflik maskasi bor rasm JPEG ga aylantirilsa mask o'lchami mos kelmay qoladi.
        if (dictionary.Elements.ContainsKey("/SMask") || dictionary.Elements.ContainsKey("/Mask"))
            return false;

        if (dictionary.Elements.ContainsKey("/ImageMask") && dictionary.Elements.GetBoolean("/ImageMask"))
            return false;

        if (filters.Count != 1)
            return false; // Zanjirli filtrlar (masalan /ASCII85Decode + /FlateDecode) — xavfli.

        var filter = filters[0];
        if (filter is not ("/DCTDecode" or "/FlateDecode"))
            return false; // /CCITTFaxDecode, /JPXDecode, /JBIG2Decode va boshqalar chetlab o'tiladi.

        if (colorSpace.Contains("Indexed", StringComparison.Ordinal)
            || colorSpace.Contains("Separation", StringComparison.Ordinal)
            || colorSpace.Contains("DeviceN", StringComparison.Ordinal)
            || colorSpace.Contains("CMYK", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    /// <summary>Rasm oqimini <see cref="SKBitmap"/> ga yechadi; imkoni bo'lmasa <c>null</c>.</summary>
    private static SKBitmap? DecodeImage(PdfDictionary dictionary, List<string> filters, string colorSpace, int width, int height)
    {
        var stream = dictionary.Stream;
        if (stream is null)
            return null;

        if (filters[0] == "/DCTDecode")
        {
            // /DCTDecode oqimi — tayyor JPEG fayl baytlari.
            return SKBitmap.Decode(stream.Value);
        }

        if (dictionary.Elements.GetInteger("/BitsPerComponent") != 8)
            return null;

        var components = colorSpace switch
        {
            "/DeviceRGB" => 3,
            "/DeviceGray" => 1,
            _ => 0
        };

        if (components == 0)
            return null;

        var pixels = stream.UnfilteredValue;
        if (pixels is null || pixels.Length < (long)width * height * components)
            return null;

        return BuildBitmap(pixels, width, height, components);
    }

    /// <summary>Filtrlanmagan 8-bitli RGB/Gray piksellardan <see cref="SKBitmap"/> yig'adi.</summary>
    private static SKBitmap? BuildBitmap(byte[] pixels, int width, int height, int components)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        var bitmap = new SKBitmap(info);
        try
        {
            var rowBytes = bitmap.RowBytes;
            var buffer = new byte[(long)rowBytes * height <= int.MaxValue ? rowBytes * height : 0];
            if (buffer.Length == 0)
            {
                bitmap.Dispose();
                return null;
            }

            for (var y = 0; y < height; y++)
            {
                var source = y * width * components;
                var target = y * rowBytes;
                for (var x = 0; x < width; x++)
                {
                    byte r, g, b;
                    if (components == 1)
                    {
                        r = g = b = pixels[source + x];
                    }
                    else
                    {
                        var offset = source + (x * 3);
                        r = pixels[offset];
                        g = pixels[offset + 1];
                        b = pixels[offset + 2];
                    }

                    var index = target + (x * 4);
                    buffer[index] = r;
                    buffer[index + 1] = g;
                    buffer[index + 2] = b;
                    buffer[index + 3] = byte.MaxValue;
                }
            }

            var destination = bitmap.GetPixels();
            if (destination == IntPtr.Zero)
            {
                bitmap.Dispose();
                return null;
            }

            Marshal.Copy(buffer, 0, destination, buffer.Length);
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Oqim filtrlari nomlari (<c>/Filter</c> bitta nom ham, massiv ham bo'lishi mumkin).</summary>
    private static List<string> GetFilterNames(PdfDictionary dictionary)
    {
        var names = new List<string>(2);
        var item = dictionary.Elements.GetValue("/Filter");

        switch (item)
        {
            case PdfName name:
                names.Add(name.Value);
                break;

            case PdfArray array:
                foreach (var element in array.Elements)
                {
                    if (element is PdfName arrayName)
                        names.Add(arrayName.Value);
                }

                break;
        }

        return names;
    }

    /// <summary>
    /// <c>/ColorSpace</c> ning matnli ko'rinishi. Massiv yoki havola bo'lsa uning butun matni
    /// qaytadi — bizga faqat "Indexed", "CMYK" kabi kalit so'zlar bor-yo'qligi kerak.
    /// </summary>
    private static string GetColorSpaceName(PdfDictionary dictionary)
    {
        var item = dictionary.Elements.GetValue("/ColorSpace");
        return item switch
        {
            null => string.Empty,
            PdfName name => name.Value,
            _ => item.ToString() ?? string.Empty
        };
    }

    /// <summary>Hujjat ma'lumotlari va XMP metama'lumotlarini tozalaydi.</summary>
    private static void StripMetadata(PdfDocument document)
    {
        try
        {
            document.Info.Title = string.Empty;
            document.Info.Author = string.Empty;
            document.Info.Subject = string.Empty;
            document.Info.Keywords = string.Empty;
            document.Info.Creator = string.Empty;
            document.Info.Elements.Remove("/Producer");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Metama'lumotni tozalay olmaslik — siqishni bekor qilish uchun sabab emas.
        }

        try
        {
            document.Internals.Catalog.Elements.Remove("/Metadata");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Ba'zi hujjatlarda katalog qulflangan bo'ladi; shunchaki o'tkazib yuboramiz.
        }
    }

    // =================================================================================
    //  Himoyalash va qulfni ochish
    // =================================================================================

    /// <inheritdoc />
    public Task ProtectPdfAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Sodda ko'rinish: ochish paroli qo'yiladi, nusxa ko'chirish va yuqori sifatli chop etish taqiqlanadi.
        var options = new ProtectOptions
        {
            UserPassword = password ?? string.Empty,
            OwnerPassword = string.Empty,
            UseAes256 = true,
            Permissions = new PdfPermissions
            {
                AllowPrinting = true,
                AllowHighQualityPrinting = false,
                AllowCopying = false,
                AllowModifying = false,
                AllowAnnotations = false,
                AllowFormFilling = true,
                AllowAssembly = false
            }
        };

        return ProtectPdfAsync(inputPath, outputPath, options, progress, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ProtectPdfAsync(
        string inputPath,
        string outputPath,
        ProtectOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(options);
            EnsureFileExists(inputPath);
            ValidateOutputPath(outputPath);

            if (string.IsNullOrEmpty(options.UserPassword) && string.IsNullOrEmpty(options.OwnerPassword))
                throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Parol bo'sh bo'lishi mumkin emas.", inputPath);

            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
            progress?.Report(20);

            await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var document = OpenForModify(inputPath, bytes, password: null);
                        ApplySecurity(document, options);
                        progress?.Report(70);
                        SaveAtomically(document, outputPath);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(100);
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
            throw Wrap(ex, inputPath);
        }
    }

    /// <summary>
    /// Parollar, ruxsatlar va shifrlash darajasini qo'yadi.
    /// <para>
    /// Shifrlash darajasi PDFsharp 6 dagi <see cref="PdfStandardSecurityHandler.SetEncryption(PdfDefaultEncryption)"/>
    /// orqali beriladi: <see cref="PdfDefaultEncryption.V5"/> — AES-256 (PDF 2.0),
    /// <see cref="PdfDefaultEncryption.V4UsingAES"/> — AES-128 (eski dasturlar bilan ham mos keladi).
    /// </para>
    /// </summary>
    private static void ApplySecurity(PdfDocument document, ProtectOptions options)
    {
        var handler = document.SecurityHandler;
        handler.SetEncryption(options.UseAes256 ? PdfDefaultEncryption.V5 : PdfDefaultEncryption.V4UsingAES);

        var settings = document.SecuritySettings;
        var owner = string.IsNullOrEmpty(options.OwnerPassword) ? options.UserPassword : options.OwnerPassword;

        settings.UserPassword = options.UserPassword ?? string.Empty;
        settings.OwnerPassword = owner ?? string.Empty;

        var permissions = options.Permissions ?? new PdfPermissions();
        settings.PermitPrint = permissions.AllowPrinting;
        settings.PermitFullQualityPrint = permissions.AllowPrinting && permissions.AllowHighQualityPrinting;
        settings.PermitExtractContent = permissions.AllowCopying;
        settings.PermitModifyDocument = permissions.AllowModifying;
        settings.PermitAnnotations = permissions.AllowAnnotations;
        settings.PermitFormsFill = permissions.AllowFormFilling;
        settings.PermitAssembleDocument = permissions.AllowAssembly;
    }

    /// <inheritdoc />
    public async Task UnlockPdfAsync(
        string inputPath,
        string outputPath,
        string password,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(inputPath);
            ValidateOutputPath(outputPath);

            if (string.IsNullOrEmpty(password))
                throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Qulfni ochish uchun parol kiriting.", inputPath);

            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
            progress?.Report(20);

            await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var document = OpenForModify(inputPath, bytes, password);
                        document.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
                        progress?.Report(70);
                        SaveAtomically(document, outputPath);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(100);
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
            throw Wrap(ex, inputPath);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsPasswordProtectedAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(pdfPath);
            var bytes = await File.ReadAllBytesAsync(pdfPath, cancellationToken).ConfigureAwait(false);

            return await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            // Parolsiz ochib ko'ramiz. Hujjatda faqat egalik paroli bo'lsa ham ochiladi —
                            // bunday hujjat ochish uchun parol so'ramaydi, ya'ni javob "yo'q" bo'ladi.
                            // (InformationOnly rejimi PDFsharp 6 da amalga oshirilmagan, shuning uchun Import.)
                            using var stream = new MemoryStream(bytes, writable: false);
                            using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
                            return false;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                        {
                            // Parol so'ralsa PDFsharp istisno tashlaydi — bu ham "himoyalangan" degani.
                            if (MentionsPassword(ex))
                                return true;

                            throw Wrap(ex, pdfPath, PdfErrorKind.CorruptedDocument);
                        }
                    },
                    cancellationToken)
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
            throw Wrap(ex, pdfPath);
        }
    }

    // =================================================================================
    //  Suv belgisi
    // =================================================================================

    /// <inheritdoc />
    public async Task AddWatermarkAsync(
        string inputPath,
        string outputPath,
        WatermarkOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(options);
            EnsureFileExists(inputPath);
            ValidateOutputPath(outputPath);

            if (string.IsNullOrWhiteSpace(options.Text))
                throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Suv belgisi matni bo'sh bo'lishi mumkin emas.", inputPath);

            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);

            await Task.Run(() => WatermarkCore(inputPath, bytes, outputPath, options, progress, cancellationToken), cancellationToken)
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
            throw Wrap(ex, inputPath);
        }
    }

    private static void WatermarkCore(
        string inputPath,
        byte[] bytes,
        string outputPath,
        WatermarkOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var document = OpenForModify(inputPath, bytes, password: null);
        if (document.PageCount == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, $"'{Path.GetFileName(inputPath)}' faylida birorta ham sahifa yo'q.", inputPath);

        var font = CreateFont(options.FontFamily, options.FontSize, XFontStyleEx.Bold);
        var alpha = (int)Math.Round(Math.Clamp(options.Opacity, 0.05d, 1d) * 255d);
        var (red, green, blue) = ParseHexColor(options.ColorHex, 229, 72, 77);
        var brush = new XSolidBrush(XColor.FromArgb(alpha, red, green, blue));

        // Append — mazmun ustidan, Prepend — mazmun ostidan chiziladi.
        var pageOptions = options.DrawOnTop ? XGraphicsPdfPageOptions.Append : XGraphicsPdfPageOptions.Prepend;

        for (var index = 0; index < document.PageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.Pages[index];
            using (var gfx = XGraphics.FromPdfPage(page, pageOptions))
            {
                if (options.Position == WatermarkPosition.Tiled)
                    DrawTiledWatermark(gfx, page, options, font, brush);
                else
                    DrawSingleWatermark(gfx, page, options, font, brush);
            }

            ReportPercent(progress, index + 1, document.PageCount, 0, 95);
        }

        SaveAtomically(document, outputPath);
        progress?.Report(100);
    }

    private static void DrawSingleWatermark(XGraphics gfx, PdfPage page, WatermarkOptions options, XFont font, XBrush brush)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;
        var size = gfx.MeasureString(options.Text, font);

        var halfWidth = size.Width / 2d;
        var halfHeight = size.Height / 2d;

        var center = options.Position switch
        {
            WatermarkPosition.TopLeft => new XPoint(EdgeMarginPoints + halfWidth, EdgeMarginPoints + halfHeight),
            WatermarkPosition.TopRight => new XPoint(width - EdgeMarginPoints - halfWidth, EdgeMarginPoints + halfHeight),
            WatermarkPosition.BottomLeft => new XPoint(EdgeMarginPoints + halfWidth, height - EdgeMarginPoints - halfHeight),
            WatermarkPosition.BottomRight => new XPoint(width - EdgeMarginPoints - halfWidth, height - EdgeMarginPoints - halfHeight),
            _ => new XPoint(width / 2d, height / 2d)
        };

        DrawRotatedText(gfx, options.Text, font, brush, center, options.RotationDegrees);
    }

    private static void DrawTiledWatermark(XGraphics gfx, PdfPage page, WatermarkOptions options, XFont font, XBrush brush)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;

        for (var y = 0d; y < height; y += TileStepPoints)
        {
            for (var x = 0d; x < width; x += TileStepPoints)
            {
                var center = new XPoint(x + (TileStepPoints / 2d), y + (TileStepPoints / 2d));
                DrawRotatedText(gfx, options.Text, font, brush, center, options.RotationDegrees);
            }
        }
    }

    /// <summary>Matnni <paramref name="center"/> atrofida burib chizadi.</summary>
    private static void DrawRotatedText(XGraphics gfx, string text, XFont font, XBrush brush, XPoint center, double rotationDegrees)
    {
        var state = gfx.Save();
        try
        {
            if (Math.Abs(rotationDegrees) > 0.01d)
                gfx.RotateAtTransform(-rotationDegrees, center);

            gfx.DrawString(text, font, brush, center, XStringFormats.Center);
        }
        finally
        {
            gfx.Restore(state);
        }
    }

    // =================================================================================
    //  Sahifa raqamlari
    // =================================================================================

    /// <inheritdoc />
    public async Task AddPageNumbersAsync(
        string inputPath,
        string outputPath,
        PageNumberOptions options,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(options);
            EnsureFileExists(inputPath);
            ValidateOutputPath(outputPath);

            if (options.SkipFirstPages < 0)
                throw new PdfServiceException(PdfErrorKind.InvalidOptions, "O'tkazib yuboriladigan sahifalar soni manfiy bo'lishi mumkin emas.", inputPath);

            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);

            await Task.Run(() => PageNumberCore(inputPath, bytes, outputPath, options, progress, cancellationToken), cancellationToken)
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
            throw Wrap(ex, inputPath);
        }
    }

    private static void PageNumberCore(
        string inputPath,
        byte[] bytes,
        string outputPath,
        PageNumberOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var document = OpenForModify(inputPath, bytes, password: null);
        if (document.PageCount == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, $"'{Path.GetFileName(inputPath)}' faylida birorta ham sahifa yo'q.", inputPath);

        var font = CreateFont(options.FontFamily, options.FontSize, XFontStyleEx.Regular);
        var (red, green, blue) = ParseHexColor(options.ColorHex, 64, 64, 64);
        var brush = new XSolidBrush(XColor.FromArgb(255, red, green, blue));
        var format = string.IsNullOrWhiteSpace(options.Format) ? "{0}" : options.Format;
        var numbered = Math.Max(0, document.PageCount - options.SkipFirstPages);

        for (var index = 0; index < document.PageCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (index < options.SkipFirstPages)
            {
                ReportPercent(progress, index + 1, document.PageCount, 0, 95);
                continue;
            }

            var number = options.StartNumber + index - options.SkipFirstPages;
            var text = FormatPageNumber(format, number, numbered, inputPath);

            var page = document.Pages[index];
            using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                var (point, stringFormat) = ResolvePageNumberLayout(page, options);
                gfx.DrawString(text, font, brush, point, stringFormat);
            }

            ReportPercent(progress, index + 1, document.PageCount, 0, 95);
        }

        SaveAtomically(document, outputPath);
        progress?.Report(100);
    }

    private static string FormatPageNumber(string format, int number, int total, string inputPath)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, number, total);
        }
        catch (FormatException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                $"'{format}' — raqam shabloni noto'g'ri. {{0}} joriy raqam, {{1}} jami sahifalar uchun ishlatiladi.",
                inputPath,
                ex);
        }
    }

    private static (XPoint Point, XStringFormat Format) ResolvePageNumberLayout(PdfPage page, PageNumberOptions options)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;
        var margin = Math.Max(4d, Math.Min(options.MarginPoints, Math.Min(width, height) / 3d));

        return options.Position switch
        {
            PageNumberPosition.BottomLeft => (new XPoint(margin, height - margin), XStringFormats.BottomLeft),
            PageNumberPosition.BottomRight => (new XPoint(width - margin, height - margin), XStringFormats.BottomRight),
            PageNumberPosition.TopLeft => (new XPoint(margin, margin), XStringFormats.TopLeft),
            PageNumberPosition.TopCenter => (new XPoint(width / 2d, margin), XStringFormats.TopCenter),
            PageNumberPosition.TopRight => (new XPoint(width - margin, margin), XStringFormats.TopRight),
            _ => (new XPoint(width / 2d, height - margin), XStringFormats.BottomCenter)
        };
    }

    // =================================================================================
    //  Burish
    // =================================================================================

    /// <inheritdoc />
    public async Task RotatePagesAsync(
        string inputPath,
        string outputPath,
        int degrees,
        IReadOnlyList<int>? pageIndices = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureFileExists(inputPath);
            ValidateOutputPath(outputPath);

            if (degrees % 90 != 0)
                throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Burilish burchagi 90 ga karrali bo'lishi kerak.", inputPath);

            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
            var indices = pageIndices?.ToList();

            await Task.Run(() => RotateCore(inputPath, bytes, outputPath, degrees, indices, progress, cancellationToken), cancellationToken)
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
            throw Wrap(ex, inputPath);
        }
    }

    private static void RotateCore(
        string inputPath,
        byte[] bytes,
        string outputPath,
        int degrees,
        List<int>? pageIndices,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var document = OpenForModify(inputPath, bytes, password: null);
        if (document.PageCount == 0)
            throw new PdfServiceException(PdfErrorKind.EmptySelection, $"'{Path.GetFileName(inputPath)}' faylida birorta ham sahifa yo'q.", inputPath);

        // null — barcha sahifalar; aks holda faqat ko'rsatilganlari.
        var targets = pageIndices;
        if (targets is not null)
        {
            foreach (var index in targets)
            {
                if (index < 0 || index >= document.PageCount)
                    throw new PdfServiceException(
                        PdfErrorKind.PageIndexOutOfRange,
                        $"{index + 1}-sahifa mavjud emas — hujjatda jami {document.PageCount} ta sahifa bor.",
                        inputPath);
            }
        }
        else
        {
            targets = Enumerable.Range(0, document.PageCount).ToList();
        }

        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.Pages[targets[i]];
            var rotation = (page.Rotate + degrees) % 360;
            if (rotation < 0)
                rotation += 360;

            page.Rotate = rotation;
            ReportPercent(progress, i + 1, targets.Count, 0, 95);
        }

        SaveAtomically(document, outputPath);
        progress?.Report(100);
    }

    // =================================================================================
    //  Umumiy yordamchilar
    // =================================================================================

    /// <summary>
    /// PDFsharp 6 da hech qanday shrift o'z-o'zidan mavjud emas: hujjatga matn chizishdan oldin
    /// Windows shriftlar papkasidan foydalanishga ruxsat berilishi kerak. Buni bir marta,
    /// birinchi <see cref="XFont"/> yaratilishidan oldin qilamiz va boshqa joyda o'rnatilgan
    /// shrift hal qiluvchisi (font resolver) bo'lsa unga tegmaymiz.
    /// </summary>
    private static void EnsureFontsAvailable()
    {
        if (Interlocked.Exchange(ref _fontSetupDone, 1) != 0)
            return;

        try
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Sozlama allaqachon qulflangan bo'lsa — quyidagi XFont yaratish o'zi xato beradi.
        }
    }

    /// <summary>Talab qilingan shriftni, topilmasa zaxira shriftlardan birini yaratadi.</summary>
    private static XFont CreateFont(string? familyName, double size, XFontStyleEx style)
    {
        EnsureFontsAvailable();
        var emSize = Math.Clamp(size <= 0 ? 12d : size, 1d, 1000d);

        if (!string.IsNullOrWhiteSpace(familyName))
        {
            try
            {
                return new XFont(familyName, emSize, style);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Shrift tizimda yo'q — pastdagi zaxira ro'yxatiga o'tamiz.
            }
        }

        foreach (var fallback in FallbackFontFamilies)
        {
            try
            {
                return new XFont(fallback, emSize, style);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Keyingisini sinaymiz.
            }
        }

        throw new PdfServiceException(
            PdfErrorKind.MissingComponent,
            $"'{familyName}' shrifti va zaxira shriftlarning hech biri tizimda topilmadi.");
    }

    /// <summary><c>#RRGGBB</c>, <c>#RGB</c> yoki <c>#AARRGGBB</c> ni RGB uchligiga aylantiradi.</summary>
    private static (int Red, int Green, int Blue) ParseHexColor(string? hex, int defaultRed, int defaultGreen, int defaultBlue)
    {
        var text = (hex ?? string.Empty).Trim().TrimStart('#');

        if (text.Length == 3)
            text = string.Concat(text[0], text[0], text[1], text[1], text[2], text[2]);
        else if (text.Length == 8)
            text = text[2..]; // Alfa kanali alohida boshqariladi.

        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return (defaultRed, defaultGreen, defaultBlue);

        return ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
    }

    /// <summary>0..100 oralig'idagi foizni <paramref name="from"/>..<paramref name="to"/> bo'lagiga joylab xabar qiladi.</summary>
    private static void ReportPercent(IProgress<int>? progress, int done, int total, int from, int to)
    {
        if (progress is null)
            return;

        if (total <= 0)
        {
            progress.Report(to);
            return;
        }

        var value = from + (int)Math.Round((to - from) * (double)done / total);
        progress.Report(Math.Clamp(value, 0, 100));
    }

    /// <summary>Manba faylni faqat sahifa import qilish uchun ochadi.</summary>
    private static PdfDocument OpenForImport(string path, byte[] bytes)
    {
        try
        {
            // MemoryStream qaytgan hujjatga tegishli va u bilan birga bo'shatiladi.
            var stream = new MemoryStream(bytes, writable: false);
            return PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            throw Wrap(ex, path, PdfErrorKind.CorruptedDocument);
        }
    }

    /// <summary>Hujjatni o'zgartirish uchun ochadi; parol noto'g'ri bo'lsa tushunarli xato beradi.</summary>
    private static PdfDocument OpenForModify(string path, byte[] bytes, string? password)
    {
        try
        {
            var stream = new MemoryStream(bytes, writable: false);
            return password is null
                ? PdfReader.Open(stream, PdfDocumentOpenMode.Modify)
                : PdfReader.Open(stream, password, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            if (password is not null && MentionsPassword(ex))
                throw new PdfServiceException(
                    PdfErrorKind.InvalidPassword,
                    $"'{Path.GetFileName(path)}' uchun kiritilgan parol noto'g'ri.",
                    path,
                    ex);

            throw Wrap(ex, path, PdfErrorKind.CorruptedDocument);
        }
    }

    /// <summary>
    /// Yonidagi vaqtinchalik faylga yozib, keyin nishon ustiga ko'chiradi: nishon fayl har doim
    /// yo eskisi, yo to'liq yangisi bo'ladi.
    /// </summary>
    private static void SaveAtomically(PdfDocument document, string outputPath)
    {
        var tempPath = MakeTempPath(outputPath);
        try
        {
            document.Save(tempPath);
            MoveOverwrite(tempPath, outputPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(outputPath)}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                outputPath,
                ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static string MakeTempPath(string outputPath)
        => outputPath + ".tmp-" + Guid.NewGuid().ToString("N");

    private static void MoveOverwrite(string tempPath, string outputPath)
        => File.Move(tempPath, outputPath, overwrite: true);

    /// <summary>Nom band bo'lsa <c>-1</c>, <c>-2</c> qo'shib bo'sh nom topadi.</summary>
    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var attempt = 1; attempt < 10_000; attempt++)
        {
            var candidate = Path.Combine(directory, $"{name}-{attempt}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new PdfServiceException(
            PdfErrorKind.OutputNotWritable,
            $"'{name}{extension}' uchun bo'sh fayl nomi topilmadi.",
            path);
    }

    /// <summary>Fayl nomida ishlatib bo'lmaydigan belgilarni pastki chiziqqa almashtiradi.</summary>
    private static string SanitizeFileName(string name)
    {
        var cleaned = name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(cleaned) ? "hujjat" : cleaned;
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
            // Qolib ketgan vaqtinchalik fayl muvaffaqiyatli saqlashni bekor qilishga arzimaydi.
        }
    }

    private static void EnsureFileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, "Fayl ko'rsatilmadi.", path);

        if (!File.Exists(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"'{Path.GetFileName(path)}' fayli topilmadi.", path);
    }

    /// <summary>Natija papkasi mavjudligini (kerak bo'lsa yaratib) ta'minlaydi.</summary>
    private static void EnsureFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natija papkasi ko'rsatilmadi.", folder);

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

    private static void ValidateOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natija fayli ko'rsatilmadi.", outputPath);

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, $"'{outputPath}' — yaroqsiz fayl yo'li.", outputPath, ex);
        }

        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, $"'{directory}' papkasini yaratib bo'lmadi.", outputPath, ex);
        }
    }

    /// <summary>Kutubxona istisnosini interfeys va'da qilgan <see cref="PdfErrorKind"/> ga o'giradi.</summary>
    private static PdfServiceException Wrap(Exception exception, string? filePath, PdfErrorKind fallback = PdfErrorKind.Unknown)
    {
        if (exception is PdfServiceException already)
            return already;

        var name = string.IsNullOrEmpty(filePath) ? "Fayl" : $"'{Path.GetFileName(filePath)}'";

        if (exception is FileNotFoundException or DirectoryNotFoundException)
            return new PdfServiceException(PdfErrorKind.FileNotFound, $"{name} topilmadi.", filePath, exception);

        if (MentionsPassword(exception))
            return new PdfServiceException(PdfErrorKind.PasswordProtected, $"{name} parol bilan himoyalangan.", filePath, exception);

        if (exception is PdfReaderException or PdfSharp.PdfSharpException)
            return new PdfServiceException(PdfErrorKind.CorruptedDocument, $"{name} shikastlangan yoki PDF emas.", filePath, exception);

        if (exception is UnauthorizedAccessException or IOException)
            return new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"{name} faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                filePath,
                exception);

        if (exception is ArgumentException or FormatException)
            return new PdfServiceException(PdfErrorKind.InvalidOptions, $"{name} uchun berilgan sozlamalar noto'g'ri.", filePath, exception);

        if (fallback == PdfErrorKind.CorruptedDocument)
            return new PdfServiceException(PdfErrorKind.CorruptedDocument, $"{name} shikastlangan yoki PDF emas.", filePath, exception);

        return new PdfServiceException(PdfErrorKind.Unknown, $"{name} ustidagi amal bajarilmadi.", filePath, exception);
    }

    private static bool MentionsPassword(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("parol", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
