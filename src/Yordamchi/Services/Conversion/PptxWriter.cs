using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Yordamchi.Models;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Yordamchi.Services.Conversion;

// =====================================================================================
//  DocumentContent → .pptx (OpenXML).
//
//  PresentationDocument.Create() butunlay bo'sh paket yaratadi: unda na slayd shabloni
//  (SlideMaster), na maket (SlideLayout), na mavzu (Theme) bo'ladi. PowerPoint esa
//  ularsiz faylni "buzilgan" deb hisoblaydi. Shuning uchun quyida shu uchta qism qo'lda,
//  minimal lekin to'liq haqiqiy ko'rinishda yig'iladi:
//
//    PresentationPart
//      ├─ SlideMasterPart  (ShapeTree + ColorMap + SlideLayoutIdList)
//      │    ├─ SlideLayoutPart  (ShapeTree + orqaga havola: master)
//      │    └─ ThemePart        (ColorScheme + FontScheme + FormatScheme)
//      └─ SlidePart × N         (har biri maketga havola qiladi)
//
//  Slaydlarning o'zi oddiy matn ramkalaridan iborat: sarlavha va tana. Bu "chiroyli
//  dizayn" emas, lekin matn to'liq tahrirlanadigan holda o'tadi — konvertatsiyadan
//  kutiladigan asosiy narsa shu.
// =====================================================================================

/// <summary>Oraliq hujjat modelini PowerPoint (.pptx) fayliga yozadi.</summary>
public static class PptxWriter
{
    /// <summary>16:9 slayd kengligi (EMU).</summary>
    private const int SlideWidthEmu = 12_192_000;

    /// <summary>16:9 slayd balandligi (EMU).</summary>
    private const int SlideHeightEmu = 6_858_000;

    /// <summary>1 punkt = 12700 EMU.</summary>
    private const int EmuPerPoint = 12_700;

    private const int MarginEmu = 685_800;            // 54 pt
    private const int TitleTopEmu = 457_200;          // 36 pt
    private const int TitleHeightEmu = 1_143_000;     // 90 pt
    private const int BodyTopEmu = 1_828_800;         // 144 pt

    /// <summary>Sarlavha shrifti (yuzdan bir punktda — OpenXML shunday o'lchaydi).</summary>
    private const int TitleFontSize = 4000;

    /// <summary>Tana matni uchun eng katta shrift.</summary>
    private const int BodyFontSizeLarge = 1800;

    private const int BodyFontSizeMedium = 1400;
    private const int BodyFontSizeSmall = 1100;

