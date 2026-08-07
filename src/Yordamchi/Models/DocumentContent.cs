namespace Yordamchi.Models;

// =====================================================================================
//  Konvertorlarning oraliq (intermediate) hujjat modeli.
//
//  PDF dan o'qish (PdfPig / OCR) va Word/Excel/PowerPoint ga yozish (OpenXML) shu model
//  orqali bog'lanadi. Natijada har bir chiquvchi format uchun alohida PDF tahlilchisi
//  yozish shart emas: "o'qish" va "yozish" bir-biridan mustaqil qoladi.
// =====================================================================================

/// <summary>Bitta matn bo'lagi va uning ko'rinishi.</summary>
/// <param name="Text">Matn.</param>
/// <param name="FontFamily">Shrift oilasi nomi (masalan <c>Times New Roman</c>).</param>
/// <param name="FontSize">O'lcham, punktda.</param>
/// <param name="IsBold">Qalin.</param>
/// <param name="IsItalic">Kursiv.</param>
/// <param name="ColorHex">#RRGGBB rangi; <c>null</c> bo'lsa qora.</param>
public sealed record TextRun(
    string Text,
    string FontFamily = "Calibri",
    double FontSize = 11d,
    bool IsBold = false,
    bool IsItalic = false,
    string? ColorHex = null);

/// <summary>Abzasning gorizontal tekislanishi.</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}

/// <summary>Blok turi — yozuvchi shunga qarab uslub tanlaydi.</summary>
public enum BlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    /// <summary>Ro'yxat elementi (• yoki raqamli).</summary>
    ListItem
}

/// <summary>Hujjat blokining umumiy asosi.</summary>
public abstract class ContentBlock
{
    /// <summary>Sahifadagi joylashuv (PDF punktlarida, chap-yuqori burchakdan).</summary>
    public double Left { get; set; }

    /// <summary>Sahifa tepasidan masofa (PDF punktlarida).</summary>
    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

/// <summary>Bir yoki bir nechta <see cref="TextRun"/> dan iborat abzas.</summary>
public sealed class ParagraphBlock : ContentBlock
{
    public List<TextRun> Runs { get; } = [];

    public BlockKind Kind { get; set; } = BlockKind.Paragraph;

    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>Chap chekinish (punkt) — Word'da <c>w:ind</c> ga aylanadi.</summary>
    public double IndentPoints { get; set; }

    /// <summary>Abzasdan keyingi bo'sh joy (punkt).</summary>
    public double SpaceAfterPoints { get; set; } = 6d;

    public string Text => string.Concat(Runs.Select(run => run.Text));

    public bool IsEmpty => Runs.Count == 0 || string.IsNullOrWhiteSpace(Text);

    public static ParagraphBlock FromText(string text, BlockKind kind = BlockKind.Paragraph)
    {
        var block = new ParagraphBlock { Kind = kind };
        block.Runs.Add(new TextRun(text));
        return block;
    }
}

/// <summary>Jadval katakchasi.</summary>
public sealed class TableCell
{
    public List<TextRun> Runs { get; } = [];

    /// <summary>Katakcha nechta ustunni egallaydi.</summary>
    public int ColumnSpan { get; set; } = 1;

    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    public string Text => string.Concat(Runs.Select(run => run.Text));

    public static TableCell FromText(string text)
    {
        var cell = new TableCell();
        if (!string.IsNullOrEmpty(text))
            cell.Runs.Add(new TextRun(text));
        return cell;
    }
}

/// <summary>Jadval qatori.</summary>
public sealed class TableRow
{
    public List<TableCell> Cells { get; } = [];

    /// <summary>Sarlavha qatori (qalin va soyali fon bilan yoziladi).</summary>
    public bool IsHeader { get; set; }
}

/// <summary>Aniqlangan jadval.</summary>
public sealed class TableBlock : ContentBlock
{
    public List<TableRow> Rows { get; } = [];

    /// <summary>Ustunlarning nisbiy kengliklari (yig'indisi 1.0 bo'lishi shart emas).</summary>
    public List<double> ColumnWidths { get; } = [];

    public bool HasBorders { get; set; } = true;

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Cells.Sum(cell => cell.ColumnSpan));
}

/// <summary>Hujjatga joylashtiriladigan rasm.</summary>
public sealed class ImageBlock : ContentBlock
{
    /// <summary>PNG yoki JPEG ko'rinishidagi tayyor baytlar.</summary>
    public required byte[] Data { get; init; }

    /// <summary>MIME turi, masalan <c>image/png</c>.</summary>
    public string ContentType { get; init; } = "image/png";

    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }
}

/// <summary>Bitta manba sahifasidan olingan bloklar.</summary>
public sealed class ContentPage
{
    /// <summary>1 dan boshlanadigan sahifa raqami.</summary>
    public int Number { get; set; }

    /// <summary>Sahifa kengligi (punkt).</summary>
    public double WidthPoints { get; set; } = 595d;

    /// <summary>Sahifa balandligi (punkt).</summary>
    public double HeightPoints { get; set; } = 842d;

    public double MarginLeftPoints { get; set; } = 56d;

    public double MarginTopPoints { get; set; } = 56d;

    public List<ContentBlock> Blocks { get; } = [];

    /// <summary>Sahifa OCR orqali o'qilganmi.</summary>
    public bool WasRecognized { get; set; }

    /// <summary>OCR ishonch darajasi (0..100); matn qatlamidan o'qilgan bo'lsa 100.</summary>
    public float Confidence { get; set; } = 100f;

    public bool IsLandscape => WidthPoints > HeightPoints;

    public int CharacterCount => Blocks.OfType<ParagraphBlock>().Sum(block => block.Text.Length)
        + Blocks.OfType<TableBlock>().Sum(table => table.Rows.Sum(row => row.Cells.Sum(cell => cell.Text.Length)));
}

/// <summary>Konvertatsiya uchun to'liq hujjat.</summary>
public sealed class DocumentContent
{
    public List<ContentPage> Pages { get; } = [];

    public string? Title { get; set; }

    public string? Author { get; set; }

    /// <summary>Manba fayl yo'li — xatoliklarni tushunarli qilib ko'rsatish uchun.</summary>
    public string? SourcePath { get; set; }

    /// <summary>Kamida bitta sahifa OCR orqali o'qilganmi.</summary>
    public bool UsedOcr => Pages.Any(page => page.WasRecognized);

    public int TotalCharacters => Pages.Sum(page => page.CharacterCount);
}
