using System.Globalization;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Yordamchi.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Yordamchi.Services.Conversion;

// =====================================================================================
//  Word (.docx) → PDF: Microsoft Office talab qilmaydigan ichki renderer.
//
//  Bu — kichik matn terish (typesetting) dvigateli. OpenXML hujjatidan abzas, jadval va
//  rasmlarni o'qiydi va ularni PDFsharp'ning XGraphics'i bilan sahifaga chizadi. Word'ning
//  o'zi qiladigan ishning hammasini takrorlash imkonsiz (kolontitullar, matnli ramkalar,
//  murakkab oqimlar qoldirilgan), lekin oddiy hujjatlar — ariza, hisobot, jadval, ro'yxat —
//  ko'rinishi asliga juda yaqin chiqadi.
//
//  Ish bosqichlari:
//    1. Uslublar jadvali o'qiladi (docDefaults → Normal → uslub → to'g'ridan-to'g'ri formatlash).
//    2. Bo'lim xossalaridan sahifa o'lchami va chekkalari olinadi.
//    3. Har bir abzas "tokenlar" (so'z, bo'sh joy, tab, uzilish, rasm) ketma-ketligiga aylanadi.
//    4. Tokenlar o'lchanadi va qatorlarga yig'iladi (word wrap), qator sahifaga chiziladi.
//    5. Sahifa to'lganda yangisi ochiladi.
//
//  Shriftlar haqida: PDFsharp 6 o'zicha shrift fayllarini topa olmaydi — unga "font
//  resolver" kerak. Shuning uchun quyida Windows'ning shrift papkalarini o'qiydigan
//  soddagina yechuvchi bor (pastdagi WindowsFileFontResolver sinfiga qarang).
// =====================================================================================

/// <summary>Word hujjatini Office'siz PDF ga aylantiradi.</summary>
public static class WordToPdfRenderer
{
    /// <summary>Shrift muhiti butun jarayon davomida bir marta sozlanadi.</summary>
    private static readonly object FontGate = new();

    private static bool _fontEnvironmentReady;

    /// <summary>
    /// Word hujjatini PDF ga chizadi.
    /// </summary>
    /// <param name="docxPath">Manba .docx fayl.</param>
    /// <param name="pdfPath">Yaratiladigan PDF fayl.</param>
    /// <param name="options">Konvertatsiya sozlamalari.</param>
    /// <param name="progress">Jarayon haqida xabar berish uchun (ixtiyoriy).</param>
    /// <param name="cancellationToken">Bekor qilish belgisi.</param>
    public static void Render(
        string docxPath,
        string pdfPath,
        WordToPdfOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        options ??= WordToPdfOptions.Default;

        if (string.IsNullOrWhiteSpace(docxPath))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Manba Word hujjatining yo'li ko'rsatilmagan.");

        if (string.IsNullOrWhiteSpace(pdfPath))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Natijaviy PDF fayl yo'li ko'rsatilmagan.", docxPath);

        var fullSource = Path.GetFullPath(docxPath);
        var fullTarget = Path.GetFullPath(pdfPath);

        if (!File.Exists(fullSource))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"'{Path.GetFileName(fullSource)}' fayli topilmadi.", fullSource);

        if (!string.Equals(Path.GetExtension(fullSource), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                "Ichki renderer faqat .docx hujjatlarini o'qiydi. Eski .doc formatini avval .docx ga saqlang "
                + "yoki Microsoft Word dvigatelidan foydalaning.",
                fullSource);
        }

        var directory = Path.GetDirectoryName(fullTarget);
        if (string.IsNullOrEmpty(directory))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natijaviy fayl papkasini aniqlab bo'lmadi.", fullTarget);

