using System.Globalization;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Yordamchi.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Yordamchi.Services.Conversion;

/// <summary>
/// <see cref="DocumentContent"/> oraliq modelini haqiqiy, to'liq tahrirlanadigan
/// Word (.docx) hujjatiga yozadi — OpenXML orqali, hech qanday rasmga aylantirmasdan.
/// <para>
/// Har bir abzas <c>w:p</c>, har bir matn bo'lagi o'z shrifti va rangi bilan <c>w:r</c>,
/// aniqlangan jadvallar esa chegaralari bilan <c>w:tbl</c> bo'lib tushadi. Shu sababli
/// foydalanuvchi Word'da matnni to'g'ridan-to'g'ri tahrirlay oladi.
/// </para>
/// <para>
/// O'lchov birliklari: 1 punkt = 20 twip, 1 punkt = 2 half-point (shrift o'lchami),
/// 1 punkt = 12700 EMU (rasm o'lchami).
/// </para>
/// </summary>
public static class DocxWriter
{
    private const double TwipsPerPoint = 20d;
    private const double EmuPerPoint = 12700d;
    private const string DefaultFontFamily = "Calibri";
    private const string HeaderShadingFill = "F2F2F2";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Ro'yxat belgisi sifatida qabul qilinadigan boshlang'ich belgilar.</summary>
    private static readonly char[] BulletCharacters = ['•', '‣', '●', '○', '▪', '◦', '·', '⁃', '∙', '*', '-', '–', '—'];