    /// <summary>
    /// PDF dan o'qilgan mazmunni PowerPoint taqdimotiga yozadi.
    /// </summary>
    /// <param name="content">Manba hujjat modeli.</param>
    /// <param name="pptxPath">Yaratiladigan .pptx fayl yo'li.</param>
    /// <param name="options">Konvertatsiya sozlamalari.</param>
    /// <param name="cancellationToken">Bekor qilish belgisi.</param>
    public static void Write(DocumentContent content, string pptxPath, PdfToPowerPointOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        options ??= PdfToPowerPointOptions.Default;

        if (string.IsNullOrWhiteSpace(pptxPath))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Natijaviy PowerPoint fayl yo'li ko'rsatilmagan.");

        var fullPath = Path.GetFullPath(pptxPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natijaviy fayl papkasini aniqlab bo'lmadi.", fullPath);

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Path.GetRandomFileName() + ".pptx");

        try
        {
            try
            {
                WriteCore(content, temporaryPath, options, cancellationToken);
                File.Move(temporaryPath, fullPath, overwrite: true);
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
                $"'{Path.GetFileName(fullPath)}' faylini yozib bo'lmadi: ruxsat yo'q yoki fayl boshqa dasturda ochiq.",
                fullPath,
                ex);
        }
        catch (IOException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(fullPath)}' faylini yozib bo'lmadi: {ex.Message}",
                fullPath,
                ex);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"PowerPoint faylini yaratishda xato yuz berdi: {ex.Message}",
                fullPath,
                ex);
        }
    }

    /// <summary>Taqdimot paketini yig'adi.</summary>
    private static void WriteCore(DocumentContent content, string path, PdfToPowerPointOptions options, CancellationToken cancellationToken)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);

        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        var slideMasterPart = CreateSlideMasterPart(presentationPart);
        var slideLayoutPart = slideMasterPart.GetPartsOfType<SlideLayoutPart>().First();

        var slideIdList = new SlideIdList();
        var slides = BuildSlides(content, options, cancellationToken);

        uint slideId = 256;
        foreach (var model in slides)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slidePart = presentationPart.AddNewPart<SlidePart>($"rIdSlide{slideId}");
            slidePart.AddPart(slideLayoutPart, "rId1");
            BuildSlide(slidePart, model, options);

            slideIdList.Append(new SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(slidePart)
            });
        }

        // Hech bo'lmaganda bitta slayd bo'lishi shart — bo'sh taqdimotni PowerPoint ochmaydi.
        if (slideIdList.ChildElements.Count == 0)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>("rIdSlide256");
            slidePart.AddPart(slideLayoutPart, "rId1");
            BuildSlide(slidePart, new SlideModel("Hujjat bo'sh", [], null), options);
            slideIdList.Append(new SlideId { Id = 256U, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        }

        presentationPart.Presentation.Append(
            new SlideMasterIdList(new SlideMasterId
            {
                Id = 2_147_483_648U,
                RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
            }),
            slideIdList,
            new SlideSize { Cx = SlideWidthEmu, Cy = SlideHeightEmu },
            new NotesSize { Cx = SlideHeightEmu, Cy = SlideWidthEmu });

        presentationPart.Presentation.Save();
        slideMasterPart.SlideMaster?.Save();
        slideLayoutPart.SlideLayout?.Save();
    }

    // ---------------------------------------------------------------------------------
    //  Shablon, maket va mavzu
    // ---------------------------------------------------------------------------------

    /// <summary>Slayd shabloni, maketi va mavzusini yaratadi.</summary>
    private static SlideMasterPart CreateSlideMasterPart(PresentationPart presentationPart)
    {
        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>("rIdMaster1");
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>("rIdLayout1");

        slideLayoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(CreateEmptyShapeTree("Maket")) { Name = "Sarlavha va matn" },
            new ColorMapOverride(new A.MasterColorMapping()))
        {
            Type = SlideLayoutValues.Blank,
            Preserve = true
        };

        // Maket o'z shabloniga orqaga havola qilishi shart, aks holda fayl yaroqsiz bo'ladi.
        slideLayoutPart.AddPart(slideMasterPart, "rIdMasterBack");

        slideMasterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(CreateEmptyShapeTree("Shablon")) { Name = "Asosiy shablon" },
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new SlideLayoutIdList(new SlideLayoutId
            {
                Id = 2_147_483_649U,
                RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart)
            }));

        var themePart = slideMasterPart.AddNewPart<ThemePart>("rIdTheme1");
        WriteMinimalTheme(themePart);

        return slideMasterPart;
    }

    /// <summary>Har qanday slaydda bo'lishi shart bo'lgan bo'sh shakllar daraxti.</summary>
    private static ShapeTree CreateEmptyShapeTree(string name)
        => new(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = name },
                new P.NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup(
                new A.Offset { X = 0L, Y = 0L },
                new A.Extents { Cx = SlideWidthEmu, Cy = SlideHeightEmu },
                new A.ChildOffset { X = 0L, Y = 0L },
                new A.ChildExtents { Cx = SlideWidthEmu, Cy = SlideHeightEmu })));

    /// <summary>
    /// Mavzu qismini yozadi. Mavzu XML'i to'liq tiplashtirilgan obyektlar bilan yig'ilganda
    /// yuzlab qator bo'lib ketadi, holbuki uning mazmuni butunlay o'zgarmas. Shuning uchun
    /// u tayyor XML ko'rinishida yoziladi — natija xuddi shunday yaroqli bo'ladi.
    /// </summary>
    private static void WriteMinimalTheme(ThemePart themePart)
    {
        const string ns = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var builder = new StringBuilder(4096);
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append($"<a:theme xmlns:a=\"{ns}\" name=\"Yordamchi\">");
        builder.Append("<a:themeElements>");

        builder.Append("<a:clrScheme name=\"Yordamchi\">");
        builder.Append("<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>");
        builder.Append("<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>");
        builder.Append("<a:dk2><a:srgbClr val=\"1F3864\"/></a:dk2>");
        builder.Append("<a:lt2><a:srgbClr val=\"EEECE1\"/></a:lt2>");
        builder.Append("<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1>");
        builder.Append("<a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2>");
        builder.Append("<a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3>");
        builder.Append("<a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>");
        builder.Append("<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5>");
        builder.Append("<a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>");
        builder.Append("<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink>");
        builder.Append("<a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>");
        builder.Append("</a:clrScheme>");

        builder.Append("<a:fontScheme name=\"Yordamchi\">");
        builder.Append("<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>");
        builder.Append("<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>");
        builder.Append("</a:fontScheme>");

        builder.Append("<a:fmtScheme name=\"Yordamchi\">");
        builder.Append("<a:fillStyleLst>");
        builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
        builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
        builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
        builder.Append("</a:fillStyleLst>");
        builder.Append("<a:lnStyleLst>");
        for (var width = 6350; width <= 6350 * 3; width += 6350)
        {
            builder.Append($"<a:ln w=\"{width}\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\">");
            builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
            builder.Append("<a:prstDash val=\"solid\"/></a:ln>");
        }

        builder.Append("</a:lnStyleLst>");
        builder.Append("<a:effectStyleLst>");
        builder.Append("<a:effectStyle><a:effectLst/></a:effectStyle>");
        builder.Append("<a:effectStyle><a:effectLst/></a:effectStyle>");
        builder.Append("<a:effectStyle><a:effectLst/></a:effectStyle>");
        builder.Append("</a:effectStyleLst>");
        builder.Append("<a:bgFillStyleLst>");
        builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
        builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
        builder.Append("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>");
        builder.Append("</a:bgFillStyleLst>");
        builder.Append("</a:fmtScheme>");

        builder.Append("</a:themeElements>");
        builder.Append("<a:objectDefaults/><a:extraClrSchemeLst/>");
        builder.Append("</a:theme>");

        using var stream = themePart.GetStream(FileMode.Create, FileAccess.Write);
        var bytes = new UTF8Encoding(false).GetBytes(builder.ToString());
        stream.Write(bytes, 0, bytes.Length);
    }

    // ---------------------------------------------------------------------------------
    //  Slaydlar
    // ---------------------------------------------------------------------------------

    /// <summary>Sahifalarni slaydlarga bo'ladi.</summary>
    private static List<SlideModel> BuildSlides(DocumentContent content, PdfToPowerPointOptions options, CancellationToken cancellationToken)
    {
        var slides = new List<SlideModel>();

        foreach (var page in content.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lines = ExtractLines(page);
            var background = options.IncludePageImage ? page.Blocks.OfType<ImageBlock>().FirstOrDefault() : null;

            string title;
            if (options.FirstLineAsTitle && lines.Count > 0)
            {
                title = Shorten(lines[0], 120);
                lines.RemoveAt(0);
            }
            else
            {
                title = $"{page.Number}-sahifa";
            }

            if (lines.Count == 0)
            {
                slides.Add(new SlideModel(title, [], background));
                continue;
            }

            // Matn juda uzun bo'lsa slaydga sig'maydi — uni bir necha slaydga bo'lamiz.
            var chunks = SplitIntoChunks(lines);
            for (var i = 0; i < chunks.Count; i++)
            {
                var slideTitle = i == 0 ? title : $"{title} (davomi)";
                slides.Add(new SlideModel(slideTitle, chunks[i], i == 0 ? background : null));
            }
        }

        return slides;
    }

    /// <summary>Sahifa bloklaridan matn qatorlarini ajratadi.</summary>
    private static List<string> ExtractLines(ContentPage page)
    {
        var lines = new List<string>();

        foreach (var block in page.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph when !paragraph.IsEmpty:
                    lines.Add(Collapse(paragraph.Text));
                    break;

                case TableBlock table:
                    foreach (var row in table.Rows)
                    {
                        var cells = row.Cells.Select(cell => Collapse(cell.Text)).Where(text => text.Length > 0);
                        var joined = string.Join("  |  ", cells);
                        if (joined.Length > 0)
                            lines.Add(joined);
                    }

                    break;
            }
        }

        return lines;
    }

    /// <summary>Qatorlarni bitta slaydga sig'adigan bo'laklarga ajratadi.</summary>
    private static List<List<string>> SplitIntoChunks(List<string> lines)
    {
        // 18 pt shriftda slayd tanasiga taxminan 12 ta qator (har biri ~95 belgi) sig'adi.
        const int maxRows = 12;
        const int charactersPerRow = 95;

        var chunks = new List<List<string>>();
        var current = new List<string>();
        var used = 0;

        foreach (var line in lines)
        {
            var rows = Math.Max(1, (int)Math.Ceiling(line.Length / (double)charactersPerRow));
            if (current.Count > 0 && used + rows > maxRows)
            {
                chunks.Add(current);
                current = [];
                used = 0;
            }

            current.Add(line);
            used += rows;
        }

        if (current.Count > 0)
            chunks.Add(current);

        return chunks;
    }

    /// <summary>Bitta slaydning mazmunini yozadi.</summary>
    private static void BuildSlide(SlidePart slidePart, SlideModel model, PdfToPowerPointOptions options)
    {
        var shapeTree = CreateEmptyShapeTree("Slayd");
        uint shapeId = 2;

        // Fon rasmi eng birinchi bo'lib qo'yiladi, shunda matn uning ustida ko'rinadi.
        if (options.IncludePageImage && model.Background is not null)
        {
            var picture = TryCreateBackgroundPicture(slidePart, model.Background, shapeId);
            if (picture is not null)
            {
                shapeTree.Append(picture);
                shapeId++;
            }
        }

        shapeTree.Append(CreateTextBox(
            shapeId++,
            "Sarlavha",
            MarginEmu,
            TitleTopEmu,
            SlideWidthEmu - 2 * MarginEmu,
            TitleHeightEmu,
            [model.Title],
            TitleFontSize,
            bold: true));

        if (model.Lines.Count > 0)
        {
            var fontSize = ChooseBodyFontSize(model.Lines);
            shapeTree.Append(CreateTextBox(
                shapeId,
                "Matn",
                MarginEmu,
                BodyTopEmu,
                SlideWidthEmu - 2 * MarginEmu,
                SlideHeightEmu - BodyTopEmu - MarginEmu / 2,
                model.Lines,
                fontSize,
                bold: false));
        }

        slidePart.Slide = new Slide(new CommonSlideData(shapeTree), new ColorMapOverride(new A.MasterColorMapping()));
        slidePart.Slide.Save();
    }

    /// <summary>Matn hajmiga qarab shrift o'lchamini tanlaydi.</summary>
    private static int ChooseBodyFontSize(IReadOnlyList<string> lines)
    {
        var characters = lines.Sum(line => line.Length);
        if (characters > 1200 || lines.Count > 14)
            return BodyFontSizeSmall;

        return characters > 700 || lines.Count > 10 ? BodyFontSizeMedium : BodyFontSizeLarge;
    }

    /// <summary>Berilgan joyda matn ramkasi yaratadi.</summary>
    private static P.Shape CreateTextBox(
        uint id,
        string name,
        long x,
        long y,
        long width,
        long height,
        IReadOnlyList<string> lines,
        int fontSize,
        bool bold)
    {
        var body = new TextBody(
            new A.BodyProperties { Wrap = A.TextWrappingValues.Square, RightInset = 0, LeftInset = 0 },
            new A.ListStyle());

        if (lines.Count == 0)
        {
            body.Append(new A.Paragraph(new A.EndParagraphRunProperties { Language = "uz-UZ" }));
        }
        else
        {
            foreach (var line in lines)
            {
                var paragraph = new A.Paragraph(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left });
                paragraph.Append(new A.Run(
                    new A.RunProperties { Language = "uz-UZ", FontSize = fontSize, Bold = bold, Dirty = false },
                    new A.Text(Sanitize(line))));
                body.Append(paragraph);
            }
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = x, Y = y },
                    new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
            body);
    }

    /// <summary>Sahifa rasmini slayd foni sifatida qo'yadi.</summary>
    private static P.Picture? TryCreateBackgroundPicture(SlidePart slidePart, ImageBlock image, uint id)
    {
        try
        {
            if (image.Data.Length == 0)
                return null;

            var contentType = string.IsNullOrWhiteSpace(image.ContentType) ? "image/png" : image.ContentType;
            var relationshipId = $"rIdImage{id}";
            var imagePart = slidePart.AddNewPart<ImagePart>(contentType, relationshipId);

            using (var stream = new MemoryStream(image.Data, writable: false))
                imagePart.FeedData(stream);

            return new P.Picture(
                new P.NonVisualPictureProperties(
                    new P.NonVisualDrawingProperties { Id = id, Name = "Sahifa rasmi" },
                    new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
                    new ApplicationNonVisualDrawingProperties()),
                new P.BlipFill(
                    new A.Blip { Embed = relationshipId },
                    new A.Stretch(new A.FillRectangle())),
                new P.ShapeProperties(
                    new A.Transform2D(
                        new A.Offset { X = 0L, Y = 0L },
                        new A.Extents { Cx = SlideWidthEmu, Cy = SlideHeightEmu }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
        }
        catch
        {
            // Rasm qo'shilmasa ham slayd matni saqlanib qoladi — bu halokatli xato emas.
            return null;
        }
    }

    // ---------------------------------------------------------------------------------
    //  Yordamchilar
    // ---------------------------------------------------------------------------------

    /// <summary>Ko'p bo'sh joylarni bittaga keltiradi.</summary>
    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var symbol in text)
        {
            var isSpace = char.IsWhiteSpace(symbol);
            if (isSpace)
            {
                if (!lastWasSpace && builder.Length > 0)
                    builder.Append(' ');
            }
            else if (!char.IsControl(symbol))
            {
                builder.Append(symbol);
            }

            lastWasSpace = isSpace;
        }

        return builder.ToString().TrimEnd();
    }

    private static string Sanitize(string? text)
    {
        var collapsed = Collapse(text);
        return collapsed.Length > 4000 ? collapsed[..4000] : collapsed;
    }

    private static string Shorten(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "…";

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

    /// <summary>Bitta slaydga tayyorlangan mazmun.</summary>
    /// <param name="Title">Slayd sarlavhasi.</param>
    /// <param name="Lines">Tana matnining qatorlari.</param>
    /// <param name="Background">Fon rasmi (bo'lmasligi mumkin).</param>
    private sealed record SlideModel(string Title, List<string> Lines, ImageBlock? Background);
}