        try
        {
            Directory.CreateDirectory(directory);
            EnsureFontEnvironment();

            // Natija avval vaqtinchalik faylga yoziladi: shunda xato yuz bersa,
            // eski PDF joyida buzilgan yarim fayl qolib ketmaydi.
            var temporaryPath = Path.Combine(directory, Path.GetRandomFileName() + ".pdf");
            try
            {
                RenderCore(fullSource, temporaryPath, options, progress, cancellationToken);
                File.Move(temporaryPath, fullTarget, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(fullTarget)}' faylini yozib bo'lmadi: ruxsat yo'q yoki fayl boshqa dasturda ochiq.",
                fullTarget,
                ex);
        }
        catch (IOException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(fullTarget)}' faylini yozib bo'lmadi: {ex.Message}",
                fullTarget,
                ex);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"Word hujjatini PDF ga aylantirishda xato yuz berdi: {ex.Message}",
                fullSource,
                ex);
        }
    }

    /// <summary>Hujjatni ochib, bloklarni sahifaga chizadi.</summary>
    private static void RenderCore(
        string docxPath,
        string pdfPath,
        WordToPdfOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(docxPath, false);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                $"'{Path.GetFileName(docxPath)}' Word hujjati sifatida ochilmadi: {ex.Message}",
                docxPath,
                ex);
        }

        using (document)
        {
            var mainPart = document.MainDocumentPart
                ?? throw new PdfServiceException(PdfErrorKind.CorruptedDocument, "Word hujjatining asosiy qismi topilmadi.", docxPath);

            var body = mainPart.Document?.Body
                ?? throw new PdfServiceException(PdfErrorKind.CorruptedDocument, "Word hujjatining matn qismi bo'sh yoki buzilgan.", docxPath);

            var styles = new StyleTable(mainPart);
            var numbering = new NumberingTable(mainPart);
            var setup = PageSetup.FromBody(body);

            using var pdf = new PdfDocument();
            pdf.Info.Title = Path.GetFileNameWithoutExtension(docxPath);
            pdf.Info.Creator = "Yordamchi";

            using (var engine = new LayoutEngine(pdf, setup, mainPart, styles, numbering, options, cancellationToken))
            {
                var blocks = body.ChildElements
                    .Where(element => element is W.Paragraph or W.Table)
                    .ToList();

                var processed = 0;
                foreach (var element in blocks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (element is W.Paragraph paragraph)
                        engine.WriteParagraph(paragraph);
                    else if (element is W.Table table)
                        engine.WriteTable(table);

                    processed++;
                    progress?.Report(new PdfProgress(
                        processed,
                        blocks.Count,
                        $"{processed}/{blocks.Count} blok chizildi ({pdf.PageCount}-sahifa)"));
                }
            }

            if (pdf.PageCount == 0)
                pdf.AddPage();

            pdf.Save(pdfPath);
        }
    }

    /// <summary>
    /// PDFsharp'ga shrift fayllarini qayerdan olishni o'rgatadi.
    /// <para>
    /// PDFsharp 6 da WPF ilovasi ham "core" rejimida ishlaydi, ya'ni shriftlarni o'zi
    /// qidirmaydi va yechuvchisiz <c>XFont</c> yaratilganda xato beradi. Ikkita chora
    /// birdaniga qo'llanadi: Windows shriftlarini ishlatish bayrog'i yoqiladi va o'z
    /// yechuvchimiz o'rnatiladi. Yechuvchi allaqachon o'rnatilgan bo'lsa (dasturning
    /// boshqa qismi tomonidan) unga tegilmaydi — PDFsharp uni almashtirishga ruxsat bermaydi.
    /// </para>
    /// </summary>
    private static void EnsureFontEnvironment()
    {
        if (_fontEnvironmentReady)
            return;

        lock (FontGate)
        {
            if (_fontEnvironmentReady)
                return;

            try
            {
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            }
            catch
            {
                // Bayroq mavjud bo'lmasa ham quyidagi yechuvchi ishni bajaradi.
            }

            try
            {
                if (GlobalFontSettings.FontResolver is null)
                    GlobalFontSettings.FontResolver = new WindowsFileFontResolver();
            }
            catch
            {
                // Shriftlar allaqachon ishlatilgan bo'lsa PDFsharp yechuvchini almashtirmaydi.
            }

            _fontEnvironmentReady = true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Vaqtinchalik fayl o'chmasa ham asosiy natijaga ta'sir qilmaydi.
        }
    }

    // =================================================================================
    //  Sahifa xossalari
    // =================================================================================

    /// <summary>Sahifa o'lchami va chekkalari (hammasi punktda).</summary>
    private sealed class PageSetup
    {
        /// <summary>A4 kengligi.</summary>
        public double Width { get; private set; } = 595.32d;

        /// <summary>A4 balandligi.</summary>
        public double Height { get; private set; } = 841.92d;

        public double MarginLeft { get; private set; } = 70.87d;

        public double MarginRight { get; private set; } = 70.87d;

        public double MarginTop { get; private set; } = 70.87d;

        public double MarginBottom { get; private set; } = 70.87d;

        public double ContentWidth => Math.Max(72d, Width - MarginLeft - MarginRight);

        public double ContentBottom => Math.Max(MarginTop + 72d, Height - MarginBottom);

        /// <summary>Hujjatning oxirgi bo'lim xossalaridan sahifa o'lchamini o'qiydi.</summary>
        public static PageSetup FromBody(W.Body body)
        {
            var setup = new PageSetup();
            var section = body.Elements<W.SectionProperties>().LastOrDefault();
            if (section is null)
                return setup;

            var size = section.Elements<W.PageSize>().FirstOrDefault();
            if (size is not null)
            {
                var width = TwipsToPoints(size.Width?.Value);
                var height = TwipsToPoints(size.Height?.Value);
                if (width > 72d && height > 72d)
                {
                    setup.Width = width;
                    setup.Height = height;
                }

                // Word landshaft sahifada kenglik/balandlikni allaqachon almashtirib yozadi,
                // lekin ba'zi generatorlar buni qilmaydi — shuning uchun tekshirib qo'yamiz.
                var landscape = string.Equals(size.Orient?.ToString(), "landscape", StringComparison.OrdinalIgnoreCase);
                if (landscape && setup.Width < setup.Height)
                    (setup.Width, setup.Height) = (setup.Height, setup.Width);
            }

            var margin = section.Elements<W.PageMargin>().FirstOrDefault();
            if (margin is not null)
            {
                setup.MarginLeft = Clamp(TwipsToPoints(margin.Left?.Value), setup.Width / 3d);
                setup.MarginRight = Clamp(TwipsToPoints(margin.Right?.Value), setup.Width / 3d);
                setup.MarginTop = Clamp(TwipsToPoints(margin.Top?.Value), setup.Height / 3d);
                setup.MarginBottom = Clamp(TwipsToPoints(margin.Bottom?.Value), setup.Height / 3d);
            }

            return setup;

            static double Clamp(double value, double maximum) => Math.Clamp(value, 0d, Math.Max(18d, maximum));
        }

        private static double TwipsToPoints(uint? twips) => twips.HasValue ? twips.Value / 20d : 0d;

        private static double TwipsToPoints(int? twips) => twips.HasValue ? Math.Abs(twips.Value) / 20d : 0d;
    }

    // =================================================================================
    //  Formatlar
    // =================================================================================

    /// <summary>Matn bo'lagining ko'rinishi.</summary>
    private sealed class CharFormat
    {
        public string FontName { get; set; } = "Calibri";

        public double Size { get; set; } = 11d;

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        public bool Underline { get; set; }

        public XColor Color { get; set; } = XColors.Black;

        public CharFormat Clone() => (CharFormat)MemberwiseClone();
    }

    /// <summary>Qator tekislanishi.</summary>
    private enum LineAlign
    {
        Left,
        Center,
        Right,
        Justify
    }

    /// <summary>Abzasning joylashuv xossalari (hammasi punktda).</summary>
    private sealed class ParaFormat
    {
        public LineAlign Align { get; set; } = LineAlign.Left;

        public double IndentLeft { get; set; }

        public double IndentRight { get; set; }

        /// <summary>Birinchi qator chekinishi; manfiy qiymat — osilma (hanging) chekinish.</summary>
        public double FirstLine { get; set; }

        public double SpaceBefore { get; set; }

        public double SpaceAfter { get; set; } = 8d;

        /// <summary>Qator oralig'i koeffitsiyenti (1.0 — bir qator).</summary>
        public double LineSpacing { get; set; } = 1.15d;

        /// <summary>Qat'iy qator balandligi (punkt); 0 bo'lsa koeffitsiyent ishlatiladi.</summary>
        public double ExactLineHeight { get; set; }

        public bool PageBreakBefore { get; set; }

        public ParaFormat Clone() => (ParaFormat)MemberwiseClone();
    }

    // =================================================================================
    //  Uslublar jadvali
    // =================================================================================

    /// <summary>styles.xml dan abzas va belgi uslublarini o'qiydi.</summary>
    private sealed class StyleTable
    {
        private readonly Dictionary<string, W.Style> _styles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (CharFormat Char, ParaFormat Para)> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly CharFormat _defaultChar = new();
        private readonly ParaFormat _defaultPara = new();
        private string? _defaultParagraphStyleId;

        public StyleTable(MainDocumentPart mainPart)
        {
            var definitions = mainPart.StyleDefinitionsPart?.Styles;
            if (definitions is null)
                return;

            // 1-qadam: hujjat bo'yicha standart formatlash.
            var runDefaults = definitions.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;
            ApplyRunProperties(runDefaults, _defaultChar);

            var paragraphDefaults = definitions.DocDefaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle;
            ApplyParagraphProperties(paragraphDefaults, _defaultPara);

            // 2-qadam: barcha uslublarni id bo'yicha indekslaymiz.
            foreach (var style in definitions.Elements<W.Style>())
            {
                var id = style.StyleId?.Value;
                if (string.IsNullOrEmpty(id))
                    continue;

                _styles[id] = style;

                var isParagraph = string.Equals(style.Type?.ToString(), "paragraph", StringComparison.OrdinalIgnoreCase);
                if (isParagraph && style.Default?.Value == true)
                    _defaultParagraphStyleId = id;
            }
        }

        /// <summary>Abzas uslubidan meros olingan formatlarni qaytaradi (nusxa ko'rinishida).</summary>
        public (CharFormat Char, ParaFormat Para) ResolveParagraphStyle(string? styleId)
        {
            var key = styleId ?? string.Empty;
            if (!_cache.TryGetValue(key, out var resolved))
            {
                var charFormat = _defaultChar.Clone();
                var paraFormat = _defaultPara.Clone();

                // Avval hujjatning standart abzas uslubi (odatda "Normal"), keyin so'ralgani.
                if (!string.IsNullOrEmpty(_defaultParagraphStyleId)
                    && !string.Equals(_defaultParagraphStyleId, styleId, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyChain(_defaultParagraphStyleId, charFormat, paraFormat);
                }

                if (!string.IsNullOrEmpty(styleId))
                    ApplyChain(styleId, charFormat, paraFormat);

                resolved = (charFormat, paraFormat);
                _cache[key] = resolved;
            }

            return (resolved.Char.Clone(), resolved.Para.Clone());
        }

        /// <summary>Belgi uslubini (rStyle) mavjud formatga qo'llaydi.</summary>
        public void ApplyCharacterStyle(string? styleId, CharFormat format)
        {
            if (string.IsNullOrEmpty(styleId))
                return;

            foreach (var style in EnumerateChain(styleId))
                ApplyRunProperties(style.StyleRunProperties, format);
        }

        private void ApplyChain(string styleId, CharFormat charFormat, ParaFormat paraFormat)
        {
            foreach (var style in EnumerateChain(styleId))
            {
                ApplyRunProperties(style.StyleRunProperties, charFormat);
                ApplyParagraphProperties(style.StyleParagraphProperties, paraFormat);
            }
        }

        /// <summary>Uslub merosi zanjirini ildizdan boshlab qaytaradi.</summary>
        private List<W.Style> EnumerateChain(string styleId)
        {
            var chain = new List<W.Style>(4);
            var id = styleId;

            // 16 — halqadan himoya: buzilgan hujjatlarda basedOn o'zini ko'rsatishi mumkin.
            while (!string.IsNullOrEmpty(id) && chain.Count < 16 && _styles.TryGetValue(id, out var style))
            {
                if (chain.Contains(style))
                    break;

                chain.Add(style);
                id = style.BasedOn?.Val?.Value;
            }

            chain.Reverse();
            return chain;
        }
    }

    // =================================================================================
    //  Ro'yxatlar
    // =================================================================================

    /// <summary>numbering.xml dan ro'yxat belgilarini yasaydi.</summary>
    private sealed class NumberingTable
    {
        private readonly Dictionary<int, W.AbstractNum> _abstracts = new();
        private readonly Dictionary<int, int> _instances = new();
        private readonly Dictionary<(int Number, int Level), int> _counters = new();

        public NumberingTable(MainDocumentPart mainPart)
        {
            var definitions = mainPart.NumberingDefinitionsPart?.Numbering;
            if (definitions is null)
                return;

            foreach (var abstractNum in definitions.Elements<W.AbstractNum>())
            {
                if (abstractNum.AbstractNumberId?.Value is { } id)
                    _abstracts[id] = abstractNum;
            }

            foreach (var instance in definitions.Elements<W.NumberingInstance>())
            {
                var numberId = instance.NumberID?.Value;
                var abstractId = instance.AbstractNumId?.Val?.Value;
                if (numberId.HasValue && abstractId.HasValue)
                    _instances[numberId.Value] = abstractId.Value;
            }
        }

        /// <summary>
        /// Abzas uchun ro'yxat belgisini qaytaradi va chekinishni moslaydi.
        /// Ro'yxat bo'lmasa <c>null</c> qaytadi.
        /// </summary>
        public string? BuildMarker(W.Paragraph paragraph, ParaFormat format)
        {
            var properties = paragraph.ParagraphProperties?.NumberingProperties;
            if (properties is null)
                return null;

            var numberId = properties.NumberingId?.Val?.Value ?? 0;
            var level = properties.NumberingLevelReference?.Val?.Value ?? 0;
            if (numberId <= 0)
                return null;

            level = Math.Clamp(level, 0, 8);

            var levelDefinition = FindLevel(numberId, level);
            var levelText = levelDefinition?.LevelText?.Val?.Value;
            var numberFormat = levelDefinition?.NumberingFormat?.Val?.ToString() ?? "bullet";

            // Chekinish: uslubda ko'rsatilgani bo'lsa o'shani, aks holda daraja bo'yicha.
            var indentation = levelDefinition?.PreviousParagraphProperties?.Indentation;
            var indent = ParseTwips(indentation?.Left?.Value) ?? (level + 1) * 18d;
            var hanging = ParseTwips(indentation?.Hanging?.Value) ?? 18d;

            format.IndentLeft = Math.Max(format.IndentLeft, indent);
            format.FirstLine = -Math.Min(hanging, format.IndentLeft);

            if (string.Equals(numberFormat, "bullet", StringComparison.OrdinalIgnoreCase))
            {
                return level switch
                {
                    0 => "•",
                    1 => "◦",
                    _ => "▪"
                };
            }

            if (string.Equals(numberFormat, "none", StringComparison.OrdinalIgnoreCase))
                return null;

            var value = NextValue(numberId, level, levelDefinition);
            return FormatMarker(levelText, numberId, level, value, numberFormat);
        }

        private W.Level? FindLevel(int numberId, int level)
        {
            if (!_instances.TryGetValue(numberId, out var abstractId) || !_abstracts.TryGetValue(abstractId, out var abstractNum))
                return null;

            return abstractNum.Elements<W.Level>().FirstOrDefault(item => (item.LevelIndex?.Value ?? 0) == level);
        }

        /// <summary>Joriy darajaning hisoblagichini oshiradi va chuqurroq darajalarni tiklaydi.</summary>
        private int NextValue(int numberId, int level, W.Level? definition)
        {
            var start = definition?.StartNumberingValue?.Val?.Value ?? 1;

            var value = _counters.TryGetValue((numberId, level), out var previous) ? previous + 1 : start;
            _counters[(numberId, level)] = value;

            for (var deeper = level + 1; deeper <= 8; deeper++)
                _counters.Remove((numberId, deeper));

            return value;
        }

        /// <summary>"%1.%2)" kabi shablonni haqiqiy raqamlar bilan to'ldiradi.</summary>
        private string FormatMarker(string? levelText, int numberId, int level, int value, string numberFormat)
        {
            if (string.IsNullOrEmpty(levelText))
                return Convert(value, numberFormat) + ".";

            var builder = new StringBuilder(levelText.Length + 4);
            for (var i = 0; i < levelText.Length; i++)
            {
                if (levelText[i] == '%' && i + 1 < levelText.Length && char.IsDigit(levelText[i + 1]))
                {
                    var placeholder = levelText[i + 1] - '1';
                    var number = placeholder == level
                        ? value
                        : _counters.TryGetValue((numberId, placeholder), out var other) ? other : 1;

                    builder.Append(Convert(number, placeholder == level ? numberFormat : "decimal"));
                    i++;
                }
                else
                {
                    builder.Append(levelText[i]);
                }
            }

            return builder.ToString();
        }

        private static string Convert(int value, string numberFormat) => numberFormat.ToLowerInvariant() switch
        {
            "lowerletter" => ToLetters(value, lower: true),
            "upperletter" => ToLetters(value, lower: false),
            "lowerroman" => ToRoman(value).ToLowerInvariant(),
            "upperroman" => ToRoman(value),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };

        private static string ToLetters(int value, bool lower)
        {
            if (value <= 0)
                return lower ? "a" : "A";

            var builder = new StringBuilder(3);
            var start = lower ? 'a' : 'A';
            var number = value;
            while (number > 0)
            {
                number--;
                builder.Insert(0, (char)(start + number % 26));
                number /= 26;
            }

            return builder.ToString();
        }

        private static string ToRoman(int value)
        {
            if (value is <= 0 or > 3999)
                return value.ToString(CultureInfo.InvariantCulture);

            int[] numbers = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
            string[] letters = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];

            var builder = new StringBuilder(8);
            var rest = value;
            for (var i = 0; i < numbers.Length; i++)
            {
                while (rest >= numbers[i])
                {
                    builder.Append(letters[i]);
                    rest -= numbers[i];
                }
            }

            return builder.ToString();
        }
    }

    // =================================================================================
    //  Tokenlar va qatorlar
    // =================================================================================

    private enum TokenKind
    {
        Word,
        Space,
        Tab,
        LineBreak,
        PageBreak,
        Image
    }

    /// <summary>Abzasning eng kichik bo'linmas bo'lagi.</summary>
    private sealed class Token
    {
        public TokenKind Kind { get; init; }

        public string Text { get; init; } = string.Empty;

        public CharFormat Format { get; init; } = new();

        public XImage? Image { get; init; }

        public double ImageWidth { get; init; }

        public double ImageHeight { get; init; }
    }

    /// <summary>Chizishga tayyor qator bo'lagi.</summary>
    private sealed class LineSegment
    {
        public string Text { get; init; } = string.Empty;

        public CharFormat Format { get; init; } = new();

        public XFont? Font { get; init; }

        public double Width { get; set; }

        public bool IsSpace { get; init; }

        public XImage? Image { get; init; }

        public double ImageWidth { get; init; }

        public double ImageHeight { get; init; }
    }

    /// <summary>O'lchangan va joylashtirilgan bitta qator.</summary>
    private sealed class TextLine
    {
        public List<LineSegment> Segments { get; } = [];

        public double Width { get; set; }

        public double Height { get; set; }

        public double Ascent { get; set; }

        public double Indent { get; set; }

        public double AvailableWidth { get; set; }

        /// <summary>Abzasning oxirgi qatori — kenglik bo'yicha cho'zilmaydi.</summary>
        public bool IsLast { get; set; }

        public bool BreakPageAfter { get; set; }

        public string? Marker { get; set; }

        public CharFormat? MarkerFormat { get; set; }
    }

    /// <summary>Jadval katagining tayyorlangan mazmuni.</summary>
    private sealed class CellLayout
    {
        public double X { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        public List<(List<TextLine> Lines, ParaFormat Format)> Paragraphs { get; } = [];

        public XColor? Shading { get; set; }

        public BorderEdge Top { get; set; }

        public BorderEdge Bottom { get; set; }

        public BorderEdge Left { get; set; }

        public BorderEdge Right { get; set; }
    }

    /// <summary>Jadval chegarasining bir tomoni.</summary>
    private readonly record struct BorderEdge(bool Visible, double Width, XColor Color)
    {
        public static BorderEdge Default => new(true, 0.5d, XColor.FromArgb(150, 150, 150));

        public static BorderEdge Hidden => new(false, 0d, XColors.Black);
    }

    // =================================================================================
    //  Chizuvchi dvigatel
    // =================================================================================

    /// <summary>Sahifalarni ochadi, qatorlarni yig'adi va chizadi.</summary>
    private sealed class LayoutEngine : IDisposable
    {
        private const double TabStop = 36d;              // 0,5 dyuym — Word'ning standarti
        private const double CellPadding = 5.4d;         // Word'ning standart katak chekinishi
        private const double TableSpacing = 6d;

        private readonly PdfDocument _pdf;
        private readonly PageSetup _setup;
        private readonly MainDocumentPart _mainPart;
        private readonly StyleTable _styles;
        private readonly NumberingTable _numbering;
        private readonly CancellationToken _cancellationToken;
        private readonly XPdfFontOptions _fontOptions;
        private readonly Dictionary<string, XFont> _fonts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XSolidBrush> _brushes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XImage?> _images = new(StringComparer.Ordinal);

        private PdfPage? _page;
        private XGraphics? _graphics;
        private double _y;

        public LayoutEngine(
            PdfDocument pdf,
            PageSetup setup,
            MainDocumentPart mainPart,
            StyleTable styles,
            NumberingTable numbering,
            WordToPdfOptions options,
            CancellationToken cancellationToken)
        {
            _pdf = pdf;
            _setup = setup;
            _mainPart = mainPart;
            _styles = styles;
            _numbering = numbering;
            _cancellationToken = cancellationToken;

            // MUHIM (options.EmbedFonts haqida): PDFsharp 6 da shriftni PDF ichiga
            // joylashtirmaslik imkoniyati butunlay olib tashlangan — PdfFontEmbedding.None
            // eskirgan deb belgilangan va baribir joylashtirishga aylantiriladi. Shuning
            // uchun bu yerda har doim "kerakli belgilarni joylashtirish" (subset) rejimi
            // ishlatiladi: bu ham eng kichik fayl, ham har qanday kompyuterda bir xil
            // ko'rinish beradi. Sozlama o'chirilgan bo'lsa ham natija o'zgarmaydi.
            _ = options.EmbedFonts;
            _fontOptions = new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset);

            NewPage();
        }

        private XGraphics Graphics => _graphics ?? throw new InvalidOperationException("Sahifa ochilmagan.");

        public void Dispose()
        {
            _graphics?.Dispose();
            _graphics = null;

            foreach (var image in _images.Values)
                image?.Dispose();

            _images.Clear();
        }

        // -----------------------------------------------------------------------------
        //  Sahifalar
        // -----------------------------------------------------------------------------

        private void NewPage()
        {
            _graphics?.Dispose();
            _page = _pdf.AddPage();
            _page.Width = XUnit.FromPoint(_setup.Width);
            _page.Height = XUnit.FromPoint(_setup.Height);
            _graphics = XGraphics.FromPdfPage(_page);
            _y = _setup.MarginTop;
        }

        /// <summary>Kerak bo'lsa yangi sahifa ochadi.</summary>
        private void EnsureSpace(double height)
        {
            if (_page is null)
            {
                NewPage();
                return;
            }

            // Sahifaning eng tepasida turgan bo'lsak yangi sahifa ochishdan foyda yo'q:
            // baribir sig'maydi, faqat bo'sh sahifalar ko'payadi.
            if (_y + height > _setup.ContentBottom && _y > _setup.MarginTop + 0.5d)
                NewPage();
        }

        private bool IsAtPageTop => _y <= _setup.MarginTop + 0.5d;

        // -----------------------------------------------------------------------------
        //  Abzaslar
        // -----------------------------------------------------------------------------

        /// <summary>Abzasni oqim bo'yicha chizadi.</summary>
        public void WriteParagraph(W.Paragraph paragraph)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var (charFormat, paraFormat) = ResolveFormats(paragraph);
            var marker = _numbering.BuildMarker(paragraph, paraFormat);

            if (paraFormat.PageBreakBefore && !IsAtPageTop)
                NewPage();

            var tokens = BuildTokens(paragraph, charFormat);
            var lines = BuildLines(tokens, paraFormat, charFormat, _setup.ContentWidth);

            if (lines.Count > 0 && marker is not null)
            {
                lines[0].Marker = marker;
                lines[0].MarkerFormat = charFormat;
            }

            if (!IsAtPageTop)
                _y += paraFormat.SpaceBefore;

            foreach (var line in lines)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                EnsureSpace(line.Height);
                DrawLine(line, _setup.MarginLeft, _y, paraFormat.Align);
                _y += line.Height;

                if (line.BreakPageAfter)
                    NewPage();
            }

            _y += paraFormat.SpaceAfter;
        }

        /// <summary>Abzas uchun yakuniy belgi va joylashuv formatlarini hisoblaydi.</summary>
        private (CharFormat Char, ParaFormat Para) ResolveFormats(W.Paragraph paragraph)
        {
            var properties = paragraph.ParagraphProperties;
            var (charFormat, paraFormat) = _styles.ResolveParagraphStyle(properties?.ParagraphStyleId?.Val?.Value);

            ApplyParagraphProperties(properties, paraFormat);
            ApplyRunProperties(properties?.ParagraphMarkRunProperties, charFormat);

            if (properties?.PageBreakBefore is { } pageBreak)
                paraFormat.PageBreakBefore = pageBreak.Val is null || pageBreak.Val.Value;

            return (charFormat, paraFormat);
        }

        /// <summary>Abzasni tokenlar ketma-ketligiga aylantiradi.</summary>
        private List<Token> BuildTokens(W.Paragraph paragraph, CharFormat baseFormat)
        {
            var tokens = new List<Token>();

            foreach (var run in paragraph.Descendants<W.Run>())
            {
                _cancellationToken.ThrowIfCancellationRequested();

                // O'chirilgan (track changes) matn PDF ga tushmasligi kerak.
                if (run.Ancestors<W.DeletedRun>().Any())
                    continue;

                var format = baseFormat.Clone();
                _styles.ApplyCharacterStyle(run.RunProperties?.RunStyle?.Val?.Value, format);
                ApplyRunProperties(run.RunProperties, format);

                foreach (var child in run.ChildElements)
                {
                    switch (child)
                    {
                        case W.Text text:
                            AppendText(tokens, text.Text, format);
                            break;

                        case W.NoBreakHyphen:
                            tokens.Add(new Token { Kind = TokenKind.Word, Text = "-", Format = format });
                            break;

                        case W.TabChar:
                            tokens.Add(new Token { Kind = TokenKind.Tab, Format = format });
                            break;

                        case W.CarriageReturn:
                            tokens.Add(new Token { Kind = TokenKind.LineBreak, Format = format });
                            break;

                        case W.Break brk:
                            tokens.Add(new Token
                            {
                                Kind = string.Equals(brk.Type?.ToString(), "page", StringComparison.OrdinalIgnoreCase)
                                    ? TokenKind.PageBreak
                                    : TokenKind.LineBreak,
                                Format = format
                            });
                            break;

                        case W.Drawing drawing:
                            AppendImage(tokens, drawing, format);
                            break;
                    }
                }
            }

            return tokens;
        }

        /// <summary>Matnni so'z va bo'sh joy tokenlariga bo'ladi.</summary>
        private static void AppendText(List<Token> tokens, string? text, CharFormat format)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var index = 0;
            while (index < text.Length)
            {
                var isSpace = char.IsWhiteSpace(text[index]);
                var start = index;
                while (index < text.Length && char.IsWhiteSpace(text[index]) == isSpace)
                    index++;

                var piece = text[start..index];
                tokens.Add(new Token
                {
                    Kind = isSpace ? TokenKind.Space : TokenKind.Word,
                    Text = isSpace ? " " : piece,
                    Format = format
                });
            }
        }

        /// <summary>Hujjatga joylashtirilgan rasmni token sifatida qo'shadi.</summary>
        private void AppendImage(List<Token> tokens, W.Drawing drawing, CharFormat format)
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            var relationshipId = blip?.Embed?.Value;
            if (string.IsNullOrEmpty(relationshipId))
                return;

            var image = LoadImage(relationshipId);
            if (image is null)
                return;

            // EMU (English Metric Unit) → punkt.
            var extent = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>().FirstOrDefault();
            var width = extent?.Cx?.Value is { } cx && cx > 0 ? cx / 12700d : image.PointWidth;
            var height = extent?.Cy?.Value is { } cy && cy > 0 ? cy / 12700d : image.PointHeight;

            if (width <= 0 || height <= 0)
                return;

            tokens.Add(new Token
            {
                Kind = TokenKind.Image,
                Format = format,
                Image = image,
                ImageWidth = width,
                ImageHeight = height
            });
        }

        /// <summary>Rasm qismini o'qiydi (bir marta) va keshlaydi.</summary>
        private XImage? LoadImage(string relationshipId)
        {
            if (_images.TryGetValue(relationshipId, out var cached))
                return cached;

            XImage? image = null;
            try
            {
                if (_mainPart.GetPartById(relationshipId) is ImagePart part)
                {
                    using var source = part.GetStream();
                    var buffer = new MemoryStream();
                    source.CopyTo(buffer);
                    buffer.Position = 0;
                    image = XImage.FromStream(buffer);
                }
            }
            catch
            {
                // Qo'llab-quvvatlanmaydigan rasm formati butun konvertatsiyani to'xtatmasligi kerak.
                image = null;
            }

            _images[relationshipId] = image;
            return image;
        }

        // -----------------------------------------------------------------------------
        //  Qator yig'ish (word wrap)
        // -----------------------------------------------------------------------------

        /// <summary>Tokenlarni berilgan kenglikka sig'adigan qatorlarga yig'adi.</summary>
        private List<TextLine> BuildLines(List<Token> tokens, ParaFormat format, CharFormat baseFormat, double contentWidth)
        {
            var lines = new List<TextLine>();
            var current = NewLine(true);
            var used = 0d;
            var pendingSpaces = new List<LineSegment>();
            var pendingWidth = 0d;

            TextLine NewLine(bool first)
            {
                var indent = format.IndentLeft + (first ? format.FirstLine : 0d);
                indent = Math.Max(0d, indent);
                return new TextLine
                {
                    Indent = indent,
                    AvailableWidth = Math.Max(24d, contentWidth - indent - format.IndentRight)
                };
            }

            void Flush(bool endsParagraph, bool pageBreak)
            {
                current.IsLast = endsParagraph;
                current.BreakPageAfter = pageBreak;
                FinishLine(current, baseFormat, format);
                lines.Add(current);

                current = NewLine(false);
                used = 0d;
                pendingSpaces.Clear();
                pendingWidth = 0d;
            }

            foreach (var token in tokens)
            {
                switch (token.Kind)
                {
                    case TokenKind.Space:
                    {
                        if (current.Segments.Count == 0)
                            break;   // qator boshidagi bo'sh joy tashlanadi

                        var font = GetFont(token.Format);
                        var width = MeasureSpace(font);
                        pendingSpaces.Add(new LineSegment { Text = " ", Format = token.Format, Font = font, Width = width, IsSpace = true });
                        pendingWidth += width;
                        break;
                    }

                    case TokenKind.Tab:
                    {
                        var font = GetFont(token.Format);
                        var position = current.Indent + used + pendingWidth;
                        var next = (Math.Floor(position / TabStop) + 1) * TabStop;
                        var width = Math.Max(4d, next - position);

                        CommitPending();
                        current.Segments.Add(new LineSegment { Text = string.Empty, Format = token.Format, Font = font, Width = width, IsSpace = true });
                        used += width;
                        break;
                    }

                    case TokenKind.LineBreak:
                        Flush(true, false);
                        break;

                    case TokenKind.PageBreak:
                        Flush(true, true);
                        break;

                    case TokenKind.Image:
                    {
                        var width = token.ImageWidth;
                        var height = token.ImageHeight;

                        // Sahifaga sig'maydigan rasm proporsiyasini saqlab kichraytiriladi.
                        if (width > current.AvailableWidth)
                        {
                            var scale = current.AvailableWidth / width;
                            width *= scale;
                            height *= scale;
                        }

                        if (used > 0 && used + pendingWidth + width > current.AvailableWidth)
                            Flush(false, false);

                        CommitPending();
                        current.Segments.Add(new LineSegment
                        {
                            Format = token.Format,
                            Image = token.Image,
                            ImageWidth = width,
                            ImageHeight = height,
                            Width = width
                        });

                        used += width;
                        break;
                    }

                    default:
                    {
                        var font = GetFont(token.Format);
                        var width = Measure(token.Text, font);

                        if (used > 0 && used + pendingWidth + width > current.AvailableWidth)
                            Flush(false, false);

                        if (width > current.AvailableWidth && used <= 0)
                        {
                            // Bitta uzun "so'z" (masalan uzun havola) qatorga sig'masa — belgilab bo'linadi.
                            foreach (var piece in SplitLongWord(token.Text, font, current.AvailableWidth))
                            {
                                if (used > 0)
                                    Flush(false, false);

                                current.Segments.Add(new LineSegment
                                {
                                    Text = piece,
                                    Format = token.Format,
                                    Font = font,
                                    Width = Measure(piece, font)
                                });

                                used += Measure(piece, font);
                            }

                            break;
                        }

                        CommitPending();
                        current.Segments.Add(new LineSegment { Text = token.Text, Format = token.Format, Font = font, Width = width });
                        used += width;
                        break;
                    }
                }
            }

            Flush(true, false);
            return lines;

            void CommitPending()
            {
                if (pendingSpaces.Count == 0)
                    return;

                foreach (var space in pendingSpaces)
                {
                    current.Segments.Add(space);
                    used += space.Width;
                }

                pendingSpaces.Clear();
                pendingWidth = 0d;
            }
        }

        /// <summary>Qatorning kengligi, balandligi va tayanch chizig'ini hisoblaydi.</summary>
        private void FinishLine(TextLine line, CharFormat baseFormat, ParaFormat format)
        {
            var width = 0d;
            var height = 0d;
            var ascent = 0d;

            foreach (var segment in line.Segments)
            {
                width += segment.Width;

                if (segment.Image is not null)
                {
                    height = Math.Max(height, segment.ImageHeight);
                    ascent = Math.Max(ascent, segment.ImageHeight);
                    continue;
                }

                if (segment.Font is null)
                    continue;

                height = Math.Max(height, segment.Font.GetHeight());
                ascent = Math.Max(ascent, AscentOf(segment.Font));
            }

            if (height <= 0)
            {
                // Bo'sh abzas ham joy egallaydi.
                var font = GetFont(baseFormat);
                height = font.GetHeight();
                ascent = AscentOf(font);
            }

            line.Width = width;
            line.Ascent = ascent;
            line.Height = format.ExactLineHeight > 0
                ? Math.Max(format.ExactLineHeight, ascent)
                : height * Math.Clamp(format.LineSpacing, 0.5d, 4d);
        }

        /// <summary>Sig'magan so'zni bo'laklarga bo'ladi.</summary>
        private IEnumerable<string> SplitLongWord(string text, XFont font, double availableWidth)
        {
            var start = 0;
            while (start < text.Length)
            {
                var length = 1;
                while (start + length < text.Length && Measure(text.Substring(start, length + 1), font) <= availableWidth)
                    length++;

                yield return text.Substring(start, length);
                start += length;
            }
        }

        // -----------------------------------------------------------------------------
        //  Chizish
        // -----------------------------------------------------------------------------

        /// <summary>Qatorni berilgan joyga chizadi.</summary>
        private void DrawLine(TextLine line, double left, double top, LineAlign align)
        {
            var x = left + line.Indent;
            var free = line.AvailableWidth - line.Width;
            var extraPerSpace = 0d;

            if (align == LineAlign.Center)
            {
                x += Math.Max(0d, free / 2d);
            }
            else if (align == LineAlign.Right)
            {
                x += Math.Max(0d, free);
            }
            else if (align == LineAlign.Justify && !line.IsLast && free > 0)
            {
                var gaps = line.Segments.Count(segment => segment.IsSpace);
                if (gaps > 0)
                    extraPerSpace = free / gaps;
            }

            var baseline = top + line.Ascent;

            // Ro'yxat belgisi chekinish maydonining chap tomoniga chiqariladi.
            if (line.Marker is not null && line.MarkerFormat is not null)
            {
                var markerFont = GetFont(line.MarkerFormat);
                var markerWidth = Measure(line.Marker, markerFont);
                var markerX = Math.Max(left, left + line.Indent - markerWidth - 6d);
                Graphics.DrawString(line.Marker, markerFont, GetBrush(line.MarkerFormat.Color), markerX, baseline, XStringFormats.BaseLineLeft);
            }

            foreach (var segment in line.Segments)
            {
                if (segment.Image is not null)
                {
                    var offset = Math.Max(0d, line.Height - segment.ImageHeight);
                    Graphics.DrawImage(segment.Image, x, top + offset, segment.ImageWidth, segment.ImageHeight);
                    x += segment.Width;
                    continue;
                }

                var width = segment.Width + (segment.IsSpace ? extraPerSpace : 0d);

                if (!segment.IsSpace && segment.Text.Length > 0 && segment.Font is not null)
                {
                    try
                    {
                        Graphics.DrawString(segment.Text, segment.Font, GetBrush(segment.Format.Color), x, baseline, XStringFormats.BaseLineLeft);
                    }
                    catch
                    {
                        // Shrift ba'zi belgilarni qo'llab-quvvatlamasligi mumkin — qolgan matn chizilaveradi.
                    }
                }

                x += width;
            }
        }

        // -----------------------------------------------------------------------------
        //  Jadvallar
        // -----------------------------------------------------------------------------

        /// <summary>Jadvalni qator-qator chizadi.</summary>
        public void WriteTable(W.Table table)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var columnWidths = ResolveColumnWidths(table);
            if (columnWidths.Count == 0)
                return;

            var tableBorders = table.GetFirstChild<W.TableProperties>()?.TableBorders;

            foreach (var row in table.Elements<W.TableRow>())
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var cells = BuildRow(row, columnWidths, tableBorders);
                if (cells.Count == 0)
                    continue;

                var height = cells.Max(cell => cell.Height);

                // Qator sahifaga sig'masa — butunicha keyingi sahifaga o'tadi.
                EnsureSpace(height);
                DrawRow(cells, _setup.MarginLeft, _y, height);
                _y += height;
            }

            _y += TableSpacing;
        }

        /// <summary>Jadval ustunlarining kengliklarini punktda hisoblaydi.</summary>
        private List<double> ResolveColumnWidths(W.Table table)
        {
            var widths = new List<double>();
            var grid = table.Elements<W.TableGrid>().FirstOrDefault();

            if (grid is not null)
            {
                foreach (var column in grid.Elements<W.GridColumn>())
                    widths.Add(ParseTwips(column.Width?.Value) ?? 0d);
            }

            if (widths.Count == 0)
            {
                // Setka yo'q bo'lsa katak sonidan kelib chiqib teng bo'lamiz.
                var columns = table.Elements<W.TableRow>()
                    .Select(row => row.Elements<W.TableCell>().Sum(cell => cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1))
                    .DefaultIfEmpty(0)
                    .Max();

                if (columns <= 0)
                    return widths;

                for (var i = 0; i < columns; i++)
                    widths.Add(_setup.ContentWidth / columns);

                return widths;
            }

            var total = widths.Sum();
            if (total <= 0)
            {
                for (var i = 0; i < widths.Count; i++)
                    widths[i] = _setup.ContentWidth / widths.Count;

                return widths;
            }

            // Jadval sahifaga sig'masa — proporsiyani saqlab kichraytiramiz.
            if (total > _setup.ContentWidth)
            {
                var scale = _setup.ContentWidth / total;
                for (var i = 0; i < widths.Count; i++)
                    widths[i] *= scale;
            }

            return widths;
        }

        /// <summary>Jadval qatoridagi kataklarni tayyorlaydi (matnni sig'diradi, balandlikni hisoblaydi).</summary>
        private List<CellLayout> BuildRow(W.TableRow row, List<double> columnWidths, W.TableBorders? tableBorders)
        {
            var cells = new List<CellLayout>();
            var x = 0d;
            var columnIndex = 0;

            foreach (var cell in row.Elements<W.TableCell>())
            {
                var properties = cell.TableCellProperties;
                var span = Math.Max(1, properties?.GridSpan?.Val?.Value ?? 1);

                var width = 0d;
                for (var i = 0; i < span && columnIndex + i < columnWidths.Count; i++)
                    width += columnWidths[columnIndex + i];

                if (width <= 1d)
                    width = _setup.ContentWidth / Math.Max(1, columnWidths.Count);

                var layout = new CellLayout
                {
                    X = x,
                    Width = width,
                    Shading = ParseShading(properties?.Shading?.Fill?.Value)
                };

                var borders = properties?.TableCellBorders;
                layout.Top = ResolveEdge(borders?.TopBorder, (W.BorderType?)tableBorders?.TopBorder ?? tableBorders?.InsideHorizontalBorder);
                layout.Bottom = ResolveEdge(borders?.BottomBorder, (W.BorderType?)tableBorders?.BottomBorder ?? tableBorders?.InsideHorizontalBorder);
                layout.Left = ResolveEdge(borders?.LeftBorder, (W.BorderType?)tableBorders?.LeftBorder ?? tableBorders?.InsideVerticalBorder);
                layout.Right = ResolveEdge(borders?.RightBorder, (W.BorderType?)tableBorders?.RightBorder ?? tableBorders?.InsideVerticalBorder);

                var innerWidth = Math.Max(12d, width - 2 * CellPadding);
                var height = 2 * CellPadding;

                // Ichma-ich jadvallar qo'llab-quvvatlanmaydi: katakdagi abzaslar chiziladi.
                foreach (var paragraph in cell.Elements<W.Paragraph>())
                {
                    var (charFormat, paraFormat) = ResolveFormats(paragraph);

                    // Katak ichida abzaslararo bo'shliqni kamaytiramiz — jadval ixchamroq chiqadi.
                    paraFormat.SpaceBefore = Math.Min(paraFormat.SpaceBefore, 2d);
                    paraFormat.SpaceAfter = Math.Min(paraFormat.SpaceAfter, 2d);

                    var tokens = BuildTokens(paragraph, charFormat);
                    var lines = BuildLines(tokens, paraFormat, charFormat, innerWidth);

                    layout.Paragraphs.Add((lines, paraFormat));
                    height += paraFormat.SpaceBefore + paraFormat.SpaceAfter + lines.Sum(line => line.Height);
                }

                var minimumHeight = ParseTwips(row.TableRowProperties?.Elements<W.TableRowHeight>().FirstOrDefault()?.Val?.Value) ?? 0d;
                layout.Height = Math.Max(height, Math.Max(minimumHeight, 14d));

                cells.Add(layout);
                x += width;
                columnIndex += span;
            }

            return cells;
        }

        /// <summary>Tayyorlangan qatorni chizadi.</summary>
        private void DrawRow(List<CellLayout> cells, double left, double top, double height)
        {
            foreach (var cell in cells)
            {
                var rectangle = new XRect(left + cell.X, top, cell.Width, height);

                if (cell.Shading is { } shading)
                    Graphics.DrawRectangle(new XSolidBrush(shading), rectangle);

                DrawEdge(cell.Top, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Top);
                DrawEdge(cell.Bottom, rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Bottom);
                DrawEdge(cell.Left, rectangle.Left, rectangle.Top, rectangle.Left, rectangle.Bottom);
                DrawEdge(cell.Right, rectangle.Right, rectangle.Top, rectangle.Right, rectangle.Bottom);

                var y = top + CellPadding;
                foreach (var (lines, format) in cell.Paragraphs)
                {
                    y += format.SpaceBefore;
                    foreach (var line in lines)
                    {
                        // Katakdan toshib ketgan matnni chizmaymiz — u qo'shni katakka kirib ketardi.
                        if (y + line.Height > top + height + 0.5d)
                            break;

                        DrawLine(line, left + cell.X + CellPadding, y, format.Align);
                        y += line.Height;
                    }

                    y += format.SpaceAfter;
                }
            }
        }

        private void DrawEdge(BorderEdge edge, double x1, double y1, double x2, double y2)
        {
            if (!edge.Visible)
                return;

            Graphics.DrawLine(new XPen(edge.Color, edge.Width), x1, y1, x2, y2);
        }

        /// <summary>Katak va jadval chegaralaridan yakuniy chiziqni tanlaydi.</summary>
        private static BorderEdge ResolveEdge(W.BorderType? cellBorder, W.BorderType? tableBorder)
        {
            var border = cellBorder ?? tableBorder;
            if (border is null)
                return BorderEdge.Default;   // hujjatda ko'rsatilmagan bo'lsa — ingichka kulrang chiziq

            var value = border.Val?.ToString();
            if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "nil", StringComparison.OrdinalIgnoreCase))
            {
                return BorderEdge.Hidden;
            }

            // w:sz — chegara qalinligi 1/8 punktda.
            var width = border.Size?.Value is { } size && size > 0 ? Math.Clamp(size / 8d, 0.25d, 4d) : 0.5d;
            var color = ParseColor(border.Color?.Value) ?? XColor.FromArgb(90, 90, 90);
            return new BorderEdge(true, width, color);
        }

        // -----------------------------------------------------------------------------
        //  Shriftlar, ranglar, o'lchash
        // -----------------------------------------------------------------------------

        /// <summary>Formatga mos <see cref="XFont"/> ni yaratadi yoki keshdan oladi.</summary>
        private XFont GetFont(CharFormat format)
        {
            var style = XFontStyleEx.Regular;
            if (format.Bold)
                style |= XFontStyleEx.Bold;
            if (format.Italic)
                style |= XFontStyleEx.Italic;
            if (format.Underline)
                style |= XFontStyleEx.Underline;

            var size = Math.Clamp(format.Size, 4d, 400d);
            var key = string.Create(CultureInfo.InvariantCulture, $"{format.FontName}|{size:0.##}|{(int)style}");

            if (_fonts.TryGetValue(key, out var cached))
                return cached;

            XFont font;
            try
            {
                font = new XFont(format.FontName, size, style, _fontOptions);
            }
            catch
            {
                try
                {
                    // Tizimda bo'lmagan shrift — hamma joyda mavjud bo'lgan Arial bilan almashtiriladi.
                    font = new XFont("Arial", size, style, _fontOptions);
                }
                catch
                {
                    font = new XFont("Arial", size, XFontStyleEx.Regular, _fontOptions);
                }
            }

            _fonts[key] = font;
            return font;
        }

        private XSolidBrush GetBrush(XColor color)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{color.R:X2}{color.G:X2}{color.B:X2}");
            if (_brushes.TryGetValue(key, out var brush))
                return brush;

            brush = new XSolidBrush(color);
            _brushes[key] = brush;
            return brush;
        }

        private double Measure(string text, XFont font)
        {
            if (string.IsNullOrEmpty(text))
                return 0d;

            try
            {
                return Graphics.MeasureString(text, font).Width;
            }
            catch
            {
                // O'lchab bo'lmasa taxminiy kenglik: shrift o'lchamining yarmi.
                return text.Length * font.Size * 0.5d;
            }
        }

        private double MeasureSpace(XFont font)
        {
            var width = Measure(" ", font);
            return width > 0 ? width : font.Size * 0.28d;
        }

        private static double AscentOf(XFont font)
        {
            try
            {
                var metrics = font.Metrics;
                if (metrics.UnitsPerEm > 0 && metrics.Ascent > 0)
                    return font.Size * metrics.Ascent / metrics.UnitsPerEm;
            }
            catch
            {
                // Ba'zi shriftlarda metrik ma'lumot to'liq bo'lmaydi.
            }

            return font.GetHeight() * 0.8d;
        }
    }

    // =================================================================================
    //  OpenXML xossalarini formatga ko'chirish
    // =================================================================================

    /// <summary>
    /// Belgi xossalarini qo'llaydi. Kirish turi ataylab <see cref="OpenXmlElement"/>:
    /// <c>w:rPr</c> hujjatda uch xil sinf bilan ifodalanadi (docDefaults, uslub, run),
    /// lekin ularning bolalari bir xil.
    /// </summary>
    private static void ApplyRunProperties(OpenXmlElement? properties, CharFormat format)
    {
        if (properties is null)
            return;

        foreach (var fonts in properties.Elements<W.RunFonts>())
        {
            var name = FirstNonEmpty(fonts.Ascii?.Value, fonts.HighAnsi?.Value, fonts.ComplexScript?.Value, fonts.EastAsia?.Value);
            if (name is not null)
                format.FontName = name;
        }

        foreach (var size in properties.Elements<W.FontSize>())
        {
            // w:sz — yarim punktda.
            if (double.TryParse(size.Val?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var halfPoints) && halfPoints > 0)
                format.Size = halfPoints / 2d;
        }

        foreach (var bold in properties.Elements<W.Bold>())
            format.Bold = bold.Val is null || bold.Val.Value;

        foreach (var italic in properties.Elements<W.Italic>())
            format.Italic = italic.Val is null || italic.Val.Value;

        foreach (var underline in properties.Elements<W.Underline>())
        {
            var value = underline.Val?.ToString();
            format.Underline = value is null || !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var color in properties.Elements<W.Color>())
        {
            if (ParseColor(color.Val?.Value) is { } parsed)
                format.Color = parsed;
        }
    }

    /// <summary>Abzas xossalarini qo'llaydi.</summary>
    private static void ApplyParagraphProperties(OpenXmlElement? properties, ParaFormat format)
    {
        if (properties is null)
            return;

        foreach (var justification in properties.Elements<W.Justification>())
        {
            format.Align = justification.Val?.ToString()?.ToLowerInvariant() switch
            {
                "center" => LineAlign.Center,
                "right" or "end" => LineAlign.Right,
                "both" or "distribute" => LineAlign.Justify,
                _ => LineAlign.Left
            };
        }

        foreach (var indentation in properties.Elements<W.Indentation>())
        {
            if (ParseTwips(indentation.Left?.Value) is { } left)
                format.IndentLeft = Math.Clamp(left, 0d, 400d);

            if (ParseTwips(indentation.Right?.Value) is { } right)
                format.IndentRight = Math.Clamp(right, 0d, 400d);

            if (ParseTwips(indentation.FirstLine?.Value) is { } firstLine)
                format.FirstLine = Math.Clamp(firstLine, 0d, 400d);

            if (ParseTwips(indentation.Hanging?.Value) is { } hanging)
                format.FirstLine = -Math.Clamp(hanging, 0d, 400d);
        }

        foreach (var spacing in properties.Elements<W.SpacingBetweenLines>())
        {
            if (ParseTwips(spacing.Before?.Value) is { } before)
                format.SpaceBefore = Math.Clamp(before, 0d, 200d);

            if (ParseTwips(spacing.After?.Value) is { } after)
                format.SpaceAfter = Math.Clamp(after, 0d, 200d);

            if (double.TryParse(spacing.Line?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var line) && line > 0)
            {
                var rule = spacing.LineRule?.ToString();
                if (string.Equals(rule, "exact", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule, "atLeast", StringComparison.OrdinalIgnoreCase))
                {
                    format.ExactLineHeight = Math.Clamp(line / 20d, 4d, 400d);
                }
                else
                {
                    // "auto" rejimida qiymat 240 = bir qator.
                    format.LineSpacing = Math.Clamp(line / 240d, 0.5d, 4d);
                    format.ExactLineHeight = 0d;
                }
            }
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>Twip (1/1440 dyuym) qiymatini punktga aylantiradi.</summary>
    private static double? ParseTwips(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var twips) ? twips / 20d : null;

    private static double? ParseTwips(uint? value) => value.HasValue ? value.Value / 20d : null;

    /// <summary>#RRGGBB yoki RRGGBB ko'rinishidagi rangni o'qiydi.</summary>
    private static XColor? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().TrimStart('#');
        if (text.Length != 6 || string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return null;

        return XColor.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    /// <summary>Katak fonini o'qiydi ("auto" va bo'sh qiymatlar e'tiborsiz qoladi).</summary>
    private static XColor? ParseShading(string? fill)
        => string.Equals(fill, "auto", StringComparison.OrdinalIgnoreCase) ? null : ParseColor(fill);

    // =================================================================================
    //  Shrift yechuvchisi
    // =================================================================================

    /// <summary>
    /// Windows'ning shrift papkalarini o'qib, oila nomi bo'yicha .ttf/.otf faylini topadigan
    /// yechuvchi. Indeks bir marta yig'iladi: har bir shrift faylining "name" jadvali
    /// o'qilib, oila nomi va uslubi (qalin/kursiv) aniqlanadi.
    /// </summary>
    private sealed class WindowsFileFontResolver : IFontResolver
    {
        /// <summary>So'ralgan shrift topilmaganda ketma-ket sinaladigan oilalar.</summary>
        private static readonly string[] FallbackFamilies =
            ["arial", "calibri", "segoe ui", "times new roman", "tahoma", "verdana", "microsoft sans serif"];

        private readonly Lazy<Dictionary<string, FontFamilyFiles>> _index =
            new(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            var index = _index.Value;
            if (index.Count == 0)
                return null;

            var family = FindFamily(index, familyName);
            if (family is null)
                return null;

            var choice = family.Pick(isBold, isItalic);
            if (choice is null)
                return null;

            return new FontResolverInfo(choice.Value.Path, choice.Value.SimulateBold, choice.Value.SimulateItalic);
        }

        public byte[]? GetFont(string faceName)
        {
            try
            {
                return File.Exists(faceName) ? File.ReadAllBytes(faceName) : null;
            }
            catch
            {
                return null;
            }
        }

        private static FontFamilyFiles? FindFamily(Dictionary<string, FontFamilyFiles> index, string? familyName)
        {
            var key = (familyName ?? string.Empty).Trim();
            if (key.Length > 0 && index.TryGetValue(key, out var exact))
                return exact;

            // "Calibri Light" kabi nomlar uchun oxirgi so'zni tashlab ko'ramiz.
            var space = key.LastIndexOf(' ');
            if (space > 0 && index.TryGetValue(key[..space], out var shortened))
                return shortened;

            foreach (var fallback in FallbackFamilies)
            {
                if (index.TryGetValue(fallback, out var found))
                    return found;
            }

            return index.Values.FirstOrDefault();
        }

        /// <summary>Windows'dagi barcha shrift fayllarini indekslaydi.</summary>
        private static Dictionary<string, FontFamilyFiles> BuildIndex()
        {
            var index = new Dictionary<string, FontFamilyFiles>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in EnumerateFontDirectories())
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(directory)
                        .Where(file => file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                            || file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    // Papkaga kirish taqiqlangan bo'lsa keyingisiga o'tamiz.
                    continue;
                }

                foreach (var file in files)
                {
                    if (!TryReadFontNames(file, out var family, out var bold, out var italic))
                        continue;

                    if (!index.TryGetValue(family, out var entry))
                    {
                        entry = new FontFamilyFiles();
                        index[family] = entry;
                    }

                    entry.Add(bold, italic, file);
                }
            }

            return index;
        }

        private static IEnumerable<string> EnumerateFontDirectories()
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(system))
                yield return system;

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                // Foydalanuvchi o'zi uchun o'rnatgan shriftlar.
                var userFonts = Path.Combine(local, "Microsoft", "Windows", "Fonts");
                if (Directory.Exists(userFonts))
                    yield return userFonts;
            }
        }

        /// <summary>
        /// Shrift faylining "name" jadvalidan oila nomi va uslubini o'qiydi.
        /// TrueType/OpenType tuzilmasi: sarlavha → jadvallar ro'yxati → "name" jadvali.
        /// </summary>
        private static bool TryReadFontNames(string path, out string family, out bool bold, out bool italic)
        {
            family = string.Empty;
            bold = false;
            italic = false;

            try
            {
                using var stream = File.OpenRead(path);

                var header = ReadBytes(stream, 12);
                if (header is null)
                    return false;

                var signature = ReadUInt32(header, 0);
                // 0x00010000 — TrueType, "OTTO" — CFF konturli OpenType, "true" — eski Mac shrifti.
                if (signature is not (0x00010000u or 0x4F54544Fu or 0x74727565u))
                    return false;

                var tableCount = ReadUInt16(header, 4);
                if (tableCount is 0 or > 512)
                    return false;

                var directory = ReadBytes(stream, tableCount * 16);
                if (directory is null)
                    return false;

                uint nameOffset = 0;
                uint nameLength = 0;
                for (var i = 0; i < tableCount; i++)
                {
                    var record = i * 16;
                    if (directory[record] == 'n' && directory[record + 1] == 'a'
                        && directory[record + 2] == 'm' && directory[record + 3] == 'e')
                    {
                        nameOffset = ReadUInt32(directory, record + 8);
                        nameLength = ReadUInt32(directory, record + 12);
                        break;
                    }
                }

                if (nameLength is < 6 or > 1_000_000)
                    return false;

                stream.Seek(nameOffset, SeekOrigin.Begin);
                var table = ReadBytes(stream, (int)nameLength);
                if (table is null)
                    return false;

                var count = ReadUInt16(table, 2);
                var storage = ReadUInt16(table, 4);

                string? familyName = null, typographicFamily = null, subFamily = null, typographicSubFamily = null;
                int familyScore = -1, typographicScore = -1, subScore = -1, typographicSubScore = -1;

                for (var i = 0; i < count; i++)
                {
                    var record = 6 + i * 12;
                    if (record + 12 > table.Length)
                        break;

                    var platform = ReadUInt16(table, record);
                    var language = ReadUInt16(table, record + 4);
                    var nameId = ReadUInt16(table, record + 6);
                    var length = ReadUInt16(table, record + 8);
                    var offset = ReadUInt16(table, record + 10);

                    if (nameId is not (1 or 2 or 16 or 17))
                        continue;

                    var start = storage + offset;
                    if (start + length > table.Length || length == 0)
                        continue;

                    var text = Decode(platform, table, start, length);
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    // Windows yozuvlari (platform 3) va ingliz tili ustunroq.
                    var score = (platform == 3 ? 2 : 0) + (language is 0x0409 or 0 ? 1 : 0);

                    switch (nameId)
                    {
                        case 1 when score > familyScore:
                            familyName = text;
                            familyScore = score;
                            break;
                        case 2 when score > subScore:
                            subFamily = text;
                            subScore = score;
                            break;
                        case 16 when score > typographicScore:
                            typographicFamily = text;
                            typographicScore = score;
                            break;
                        case 17 when score > typographicSubScore:
                            typographicSubFamily = text;
                            typographicSubScore = score;
                            break;
                    }
                }

                family = (typographicFamily ?? familyName ?? string.Empty).Trim();
                if (family.Length == 0)
                    return false;

                var style = (typographicFamily is not null ? typographicSubFamily ?? subFamily : subFamily) ?? string.Empty;
                bold = style.Contains("bold", StringComparison.OrdinalIgnoreCase);
                italic = style.Contains("italic", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("oblique", StringComparison.OrdinalIgnoreCase);

                return true;
            }
            catch
            {
                // Buzilgan yoki qulflangan shrift fayli — shunchaki o'tkazib yuboramiz.
                return false;
            }
        }

        private static string Decode(int platform, byte[] buffer, int start, int length)
        {
            // Platform 3 (Windows) va 0 (Unicode) — UTF-16 katta-endian; qolganlari — bir baytli.
            return platform is 3 or 0
                ? Encoding.BigEndianUnicode.GetString(buffer, start, length)
                : Encoding.ASCII.GetString(buffer, start, length);
        }

        private static byte[]? ReadBytes(Stream stream, int count)
        {
            if (count <= 0)
                return null;

            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var step = stream.Read(buffer, read, count - read);
                if (step <= 0)
                    return null;

                read += step;
            }

            return buffer;
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
            => (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

        private static uint ReadUInt32(byte[] buffer, int offset)
            => ((uint)buffer[offset] << 24) | ((uint)buffer[offset + 1] << 16)
                | ((uint)buffer[offset + 2] << 8) | buffer[offset + 3];

        /// <summary>Bitta shrift oilasining to'rtta uslubi.</summary>
        private sealed class FontFamilyFiles
        {
            public string? Regular { get; private set; }

            public string? Bold { get; private set; }

            public string? Italic { get; private set; }

            public string? BoldItalic { get; private set; }

            public void Add(bool bold, bool italic, string path)
            {
                switch (bold, italic)
                {
                    case (false, false):
                        Regular ??= path;
                        break;
                    case (true, false):
                        Bold ??= path;
                        break;
                    case (false, true):
                        Italic ??= path;
                        break;
                    default:
                        BoldItalic ??= path;
                        break;
                }
            }

            /// <summary>
            /// Kerakli uslubni tanlaydi. Aynan mos fayl bo'lmasa, PDFsharp qalin/kursivni
            /// o'zi "taqlid" qilib chizishi uchun tegishli bayroq qaytariladi.
            /// </summary>
            public (string Path, bool SimulateBold, bool SimulateItalic)? Pick(bool bold, bool italic)
            {
                switch (bold, italic)
                {
                    case (true, true):
                        if (BoldItalic is not null)
                            return (BoldItalic, false, false);
                        if (Bold is not null)
                            return (Bold, false, true);
                        if (Italic is not null)
                            return (Italic, true, false);
                        return Regular is not null ? (Regular, true, true) : null;

                    case (true, false):
                        if (Bold is not null)
                            return (Bold, false, false);
                        return Regular is not null ? (Regular, true, false)
                            : BoldItalic is not null ? (BoldItalic, false, false) : null;

                    case (false, true):
                        if (Italic is not null)
                            return (Italic, false, false);
                        return Regular is not null ? (Regular, false, true)
                            : BoldItalic is not null ? (BoldItalic, false, false) : null;

                    default:
                        var any = Regular ?? Bold ?? Italic ?? BoldItalic;
                        return any is not null ? (any, false, false) : null;
                }
            }
        }
    }
}
