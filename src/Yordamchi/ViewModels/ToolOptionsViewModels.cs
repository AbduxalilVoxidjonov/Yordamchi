using CommunityToolkit.Mvvm.ComponentModel;
using Yordamchi.Models;

namespace Yordamchi.ViewModels;

// =====================================================================================
//  Har bir vositaning sozlamalar paneli uchun bittadan kichik ViewModel.
//
//  Qoida: bu yerdagi sinflar UI holatini saqlaydi, <see cref="ToolOptionsViewModel.ToModel"/>
//  esa uni servis qatlami tushunadigan oddiy POCO ga aylantiradi. Shu tufayli
//  ToolWorkspaceViewModel vositalar tafsilotini bilmaydi: u faqat ToModel() ni chaqiradi.
//
//  Ko'rinishi (DataTemplate) — Views/ToolOptionTemplates.xaml faylida.
// =====================================================================================

/// <summary>
/// <c>ComboBox</c> uchun universal element: ko'rinadigan nom + haqiqiy qiymat.
/// <para>
/// XAML da <c>DisplayMemberPath="Title"</c> va <c>SelectedValuePath="Value"</c> bilan
/// ishlatiladi, shuning uchun VM tomonda "tanlangan element" xossasini takrorlash shart emas.
/// </para>
/// </summary>
/// <param name="Title">Ro'yxatda ko'rinadigan matn.</param>
/// <param name="Value">Bog'lanadigan qiymat (enum, matn yoki son).</param>
/// <param name="Hint">Ixtiyoriy qo'shimcha izoh.</param>
public sealed record ToolChoice(string Title, object Value, string? Hint = null);

/// <summary>Barcha sozlama ViewModel lari uchun umumiy asos.</summary>
public abstract class ToolOptionsViewModel : ObservableObject
{
    /// <summary>
    /// Sozlamalarni servis qatlami kutayotgan model obyektiga aylantiradi.
    /// Vosita qo'shimcha sozlamasiz ishlasa <c>null</c> qaytarilishi mumkin.
    /// </summary>
    public abstract object? ToModel();

    /// <summary>
    /// Faqat UI da ma'noga ega tekshiruvlar (masalan "parolni tasdiqlash" maydoni modelga
    /// tushmaydi, shuning uchun dvigatel uni ko'ra olmaydi). Muammo bo'lsa — tushunarli matn,
    /// aks holda <c>null</c>.
    /// </summary>
    public virtual string? Validate() => null;
}

// -------------------------------------------------------------------------------------
//  PDF bo'lish
// -------------------------------------------------------------------------------------

/// <summary>"PDF bo'lish" vositasi sozlamalari.</summary>
public sealed partial class SplitOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private SplitMode _mode = SplitMode.EveryPage;

    /// <summary>Foydalanuvchi kiritgan oraliqlar: <c>1-3, 7, 10-12</c>.</summary>
    [ObservableProperty]
    private string _rangeExpression = string.Empty;

    /// <summary>Teng bo'laklarga bo'lishda bitta fayldagi sahifalar soni.</summary>
    [ObservableProperty]
    private int _pagesPerFile = 10;

    public override object ToModel() => new SplitOptions
    {
        Mode = Mode,
        RangeExpression = RangeExpression,
        PagesPerFile = Math.Max(1, PagesPerFile)
    };
}

// -------------------------------------------------------------------------------------
//  Siqish
// -------------------------------------------------------------------------------------

/// <summary>"PDF siqish" vositasi sozlamalari — uchta tayyor daraja.</summary>
public sealed partial class CompressOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private CompressionLevel _level = CompressionLevel.Medium;

    public string LowHint => CompressionProfile.Describe(CompressionLevel.Low);

    public string MediumHint => CompressionProfile.Describe(CompressionLevel.Medium);

    public string HighHint => CompressionProfile.Describe(CompressionLevel.High);

    // Daraja enum ning o'zi uzatiladi: servis undan CompressionProfile ni yasab oladi.
    public override object ToModel() => Level;
}

// -------------------------------------------------------------------------------------
//  Himoyalash / qulfni ochish
// -------------------------------------------------------------------------------------

