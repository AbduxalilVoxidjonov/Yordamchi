namespace Yordamchi.Models;

// =====================================================================================
//  Hujjat konvertatsiyasi (PDF <-> Word / Excel / PowerPoint) sozlamalari.
// =====================================================================================

/// <summary>PDF dan matn qanday olinishini belgilaydi.</summary>
public enum TextRecognitionMode
{
    /// <summary>
    /// Avval haqiqiy matn qatlami o'qiladi; sahifada matn topilmasa (skaner qilingan hujjat)
    /// o'sha sahifa uchun OCR ishga tushadi. Kundalik foydalanish uchun eng to'g'ri rejim.
    /// </summary>
    Automatic,

    /// <summary>Faqat PDF ichidagi matn qatlami o'qiladi; OCR umuman ishlatilmaydi.</summary>
    TextLayerOnly,

    /// <summary>Har bir sahifa rasm sifatida OCR qilinadi (matn qatlami bo'lsa ham).</summary>
    ForceOcr
}

/// <summary>Word hujjatida matn qanday joylashtirilishi.</summary>
public enum DocumentLayoutMode
{
    /// <summary>
    /// Oqim (flowing) rejimi — abzas va jadvallar oddiy Word obyektlari sifatida yoziladi.
    /// Tahrirlash uchun eng qulay va tavsiya etiladigan rejim.
    /// </summary>
    Flowing,

    /// <summary>
    /// Aniq joylashuv — har bir matn qatori sahifadagi koordinatasi bo'yicha ramkaga joylanadi.
    /// Ko'rinish PDF ga juda yaqin bo'ladi, lekin tahrirlash biroz qiyinroq.
    /// </summary>
    Exact
}

/// <summary>PDF → Word konvertatsiyasi sozlamalari.</summary>
public sealed class PdfToWordOptions
{
    public TextRecognitionMode Recognition { get; set; } = TextRecognitionMode.Automatic;

    public DocumentLayoutMode Layout { get; set; } = DocumentLayoutMode.Flowing;

    /// <summary>Ustun oralig'i bo'yicha jadvallarni aniqlashga urinilsinmi.</summary>
    public bool DetectTables { get; set; } = true;

    /// <summary>Katta shriftli qatorlar sarlavha (Heading 1/2) sifatida belgilansinmi.</summary>
    public bool DetectHeadings { get; set; } = true;

    /// <summary>PDF ichidagi rasmlar ham Word hujjatiga ko'chirilsinmi.</summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>Har bir PDF sahifasidan keyin Word'da sahifa uzilishi qo'yilsinmi.</summary>
    public bool InsertPageBreaks { get; set; } = true;

    /// <summary>OCR tillari, Tesseract formatida: <c>uzb+eng+rus</c>.</summary>
    public string OcrLanguage { get; set; } = OcrOptions.DefaultLanguage;

    /// <summary>
    /// Sahifada shundan kam "haqiqiy" belgi bo'lsa, sahifa skaner qilingan deb hisoblanadi
    /// (<see cref="TextRecognitionMode.Automatic"/> rejimida).
    /// </summary>
    public int MinimumCharactersPerPage { get; set; } = 24;

    public static PdfToWordOptions Default => new();
}

/// <summary>Word → PDF konvertatsiyasida qaysi dvigatel ishlatilishi.</summary>
public enum WordToPdfEngine
{
    /// <summary>Microsoft Word o'rnatilgan bo'lsa — o'sha, aks holda ichki renderer.</summary>
    Automatic,

    /// <summary>Faqat Microsoft Word (COM). Eng yuqori aniqlik, lekin Office talab qiladi.</summary>
    MicrosoftWord,

    /// <summary>Faqat dasturga o'rnatilgan OpenXML → PDF renderer. Office talab qilmaydi.</summary>
    Builtin
}

/// <summary>Word → PDF sozlamalari.</summary>
public sealed class WordToPdfOptions
{
    public WordToPdfEngine Engine { get; set; } = WordToPdfEngine.Automatic;

    /// <summary>
    /// PDF ichiga shriftlar joylashtirilsinmi.
    /// <para>
    /// Amalda doim <c>true</c>: PDFsharp 6 da joylashtirmaslik rejimi olib tashlangan, Word COM
    /// esa buni o'zi hal qiladi. Maydon shartnomani buzmaslik uchun qoldirilgan va UI da
    /// tahrirlanmaydi (<c>WordToPdfOptionsViewModel.IsEmbedFontsAdjustable</c>).
    /// </para>
    /// </summary>
    public bool EmbedFonts { get; set; } = true;

