namespace Yordamchi.Models;

// =====================================================================================
//  Bu fayl barcha modullar uchun "operatsiya sozlamalari" turlarini jamlaydi.
//  Ular oddiy POCO — hech qanday UI yoki kutubxonaga bog'liq emas, shuning uchun
//  servis qatlamini test qilish oson bo'ladi.
// =====================================================================================

// -------------------------------------------------------------------------------------
//  Bo'lish (Split)
// -------------------------------------------------------------------------------------

/// <summary>PDF qanday bo'linishini belgilaydi.</summary>
public enum SplitMode
{
    /// <summary>Har bir sahifa alohida fayl bo'ladi.</summary>
    EveryPage,
    /// <summary>Foydalanuvchi kiritgan oraliqlar (masalan <c>1-3, 5, 8-10</c>).</summary>
    Ranges,
    /// <summary>Har <see cref="SplitOptions.PagesPerFile"/> sahifadan bitta fayl.</summary>
    FixedChunks
}

/// <summary>Bo'lish operatsiyasi sozlamalari.</summary>
public sealed class SplitOptions
{
    public SplitMode Mode { get; set; } = SplitMode.EveryPage;

    /// <summary>Foydalanuvchi kiritgan oraliqlar matni, masalan <c>"1-3, 7, 10-12"</c>.</summary>
    public string RangeExpression { get; set; } = string.Empty;

    /// <summary><see cref="SplitMode.FixedChunks"/> uchun bitta fayldagi sahifalar soni.</summary>
    public int PagesPerFile { get; set; } = 10;

    /// <summary>Natija fayllari nomi uchun old qo'shimcha; bo'sh bo'lsa manba fayl nomi olinadi.</summary>
    public string? FileNamePrefix { get; set; }

    public static SplitOptions Default => new();
}

// -------------------------------------------------------------------------------------
//  Burish (Rotate)
// -------------------------------------------------------------------------------------

/// <summary>
/// "Sahifalarni burish" vositasi sozlamalari.
/// <para>
/// Eskizlar yuklangan bo'lsa burilish <c>ToolRequest.PagePlan</c> ichida keladi va bu obyekt
/// faqat foydalanuvchi tanlovini hujjatlashtiradi. Eskizsiz (juda katta hujjat) holatda esa
/// dvigatel aynan shu yerdagi burchakni ishlatadi — shuning uchun u model qatlamida turadi.
/// </para>
/// </summary>
/// <param name="Degrees">90 ga karrali burchak: 90, 180 yoki 270.</param>
/// <param name="ApplyToAll">Amal barcha sahifalarga qo'llanadimi (aks holda faqat tanlanganlarga).</param>
public sealed record RotateRequest(int Degrees, bool ApplyToAll = true);

// -------------------------------------------------------------------------------------
//  Siqish (Compress)
// -------------------------------------------------------------------------------------

/// <summary>Siqish darajasi — rasm rezolyutsiyasi va JPEG sifatini belgilaydi.</summary>
public enum CompressionLevel
{
    /// <summary>Sifatni deyarli yo'qotmaydi (~150 dpi, sifat 82). Odatda 20–40% yutuq.</summary>
    Low,
    /// <summary>Muvozanatli rejim (~120 dpi, sifat 72). Odatda 40–60% yutuq.</summary>
    Medium,
    /// <summary>Maksimal siqish (~96 dpi, sifat 60). Odatda 55–75% yutuq.</summary>
    High
}

/// <summary>Siqish parametrlari; <see cref="CompressionLevel"/> dan hosil qilinadi.</summary>
public sealed class CompressionProfile
{
    public required int TargetDpi { get; init; }

    public required int JpegQuality { get; init; }

    /// <summary>Shu piksel sonidan kichik rasmlar umuman qayta kodlanmaydi.</summary>
    public int MinimumPixelsToTouch { get; init; } = 64 * 64;

    /// <summary>Hujjat metama'lumotlari (muallif, ishlab chiqaruvchi, XMP) tozalansinmi.</summary>
    public bool StripMetadata { get; init; } = true;

    public static CompressionProfile From(CompressionLevel level) => level switch
    {
        CompressionLevel.Low => new CompressionProfile { TargetDpi = 150, JpegQuality = 82, StripMetadata = false },
        CompressionLevel.High => new CompressionProfile { TargetDpi = 96, JpegQuality = 60 },
        _ => new CompressionProfile { TargetDpi = 120, JpegQuality = 72 }
    };

    public static string Describe(CompressionLevel level) => level switch
    {
        CompressionLevel.Low => "Kam siqish — sifat deyarli o'zgarmaydi",
        CompressionLevel.High => "Kuchli siqish — eng kichik hajm",
        _ => "O'rtacha — hajm va sifat muvozanati"
    };
}

// -------------------------------------------------------------------------------------
//  Himoyalash (Protect)
// -------------------------------------------------------------------------------------