/// <summary>
/// "PDF himoyalash" sozlamalari.
/// <para>
/// Parol maydoni ataylab oddiy <c>TextBox</c>: WPF ning <c>PasswordBox</c> ida
/// <c>Password</c> xossasi bog'lanmaydigan (dependency property emas) qilib yozilgan, uni
/// MVVM ga ulash uchun esa code-behind yoki qo'shimcha behavior kerak bo'ladi.
/// ResourceDictionary da code-behind yo'q, shuning uchun parol ochiq ko'rinadi va bu haqda
/// panelning o'zida ogohlantirish beriladi.
/// </para>
/// </summary>
public sealed partial class ProtectOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _userPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordsMatch))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _confirmPassword = string.Empty;

    /// <summary>Cheklovlarni o'zgartirish uchun egalik paroli; bo'sh bo'lsa foydalanuvchi paroli ishlatiladi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _ownerPassword = string.Empty;

    [ObservableProperty]
    private bool _allowPrinting = true;

    [ObservableProperty]
    private bool _allowHighQualityPrinting = true;

    [ObservableProperty]
    private bool _allowCopying;

    [ObservableProperty]
    private bool _allowModifying;

    [ObservableProperty]
    private bool _allowAnnotations;

    [ObservableProperty]
    private bool _allowFormFilling = true;

    [ObservableProperty]
    private bool _allowAssembly;

    /// <summary>AES-256 (PDF 2.0); o'chirilsa AES-128 ishlatiladi — u eski dasturlarda ham ochiladi.</summary>
    [ObservableProperty]
    private bool _useAes256 = true;

    public bool PasswordsMatch => string.Equals(UserPassword, ConfirmPassword, StringComparison.Ordinal);

    /// <summary>
    /// Panel ostidagi sariq izoh. Ikki xil bo'ladi: bajarishga to'sqinlik qiladigan xato
    /// (<see cref="Validate"/> bilan bir xil matn) yoki shunchaki ogohlantirish.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            var blocking = Validate();
            if (blocking is not null)
                return blocking;

            // Ochish paroli yo'q, lekin egalik paroli bor: hujjat erkin ochiladi va faqat
            // cheklovlar amal qiladi — bu haqiqiy va foydali holat, ogohlantirish yetarli.
            return string.IsNullOrEmpty(UserPassword)
                ? "Ochish paroli kiritilmagan — hujjat parolsiz ochiladi, faqat cheklovlar amal qiladi."
                : string.Empty;
        }
    }

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    /// <inheritdoc />
    public override string? Validate()
    {
        // Ruxsatlarni PDF ga yozish uchun kamida bitta parol kerak: parolsiz hujjatda
        // cheklovlarni saqlab bo'lmaydi (shifrlash kaliti bo'lmaydi).
        if (string.IsNullOrEmpty(UserPassword) && string.IsNullOrEmpty(OwnerPassword))
            return "Parol kiriting: ochish paroli yoki (faqat cheklovlar uchun) egalik paroli.";

        // Tasdiqlash maydoni model obyektiga tushmaydi, shuning uchun uni faqat shu yerda
        // tekshirish mumkin — aks holda xato parol jimgina saqlanib ketardi.
        if (!string.IsNullOrEmpty(UserPassword) && !PasswordsMatch)
            return "Parollar mos kelmadi.";

        return null;
    }

    public override object ToModel() => new ProtectOptions
    {
        UserPassword = UserPassword,
        OwnerPassword = OwnerPassword,
        UseAes256 = UseAes256,
        Permissions = new PdfPermissions
        {
            AllowPrinting = AllowPrinting,
            AllowHighQualityPrinting = AllowHighQualityPrinting,
            AllowCopying = AllowCopying,
            AllowModifying = AllowModifying,
            AllowAnnotations = AllowAnnotations,
            AllowFormFilling = AllowFormFilling,
            AllowAssembly = AllowAssembly
        }
    };
}

/// <summary>"Qulfni ochish" sozlamasi — faqat joriy parol.</summary>
public sealed partial class UnlockOptionsViewModel : ToolOptionsViewModel
{
    /// <summary>Hujjatni ochish uchun ma'lum bo'lgan parol.</summary>
    [ObservableProperty]
    private string _password = string.Empty;

    // Bu vositada model obyekti yo'q: parol ToolRequest.Password maydoniga alohida uzatiladi.
    public override object? ToModel() => null;
}

// -------------------------------------------------------------------------------------
//  Suv belgisi
// -------------------------------------------------------------------------------------

