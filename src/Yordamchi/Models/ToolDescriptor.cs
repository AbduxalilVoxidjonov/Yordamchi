namespace Yordamchi.Models;

/// <summary>Dasturdagi har bir vosita (modul) uchun barqaror identifikator.</summary>
public enum ToolId
{
    // 1 — Sahifalar bilan ishlash
    Merge,
    Split,
    Organize,
    Rotate,

    // 2 — Konvertatsiya
    PdfToWord,
    WordToPdf,
    PdfToImage,
    ImageToPdf,
    PdfToExcel,
    PdfToPowerPoint,

    // 3 — Optimizatsiya va xavfsizlik
    Compress,
    Protect,
    Unlock,
    Watermark,
    PageNumbers,

    // 4 — Sun'iy intellekt
    OcrToWord,
    BackgroundRemover
}

/// <summary>Bosh sahifadagi kartochkalar guruhlanadigan bo'limlar.</summary>
public enum ToolCategory
{
    Pages,
    Convert,
    Optimize,
    Ai
}

/// <summary>
/// Vosita ishlashi uchun yetishmayotgan, lekin dastur o'zi internetdan olib bera oladigan
/// komponent. Ishchi oynadagi ogohlantirish paneli shu asosda "Yuklab olish" tugmasini
/// ko'rsatadi.
/// <para>
/// Microsoft Word bu ro'yxatga kirmaydi: u foydalanuvchi o'zi o'rnatadigan tashqi dastur,
/// va uni yuklab olib bo'lmaydi — u yerda faqat ogohlantirish matni qoladi.
/// </para>
/// </summary>
public enum DownloadableComponent
{
    /// <summary>Hamma narsa joyida yoki yetishmayotgan narsani yuklab olib bo'lmaydi.</summary>
    None,

    /// <summary>Tesseract til fayllari (<c>*.traineddata</c>).</summary>
    OcrLanguages,

    /// <summary>Fon olib tashlash uchun u2net ONNX modeli.</summary>
    AiModel
}

/// <summary>Vosita nima qabul qilishini bildiradi — ishchi oyna shu asosda quriladi.</summary>
public enum ToolInputKind
{
    /// <summary>Bitta PDF fayl.</summary>
    SinglePdf,
    /// <summary>Bir nechta PDF fayl (birlashtirish).</summary>
    MultiplePdf,
    /// <summary>Bir nechta rasm fayli.</summary>
    Images,
    /// <summary>Bitta Word hujjati (.docx / .doc).</summary>
    WordDocument
}

/// <summary>
/// Bosh sahifadagi kartochka va uning ishchi oynasi uchun barcha statik ma'lumot.
/// <para>
/// Bu ro'yxat — dasturning yagona "haqiqat manbai": yangi vosita qo'shish uchun shu yerga
/// bitta yozuv qo'shiladi, UI esa avtomatik ravishda kartochka va ishchi oynani hosil qiladi.
/// </para>
/// </summary>
/// <param name="Id">Vosita identifikatori.</param>
/// <param name="Title">Kartochkada ko'rinadigan nom.</param>
/// <param name="Description">Bir qatorli izoh.</param>
/// <param name="Glyph">Segoe Fluent Icons shriftidagi belgi kodi.</param>
/// <param name="Category">Bosh sahifadagi bo'lim.</param>
/// <param name="Input">Vosita qabul qiladigan fayl turi.</param>
/// <param name="AccentColor">Kartochka ikonasi foni (HEX).</param>
/// <param name="OutputExtension">Natija faylining kengaytmasi; papkaga yozadigan vositalarda <c>null</c>.</param>
public sealed record ToolDescriptor(
    ToolId Id,
    string Title,
    string Description,
    string Glyph,
    ToolCategory Category,
    ToolInputKind Input,
    string AccentColor,
    string? OutputExtension)
{
    /// <summary>Sahifa eskizlari ko'rsatiladigan vositalar (tartiblash, burish, bo'lish…).</summary>
    public bool ShowsPageThumbnails => Id is ToolId.Organize or ToolId.Rotate or ToolId.Split or ToolId.Merge;

    /// <summary>Natija bitta fayl emas, papka bo'lgan vositalar.</summary>
    public bool WritesToFolder => Id is ToolId.Split or ToolId.PdfToImage;

    public string CategoryTitle => Category switch
    {
        ToolCategory.Pages => "Sahifalar bilan ishlash",
        ToolCategory.Convert => "Konvertatsiya",
        ToolCategory.Optimize => "Optimizatsiya va xavfsizlik",
        ToolCategory.Ai => "Sun'iy intellekt",
        _ => "Boshqa"
    };
}

