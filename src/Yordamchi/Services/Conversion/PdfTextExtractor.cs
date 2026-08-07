using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Yordamchi.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Graphics.Colors;

namespace Yordamchi.Services.Conversion;

/// <summary>
/// PDF ning haqiqiy matn qatlamini (PdfPig orqali) o'qib, uni <see cref="DocumentContent"/> oraliq
/// modeliga aylantiradi.
/// <para>
/// Bu yerda hech qanday rasterizatsiya yo'q: har bir belgi o'zining shrifti, o'lchami, qalinligi,
/// rangi va sahifadagi koordinatasi bilan o'qiladi. So'ngra belgilar so'zlarga, so'zlar
/// <em>qatorlarga</em>, qatorlar <em>abzas</em> yoki <em>jadval</em>larga guruhlanadi — natijada
/// Word'ga yozilgan hujjat rasm emas, to'liq tahrirlanadigan matn bo'ladi.
/// </para>
/// <para>
/// Muhim eslatma: PDF koordinatalari sahifaning <b>pastki-chap</b> burchagidan boshlanadi va Y
/// yuqoriga o'sadi. <see cref="ContentBlock.Top"/> esa yuqoridan hisoblanadi, shuning uchun har bir
/// vertikal qiymat <c>page.Height - y</c> formulasi bilan ag'dariladi.
/// </para>
/// </summary>
public static class PdfTextExtractor
{
    /// <summary>Qator boshidagi ro'yxat belgisi: <c>•</c>, <c>-</c>, <c>1.</c>, <c>1)</c>, <c>a)</c> va h.k.</summary>
    private static readonly Regex ListMarkerPattern = new(
        @"^\s*(?:[•‣●○▪◦·⁃∙*]|[-‐‒–—](?=\s)|\(?\d{1,3}\s*[.)](?=\s)|\(?[a-zA-Zа-яА-Я]\s*[.)](?=\s))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Shrift nomining <c>ABCDEF+</c> ko'rinishidagi "subset" prefiksi.</summary>
    private static readonly Regex SubsetPrefixPattern = new(
        @"^[A-Z]{6}\+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Shrift nomidagi uslub qo'shimchalari (<c>-Bold</c>, <c>,BoldItalic</c> …).</summary>
    private static readonly string[] StyleSuffixes =
    [
        "BoldItalic", "BoldOblique", "SemiBold", "ExtraBold", "DemiBold", "UltraBold",
        "Bold", "Italic", "Oblique", "Regular", "Roman", "Light", "Medium", "Black", "Heavy", "Thin"
    ];

    // =================================================================================
    //  Ommaviy (public) API
    // =================================================================================

    /// <summary>
    /// PDF faylning matn qatlamini o'qib, sahifama-sahifa bloklarga ajratadi.
    /// </summary>
    /// <param name="pdfPath">Manba PDF fayli.</param>
    /// <param name="options">Sarlavha/jadval/rasm aniqlash sozlamalari.</param>
    /// <param name="progress">Har sahifada xabar beriladi.</param>
    /// <exception cref="PdfServiceException">
    /// Fayl topilmasa, parol bilan yopilgan bo'lsa yoki buzilgan bo'lsa.
    /// </exception>
    public static DocumentContent Extract(
        string pdfPath,
        PdfToWordOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureFileExists(pdfPath);

        var content = new DocumentContent { SourcePath = pdfPath };
        var bytes = ReadAllBytes(pdfPath);

        using var document = OpenDocument(bytes, pdfPath);

        content.Title = NullIfBlank(document.Information?.Title);
        content.Author = NullIfBlank(document.Information?.Author);

        var total = document.NumberOfPages;
        for (var number = 1; number <= total; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PdfProgress(number - 1, total, $"{number}-sahifa o'qilmoqda…"));

            Page page;
            try
            {
                page = document.GetPage(number);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                // Bitta buzilgan sahifa butun hujjatni yo'qqa chiqarmasligi kerak.
                content.Pages.Add(new ContentPage { Number = number });
                continue;
            }

            content.Pages.Add(ExtractPage(page, options, pdfPath, cancellationToken));
            progress?.Report(new PdfProgress(number, total, $"{number}-sahifa o'qildi"));
        }

        return content;
    }

    /// <summary>
    /// PDF da foydalanish mumkin bo'lgan matn qatlami bormi.
    /// Kamida bitta sahifada <paramref name="minimumCharactersPerPage"/> dan ko'p "bo'sh bo'lmagan"
    /// belgi bo'lsa <c>true</c> qaytadi; aks holda hujjat skaner qilingan deb hisoblanadi.
    /// </summary>
    /// <exception cref="PdfServiceException">Parol bilan yopilgan yoki buzilgan hujjat.</exception>
    public static bool HasTextLayer(string pdfPath, int minimumCharactersPerPage = 24)
    {
        EnsureFileExists(pdfPath);
        var threshold = Math.Max(1, minimumCharactersPerPage);
        var bytes = ReadAllBytes(pdfPath);

        using var document = OpenDocument(bytes, pdfPath);
        for (var number = 1; number <= document.NumberOfPages; number++)
        {
            IReadOnlyList<Letter> letters;
            try
            {
                letters = document.GetPage(number).Letters;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue;
            }

            var count = 0;
            foreach (var letter in letters)
            {
                if (!string.IsNullOrWhiteSpace(letter.Value))
                    count++;

                if (count >= threshold)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Bitta sahifani bloklarga ajratadi (OCR bilan almashtirishda ham qo'l keladi).</summary>
    public static ContentPage ExtractPage(
        Page page,
        PdfToWordOptions options,
        string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);

        var result = new ContentPage
        {
            Number = page.Number,
            WidthPoints = page.Width > 0 ? page.Width : 595d,
            HeightPoints = page.Height > 0 ? page.Height : 842d
        };

        var words = SafeGetWords(page);
        var lines = BuildLines(words);

        if (lines.Count > 0)
        {
            var metrics = PageMetrics.Create(lines, result.WidthPoints, result.HeightPoints);
            cancellationToken.ThrowIfCancellationRequested();

            var consumed = new bool[lines.Count];
            if (options.DetectTables && lines.Count >= 2)
            {
                foreach (var table in DetectTables(lines, metrics, consumed))
                    result.Blocks.Add(table);
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var paragraph in BuildParagraphs(lines, consumed, metrics, options))
                result.Blocks.Add(paragraph);

            result.MarginLeftPoints = Clamp(metrics.MinLeft, 0d, result.WidthPoints / 3d);
            result.MarginTopPoints = Clamp(result.HeightPoints - metrics.MaxTop, 0d, result.HeightPoints / 3d);
        }

        if (options.ExtractImages)
        {
            foreach (var image in ExtractImages(page, result.HeightPoints))
                result.Blocks.Add(image);
        }

        // Bloklar sahifadagi tabiiy o'qish tartibida (yuqoridan pastga) turishi kerak.
        var ordered = result.Blocks.OrderBy(block => block.Top).ThenBy(block => block.Left).ToList();
        result.Blocks.Clear();
        result.Blocks.AddRange(ordered);

        _ = sourcePath;
        return result;
    }

    // =================================================================================
    //  1-bosqich: so'zlarni qatorlarga guruhlash
    // =================================================================================

    /// <summary>
    /// So'zlarni markaziy Y qiymati bo'yicha qatorlarga yig'adi. Ikki so'z bitta qatorda deb
    /// hisoblanadi, agar markazlar farqi o'rtacha belgi balandligining ~50% idan kichik bo'lsa.
    /// </summary>
    private static List<TextLine> BuildLines(IReadOnlyList<Word> words)
    {
        var usable = words
            .Where(word => word.Letters is { Count: > 0 } && !string.IsNullOrWhiteSpace(word.Text))
            .ToList();

        if (usable.Count == 0)
            return [];

        // PDF da Y yuqoriga o'sadi, shuning uchun "yuqoridan pastga" = Y kamayishi bo'yicha.
        var ordered = usable
            .OrderByDescending(CenterY)
            .ThenBy(word => word.BoundingBox.Left)
            .ToList();

        var groups = new List<List<Word>>();
        var current = new List<Word> { ordered[0] };
        var currentCenter = CenterY(ordered[0]);
        var currentHeight = GlyphHeight(ordered[0]);

        for (var i = 1; i < ordered.Count; i++)
        {
            var word = ordered[i];
            var center = CenterY(word);
            var height = GlyphHeight(word);
            var tolerance = 0.5d * Math.Max(1d, Math.Max(currentHeight, height));

            if (Math.Abs(center - currentCenter) <= tolerance)
            {
                current.Add(word);
                currentCenter = ((currentCenter * (current.Count - 1)) + center) / current.Count;
                currentHeight = Math.Max(currentHeight, height);
            }
            else
            {
                groups.Add(current);
                current = [word];
                currentCenter = center;
                currentHeight = height;
            }
        }

        groups.Add(current);

        return groups
            .Select(TextLine.Create)
            .Where(line => line.Text.Length > 0)
            .ToList();
    }

    // =================================================================================
    //  2-bosqich: qatorlarni abzaslarga birlashtirish
    // =================================================================================

    private static List<ParagraphBlock> BuildParagraphs(
        List<TextLine> lines,
        bool[] consumed,
        PageMetrics metrics,
        PdfToWordOptions options)
    {
        var blocks = new List<ParagraphBlock>();
        var group = new List<TextLine>();
        var groupKind = BlockKind.Paragraph;

        for (var i = 0; i < lines.Count; i++)
        {
            if (consumed[i])
            {
                // Jadvalga kirgan qatorlar abzas sifatida qayta yozilmaydi.
                Flush(blocks, group, groupKind, metrics);
                group = [];
                continue;
            }

            var line = lines[i];
            var kind = Classify(line, metrics, options);

            if (group.Count == 0)
            {
                group.Add(line);
                groupKind = kind;
                continue;
            }

            if (kind == groupKind && ContinuesParagraph(group, line, kind))
            {
                group.Add(line);
                continue;
            }

            Flush(blocks, group, groupKind, metrics);
            group = [line];
            groupKind = kind;
        }

        Flush(blocks, group, groupKind, metrics);
        return blocks;

        static void Flush(List<ParagraphBlock> target, List<TextLine> group, BlockKind kind, PageMetrics metrics)
        {
            if (group.Count == 0)
                return;

            var block = CreateParagraph(group, kind, metrics);
            if (!block.IsEmpty)
                target.Add(block);
        }
    }

    /// <summary>
    /// Keyingi qator oldingi abzasning davomimi: vertikal masofa satr balandligining 1.6 baravaridan
    /// kichik va chap chekkalar taxminan bir xil bo'lishi kerak.
    /// </summary>
    private static bool ContinuesParagraph(List<TextLine> group, TextLine line, BlockKind kind)
    {
        // Har bir ro'yxat elementi alohida abzas.
        if (kind == BlockKind.ListItem && StartsWithListMarker(line.Text))
            return false;

        var previous = group[^1];
        var lineHeight = Math.Max(1d, Math.Max(previous.FontSize, line.FontSize));
        var gap = previous.BaselineY - line.BaselineY;

        if (gap <= 0d || gap > lineHeight * 1.6d)
            return false;

        var tolerance = Math.Max(8d, lineHeight * 1.2d);

        // Ikkinchi qatordan boshlab abzasning "haqiqiy" chap chekkasi ma'lum bo'ladi.
        var bodyLeft = group.Count >= 2 ? group[1].Left : previous.Left;
        if (Math.Abs(line.Left - bodyLeft) <= tolerance)
            return true;

        // Birinchi qator qizil satr (chekinish) bilan boshlangan bo'lishi mumkin.
        return group.Count == 1
            && line.Left < previous.Left
            && previous.Left - line.Left <= lineHeight * 3d;
    }

    private static ParagraphBlock CreateParagraph(List<TextLine> lines, BlockKind kind, PageMetrics metrics)
    {
        var block = new ParagraphBlock { Kind = kind };

        var builder = new RunBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            builder.AppendLine(lines[i]);
            if (i == lines.Count - 1)
                continue;

            // Qator oxiridagi ko'chirish defisi olib tashlanadi, aks holda so'zlar bo'sh joy bilan ulanadi.
            if (builder.EndsWithSoftHyphen())
                builder.RemoveLastCharacter();
            else
                builder.AppendSpace();
        }

        block.Runs.AddRange(builder.Build());

        var left = lines.Min(line => line.Left);
        var right = lines.Max(line => line.Right);
        var top = lines.Max(line => line.TopY);
        var bottom = lines.Min(line => line.BottomY);

        block.Left = left;
        block.Top = metrics.PageHeight - top;
        block.Width = Math.Max(0d, right - left);
        block.Height = Math.Max(0d, top - bottom);
        block.IndentPoints = Math.Max(0d, left - metrics.MinLeft);
        block.Alignment = DetectAlignment(lines, metrics);
        block.SpaceAfterPoints = kind is BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3 ? 8d : 6d;

        return block;
    }

    // =================================================================================
    //  3–5-bosqich: sarlavha, ro'yxat va tekislashni aniqlash
    // =================================================================================

    private static BlockKind Classify(TextLine line, PageMetrics metrics, PdfToWordOptions options)
    {
        if (StartsWithListMarker(line.Text))
            return BlockKind.ListItem;

        if (!options.DetectHeadings || metrics.BodyFontSize <= 0d)
            return BlockKind.Paragraph;

        // Uzun matn sarlavha bo'lolmaydi — bu eng ko'p uchraydigan noto'g'ri aniqlash manbai.
        if (line.Text.Length > 200)
            return BlockKind.Paragraph;

        var ratio = line.FontSize / metrics.BodyFontSize;
        if (ratio >= 1.6d)
            return BlockKind.Heading1;
        if (ratio >= 1.3d)
            return BlockKind.Heading2;
        if (ratio >= 1.15d || (line.IsBold && line.Text.Length <= 120))
            return BlockKind.Heading3;

        return BlockKind.Paragraph;
    }

    private static bool StartsWithListMarker(string text)
        => !string.IsNullOrWhiteSpace(text) && ListMarkerPattern.IsMatch(text);

    private static TextAlignment DetectAlignment(List<TextLine> lines, PageMetrics metrics)
    {
        var left = lines.Min(line => line.Left);
        var right = lines.Max(line => line.Right);
        var center = (left + right) / 2d;
        var pageCenter = metrics.PageWidth / 2d;
        var indent = left - metrics.MinLeft;
        var rightGap = metrics.MaxRight - right;

        if (Math.Abs(center - pageCenter) <= metrics.PageWidth * 0.035d && indent > metrics.PageWidth * 0.06d)
            return TextAlignment.Center;

        if (rightGap <= 2d && indent > metrics.PageWidth * 0.12d)
            return TextAlignment.Right;

        return TextAlignment.Left;
    }

    // =================================================================================
    //  6-bosqich: jadvallarni aniqlash
    // =================================================================================

    /// <summary>
    /// Ketma-ket qatorlarda bir xil X pozitsiyalarida takrorlanuvchi keng "uzuq"lar jadval
    /// ustunlarini bildiradi. Jadvalga kirgan qatorlar <paramref name="consumed"/> da belgilanadi.
    /// </summary>
    private static List<TableBlock> DetectTables(List<TextLine> lines, PageMetrics metrics, bool[] consumed)
    {
        const double PositionTolerance = 5d;

        var threshold = Math.Max(metrics.MedianWordGap * 2.5d, Math.Max(4d, metrics.BodyFontSize * 0.9d));
        var gaps = lines.Select(line => FindColumnGaps(line, threshold)).ToList();
        var tables = new List<TableBlock>();

        var index = 0;
        while (index < lines.Count)
        {
            if (gaps[index].Count == 0)
            {
                index++;
                continue;
            }

            var end = index + 1;
            while (end < lines.Count && GapsMatch(gaps[index], gaps[end], PositionTolerance))
                end++;

            if (end - index < 2)
            {
                index++;
                continue;
            }

            var rows = lines.GetRange(index, end - index);
            var table = BuildTable(rows, gaps.GetRange(index, end - index), metrics);
            if (table is not null)
            {
                tables.Add(table);
                for (var i = index; i < end; i++)
                    consumed[i] = true;
            }

            index = end;
        }

        return tables;
    }

    /// <summary>
    /// Qator ichidagi keng bo'shliqlardan keyin boshlanadigan so'zlarning chap chekkalari.
    /// <para>
    /// Aynan chap chekka olinadi, bo'shliqning markazi emas: ustunlar bir xil X dan boshlanadi,
    /// markaz esa oldingi katakdagi matn uzunligiga qarab har qatorda siljib ketadi.
    /// </para>
    /// </summary>
    private static List<double> FindColumnGaps(TextLine line, double threshold)
    {
        var result = new List<double>();
        if (line.Words.Count < 2)
            return result;

        var runningRight = line.Words[0].BoundingBox.Right;
        for (var i = 1; i < line.Words.Count; i++)
        {
            var left = line.Words[i].BoundingBox.Left;
            if (left - runningRight >= threshold)
                result.Add(left);

            runningRight = Math.Max(runningRight, line.Words[i].BoundingBox.Right);
        }

        return result;
    }

    private static bool GapsMatch(List<double> reference, List<double> candidate, double tolerance)
    {
        if (reference.Count == 0 || reference.Count != candidate.Count)
            return false;

        for (var i = 0; i < reference.Count; i++)
        {
            if (Math.Abs(reference[i] - candidate[i]) > tolerance)
                return false;
        }

        return true;
    }

    private static TableBlock? BuildTable(List<TextLine> rows, List<List<double>> rowGaps, PageMetrics metrics)
    {
        var columnCount = rowGaps[0].Count + 1;
        if (columnCount < 2)
            return null;

        // Ustun chegaralari — barcha qatorlardagi uzuqlarning o'rtachasi.
        var boundaries = new double[rowGaps[0].Count];
        for (var i = 0; i < boundaries.Length; i++)
            boundaries[i] = rowGaps.Average(gaps => gaps[i]);

        var left = rows.Min(row => row.Left);
        var right = rows.Max(row => row.Right);
        var top = rows.Max(row => row.TopY);
        var bottom = rows.Min(row => row.BottomY);

        // Chegara ustun boshlanish nuqtasidan bir oz chapga suriladi, aks holda ustunning
        // birinchi so'zi chegaraning aynan ustiga tushib qolishi mumkin.
        var edges = new double[columnCount + 1];
        edges[0] = left - 1d;
        for (var i = 0; i < boundaries.Length; i++)
            edges[i + 1] = boundaries[i] - 2d;
        edges[columnCount] = right + 1d;

        var table = new TableBlock
        {
            Left = left,
            Top = metrics.PageHeight - top,
            Width = Math.Max(0d, right - left),
            Height = Math.Max(0d, top - bottom)
        };

        // Oxirgi ustunning o'ng chegarasi faqat matn uzunligidan ma'lum, shuning uchun juda tor
        // chiqishi mumkin. Har bir ustunga o'rtacha kenglikning 60% i miqdorida "pol" qo'yamiz.
        var rawWidths = new double[columnCount];
        for (var i = 0; i < columnCount; i++)
            rawWidths[i] = Math.Max(1d, edges[i + 1] - edges[i]);

        var floor = rawWidths.Average() * 0.6d;
        for (var i = 0; i < columnCount; i++)
            rawWidths[i] = Math.Max(rawWidths[i], floor);

        var totalWidth = Math.Max(1d, rawWidths.Sum());
        for (var i = 0; i < columnCount; i++)
            table.ColumnWidths.Add(rawWidths[i] / totalWidth);

        for (var r = 0; r < rows.Count; r++)
        {
            var line = rows[r];
            var row = new Models.TableRow { IsHeader = r == 0 && line.IsBold };

            for (var c = 0; c < columnCount; c++)
            {
                var cell = new Models.TableCell();
                var builder = new RunBuilder();
                var any = false;

                foreach (var word in line.Words)
                {
                    var centerX = (word.BoundingBox.Left + word.BoundingBox.Right) / 2d;
                    if (centerX < edges[c] || centerX >= edges[c + 1])
                        continue;

                    if (any)
                        builder.AppendSpace();

                    builder.AppendWord(word);
                    any = true;
                }

                cell.Runs.AddRange(builder.Build());
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }

    // =================================================================================
    //  8-bosqich: rasmlar
    // =================================================================================

    private static List<ImageBlock> ExtractImages(Page page, double pageHeight)
    {
        var blocks = new List<ImageBlock>();

        IReadOnlyList<IPdfImage> images;
        try
        {
            images = page.GetImages().ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return blocks;
        }

        foreach (var image in images)
        {
            try
            {
                if (image.WidthInSamples < 2 || image.HeightInSamples < 2)
                    continue;

                if (!image.TryGetPng(out var png) || png is null || png.Length == 0)
                    continue;

                var bounds = image.Bounds;
                var width = Math.Abs(bounds.Width);
                var height = Math.Abs(bounds.Height);
                if (width < 2d || height < 2d)
                    continue;

                blocks.Add(new ImageBlock
                {
                    Data = png,
                    ContentType = "image/png",
                    PixelWidth = image.WidthInSamples,
                    PixelHeight = image.HeightInSamples,
                    Left = bounds.Left,
                    Top = pageHeight - bounds.Top,
                    Width = width,
                    Height = height
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Qo'llab-quvvatlanmaydigan rangli fazo yoki buzilgan oqim — rasmni o'tkazib yuboramiz.
            }
        }

        return blocks;
    }

    // =================================================================================
    //  7-bosqich: shrift, o'lcham va rang
    // =================================================================================

    /// <summary>
    /// <c>ABCDEF+TimesNewRomanPS-BoldMT</c> kabi xom nomdan <c>Times New Roman</c> hosil qiladi.
    /// </summary>
    internal static string CleanFontName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "Calibri";

        var name = SubsetPrefixPattern.Replace(rawName.Trim(), string.Empty);

        var cut = name.IndexOfAny(['-', ',', '+', '_']);
        if (cut > 0)
            name = name[..cut];

        name = TrimSuffix(name, "PSMT");
        name = TrimSuffix(name, "PS");
        name = TrimSuffix(name, "MT");
        foreach (var suffix in StyleSuffixes)
            name = TrimSuffix(name, suffix);

        name = name.Trim();
        if (name.Length == 0)
            return "Calibri";

        return name.Contains(' ') ? name : SplitCamelCase(name);
    }

    private static string TrimSuffix(string value, string suffix)
        => value.Length > suffix.Length && value.EndsWith(suffix, StringComparison.Ordinal)
            ? value[..^suffix.Length]
            : value;

    /// <summary><c>TimesNewRoman</c> → <c>Times New Roman</c>.</summary>
    private static string SplitCamelCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current) && !char.IsUpper(value[i - 1]) && !char.IsWhiteSpace(value[i - 1]))
                builder.Append(' ');

            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>PdfPig rangini <c>#RRGGBB</c> ga aylantiradi; qora va oq uchun <c>null</c>.</summary>
    internal static string? ColorToHex(IColor? color)
    {
        if (color is null)
            return null;

        double r, g, b;
        try
        {
            (r, g, b) = color.ToRGBValues();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }

        if (double.IsNaN(r) || double.IsNaN(g) || double.IsNaN(b))
            return null;

        // Oq matn oq fonda ko'rinmay qoladi — uni qora (standart) rangga qaytaramiz.
        if (r >= 0.94d && g >= 0.94d && b >= 0.94d)
            return null;

        // Qora — allaqachon standart rang, ortiqcha atribut yozmaymiz.
        if (r <= 0.08d && g <= 0.08d && b <= 0.08d)
            return null;

        return string.Create(CultureInfo.InvariantCulture, $"#{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}");

        static int ToByte(double value) => (int)Math.Round(Clamp(value, 0d, 1d) * 255d);
    }

    // =================================================================================
    //  Yordamchi turlar
    // =================================================================================

    /// <summary>Bitta matn qatori va uning o'lchov ko'rsatkichlari.</summary>
    private sealed class TextLine
    {
        public required List<Word> Words { get; init; }

        public required string Text { get; init; }

        public double Left { get; init; }

        public double Right { get; init; }

        /// <summary>Qatorning eng yuqori nuqtasi (PDF koordinatasi, pastdan yuqoriga).</summary>
        public double TopY { get; init; }

        public double BottomY { get; init; }

        /// <summary>Bazaviy chiziq (baseline) — qatorlar orasidagi masofani shu bo'yicha o'lchaymiz.</summary>
        public double BaselineY { get; init; }

        public double FontSize { get; init; }

        public bool IsBold { get; init; }

        public static TextLine Create(List<Word> words)
        {
            var sorted = words.OrderBy(word => word.BoundingBox.Left).ToList();

            var sizes = new List<double>();
            var baselines = new List<double>();
            var boldLetters = 0;
            var totalLetters = 0;

            foreach (var letter in sorted.SelectMany(word => word.Letters))
            {
                var size = letter.PointSize > 0d ? letter.PointSize : letter.FontSize;
                if (size > 0d && !double.IsNaN(size))
                    sizes.Add(size);

                baselines.Add(letter.StartBaseLine.Y);
                totalLetters++;
                if (IsBoldLetter(letter))
                    boldLetters++;
            }

            return new TextLine
            {
                Words = sorted,
                Text = string.Join(" ", sorted.Select(word => word.Text)).Trim(),
                Left = sorted.Min(word => word.BoundingBox.Left),
                Right = sorted.Max(word => word.BoundingBox.Right),
                TopY = sorted.Max(word => word.BoundingBox.Top),
                BottomY = sorted.Min(word => word.BoundingBox.Bottom),
                BaselineY = baselines.Count > 0 ? Median(baselines) : sorted.Min(word => word.BoundingBox.Bottom),
                FontSize = sizes.Count > 0 ? Median(sizes) : 11d,
                IsBold = totalLetters > 0 && boldLetters >= totalLetters * 0.7d
            };
        }
    }

    /// <summary>Sahifa bo'yicha umumiy o'lchovlar — sarlavha, tekislash va jadval uchun asos.</summary>
    private sealed class PageMetrics
    {
        public required double PageWidth { get; init; }

        public required double PageHeight { get; init; }

        /// <summary>Sahifadagi "asosiy matn" shrift o'lchami (belgilar bo'yicha mediana).</summary>
        public required double BodyFontSize { get; init; }

        public required double MinLeft { get; init; }

        public required double MaxRight { get; init; }

        public required double MaxTop { get; init; }

        /// <summary>So'zlar orasidagi odatiy bo'shliq — jadval "uzuq"laridan farqlash uchun.</summary>
        public required double MedianWordGap { get; init; }

        public static PageMetrics Create(List<TextLine> lines, double pageWidth, double pageHeight)
        {
            var sizes = new List<double>();
            var gaps = new List<double>();

            foreach (var line in lines)
            {
                foreach (var letter in line.Words.SelectMany(word => word.Letters))
                {
                    var size = letter.PointSize > 0d ? letter.PointSize : letter.FontSize;
                    if (size > 0d && !double.IsNaN(size))
                        sizes.Add(size);
                }

                for (var i = 1; i < line.Words.Count; i++)
                {
                    var gap = line.Words[i].BoundingBox.Left - line.Words[i - 1].BoundingBox.Right;
                    if (gap > 0d && gap < 100d)
                        gaps.Add(gap);
                }
            }

            return new PageMetrics
            {
                PageWidth = pageWidth,
                PageHeight = pageHeight,
                BodyFontSize = sizes.Count > 0 ? Median(sizes) : 11d,
                MinLeft = lines.Min(line => line.Left),
                MaxRight = lines.Max(line => line.Right),
                MaxTop = lines.Max(line => line.TopY),
                MedianWordGap = gaps.Count > 0 ? Median(gaps) : 2d
            };
        }
    }

    /// <summary>
    /// Belgilarni bir xil ko'rinishga ega bo'laklarga (<see cref="TextRun"/>) yig'adi:
    /// shrift, o'lcham, qalinlik, kursiv yoki rang o'zgarsa — yangi bo'lak boshlanadi.
    /// </summary>
    private sealed class RunBuilder
    {
        private readonly List<(RunStyle Style, StringBuilder Text)> _segments = [];

        public void AppendLine(TextLine line)
        {
            for (var i = 0; i < line.Words.Count; i++)
            {
                if (i > 0)
                    AppendSpace();

                AppendWord(line.Words[i]);
            }
        }

        public void AppendWord(Word word)
        {
            foreach (var letter in word.Letters)
            {
                if (string.IsNullOrEmpty(letter.Value))
                    continue;

                Append(RunStyle.From(letter), letter.Value);
            }
        }

        public void AppendSpace()
        {
            if (_segments.Count == 0)
                return;

            var last = _segments[^1];
            if (last.Text.Length > 0 && last.Text[^1] == ' ')
                return;

            last.Text.Append(' ');
        }

        /// <summary>Qator oxiri so'z ko'chirish defisi bilan tugaganmi.</summary>
        public bool EndsWithSoftHyphen()
        {
            var tail = Tail(2);
            return tail.Length == 2 && (tail[1] is '-' or '‐') && char.IsLetter(tail[0]);
        }

        public void RemoveLastCharacter()
        {
            for (var i = _segments.Count - 1; i >= 0; i--)
            {
                if (_segments[i].Text.Length == 0)
                    continue;

                _segments[i].Text.Length--;
                return;
            }
        }

        public List<TextRun> Build()
        {
            var runs = new List<TextRun>(_segments.Count);
            foreach (var (style, text) in _segments)
            {
                if (text.Length == 0)
                    continue;

                runs.Add(new TextRun(
                    text.ToString(),
                    style.FontFamily,
                    style.FontSize,
                    style.IsBold,
                    style.IsItalic,
                    style.ColorHex));
            }

            return runs;
        }

        private void Append(RunStyle style, string value)
        {
            if (_segments.Count > 0 && _segments[^1].Style.Equals(style))
            {
                _segments[^1].Text.Append(value);
                return;
            }

            var builder = new StringBuilder();

            // Bo'sh joy oldingi bo'lakda "osilib" qolmasin: yangi uslub boshlansa ham u saqlanadi.
            builder.Append(value);
            _segments.Add((style, builder));
        }

        private string Tail(int count)
        {
            var buffer = new StringBuilder(count);
            for (var i = _segments.Count - 1; i >= 0 && buffer.Length < count; i--)
            {
                var text = _segments[i].Text;
                for (var j = text.Length - 1; j >= 0 && buffer.Length < count; j--)
                    buffer.Insert(0, text[j]);
            }

            return buffer.ToString();
        }
    }

    /// <summary>Bir xil ko'rinishdagi belgilar to'plamini bildiruvchi kalit.</summary>
    private readonly record struct RunStyle(
        string FontFamily,
        double FontSize,
        bool IsBold,
        bool IsItalic,
        string? ColorHex)
    {
        public static RunStyle From(Letter letter)
        {
            var size = letter.PointSize > 0d ? letter.PointSize : letter.FontSize;
            if (double.IsNaN(size) || size <= 0d)
                size = 11d;

            // Yarim punktgacha yaxlitlash mayda o'lchov farqlari tufayli bo'laklar parchalanib
            // ketishining oldini oladi.
            size = Math.Round(Clamp(size, 4d, 96d) * 2d, MidpointRounding.AwayFromZero) / 2d;

            var rawName = !string.IsNullOrWhiteSpace(letter.FontName) ? letter.FontName : letter.Font?.Name;

            return new RunStyle(
                CleanFontName(rawName),
                size,
                IsBoldLetter(letter),
                IsItalicLetter(letter),
                ColorToHex(letter.Color));
        }
    }

    // =================================================================================
    //  Kichik yordamchilar
    // =================================================================================

    private static bool IsBoldLetter(Letter letter)
    {
        if (letter.Font is { } font && (font.IsBold || font.Weight >= 600))
            return true;

        var name = letter.FontName ?? letter.Font?.Name;
        return name is not null
            && (name.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Black", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Heavy", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsItalicLetter(Letter letter)
    {
        if (letter.Font is { IsItalic: true })
            return true;

        var name = letter.FontName ?? letter.Font?.Name;
        return name is not null
            && (name.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase));
    }

    private static double CenterY(Word word)
    {
        var box = word.BoundingBox;
        return (box.Bottom + box.Top) / 2d;
    }

    private static double GlyphHeight(Word word)
    {
        var heights = word.Letters
            .Select(letter => Math.Abs(letter.GlyphRectangle.Height))
            .Where(height => height > 0d)
            .ToList();

        if (heights.Count > 0)
            return Median(heights);

        var boxHeight = Math.Abs(word.BoundingBox.Height);
        return boxHeight > 0d ? boxHeight : 10d;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0d;

        var sorted = values.ToArray();
        Array.Sort(sorted);
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static double Clamp(double value, double min, double max)
        => double.IsNaN(value) ? min : Math.Min(Math.Max(value, min), max);

    private static IReadOnlyList<Word> SafeGetWords(Page page)
    {
        try
        {
            return page.GetWords().ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static byte[] ReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"'{Path.GetFileName(path)}' faylini o'qib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                path,
                ex);
        }
    }

    /// <summary>
    /// Hujjatni bayt buferidan ochadi (fayl bandligini oldini olish uchun) va kutubxona
    /// xatolarini <see cref="PdfServiceException"/> ga o'raydi.
    /// </summary>
    private static PdfDocument OpenDocument(byte[] bytes, string pdfPath)
    {
        var options = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            ClipPaths = false
        };

        try
        {
            return PdfDocument.Open(bytes, options);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.PasswordProtected,
                $"'{Path.GetFileName(pdfPath)}' parol bilan himoyalangan. Avval parolni olib tashlang.",
                pdfPath,
                ex);
        }
        catch (PdfDocumentFormatException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                $"'{Path.GetFileName(pdfPath)}' buzilgan yoki PDF fayl emas.",
                pdfPath,
                ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            if (MentionsPassword(ex))
            {
                throw new PdfServiceException(
                    PdfErrorKind.PasswordProtected,
                    $"'{Path.GetFileName(pdfPath)}' parol bilan himoyalangan.",
                    pdfPath,
                    ex);
            }

            throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                $"'{Path.GetFileName(pdfPath)}' o'qib bo'lmadi: hujjat buzilgan yoki qo'llab-quvvatlanmaydi.",
                pdfPath,
                ex);
        }
    }

    private static bool MentionsPassword(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureFileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, "Fayl ko'rsatilmagan.", path);

        if (!File.Exists(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"'{Path.GetFileName(path)}' topilmadi.", path);
    }
}