/// <summary>"Suv belgisi" vositasi sozlamalari.</summary>
public sealed partial class WatermarkOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private string _text = "MAXFIY";

    [ObservableProperty]
    private double _fontSize = 48d;

    /// <summary>0.05 … 1.0 — slayder shu oraliqda ishlaydi.</summary>
    [ObservableProperty]
    private double _opacity = 0.25d;

    [ObservableProperty]
    private double _rotationDegrees = 45d;

    [ObservableProperty]
    private string _colorHex = "#E5484D";

    [ObservableProperty]
    private WatermarkPosition _position = WatermarkPosition.Center;

    /// <summary>Suv belgisi sahifa mazmuni ustidan chizilsinmi.</summary>
    [ObservableProperty]
    private bool _drawOnTop = true;

    public IReadOnlyList<ToolChoice> ColorChoices { get; } =
    [
        new("Qizil", "#E5484D"),
        new("Ko'k", "#2B7FFF"),
        new("Yashil", "#12A594"),
        new("Binafsha", "#8E4EC6"),
        new("Kulrang", "#8B8B8B"),
        new("Qora", "#202020")
    ];

    public IReadOnlyList<ToolChoice> PositionChoices { get; } =
    [
        new("Markazda", WatermarkPosition.Center),
        new("Yuqori chapda", WatermarkPosition.TopLeft),
        new("Yuqori o'ngda", WatermarkPosition.TopRight),
        new("Quyi chapda", WatermarkPosition.BottomLeft),
        new("Quyi o'ngda", WatermarkPosition.BottomRight),
        new("Butun sahifa bo'ylab", WatermarkPosition.Tiled)
    ];

    public override object ToModel() => new WatermarkOptions
    {
        Text = Text,
        FontSize = FontSize,
        Opacity = Math.Clamp(Opacity, 0.05d, 1d),
        RotationDegrees = RotationDegrees,
        ColorHex = ColorHex,
        Position = Position,
        DrawOnTop = DrawOnTop
    };
}

// -------------------------------------------------------------------------------------
//  Sahifa raqamlari
// -------------------------------------------------------------------------------------

/// <summary>"Sahifa raqamlari" vositasi sozlamalari.</summary>
public sealed partial class PageNumberOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private PageNumberPosition _position = PageNumberPosition.BottomCenter;

    /// <summary><c>{0}</c> — joriy raqam, <c>{1}</c> — jami sahifalar.</summary>
    [ObservableProperty]
    private string _format = "{0}";

    [ObservableProperty]
    private int _startNumber = 1;

    /// <summary>Boshidagi shuncha sahifa raqamlanmaydi (masalan muqova).</summary>
    [ObservableProperty]
    private int _skipFirstPages;

    [ObservableProperty]
    private double _fontSize = 10d;

    public IReadOnlyList<ToolChoice> PositionChoices { get; } =
    [
        new("Pastda, markazda", PageNumberPosition.BottomCenter),
        new("Pastda, chapda", PageNumberPosition.BottomLeft),
        new("Pastda, o'ngda", PageNumberPosition.BottomRight),
        new("Yuqorida, markazda", PageNumberPosition.TopCenter),
        new("Yuqorida, chapda", PageNumberPosition.TopLeft),
        new("Yuqorida, o'ngda", PageNumberPosition.TopRight)
    ];

    public IReadOnlyList<ToolChoice> FormatChoices { get; } =
    [
        new("1, 2, 3", "{0}"),
        new("1 / 12", "{0} / {1}"),
        new("- 1 -", "- {0} -"),
        new("Sahifa 1", "Sahifa {0}"),
        new("Sahifa 1 dan 12", "Sahifa {0} dan {1}")
    ];

    public override object ToModel() => new PageNumberOptions
    {
        Position = Position,
        Format = string.IsNullOrWhiteSpace(Format) ? "{0}" : Format,
        StartNumber = StartNumber,
        SkipFirstPages = Math.Max(0, SkipFirstPages),
        FontSize = FontSize
    };
}

// -------------------------------------------------------------------------------------
//  Burish
// -------------------------------------------------------------------------------------