/// <summary>Dastur qo'llab-quvvatlaydigan barcha vositalar katalogi.</summary>
public static class ToolCatalog
{
    /// <summary>Bosh sahifadagi tartibda barcha vositalar.</summary>
    public static IReadOnlyList<ToolDescriptor> All { get; } =
    [
        new(ToolId.Merge, "PDF birlashtirish",
            "Bir nechta PDF faylni kerakli tartibda bitta hujjatga jamlang.",
            "\uE8F4", ToolCategory.Pages, ToolInputKind.MultiplePdf, "#E5484D", ".pdf"),

        new(ToolId.Split, "PDF bo'lish",
            "Hujjatni sahifalarga yoki belgilangan oraliqlarga ajrating.",
            "\uE8AB", ToolCategory.Pages, ToolInputKind.SinglePdf, "#E5484D", null),

        new(ToolId.Organize, "Sahifalarni tartiblash",
            "Sahifalarni sichqoncha bilan surib joyini almashtiring, keraksizini o'chiring.",
            "\uE71D", ToolCategory.Pages, ToolInputKind.SinglePdf, "#E5484D", ".pdf"),

        new(ToolId.Rotate, "Sahifalarni burish",
            "Barcha yoki tanlangan sahifalarni 90° ga buring.",
            "\uE7AD", ToolCategory.Pages, ToolInputKind.SinglePdf, "#E5484D", ".pdf"),

        new(ToolId.PdfToWord, "PDF → Word",
            "Matn, sarlavha va jadvallarni tahrirlanadigan .docx hujjatga aylantiring.",
            "\uE8A5", ToolCategory.Convert, ToolInputKind.SinglePdf, "#2B7FFF", ".docx"),

        new(ToolId.WordToPdf, "Word → PDF",
            "Shrift va formatlashni saqlagan holda .docx ni PDF ga o'tkazing.",
            "\uE8E5", ToolCategory.Convert, ToolInputKind.WordDocument, "#2B7FFF", ".pdf"),

        new(ToolId.PdfToImage, "PDF → Rasm",
            "Har bir sahifani JPG yoki PNG rasm sifatida saqlang.",
            "\uEB9F", ToolCategory.Convert, ToolInputKind.SinglePdf, "#2B7FFF", null),

        new(ToolId.ImageToPdf, "Rasm → PDF",
            "JPG, PNG va boshqa rasmlardan bitta PDF hujjat yig'ing.",
            "\uE91B", ToolCategory.Convert, ToolInputKind.Images, "#2B7FFF", ".pdf"),

        new(ToolId.PdfToExcel, "PDF → Excel",
            "Hujjatdagi jadvallarni .xlsx kitobiga chiqaring.",
            "\uE9F9", ToolCategory.Convert, ToolInputKind.SinglePdf, "#2B7FFF", ".xlsx"),

        new(ToolId.PdfToPowerPoint, "PDF → PowerPoint",
            "Har bir sahifadan matnli slayd tayyorlang.",
            "\uE7F4", ToolCategory.Convert, ToolInputKind.SinglePdf, "#2B7FFF", ".pptx"),

        new(ToolId.Compress, "PDF siqish",
            "Rasmlarni optimallashtirib fayl hajmini 30–70% ga kichraytiring.",
            "\uE8DE", ToolCategory.Optimize, ToolInputKind.SinglePdf, "#12A594", ".pdf"),

        new(ToolId.Protect, "PDF himoyalash",
            "Ochish uchun parol qo'ying va chop etish/nusxalashni cheklang.",
            "\uE72E", ToolCategory.Optimize, ToolInputKind.SinglePdf, "#12A594", ".pdf"),

        new(ToolId.Unlock, "Qulfni ochish",
            "Parol ma'lum bo'lsa, hujjatdan himoyani olib tashlang.",
            "\uE785", ToolCategory.Optimize, ToolInputKind.SinglePdf, "#12A594", ".pdf"),

        new(ToolId.Watermark, "Suv belgisi",
            "Har bir sahifaga matnli suv belgisi qo'shing.",
            "\uE7C1", ToolCategory.Optimize, ToolInputKind.SinglePdf, "#12A594", ".pdf"),

        new(ToolId.PageNumbers, "Sahifa raqamlari",
            "Sahifalarni tanlangan joyda avtomatik raqamlang.",
            "\uE8EF", ToolCategory.Optimize, ToolInputKind.SinglePdf, "#12A594", ".pdf"),

        new(ToolId.OcrToWord, "OCR: skaner → Word",
            "Skaner qilingan rasm-PDF dan matnni tanib olib Word ga yozing.",
            "\uE721", ToolCategory.Ai, ToolInputKind.SinglePdf, "#8E4EC6", ".docx"),

        // Belgi "Rasm -> PDF" kartochkasidagi rasm ikonasidan farq qilishi shart: ikkalasi bir
        // xil bo'lganda bosh sahifadagi kartochkalarni bir qarashda ajratib bo'lmaydi.
        new(ToolId.BackgroundRemover, "Orqa fonni olib tashlash",
            "AI yordamida rasmlar fonini bir soniyada shaffof qiling.",
            "\uE75C", ToolCategory.Ai, ToolInputKind.Images, "#8E4EC6", ".png")
    ];

    public static ToolDescriptor Get(ToolId id) => All.First(tool => tool.Id == id);
}