    /// <summary>
    /// Hujjatni <paramref name="docxPath"/> ga yozadi. Fayl avval vaqtinchalik yo'lga yoziladi va
    /// faqat to'liq tayyor bo'lgach o'z o'rniga ko'chiriladi — shuning uchun yarim yozilgan .docx
    /// hech qachon qolmaydi.
    /// </summary>
    /// <exception cref="PdfServiceException"/>
    public static void Write(
        DocumentContent content,
        string docxPath,
        PdfToWordOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOutputPath(docxPath);

        var tempPath = docxPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteCore(content, tempPath, options, cancellationToken);
            File.Move(tempPath, docxPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(docxPath)}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                docxPath,
                ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"Word hujjatini yozishda xatolik yuz berdi: {ex.Message}",
                docxPath,
                ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    // =================================================================================
    //  Hujjat tanasi
    // =================================================================================

    private static void WriteCore(
        DocumentContent content,
        string path,
        PdfToWordOptions options,
        CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new W.Document();
        var body = main.Document.AppendChild(new W.Body());

        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = BuildStyles();

        WriteProperties(document, content);

        var exact = options.Layout == DocumentLayoutMode.Exact;
        var imageId = 1u;
        var wroteAnything = false;

        for (var pageIndex = 0; pageIndex < content.Pages.Count; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = content.Pages[pageIndex];

            // Sahifa uzilishi "Exact" rejimida ham kerak: ramkalar sahifaga bog'langan.
            if (pageIndex > 0 && options.InsertPageBreaks)
            {
                body.AppendChild(CreatePageBreakParagraph());
                wroteAnything = true;
            }

            foreach (var block in page.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (block)
                {
                    case ParagraphBlock paragraph when !paragraph.IsEmpty:
                        body.AppendChild(CreateParagraph(paragraph, page, exact));
                        wroteAnything = true;
                        break;

                    case TableBlock table when table.Rows.Count > 0 && table.ColumnCount > 0:
                        body.AppendChild(CreateTable(table, page));

                        // Word ikki jadval orasida va hujjat oxirida abzas talab qiladi.
                        body.AppendChild(CreateSpacerParagraph());
                        wroteAnything = true;
                        break;

                    case ImageBlock image when image.Data.Length > 0:
                        body.AppendChild(CreateImageParagraph(main, image, page, exact, ref imageId));
                        wroteAnything = true;
                        break;
                }
            }
        }

        if (!wroteAnything)
            body.AppendChild(CreateSpacerParagraph());

        body.AppendChild(CreateSectionProperties(content.Pages.FirstOrDefault()));
        main.Document.Save();
    }

    private static void WriteProperties(WordprocessingDocument document, DocumentContent content)
    {
        try
        {
            var properties = document.PackageProperties;
            if (!string.IsNullOrWhiteSpace(content.Title))
                properties.Title = content.Title;

            if (!string.IsNullOrWhiteSpace(content.Author))
                properties.Creator = content.Author;

            properties.Created = DateTime.Now;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Metama'lumot yozilmasa ham hujjat yaroqli qoladi.
        }
    }

    // =================================================================================
    //  Uslublar (styles.xml)
    // =================================================================================

    private static W.Styles BuildStyles()
    {
        var styles = new W.Styles();

        styles.AppendChild(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                new W.RunFonts { Ascii = DefaultFontFamily, HighAnsi = DefaultFontFamily, ComplexScript = DefaultFontFamily },
                new W.FontSize { Val = "22" },
                new W.FontSizeComplexScript { Val = "22" })),
            new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                new W.SpacingBetweenLines { After = "120", Line = "259", LineRule = W.LineSpacingRuleValues.Auto }))));

        var normal = new W.Style
        {
            Type = W.StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
            StyleName = new W.StyleName { Val = "Normal" },
            PrimaryStyle = new W.PrimaryStyle()
        };
        styles.AppendChild(normal);

        styles.AppendChild(CreateHeadingStyle("Heading1", "heading 1", "32", 1));
        styles.AppendChild(CreateHeadingStyle("Heading2", "heading 2", "26", 2));
        styles.AppendChild(CreateHeadingStyle("Heading3", "heading 3", "24", 3));
        styles.AppendChild(CreateTableStyle());

        return styles;
    }

    private static W.Style CreateHeadingStyle(string styleId, string name, string halfPoints, int level)
    {
        var style = new W.Style
        {
            Type = W.StyleValues.Paragraph,
            StyleId = styleId,
            StyleName = new W.StyleName { Val = name },
            BasedOn = new W.BasedOn { Val = "Normal" },
            NextParagraphStyle = new W.NextParagraphStyle { Val = "Normal" },
            PrimaryStyle = new W.PrimaryStyle(),
            UIPriority = new W.UIPriority { Val = 9 },
            // Elementlar tartibi OpenXML sxemasi bo'yicha qat'iy: keepNext → spacing → outlineLvl.
            StyleParagraphProperties = new W.StyleParagraphProperties(
                new W.KeepNext(),
                new W.SpacingBetweenLines { Before = "240", After = "120" },
                new W.OutlineLevel { Val = level - 1 }),
            StyleRunProperties = new W.StyleRunProperties(
                new W.Bold(),
                new W.BoldComplexScript(),
                new W.FontSize { Val = halfPoints },
                new W.FontSizeComplexScript { Val = halfPoints })
        };

        return style;
    }

    /// <summary>Barcha chegaralari chizilgan oddiy jadval uslubi.</summary>
    private static W.Style CreateTableStyle() => new()
    {
        Type = W.StyleValues.Table,
        StyleId = "TableGrid",
        StyleName = new W.StyleName { Val = "Table Grid" },
        UIPriority = new W.UIPriority { Val = 39 },
        StyleTableProperties = new W.StyleTableProperties(CreateTableBorders())
    };

    private static W.TableBorders CreateTableBorders() => new(
        new W.TopBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "999999" },
        new W.LeftBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "999999" },
        new W.BottomBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "999999" },
        new W.RightBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "999999" },
        new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "999999" },
        new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4U, Space = 0U, Color = "999999" });

    // =================================================================================
    //  Abzaslar
    // =================================================================================

    private static W.Paragraph CreateParagraph(ParagraphBlock block, ContentPage page, bool exact)
    {
        var paragraph = new W.Paragraph();
        var properties = new W.ParagraphProperties();

        var styleId = block.Kind switch
        {
            BlockKind.Heading1 => "Heading1",
            BlockKind.Heading2 => "Heading2",
            BlockKind.Heading3 => "Heading3",
            _ => null
        };

        if (styleId is not null)
            properties.ParagraphStyleId = new W.ParagraphStyleId { Val = styleId };

        properties.Justification = new W.Justification { Val = MapAlignment(block.Alignment) };

        var indentTwips = ToTwips(Math.Max(0d, block.IndentPoints));
        if (block.Kind == BlockKind.ListItem)
        {
            // Ro'yxat: belgi chekkada "osilib" turadi, matn esa 0.63 sm ichkariga suriladi.
            properties.Indentation = new W.Indentation
            {
                Left = (indentTwips + 360).ToString(Inv),
                Hanging = "360"
            };
        }
        else if (indentTwips > 0)
        {
            properties.Indentation = new W.Indentation { Left = indentTwips.ToString(Inv) };
        }

        properties.SpacingBetweenLines = new W.SpacingBetweenLines
        {
            After = ToTwips(Math.Max(0d, block.SpaceAfterPoints)).ToString(Inv),
            Line = "240",
            LineRule = W.LineSpacingRuleValues.Auto
        };

        if (exact)
            properties.FrameProperties = CreateFrame(block, page);

        paragraph.ParagraphProperties = properties;

        var runs = block.Runs.Count > 0 ? block.Runs : [new TextRun(block.Text)];

        // OCR dan kelgan ro'yxat elementida belgi bo'lmasligi mumkin — o'zimiz qo'shamiz.
        if (block.Kind == BlockKind.ListItem && !StartsWithBullet(block.Text))
        {
            var sample = runs[0];
            paragraph.AppendChild(CreateRun(sample with { Text = "• ", IsBold = false, IsItalic = false }));
        }

        foreach (var run in runs)
        {
            var text = Sanitize(run.Text);
            if (text.Length == 0)
                continue;

            paragraph.AppendChild(CreateRun(run with { Text = text }));
        }

        return paragraph;
    }

    private static W.Run CreateRun(TextRun run)
    {
        var element = new W.Run();
        var family = string.IsNullOrWhiteSpace(run.FontFamily) ? DefaultFontFamily : run.FontFamily.Trim();
        var halfPoints = (int)Math.Round(Clamp(run.FontSize, 4d, 96d) * 2d, MidpointRounding.AwayFromZero);

        var properties = new W.RunProperties
        {
            RunFonts = new W.RunFonts { Ascii = family, HighAnsi = family, ComplexScript = family },
            FontSize = new W.FontSize { Val = halfPoints.ToString(Inv) },
            FontSizeComplexScript = new W.FontSizeComplexScript { Val = halfPoints.ToString(Inv) }
        };

        if (run.IsBold)
        {
            properties.Bold = new W.Bold();
            properties.BoldComplexScript = new W.BoldComplexScript();
        }

        if (run.IsItalic)
        {
            properties.Italic = new W.Italic();
            properties.ItalicComplexScript = new W.ItalicComplexScript();
        }

        var color = NormalizeColor(run.ColorHex);
        if (color is not null)
            properties.Color = new W.Color { Val = color };

        element.RunProperties = properties;

        // Space="preserve" bo'lmasa Word so'z boshidagi va oxiridagi bo'sh joylarni yo'q qiladi.
        element.AppendChild(new W.Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });
        return element;
    }

    private static W.Paragraph CreatePageBreakParagraph()
        => new(new W.Run(new W.Break { Type = W.BreakValues.Page }));

    private static W.Paragraph CreateSpacerParagraph()
        => new(new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }));

    /// <summary>
    /// "Aniq joylashuv" rejimi: abzas sahifaga bog'langan ramkaga joylanadi va PDF dagi
    /// koordinatasida turadi.
    /// </summary>
    private static W.FrameProperties CreateFrame(ContentBlock block, ContentPage page)
    {
        var x = ToTwips(Clamp(block.Left, 0d, Math.Max(1d, page.WidthPoints - 10d)));
        var y = ToTwips(Clamp(block.Top, 0d, Math.Max(1d, page.HeightPoints - 10d)));
        var widthPoints = block.Width > 1d ? block.Width : page.WidthPoints - block.Left;
        var width = ToTwips(Clamp(widthPoints, 20d, page.WidthPoints));

        return new W.FrameProperties
        {
            X = x.ToString(Inv),
            Y = y.ToString(Inv),
            Width = width.ToString(Inv),
            HorizontalPosition = W.HorizontalAnchorValues.Page,
            VerticalPosition = W.VerticalAnchorValues.Page,
            Wrap = W.TextWrappingValues.Auto,
            HeightType = W.HeightRuleValues.Auto,
            AnchorLock = true
        };
    }

    // =================================================================================
    //  Jadvallar
    // =================================================================================

    private static W.Table CreateTable(TableBlock block, ContentPage page)
    {
        var table = new W.Table();
        var columnCount = Math.Max(1, block.ColumnCount);
        var widths = ResolveColumnWidths(block, columnCount, UsableWidthTwips(page));

        // Sxema tartibi: tblStyle → tblW → tblBorders → tblLayout.
        var properties = new W.TableProperties();
        properties.AppendChild(new W.TableStyle { Val = "TableGrid" });
        properties.AppendChild(new W.TableWidth
        {
            Width = widths.Sum().ToString(Inv),
            Type = W.TableWidthUnitValues.Dxa
        });

        if (block.HasBorders)
            properties.AppendChild(CreateTableBorders());

        properties.AppendChild(new W.TableLayout { Type = W.TableLayoutValues.Fixed });
        table.AppendChild(properties);

        var grid = new W.TableGrid();
        foreach (var width in widths)
            grid.AppendChild(new W.GridColumn { Width = width.ToString(Inv) });

        table.AppendChild(grid);

        foreach (var row in block.Rows)
            table.AppendChild(CreateTableRow(row, widths, columnCount));

        return table;
    }

    private static W.TableRow CreateTableRow(Models.TableRow row, IReadOnlyList<int> widths, int columnCount)
    {
        var element = new W.TableRow();
        if (row.IsHeader)
        {
            // Jadval sahifadan sahifaga o'tsa sarlavha qatori takrorlanadi.
            element.AppendChild(new W.TableRowProperties(new W.TableHeader()));
        }

        var column = 0;
        foreach (var cell in row.Cells)
        {
            if (column >= columnCount)
                break;

            var span = Math.Clamp(cell.ColumnSpan, 1, columnCount - column);
            var width = 0;
            for (var i = 0; i < span; i++)
                width += widths[column + i];

            element.AppendChild(CreateTableCell(cell, width, span, row.IsHeader));
            column += span;
        }

        // Qator to'liq bo'lmasa qolgan kataklarni bo'sh qilib to'ldiramiz.
        while (column < columnCount)
        {
            element.AppendChild(CreateTableCell(null, widths[column], 1, row.IsHeader));
            column++;
        }

        return element;
    }

    private static W.TableCell CreateTableCell(Models.TableCell? cell, int widthTwips, int span, bool isHeader)
    {
        var element = new W.TableCell();
        var properties = new W.TableCellProperties();

        properties.AppendChild(new W.TableCellWidth
        {
            Width = Math.Max(100, widthTwips).ToString(Inv),
            Type = W.TableWidthUnitValues.Dxa
        });

        if (span > 1)
            properties.AppendChild(new W.GridSpan { Val = span });

        if (isHeader)
        {
            properties.AppendChild(new W.Shading
            {
                Val = W.ShadingPatternValues.Clear,
                Color = "auto",
                Fill = HeaderShadingFill
            });
        }

        element.AppendChild(properties);

        var paragraph = new W.Paragraph
        {
            // Sxema tartibi: spacing → jc.
            ParagraphProperties = new W.ParagraphProperties(
                new W.SpacingBetweenLines { Before = "20", After = "20", Line = "240", LineRule = W.LineSpacingRuleValues.Auto },
                new W.Justification { Val = MapAlignment(cell?.Alignment ?? TextAlignment.Left) })
        };

        if (cell is not null)
        {
            foreach (var run in cell.Runs)
            {
                var text = Sanitize(run.Text);
                if (text.Length == 0)
                    continue;

                paragraph.AppendChild(CreateRun(run with { Text = text, IsBold = run.IsBold || isHeader }));
            }
        }

        // Har bir katakda kamida bitta abzas bo'lishi shart, aks holda hujjat yaroqsiz bo'ladi.
        element.AppendChild(paragraph);
        return element;
    }

    private static List<int> ResolveColumnWidths(TableBlock block, int columnCount, int usableTwips)
    {
        var relative = new List<double>(columnCount);
        for (var i = 0; i < columnCount; i++)
        {
            var value = i < block.ColumnWidths.Count ? block.ColumnWidths[i] : 0d;
            relative.Add(value > 0d && !double.IsNaN(value) ? value : 0d);
        }

        var total = relative.Sum();
        if (total <= 0d)
        {
            relative.Clear();
            for (var i = 0; i < columnCount; i++)
                relative.Add(1d);

            total = columnCount;
        }

        var widths = new List<int>(columnCount);
        for (var i = 0; i < columnCount; i++)
            widths.Add(Math.Max(400, (int)Math.Round(usableTwips * relative[i] / total)));

        return widths;
    }

    private static int UsableWidthTwips(ContentPage page)
    {
        var margins = Clamp(page.MarginLeftPoints, 0d, page.WidthPoints / 3d) * 2d;
        var points = Math.Max(72d, page.WidthPoints - margins);
        return Math.Max(1440, ToTwips(points));
    }

    // =================================================================================
    //  Rasmlar
    // =================================================================================

    private static W.Paragraph CreateImageParagraph(
        MainDocumentPart main,
        ImageBlock block,
        ContentPage page,
        bool exact,
        ref uint imageId)
    {
        var isJpeg = block.ContentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
            || block.ContentType.Contains("jpg", StringComparison.OrdinalIgnoreCase);

        var part = main.AddImagePart(isJpeg ? ImagePartType.Jpeg : ImagePartType.Png);
        using (var stream = new MemoryStream(block.Data, writable: false))
            part.FeedData(stream);

        var relationshipId = main.GetIdOfPart(part);

        // Piksellardan punktga o'tishda 96 dpi ekran zichligi asos qilib olinadi (1 px = 0.75 pt).
        var widthPoints = block.Width > 1d ? block.Width : Math.Max(1d, block.PixelWidth * 0.75d);
        var heightPoints = block.Height > 1d ? block.Height : Math.Max(1d, block.PixelHeight * 0.75d);

        var maxWidth = Math.Max(72d, page.WidthPoints - (2d * Clamp(page.MarginLeftPoints, 0d, page.WidthPoints / 3d)));
        if (widthPoints > maxWidth)
        {
            var scale = maxWidth / widthPoints;
            widthPoints = maxWidth;
            heightPoints = Math.Max(1d, heightPoints * scale);
        }

        var cx = (long)Math.Round(Clamp(widthPoints, 1d, 20000d) * EmuPerPoint);
        var cy = (long)Math.Round(Clamp(heightPoints, 1d, 20000d) * EmuPerPoint);
        var id = imageId++;

        var drawing = new W.Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = $"Rasm {id}" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = $"rasm{id}.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
                    }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

        var paragraph = new W.Paragraph();
        var properties = new W.ParagraphProperties
        {
            Justification = new W.Justification { Val = W.JustificationValues.Left },
            SpacingBetweenLines = new W.SpacingBetweenLines { After = "120", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }
        };

        if (exact)
            properties.FrameProperties = CreateFrame(block, page);

        paragraph.ParagraphProperties = properties;
        paragraph.AppendChild(new W.Run(drawing));
        return paragraph;
    }

    // =================================================================================
    //  Sahifa o'lchami va chekkalari
    // =================================================================================

    private static W.SectionProperties CreateSectionProperties(ContentPage? page)
    {
        var widthPoints = page is { WidthPoints: > 0d } ? page.WidthPoints : 595.276d;
        var heightPoints = page is { HeightPoints: > 0d } ? page.HeightPoints : 841.89d;

        var pageSize = new W.PageSize
        {
            Width = (uint)Math.Max(1440, ToTwips(Clamp(widthPoints, 72d, 3200d))),
            Height = (uint)Math.Max(1440, ToTwips(Clamp(heightPoints, 72d, 3200d)))
        };

        if (page is { IsLandscape: true })
            pageSize.Orient = W.PageOrientationValues.Landscape;

        var left = ToTwips(Clamp(page?.MarginLeftPoints ?? 56d, 18d, widthPoints / 3d));
        var top = ToTwips(Clamp(page?.MarginTopPoints ?? 56d, 18d, heightPoints / 3d));

        var margin = new W.PageMargin
        {
            Top = top,
            Bottom = top,
            Left = (uint)left,
            Right = (uint)left,
            Header = 720U,
            Footer = 720U,
            Gutter = 0U
        };

        return new W.SectionProperties(pageSize, margin);
    }

    // =================================================================================
    //  Kichik yordamchilar
    // =================================================================================

    private static W.JustificationValues MapAlignment(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Center => W.JustificationValues.Center,
        TextAlignment.Right => W.JustificationValues.Right,
        TextAlignment.Justify => W.JustificationValues.Both,
        _ => W.JustificationValues.Left
    };

    private static bool StartsWithBullet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        if (trimmed.Length == 0)
            return false;

        if (Array.IndexOf(BulletCharacters, trimmed[0]) >= 0)
            return true;

        // "1." / "12)" / "a)" ko'rinishidagi raqamlangan ro'yxat.
        var index = 0;
        while (index < trimmed.Length && index < 3 && char.IsLetterOrDigit(trimmed[index]))
            index++;

        return index > 0 && index < trimmed.Length && (trimmed[index] == '.' || trimmed[index] == ')');
    }

    /// <summary>XML ga yozib bo'lmaydigan boshqaruv belgilarini bo'sh joyga almashtiradi.</summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\r' or '\n' or '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (character < 0x20 || (character >= 0x7F && character <= 0x9F))
                continue;

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary><c>#RRGGBB</c> yoki <c>RRGGBB</c> ni Word kutadigan <c>RRGGBB</c> ga keltiradi.</summary>
    private static string? NormalizeColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return null;

        var value = colorHex.Trim().TrimStart('#');
        if (value.Length != 6)
            return null;

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return null;
        }

        return value.ToUpperInvariant();
    }

    private static int ToTwips(double points)
    {
        var value = points * TwipsPerPoint;
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        return (int)Math.Round(Math.Clamp(value, 0d, 31680d), MidpointRounding.AwayFromZero);
    }

    private static double Clamp(double value, double min, double max)
        => double.IsNaN(value) ? min : Math.Min(Math.Max(value, min), max);

    private static void ValidateOutputPath(string? outputPath)
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

        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{directory}' papkasini yaratib bo'lmadi.",
                outputPath,
                ex);
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
            // Qolib ketgan vaqtinchalik fayl muvaffaqiyatli saqlashni bekor qilishga arzimaydi.
        }
    }
}