/// <summary>
/// "Sahifalarni burish" sozlamalari.
/// <para>
/// Eskizlar yuklangan bo'lsa burilish sahifa rejasi (<c>PagePlan</c>) orqali uzatiladi, lekin
/// juda katta hujjatda eskizlar bo'lmasligi mumkin — o'shanda dvigatel aynan shu yerdagi
/// <see cref="RotateRequest"/> ni ishlatadi. Shuning uchun <see cref="ToModel"/> haqiqiy obyekt
/// qaytaradi: aks holda foydalanuvchining 180°/270° tanlovi yo'qolib, doim 90° qo'llanardi.
/// </para>
/// </summary>
public sealed partial class RotateOptionsViewModel : ToolOptionsViewModel
{
    /// <summary>Bir marta bosishda qo'llanadigan burchak: 90, 180 yoki 270.</summary>
    [ObservableProperty]
    private int _angle = 90;

    /// <summary>Amal barcha sahifalarga qo'llanadimi (aks holda faqat tanlanganlarga).</summary>
    [ObservableProperty]
    private bool _applyToAll = true;

    public IReadOnlyList<ToolChoice> AngleChoices { get; } =
    [
        new("90° o'ngga", 90),
        new("180°", 180),
        new("90° chapga", 270)
    ];

    public override object ToModel() => new RotateRequest(Angle, ApplyToAll);
}

// -------------------------------------------------------------------------------------
//  PDF -> Word
// -------------------------------------------------------------------------------------

/// <summary>"PDF → Word" konvertatsiyasi sozlamalari.</summary>
public sealed partial class PdfToWordOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private TextRecognitionMode _recognition = TextRecognitionMode.Automatic;

    [ObservableProperty]
    private DocumentLayoutMode _layout = DocumentLayoutMode.Flowing;

    [ObservableProperty]
    private bool _detectTables = true;

    [ObservableProperty]
    private bool _detectHeadings = true;

    [ObservableProperty]
    private bool _extractImages = true;

    [ObservableProperty]
    private bool _insertPageBreaks = true;

    [ObservableProperty]
    private string _ocrLanguage = OcrOptions.DefaultLanguage;

    public IReadOnlyList<ToolChoice> RecognitionChoices { get; } = BuildRecognitionChoices();

    public IReadOnlyList<ToolChoice> LayoutChoices { get; } =
    [
        new("Oqim — tahrirlash qulay", DocumentLayoutMode.Flowing),
        new("Aniq joylashuv — ko'rinishi PDF ga yaqin", DocumentLayoutMode.Exact)
    ];

    public IReadOnlyList<ToolChoice> LanguageChoices { get; } = BuildLanguageChoices();

    /// <summary>OCR tili faqat OCR ishlatilishi mumkin bo'lgan rejimlarda kerak.</summary>
    public bool IsOcrLanguageRelevant => Recognition != TextRecognitionMode.TextLayerOnly;

    partial void OnRecognitionChanged(TextRecognitionMode value)
        => OnPropertyChanged(nameof(IsOcrLanguageRelevant));

    public override object ToModel() => new PdfToWordOptions
    {
        Recognition = Recognition,
        Layout = Layout,
        DetectTables = DetectTables,
        DetectHeadings = DetectHeadings,
        ExtractImages = ExtractImages,
        InsertPageBreaks = InsertPageBreaks,
        OcrLanguage = OcrLanguage
    };

    /// <summary>Uchala matn tanish rejimi — bir necha VM da bir xil ishlatiladi.</summary>
    internal static IReadOnlyList<ToolChoice> BuildRecognitionChoices() =>
    [
        new("Avtomatik — kerak bo'lsa OCR", TextRecognitionMode.Automatic),
        new("Faqat matn qatlami — OCR ishlatilmaydi", TextRecognitionMode.TextLayerOnly),
        new("Majburiy OCR — har bir sahifa rasm sifatida", TextRecognitionMode.ForceOcr)
    ];

    /// <summary><see cref="OcrOptions.AvailableLanguages"/> ni ComboBox uchun ro'yxatga aylantiradi.</summary>
    internal static IReadOnlyList<ToolChoice> BuildLanguageChoices()
        => OcrOptions.AvailableLanguages
            .Select(language => new ToolChoice(language.Title, language.Code))
            .ToList();
}

// -------------------------------------------------------------------------------------
//  Word -> PDF
// -------------------------------------------------------------------------------------