    /// <summary>Word sarlavhalaridan PDF xatcho'plari yasalsinmi (faqat Word dvigateli).</summary>
    public bool CreateBookmarks { get; set; } = true;

    public static WordToPdfOptions Default => new();
}

/// <summary>PDF → Excel sozlamalari.</summary>
public sealed class PdfToExcelOptions
{
    /// <summary>Har bir PDF sahifasi alohida varaq bo'lsinmi.</summary>
    public bool SheetPerPage { get; set; } = true;

    /// <summary>Jadval topilmagan sahifalarda matn qatorlari ham yozilsinmi.</summary>
    public bool IncludePlainText { get; set; } = true;

    public string OcrLanguage { get; set; } = OcrOptions.DefaultLanguage;

    public TextRecognitionMode Recognition { get; set; } = TextRecognitionMode.Automatic;

    public static PdfToExcelOptions Default => new();
}

/// <summary>PDF → PowerPoint sozlamalari.</summary>
public sealed class PdfToPowerPointOptions
{
    /// <summary>Har bir slaydda sahifaning rasm ko'rinishi fon sifatida qo'yilsinmi.</summary>
    public bool IncludePageImage { get; set; }

    /// <summary>Sahifadagi birinchi yirik qator slayd sarlavhasi bo'lsinmi.</summary>
    public bool FirstLineAsTitle { get; set; } = true;

    public TextRecognitionMode Recognition { get; set; } = TextRecognitionMode.Automatic;

    public string OcrLanguage { get; set; } = OcrOptions.DefaultLanguage;

    public static PdfToPowerPointOptions Default => new();
}

/// <summary>OCR sozlamalari.</summary>
public sealed class OcrOptions
{
    /// <summary>O'zbek, ingliz va rus tillari — O'zbekistondagi hujjatlar uchun odatiy to'plam.</summary>
    public const string DefaultLanguage = "uzb+eng+rus";

    public string Language { get; set; } = DefaultLanguage;

    /// <summary>Sahifa qanday rezolyutsiyada rasterizatsiya qilinadi. 300 dpi — OCR uchun oltin standart.</summary>
    public int Dpi { get; set; } = 300;

    /// <summary>Qatorlar orasidagi abzaslarni ajratishga urinilsinmi.</summary>
    public bool DetectParagraphs { get; set; } = true;

    /// <summary>Rasmni OCR dan oldin kulrangga o'tkazib kontrastini oshirish.</summary>
    public bool Preprocess { get; set; } = true;

    /// <summary>Ishonchi shu foizdan past bo'lgan so'zlar ham yoziladi, lekin hisobotda belgilanadi.</summary>
    public float MinimumConfidence { get; set; } = 45f;

    public static OcrOptions Default => new();

    /// <summary>UI da ko'rsatiladigan tillar ro'yxati.</summary>
    public static IReadOnlyList<(string Code, string Title)> AvailableLanguages { get; } =
    [
        ("uzb+eng+rus", "O'zbek + Ingliz + Rus"),
        ("uzb", "O'zbek (lotin)"),
        ("uzb_cyrl", "O'zbek (kirill)"),
        ("eng", "Ingliz"),
        ("rus", "Rus"),
        ("uzb+eng", "O'zbek + Ingliz"),
        ("rus+eng", "Rus + Ingliz")
    ];
}

/// <summary>AI bilan fon olib tashlash sozlamalari.</summary>
public sealed class BackgroundRemovalOptions
{
    /// <summary>Model kirishining o'lchami (u2net va u2netp uchun 320x320).</summary>
    public int ModelInputSize { get; set; } = 320;

    /// <summary>
    /// Maska chegarasi: shundan past alfa qiymatlari to'liq shaffof qilinadi.
    /// 0 bo'lsa yumshoq (soft) chekka saqlanadi — sochlar uchun yaxshiroq.
    /// </summary>
    public byte AlphaThreshold { get; set; }

    /// <summary>Maska chekkasini yumshatish radiusi (piksel); 0 — o'chirilgan.</summary>
    public float FeatherRadius { get; set; } = 1.0f;

    /// <summary>Natijada obyekt atrofidagi bo'sh joy kesib tashlansinmi.</summary>
    public bool TrimTransparentBorder { get; set; }

    public static BackgroundRemovalOptions Default => new();
}
