using System.Globalization;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Yordamchi.Models;

namespace Yordamchi.Services.Conversion;

// =====================================================================================
//  DocumentContent → .xlsx (OpenXML).
//
//  Excel fayli "bir varaq = bir SheetData" tamoyili bilan yig'iladi: jadval bloklari
//  kataklarga, oddiy abzaslar esa (agar so'ralsa) A ustuniga tushadi. Uslublar jadvali
//  atigi ikkita katak formatidan iborat — sarlavha (qalin, kulrang fon) va oddiy matn;
//  bu Excel uchun yetarli va faylni yengil saqlaydi.
// =====================================================================================

/// <summary>Oraliq hujjat modelini Excel (.xlsx) fayliga yozadi.</summary>
public static class XlsxWriter
{
    /// <summary>Oddiy katak formatining indeksi.</summary>
    private const uint NormalFormatIndex = 0;

    /// <summary>Sarlavha katagi formatining indeksi.</summary>
    private const uint HeaderFormatIndex = 1;

    /// <summary>Excel varaq nomida ruxsat etilmagan belgilar.</summary>
    private static readonly char[] ForbiddenSheetNameChars = ['[', ']', ':', '*', '?', '/', '\\'];

    /// <summary>
    /// PDF dan o'qilgan mazmunni Excel kitobiga yozadi.
    /// </summary>
    /// <param name="content">Manba hujjat modeli.</param>
    /// <param name="xlsxPath">Yaratiladigan .xlsx fayl yo'li.</param>
    /// <param name="options">Konvertatsiya sozlamalari.</param>
    /// <param name="cancellationToken">Bekor qilish belgisi.</param>
    public static void Write(DocumentContent content, string xlsxPath, PdfToExcelOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        options ??= PdfToExcelOptions.Default;

        if (string.IsNullOrWhiteSpace(xlsxPath))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Natijaviy Excel fayl yo'li ko'rsatilmagan.");

        var fullPath = Path.GetFullPath(xlsxPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            throw new PdfServiceException(PdfErrorKind.OutputNotWritable, "Natijaviy fayl papkasini aniqlab bo'lmadi.", fullPath);

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Path.GetRandomFileName() + ".xlsx");

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
                $"Excel faylini yaratishda xato yuz berdi: {ex.Message}",
                fullPath,
                ex);
        }
    }

    /// <summary>Kitobni yig'ish — barcha qismlar shu yerda yaratiladi.</summary>
    private static void WriteCore(DocumentContent content, string path, PdfToExcelOptions options, CancellationToken cancellationToken)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = BuildStylesheet();
        stylesPart.Stylesheet.Save();

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint sheetId = 1;

        if (options.SheetPerPage && content.Pages.Count > 0)
        {
            foreach (var page in content.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = MakeUniqueSheetName($"Sahifa {page.Number}", usedNames);
                AppendSheet(workbookPart, sheets, name, sheetId++, [page], options, cancellationToken);
            }
        }
        else
        {
            var name = MakeUniqueSheetName("Hujjat", usedNames);
            AppendSheet(workbookPart, sheets, name, sheetId, content.Pages, options, cancellationToken);
        }

        workbookPart.Workbook.Save();
    }

    /// <summary>Bitta varaqni yaratib, unga berilgan sahifalarning mazmunini yozadi.</summary>
    private static void AppendSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        string sheetName,
        uint sheetId,
        IReadOnlyList<ContentPage> pages,
        PdfToExcelOptions options,
        CancellationToken cancellationToken)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();

        // Ustun kengligini mazmunga qarab tanlash uchun eng uzun matn uzunligini yig'ib boramiz.
        var columnLengths = new Dictionary<int, int>();
        uint rowIndex = 1;

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Bir nechta sahifa bitta varaqqa tushayotgan bo'lsa, sahifa boshini belgilab qo'yamiz.
            if (pages.Count > 1)
            {
                AppendRow(sheetData, rowIndex++, [new CellContent($"— {page.Number}-sahifa —", HeaderFormatIndex, ForceText: true)], columnLengths);
            }

            foreach (var block in page.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (block)
                {
                    case TableBlock table:
                        WriteTable(sheetData, table, ref rowIndex, columnLengths, cancellationToken);
                        // Jadvaldan keyin bitta bo'sh qator — keyingi blok bilan qo'shilib ketmasin.
                        rowIndex++;
                        break;

                    case ParagraphBlock paragraph when options.IncludePlainText && !paragraph.IsEmpty:
                        AppendRow(sheetData, rowIndex++, [new CellContent(paragraph.Text, NormalFormatIndex, ForceText: false)], columnLengths);
                        break;
                }
            }

            if (pages.Count > 1)
                rowIndex++;
        }

        var worksheet = new Worksheet();

        var columns = BuildColumns(columnLengths);
        if (columns is not null)
            worksheet.Append(columns);

        worksheet.Append(sheetData);
        worksheetPart.Worksheet = worksheet;
        worksheetPart.Worksheet.Save();

        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = sheetName
        });
    }

    /// <summary>Jadval blokini kataklarga yozadi.</summary>
    private static void WriteTable(
        SheetData sheetData,
        TableBlock table,
        ref uint rowIndex,
        Dictionary<int, int> columnLengths,
        CancellationToken cancellationToken)
    {
        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = new List<CellContent>(row.Cells.Count);
            foreach (var cell in row.Cells)
            {
                cells.Add(new CellContent(cell.Text, row.IsHeader ? HeaderFormatIndex : NormalFormatIndex, ForceText: false));

                // Birlashtirilgan kataklar o'rnini bo'sh kataklar bilan to'ldiramiz.
                for (var extra = 1; extra < cell.ColumnSpan; extra++)
                    cells.Add(new CellContent(string.Empty, row.IsHeader ? HeaderFormatIndex : NormalFormatIndex, ForceText: true));
            }

            AppendRow(sheetData, rowIndex++, cells, columnLengths);
        }
    }

    /// <summary>Bitta qatorni yasaydi va varaqqa qo'shadi.</summary>
    private static void AppendRow(SheetData sheetData, uint rowIndex, IReadOnlyList<CellContent> cells, Dictionary<int, int> columnLengths)
    {
        var row = new Row { RowIndex = rowIndex };

        for (var i = 0; i < cells.Count; i++)
        {
            var content = cells[i];
            var text = Sanitize(content.Text);
            var reference = ColumnName(i) + rowIndex.ToString(CultureInfo.InvariantCulture);

            var cell = new Cell
            {
                CellReference = reference,
                StyleIndex = content.StyleIndex
            };

            if (!content.ForceText && TryParseNumber(text, out var number))
            {
                cell.DataType = CellValues.Number;
                cell.CellValue = new CellValue(number.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (text.Length > 0)
            {
                cell.DataType = CellValues.String;
                cell.CellValue = new CellValue(text);
            }

            row.Append(cell);

            var length = text.Length;
            if (!columnLengths.TryGetValue(i, out var known) || known < length)
                columnLengths[i] = length;
        }

        sheetData.Append(row);
    }

    /// <summary>Yig'ilgan matn uzunliklari asosida ustun kengliklarini tayyorlaydi.</summary>
    private static Columns? BuildColumns(Dictionary<int, int> columnLengths)
    {
        if (columnLengths.Count == 0)
            return null;

        var columns = new Columns();
        foreach (var pair in columnLengths.OrderBy(pair => pair.Key))
        {
            // 1.15 koeffitsiyenti — Calibri 11 uchun belgi kengligiga taxminiy tuzatish.
            var width = Math.Clamp(pair.Value * 1.15d + 2d, 9d, 70d);
            columns.Append(new Column
            {
                Min = (uint)(pair.Key + 1),
                Max = (uint)(pair.Key + 1),
                Width = Math.Round(width, 2),
                CustomWidth = true
            });
        }

        return columns;
    }

    /// <summary>Ikki xil katak formati bo'lgan eng sodda uslublar jadvali.</summary>
    private static Stylesheet BuildStylesheet()
    {
        var fonts = new Fonts(
            new Font(
                new FontSize { Val = 11d },
                new FontName { Val = "Calibri" }),
            new Font(
                new Bold(),
                new FontSize { Val = 11d },
                new FontName { Val = "Calibri" }))
        {
            Count = 2
        };

        // Excel birinchi ikkita to'ldirishni (none va gray125) qat'iy talab qiladi.
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(
                new ForegroundColor { Rgb = "FFDCE6F1" },
                new BackgroundColor { Indexed = 64U })
            { PatternType = PatternValues.Solid }))
        {
            Count = 3
        };

        var thin = new Border(
            new LeftBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new RightBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new BottomBorder(new Color { Auto = true }) { Style = BorderStyleValues.Thin },
            new DiagonalBorder());

        var borders = new Borders(new Border(), thin) { Count = 2 };

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat { NumberFormatId = 0U, FontId = 0U, FillId = 0U, BorderId = 0U })
        {
            Count = 1
        };

        var cellFormats = new CellFormats(
            // 0 — oddiy matn.
            new CellFormat { NumberFormatId = 0U, FontId = 0U, FillId = 0U, BorderId = 0U, FormatId = 0U },
            // 1 — sarlavha: qalin, kulrang fon, chegara.
            new CellFormat
            {
                NumberFormatId = 0U,
                FontId = 1U,
                FillId = 2U,
                BorderId = 1U,
                FormatId = 0U,
                ApplyFont = true,
                ApplyFill = true,
                ApplyBorder = true
            })
        {
            Count = 2
        };

        return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats);
    }

    /// <summary>Matnni son sifatida o'qishga urinadi (nuqta ham, vergul ham qabul qilinadi).</summary>
    private static bool TryParseNumber(string text, out double value)
    {
        value = 0d;
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 24)
            return false;

        // Faqat raqam, ajratgich va ishoradan iborat bo'lsin — "12-bet" kabi matnlar son emas.
        foreach (var symbol in trimmed)
        {
            if (!char.IsDigit(symbol) && symbol is not ('.' or ',' or '-' or '+') && !char.IsWhiteSpace(symbol))
                return false;
        }

        if (!trimmed.Any(char.IsDigit))
            return false;

        // "007" kabi qiymatlar shaxsiy raqam bo'lishi mumkin — ularni matn holida qoldiramiz.
        var digits = trimmed.TrimStart('+', '-');
        if (digits.Length > 1 && digits[0] == '0' && digits[1] is not ('.' or ','))
            return false;

        // Bo'sh joy (shu jumladan uzilmas probel) — mingliklar ajratgichi, uni olib tashlaymiz.
        var cleaned = new string(trimmed.Where(symbol => !char.IsWhiteSpace(symbol)).ToArray());

        // Vergul ikki xil ma'noda kelishi mumkin: o'zbek/rus hujjatlarida — o'nlik ajratgich
        // ("1000,50"), ingliz hujjatlarida — mingliklar ajratgichi ("1,250").
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            // Ikkalasi ham bor: oxirgi kelgani o'nlik ajratgich, ikkinchisi — mingliklar.
            cleaned = lastComma > lastDot
                ? cleaned.Replace(".", string.Empty).Replace(',', '.')
                : cleaned.Replace(",", string.Empty);
        }
        else if (lastComma >= 0)
        {
            // Faqat vergul: bir nechta bo'lsa yoki aynan uchta raqamdan oldin tursa —
            // mingliklar ajratgichi ("1,250"), aks holda o'nlik ajratgich ("1000,50").
            var commaCount = cleaned.Count(symbol => symbol == ',');
            var digitsAfter = cleaned.Length - lastComma - 1;

            cleaned = commaCount > 1 || digitsAfter == 3
                ? cleaned.Replace(",", string.Empty)
                : cleaned.Replace(',', '.');
        }

        // Mingliklar ajratgichlari yuqorida olib tashlangani uchun bu yerda faqat oddiy son kutamiz.
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>0 → "A", 25 → "Z", 26 → "AA".</summary>
    private static string ColumnName(int index)
    {
        var builder = new StringBuilder(3);
        var value = index;
        do
        {
            builder.Insert(0, (char)('A' + value % 26));
            value = value / 26 - 1;
        }
        while (value >= 0);

        return builder.ToString();
    }

    /// <summary>XML ga tushmaydigan boshqaruv belgilarini olib tashlaydi.</summary>
    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (var symbol in text)
        {
            if (symbol is '\t' or '\n' or '\r' || !char.IsControl(symbol))
                builder.Append(symbol is '\n' or '\r' or '\t' ? ' ' : symbol);
        }

        // Excel bitta katakda 32767 belgidan ko'pini ko'tarmaydi.
        var result = builder.ToString().Trim();
        return result.Length > 32000 ? result[..32000] : result;
    }

    /// <summary>Varaq nomini Excel qoidalariga moslaydi va takrorlanmasligini ta'minlaydi.</summary>
    private static string MakeUniqueSheetName(string desired, HashSet<string> used)
    {
        var name = desired;
        foreach (var forbidden in ForbiddenSheetNameChars)
            name = name.Replace(forbidden, ' ');

        name = name.Trim('\'', ' ');
        if (name.Length == 0)
            name = "Varaq";
        if (name.Length > 31)
            name = name[..31];

        if (used.Add(name))
            return name;

        for (var suffix = 2; suffix < 10000; suffix++)
        {
            var tail = $" ({suffix})";
            var candidate = name.Length + tail.Length > 31 ? name[..(31 - tail.Length)] + tail : name + tail;
            if (used.Add(candidate))
                return candidate;
        }

        return Guid.NewGuid().ToString("N")[..8];
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

    /// <summary>Bitta katakning yozilishi kerak bo'lgan mazmuni.</summary>
    /// <param name="Text">Katak matni.</param>
    /// <param name="StyleIndex">Uslub indeksi (0 — oddiy, 1 — sarlavha).</param>
    /// <param name="ForceText">Matn son bo'lsa ham matn sifatida yozilsinmi.</param>
    private readonly record struct CellContent(string Text, uint StyleIndex, bool ForceText);
}