/// <summary>"Word → PDF" konvertatsiyasi sozlamalari.</summary>
public sealed partial class WordToPdfOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private WordToPdfEngine _engine = WordToPdfEngine.Automatic;

    /// <summary>
    /// Ichki renderer shriftlarni doim joylashtiradi (PDFsharp 6 da joylashtirmaslik rejimi
    /// olib tashlangan), Word COM esa buni o'zi hal qiladi — shuning uchun qiymat o'zgarmaydi.
    /// </summary>
    [ObservableProperty]
    private bool _embedFonts = true;

    /// <summary>
    /// Katakcha faqat ma'lumot uchun ko'rsatiladi: hech qanday dvigatelda uni o'chirib
    /// bo'lmaydi, shuning uchun UI da ham u tahrirlanmaydi.
    /// </summary>
    public bool IsEmbedFontsAdjustable => false;

    [ObservableProperty]
    private bool _createBookmarks = true;

    public IReadOnlyList<ToolChoice> EngineChoices { get; } =
    [
        new("Avtomatik — Word bo'lsa o'sha", WordToPdfEngine.Automatic),
        new("Microsoft Word (eng aniq)", WordToPdfEngine.MicrosoftWord),
        new("Ichki renderer (Office shart emas)", WordToPdfEngine.Builtin)
    ];

    public override object ToModel() => new WordToPdfOptions
    {
        Engine = Engine,
        EmbedFonts = EmbedFonts,
        CreateBookmarks = CreateBookmarks
    };
}

// -------------------------------------------------------------------------------------
//  PDF -> rasm
// -------------------------------------------------------------------------------------

/// <summary>"PDF → Rasm" sozlamalari.</summary>
public sealed partial class PdfToImageOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJpeg))]
    private ImageOutputFormat _format = ImageOutputFormat.Png;

    [ObservableProperty]
    private int _dpi = 150;

    [ObservableProperty]
    private int _jpegQuality = 90;

    [ObservableProperty]
    private bool _whiteBackground = true;

    public bool IsJpeg => Format == ImageOutputFormat.Jpeg;

    public IReadOnlyList<ToolChoice> FormatChoices { get; } =
    [
        new("PNG — yo'qotishsiz", ImageOutputFormat.Png),
        new("JPG — kichik hajm", ImageOutputFormat.Jpeg),
        new("WEBP — zamonaviy", ImageOutputFormat.Webp)
    ];

    public IReadOnlyList<ToolChoice> DpiChoices { get; } =
    [
        new("72 dpi — ekran uchun", 72),
        new("150 dpi — odatiy", 150),
        new("300 dpi — bosma sifat", 300),
        new("600 dpi — maksimal", 600)
    ];

    public override object ToModel() => new PdfToImageOptions
    {
        Format = Format,
        Dpi = Dpi,
        JpegQuality = Math.Clamp(JpegQuality, 1, 100),
        WhiteBackground = WhiteBackground
    };
}

// -------------------------------------------------------------------------------------
//  Rasm -> PDF
// -------------------------------------------------------------------------------------

/// <summary>"Rasm → PDF" sozlamalari.</summary>
public sealed partial class ImageToPdfOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedPageSize))]
    private PdfPageSizeMode _pageSizeMode = PdfPageSizeMode.FitToImage;

    [ObservableProperty]
    private double _marginPoints = 28d;

    [ObservableProperty]
    private bool _autoOrientation = true;

    [ObservableProperty]
    private int _maxImageEdgePixels = 3508;

    [ObservableProperty]
    private int _jpegQuality = 88;

    /// <summary>Chekka va yo'nalish faqat qat'iy sahifa o'lchamlarida ma'noga ega.</summary>
    public bool IsFixedPageSize => PageSizeMode != PdfPageSizeMode.FitToImage;

    public IReadOnlyList<ToolChoice> PageSizeChoices { get; } =
    [
        new("Sahifa rasm o'lchamida", PdfPageSizeMode.FitToImage),
        new("A4 (210 × 297 mm)", PdfPageSizeMode.A4),
        new("US Letter (8.5 × 11 in)", PdfPageSizeMode.Letter)
    ];

    public IReadOnlyList<ToolChoice> SizeLimitChoices { get; } =
    [
        new("Asl o'lcham", 0),
        new("Ekran — 1600 px", 1600),
        new("Bosma — 3508 px (A4, 300 dpi)", 3508),
        new("Yuqori — 5000 px", 5000)
    ];

    public override object ToModel() => new ImageToPdfOptions
    {
        PageSizeMode = PageSizeMode,
        MarginPoints = MarginPoints,
        AutoOrientation = AutoOrientation,
        MaxImageEdgePixels = MaxImageEdgePixels,
        JpegQuality = Math.Clamp(JpegQuality, 1, 100)
    };
}