/// <summary>Himoyalangan hujjatda nimalarga ruxsat berilishi.</summary>
public sealed class PdfPermissions
{
    public bool AllowPrinting { get; set; } = true;

    /// <summary>Yuqori sifatli chop etish (past sifatlisi <see cref="AllowPrinting"/> bilan boshqariladi).</summary>
    public bool AllowHighQualityPrinting { get; set; } = true;

    /// <summary>Matn va rasmlarni nusxalash (Ctrl+C).</summary>
    public bool AllowCopying { get; set; }

    public bool AllowModifying { get; set; }

    public bool AllowAnnotations { get; set; }

    public bool AllowFormFilling { get; set; } = true;

    /// <summary>Sahifalarni qo'shish/o'chirish/burish.</summary>
    public bool AllowAssembly { get; set; }
}

/// <summary>Parol qo'yish sozlamalari.</summary>
public sealed class ProtectOptions
{
    /// <summary>Hujjatni ochish uchun parol. Bo'sh bo'lsa hujjat parolsiz ochiladi.</summary>
    public string UserPassword { get; set; } = string.Empty;

    /// <summary>Cheklovlarni o'zgartirish uchun egalik paroli; bo'sh bo'lsa foydalanuvchi paroli ishlatiladi.</summary>
    public string OwnerPassword { get; set; } = string.Empty;

    public PdfPermissions Permissions { get; set; } = new();

    /// <summary>AES-256 (PDF 2.0) ishlatilsinmi; <c>false</c> bo'lsa AES-128 (keng mos keladi).</summary>
    public bool UseAes256 { get; set; } = true;
}

// -------------------------------------------------------------------------------------
//  Suv belgisi (Watermark)
// -------------------------------------------------------------------------------------

public enum WatermarkPosition
{
    Center,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    /// <summary>Butun sahifa bo'ylab takrorlanuvchi naqsh.</summary>
    Tiled
}

/// <summary>Matnli suv belgisi sozlamalari.</summary>
public sealed class WatermarkOptions
{
    public string Text { get; set; } = "MAXFIY";

    public string FontFamily { get; set; } = "Arial";

    public double FontSize { get; set; } = 48d;

    /// <summary>0.05 … 1.0 oralig'idagi shaffoflik.</summary>
    public double Opacity { get; set; } = 0.25d;

    /// <summary>Soat strelkasiga teskari burilish burchagi (gradus).</summary>
    public double RotationDegrees { get; set; } = 45d;

    /// <summary>#RRGGBB ko'rinishidagi rang.</summary>
    public string ColorHex { get; set; } = "#E5484D";

    public WatermarkPosition Position { get; set; } = WatermarkPosition.Center;

    /// <summary>Suv belgisi sahifa mazmuni ustidan chizilsinmi (aks holda ostidan).</summary>
    public bool DrawOnTop { get; set; } = true;
}

// -------------------------------------------------------------------------------------
//  Sahifa raqamlari
// -------------------------------------------------------------------------------------

public enum PageNumberPosition
{
    BottomCenter,
    BottomLeft,
    BottomRight,
    TopCenter,
    TopLeft,
    TopRight
}

/// <summary>Sahifa raqamlarini qo'shish sozlamalari.</summary>
public sealed class PageNumberOptions
{
    public PageNumberPosition Position { get; set; } = PageNumberPosition.BottomCenter;

    /// <summary><c>{0}</c> — joriy raqam, <c>{1}</c> — jami sahifalar. Masalan <c>"{0} / {1}"</c>.</summary>
    public string Format { get; set; } = "{0}";

    /// <summary>Birinchi raqamlanadigan sahifadagi son.</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>Boshidagi shuncha sahifa raqamlanmaydi (masalan muqova).</summary>
    public int SkipFirstPages { get; set; }

    public double FontSize { get; set; } = 10d;

    public string FontFamily { get; set; } = "Arial";

    public string ColorHex { get; set; } = "#404040";

    /// <summary>Sahifa chekkasidan masofa (punkt).</summary>
    public double MarginPoints { get; set; } = 28d;
}

// -------------------------------------------------------------------------------------
//  PDF -> rasm
// -------------------------------------------------------------------------------------

public enum ImageOutputFormat
{
    Png,
    Jpeg,
    Webp
}

/// <summary>PDF sahifalarini rasmga chiqarish sozlamalari.</summary>
public sealed class PdfToImageOptions
{
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Png;

    /// <summary>Chiqish rezolyutsiyasi; 72 dpi = original o'lcham.</summary>
    public int Dpi { get; set; } = 150;

    public int JpegQuality { get; set; } = 90;

    /// <summary>Shaffof fon o'rniga oq fon chizilsinmi (JPEG uchun majburiy).</summary>
    public bool WhiteBackground { get; set; } = true;

    public static PdfToImageOptions Default => new();

    public string Extension => Format switch
    {
        ImageOutputFormat.Jpeg => ".jpg",
        ImageOutputFormat.Webp => ".webp",
        _ => ".png"
    };
}