// -------------------------------------------------------------------------------------
//  OCR
// -------------------------------------------------------------------------------------

/// <summary>"OCR: skaner → Word" vositasi sozlamalari.</summary>
public sealed partial class OcrOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private string _language = OcrOptions.DefaultLanguage;

    [ObservableProperty]
    private int _dpi = 300;

    [ObservableProperty]
    private bool _detectParagraphs = true;

    /// <summary>Rasmni OCR dan oldin kulrangga o'tkazib kontrastini oshirish.</summary>
    [ObservableProperty]
    private bool _preprocess = true;

    public IReadOnlyList<ToolChoice> LanguageChoices { get; } = PdfToWordOptionsViewModel.BuildLanguageChoices();

    public IReadOnlyList<ToolChoice> DpiChoices { get; } =
    [
        new("200 dpi — tezroq", 200),
        new("300 dpi — tavsiya etiladi", 300),
        new("400 dpi — mayda matn uchun", 400)
    ];

    public override object ToModel() => new OcrOptions
    {
        Language = Language,
        Dpi = Dpi,
        DetectParagraphs = DetectParagraphs,
        Preprocess = Preprocess
    };
}

// -------------------------------------------------------------------------------------
//  PDF -> Excel / PowerPoint
// -------------------------------------------------------------------------------------

/// <summary>"PDF → Excel" sozlamalari.</summary>
public sealed partial class PdfToExcelOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private bool _sheetPerPage = true;

    [ObservableProperty]
    private bool _includePlainText = true;

    [ObservableProperty]
    private TextRecognitionMode _recognition = TextRecognitionMode.Automatic;

    [ObservableProperty]
    private string _ocrLanguage = OcrOptions.DefaultLanguage;

    public IReadOnlyList<ToolChoice> RecognitionChoices { get; } = PdfToWordOptionsViewModel.BuildRecognitionChoices();

    public IReadOnlyList<ToolChoice> LanguageChoices { get; } = PdfToWordOptionsViewModel.BuildLanguageChoices();

    public override object ToModel() => new PdfToExcelOptions
    {
        SheetPerPage = SheetPerPage,
        IncludePlainText = IncludePlainText,
        Recognition = Recognition,
        OcrLanguage = OcrLanguage
    };
}

/// <summary>"PDF → PowerPoint" sozlamalari.</summary>
public sealed partial class PdfToPowerPointOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty]
    private bool _includePageImage;

    [ObservableProperty]
    private bool _firstLineAsTitle = true;

    [ObservableProperty]
    private TextRecognitionMode _recognition = TextRecognitionMode.Automatic;

    [ObservableProperty]
    private string _ocrLanguage = OcrOptions.DefaultLanguage;

    public IReadOnlyList<ToolChoice> RecognitionChoices { get; } = PdfToWordOptionsViewModel.BuildRecognitionChoices();

    public IReadOnlyList<ToolChoice> LanguageChoices { get; } = PdfToWordOptionsViewModel.BuildLanguageChoices();

    public override object ToModel() => new PdfToPowerPointOptions
    {
        IncludePageImage = IncludePageImage,
        FirstLineAsTitle = FirstLineAsTitle,
        Recognition = Recognition,
        OcrLanguage = OcrLanguage
    };
}

// -------------------------------------------------------------------------------------
//  Orqa fonni olib tashlash (AI)
// -------------------------------------------------------------------------------------

/// <summary>"Orqa fonni olib tashlash" vositasi sozlamalari.</summary>
public sealed partial class BackgroundRemoverOptionsViewModel : ToolOptionsViewModel
{
    /// <summary>0 — yumshoq chekka saqlanadi (sochlar uchun yaxshiroq).</summary>
    [ObservableProperty]
    private int _alphaThreshold;

    /// <summary>Maska chekkasini yumshatish radiusi (piksel).</summary>
    [ObservableProperty]
    private double _featherRadius = 1.0d;

    /// <summary>Natijada obyekt atrofidagi bo'sh joy kesib tashlansinmi.</summary>
    [ObservableProperty]
    private bool _trimTransparentBorder;

    public override object ToModel() => new BackgroundRemovalOptions
    {
        AlphaThreshold = (byte)Math.Clamp(AlphaThreshold, 0, 255),
        FeatherRadius = (float)Math.Clamp(FeatherRadius, 0d, 8d),
        TrimTransparentBorder = TrimTransparentBorder
    };
}
