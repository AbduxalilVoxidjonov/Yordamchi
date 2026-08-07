# Yordamchi — arxitektura hujjati

**Versiya:** 2.0.0
**Muallif:** Abduxalil Voxidjonov — [@abduxalilvoxidjonov](https://t.me/abduxalilvoxidjonov)
**Platforma:** WPF (.NET 8, `net8.0-windows`), x64, o'zi-yetarli (self-contained)

Bu hujjat loyihaning ichki tuzilishini — qatlamlar, papkalar, sinflar, shartnomalar
(interfeyslar), ma'lumot oqimi va kengaytirish tartibini tavsiflaydi.

---

## Mundarija

1. [Umumiy qarash](#1-umumiy-qarash)
2. [Papkalar va sinflar xaritasi](#2-papkalar-va-sinflar-xaritasi)
3. [`IPdfEngineService` shartnomasi](#3-ipdfengineservice-shartnomasi)
4. [Servis interfeyslari jadvali](#4-servis-interfeyslari-jadvali)
5. [Ma'lumot oqimi: bitta amal boshdan-oxir](#5-malumot-oqimi-bitta-amal-boshdan-oxir)
6. [Modullar (17 ta vosita) jadvali](#6-modullar-17-ta-vosita-jadvali)
7. [Kutubxonalar va litsenziyalar](#7-kutubxonalar-va-litsenziyalar)
8. [Tashqi resurslar: OCR tillari va AI modeli](#8-tashqi-resurslar-ocr-tillari-va-ai-modeli)
9. [Kengaytirish qo'llanmasi: yangi vosita qo'shish](#9-kengaytirish-qollanmasi-yangi-vosita-qoshish)
10. [Ko'ndalang qarorlar](#10-kondalang-qarorlar)

---

## 1. Umumiy qarash

Yordamchi **Clean Architecture** tamoyillari ustiga qurilgan **MVVM** dasturi. Bog'liqlik
yo'nalishi faqat bitta tomonga — ichkariga (abstraksiyalar va modellar tomon) qaraydi.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Views (XAML)                                                            │
│  MainWindow, DashboardView, ToolWorkspaceView, BackgroundRemoverView,    │
│  ScreenRecorderView…                                                     │
│  Code-behind faqat InitializeComponent va HWND (Mica, oynani             │
│  kichraytirish) ishlari uchun.                                           │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  DataBinding / Command
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  ViewModels (CommunityToolkit.Mvvm)                                      │
│  MainViewModel, DashboardViewModel, ToolWorkspaceViewModel,              │
│  ScreenRecorderViewModel, …                                              │
│  WPF dialoglarini bilmaydi; faqat abstraksiyalarga tayanadi.             │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  interfeyslar
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Services.Abstractions (shartnomalar)                                    │
│  IPdfEngineService · IPdfService · IPdfManipulatorService ·              │
│  IDocumentConversionService · IOcrService · IImageBackgroundRemover ·    │
│  IScreenRecorderService · IDialogService · IThemeService                 │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  DI (Microsoft.Extensions.DependencyInjection)
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Services (implementatsiya)                                              │
│  PdfEngineService (fasad) → PdfService, PdfManipulatorService,           │
│  DocumentConversionService, OcrService, OnnxBackgroundRemover            │
│  Services\Conversion\… — past darajali yordamchi yozuvchi/o'quvchilar    │
│  ScreenRecorderService — fasaddan TASHQARIDA, mustaqil singleton         │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Tashqi kutubxonalar                                                     │
│  PDFsharp · pdfium (PDFtoImage) · SkiaSharp · PdfPig · OpenXML ·         │
│  Tesseract · ONNX Runtime · Word COM (ixtiyoriy) ·                       │
│  ScreenRecorderLib (Media Foundation + WASAPI)                           │
└──────────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Models — sof ma'lumot. Hech kimga bog'liq emas.                         │
│  (Barcha yuqoridagi qatlamlar Models ga murojaat qiladi.)                │
└──────────────────────────────────────────────────────────────────────────┘
```

### Qatlamlar orasidagi qat'iy qoidalar

| Qatlam | Nimaga murojaat qila oladi | Nimaga MUTLAQO murojaat qilmaydi |
|---|---|---|
| `Views` | `ViewModels`, `Converters`, `Behaviors`, `Themes` | `Services` implementatsiyasi, PDF kutubxonalari |
| `ViewModels` | `Services.Abstractions`, `Models` | `MessageBox`, `OpenFileDialog`, PDFsharp/PdfPig/Tesseract |
| `Services` | `Models`, tashqi kutubxonalar | `ViewModels`, `Views` |
| `Services.Abstractions` | `Models` | implementatsiya sinflari |
| `Models` | — | hech nima |

> **Yagona ataylab qilingan chekinish:** `ScreenRecorderService` `System.Windows.Application.Current.Dispatcher`
> ga murojaat qiladi. U hech qanday `View` yoki `ViewModel` ni bilmaydi — bu faqat
> hodisalarni UI oqimiga o'tkazish uchun. Muqobili — hodisalarni fon oqimida ko'tarib,
> marshalling'ni har bir obunachiga yuklash bo'lardi; shartnoma esa aksini va'da qiladi.

**Asosiy g'oya:** UI hech qachon "PDF ni qanday siqish kerak"ligini bilmaydi. U faqat
`ToolRequest` yasaydi va uni `IPdfEngineService.ExecuteAsync` ga beradi. Shu tufayli yangi
vosita qo'shish UI kodini deyarli o'zgartirmaydi.

### Nega ekran yozuvi fasadga kirmaydi

`IScreenRecorderService` — abstraksiyalar qatoridagi to'liq huquqli shartnoma, lekin u
**ataylab** `IPdfEngineService` ning sub-servisi qilinmagan. Sabablari:

| Fasad (`IPdfEngineService`) | Ekran yozuvi (`IScreenRecorderService`) |
|---|---|
| PDF quvuri: fayl kiradi → fayl chiqadi | PDF bilan hech qanday aloqasi yo'q: kirish — ekran, chiqish — `.mp4` |
| `ToolRequest` → `ExecuteAsync` → `ToolRunResult` — bir martalik amal | Uzoq davom etadigan, holatga asoslangan seans (`RecorderState`) |
| Natija `await` bilan qaytadi, `IProgress<PdfProgress>` bilan xabar beradi | Natija **hodisa** orqali keladi (`RecordingCompleted` / `RecordingFailed`) |
| Bosh sahifadagi 17 ta kartochkadan biri sifatida ochiladi | Yon paneldagi alohida bo'lim; `ToolCatalog` da umuman yo'q |

Shu sababli `App.xaml.cs` da u fasaddan mustaqil ravishda ro'yxatga olinadi va faqat
`ScreenRecorderViewModel` ga in'ektsiya qilinadi:

```csharp
services.AddSingleton<IPdfEngineService, PdfEngineService>();

// Ekran yozuvi PDF quvuriga umuman aloqador emas, shuning uchun u fasadga
// qo'shilmaydi va o'z sahifasi bilan to'g'ridan-to'g'ri ishlaydi.
services.AddSingleton<IScreenRecorderService, ScreenRecorderService>();
```

Singleton tanlanishining amaliy sababi ham bor: `IScreenRecorderService` —
`IDisposable`, va `ServiceProvider` dastur yopilganda uni `Dispose` qiladi. `Dispose`
esa hali yozilayotgan faylni to'g'ri yakunlaydi, aks holda `.mp4` da `moov` atomi
yozilmay qoladi va fayl umuman ochilmaydi.

Yon paneldagi uchta bo'lim (`MainViewModel.NavigationItems`):

| # | Bo'lim | Sahifa (`ViewModelBase.Title`) |
|---|---|---|
| 0 | PDF vositalari | `DashboardViewModel` |
| 1 | Ekran yozuvi | `ScreenRecorderViewModel` |
| 2 | Dastur haqida | `AboutViewModel` |

---

## 2. Papkalar va sinflar xaritasi

> Quyidagi daraxt `src\Yordamchi` papkasining haqiqiy tarkibi. Ba'zi fayllar 2.0.0 ishlab
> chiqish jarayonida qo'shilgan — ular ham ro'yxatda.

```
Yordamchi.sln
└─ src/Yordamchi/
   ├─ Yordamchi.csproj              Maqsadli platforma, NuGet paketlari, versiya/muallif metama'lumoti
   ├─ app.manifest                Per-monitor V2 DPI, Windows 10/11 uyg'unligi
   ├─ App.xaml(.cs)               Composition root: DI konteyner, mavzu, shell oynasi
   │
   ├─ Assets/
   │  └─ Yordamchi.ico              Dastur ikonasi (exe, yorliq, o'rnatuvchi)
   │
   ├─ Models/                     ── Sof ma'lumot; UI ham, kutubxona ham bog'liq emas
   │  ├─ ToolDescriptor.cs        ToolId / ToolCategory / ToolInputKind enum'lari + ToolCatalog (17 vosita)
   │  ├─ ToolRequest.cs           Ishchi oynadan dvigatelga uzatiladigan topshiriq + ToolRunResult
   │  ├─ OperationOptions.cs      Split/Compress/Protect/Watermark/PageNumber/PdfToImage sozlamalari
   │  ├─ ConversionOptions.cs     PdfToWord/WordToPdf/PdfToExcel/PdfToPowerPoint/Ocr/BackgroundRemoval sozlamalari
   │  ├─ DocumentContent.cs       Oraliq hujjat modeli: TextRun, ParagraphBlock, TableBlock, ImageBlock, ContentPage
   │  ├─ PageModel.cs             Bitta manba sahifa + render qilingan eskiz (thumbnail)
   │  ├─ PageEdit.cs              "Shu fayldan shu sahifani shu burchak bilan ol"
   │  ├─ PageRotation.cs          Burilish enum'i + Add/RotateClockwise kengaytmalari
   │  ├─ ImageToPdfOptions.cs     Sahifa o'lchami, chekka (margin), downscale chegarasi
   │  ├─ ScreenRecording.cs       Ekran yozuvi: RecordingSourceKind / RecordingSourceInfo /
   │  │                           AudioDeviceInfo / VideoEncoderKind / RecordingQuality /
   │  │                           RecorderState + ScreenRecordingOptions
   │  ├─ PdfProgress.cs           IProgress<T> yuki: Completed / Total / Message
   │  └─ PdfServiceException.cs   PdfErrorKind bilan yagona xato turi
   │
   ├─ Services/
   │  ├─ Abstractions/            ── Shartnomalar; ViewModels faqat shularni ko'radi
   │  │  ├─ IPdfEngineService.cs        Yagona fasad: 5 sub-servis + ExecuteAsync + Validate + CheckPrerequisites
   │  │  ├─ IPdfService.cs              Rasterizatsiya, eskiz, sahifa rejasini yozish
   │  │  ├─ IPdfManipulatorService.cs   Merge/Split/Compress/Protect/Unlock/Watermark/PageNumbers/Rotate
   │  │  ├─ IDocumentConversionService.cs  PDF ↔ Word/Excel/PowerPoint/rasm
   │  │  ├─ IOcrService.cs              Tesseract qobig'i + til fayllarini boshqarish
   │  │  ├─ IImageBackgroundRemover.cs  u2net (ONNX) bilan fonni shaffof qilish
   │  │  ├─ IScreenRecorderService.cs   Ekran yozuvi (fasadga kirmaydi) + hodisa argumentlari
   │  │  ├─ IDialogService.cs           Fayl/papka dialoglari, xabar oynalari
   │  │  └─ IThemeService.cs            Light/Dark almashtirish + tizim sozlamasini kuzatish
   │  │
   │  ├─ PdfEngineService.cs      Fasad: ToolId → mos servis; Validate va CheckPrerequisites shu yerda
   │  ├─ PdfService.cs            pdfium bilan rasterizatsiya + PDFsharp bilan sahifa rejasini yozish
   │  ├─ PdfManipulatorService.cs Hujjat darajasidagi amallar (PDFsharp): merge, split, compress, protect…
   │  ├─ DocumentConversionService.cs  Konvertatsiya orkestratori: extractor → model → writer
   │  ├─ OcrService.cs            Tesseract; tessdata papkasini topadi va til fayllarini yuklab oladi
   │  ├─ OnnxBackgroundRemover.cs u2net/u2netp ONNX modeli, maska → alfa kanal
   │  ├─ ScreenRecorderService.cs ScreenRecorderLib (Media Foundation) qobig'i; hodisalarni
   │  │                           UI oqimiga o'tkazadi, sifat → bitrate ni o'zi hisoblaydi
   │  ├─ DialogService.cs         Win32 fayl dialoglari, MessageBox — UI ning yagona kirish nuqtasi
   │  ├─ ThemeService.cs          MergedDictionaries[0] ni almashtirish, DWM sarlavha rangi
   │  │
   │  └─ Conversion/              ── Past darajali o'quvchi/yozuvchilar (bitta format = bitta fayl)
   │     ├─ PdfTextExtractor.cs   PdfPig: matn, shrift, o'lcham, koordinata → DocumentContent
   │     ├─ DocxWriter.cs         OpenXML: DocumentContent → .docx (abzas, jadval, sarlavha)
   │     ├─ WordToPdfRenderer.cs  Word o'rnatilmagan holat uchun OpenXML → PDF (PDFsharp) renderer
   │     ├─ OfficeWordInterop.cs  Microsoft Word COM (late binding) orqali eng aniq .docx → PDF
   │     ├─ XlsxWriter.cs         OpenXML: jadvallar → .xlsx kitob
   │     └─ PptxWriter.cs         OpenXML: har bir sahifa matni → .pptx slayd
   │
   ├─ ViewModels/
   │  ├─ ViewModelBase.cs             IsBusy / Progress / Cancel / xatolarni ko'rsatish uchun asos
   │  ├─ MainViewModel.cs             Shell: navigatsiya, dashboard ↔ workspace almashinuvi, mavzu tugmasi
   │  ├─ DashboardViewModel.cs        Bosh sahifa: kategoriyalar bo'yicha kartochkalar, qidiruv, ToolSelected
   │  ├─ ToolCardViewModel.cs         Bitta vosita kartochkasi (ToolDescriptor ustidagi qobiq)
   │  ├─ ToolWorkspaceViewModel.cs    Universal ishchi oyna: fayl tanlash → ToolRequest → ExecuteAsync
   │  ├─ ToolOptionsViewModels.cs     Har bir vosita sozlamalari uchun VM'lar (DataTemplate bilan tanlanadi)
   │  ├─ BackgroundRemoverViewModel.cs AI fon olib tashlash: oldin/keyin ko'rinishi, saqlash
   │  ├─ ScreenRecorderViewModel.cs   Ekran yozuvi sahifasi: manba/video/ovoz sozlamalari,
   │  │                               boshlash-pauza-to'xtatish, taymer, MinimizeRequested
   │  ├─ AboutViewModel.cs            Versiya, muallif, Telegram havolasi, litsenziyalar
   │  ├─ WorkspaceFileViewModel.cs    Ishchi oynadagi bitta tanlangan fayl (nom, hajm, holat)
   │  ├─ PageItemViewModel.cs         Bitta sahifa kartasi (eskiz + burilish + tanlov)
   │  ├─ ImageItemViewModel.cs        Galereyadagi bitta rasm
   │  └─ NavigationItemViewModel.cs   Yon paneldagi bitta bo'lim (Glyph + Content)
   │
   ├─ Views/
   │  ├─ MainWindow.xaml(.cs)         Shell: yon panel + kontent hosti
   │  │                               (code-behind: Mica + MinimizeRequested → WindowState)
   │  ├─ DashboardView.xaml(.cs)      PDF vositalari: 4 kategoriya, 17 kartochka, qidiruv maydoni
   │  ├─ ToolWorkspaceView.xaml(.cs)  Universal ishchi oyna: fayl ro'yxati, sozlamalar paneli, natija
   │  ├─ ToolOptionTemplates.xaml     Har bir Options VM uchun DataTemplate lar (ResourceDictionary)
   │  ├─ BackgroundRemoverView.xaml(.cs)  Oldin/keyin taqqoslash, shaffoflik shaxmat foni
   │  ├─ ScreenRecorderView.xaml(.cs) Ekran yozuvi: boshqaruv paneli, manba, video, ovoz, saqlash
   │  └─ AboutView.xaml(.cs)          Dastur haqida: versiya, muallif, Telegram
   │
   ├─ Behaviors/                      ── XAML'dan ulanadigan attached behavior'lar
   │  ├─ DragDropReorder.cs           Kolleksiyani o'z joyida qayta tartiblash + auto-scroll
   │  ├─ InsertionAdorner.cs          Qo'yiladigan joyni ko'rsatuvchi accent chiziq
   │  └─ FileDrop.cs                  Explorer'dan fayl tashlab yuborish (drop)
   │
   ├─ Converters/                     ── IValueConverter'lar (XAML uchun)
   │  ├─ UiConverters.cs                      2.0.0 UI uchun umumiy konvertorlar to'plami
   │  ├─ BooleanToVisibilityConverter.cs      bool → Visibility
   │  ├─ InverseBooleanConverter.cs           bool → !bool
   │  ├─ BooleanToOpacityConverter.cs         bool → shaffoflik (o'chirilgan holat)
   │  ├─ CountToVisibilityConverter.cs        Ro'yxat bo'shligiga qarab bo'sh holat paneli
   │  ├─ NullOrEmptyToVisibilityConverter.cs  Bo'sh matnni yashirish
   │  ├─ EnumEqualsConverter.cs               RadioButton'ni enum qiymatiga bog'lash
   │  ├─ FileSizeConverter.cs                 Baytlarni "12,4 MB" ko'rinishiga keltirish
   │  ├─ MathMultiplyConverter.cs             O'lchamlarni koeffitsiyentga ko'paytirish
   │  └─ PageRotationToTransformConverter.cs  PageRotation → LayoutTransform
   │
   ├─ Helpers/
   │  ├─ SkiaImageHelper.cs           SKBitmap → muzlatilgan (frozen) BitmapImage
   │  └─ WindowBackdrop.cs            Mica/Acrylic backdrop (DwmSetWindowAttribute), xavfsiz degradatsiya
   │
   └─ Themes/
      ├─ Colors.Light.xaml            Yorug' mavzu kalitlari — MergedDictionaries[0]
      ├─ Colors.Dark.xaml             Aynan o'sha kalitlar, qorong'i qiymatlar bilan
      └─ Controls.xaml                Stillar; ranglarga faqat DynamicResource orqali murojaat
```

### Papkalarning mas'uliyati

| Papka | Mas'uliyat | Bog'liqligi |
|---|---|---|
| `Models` | Ma'lumot tuzilmalari, enum'lar, katalog | Yo'q |
| `Services\Abstractions` | Shartnomalar (interfeyslar) | `Models` |
| `Services` | Biznes-mantiq implementatsiyasi | `Models` + kutubxonalar |
| `Services\Conversion` | Format-maxsus o'quvchi/yozuvchilar | PdfPig, OpenXML, PDFsharp, Word COM |
| `ViewModels` | Holat, komandalar, validatsiya | `Abstractions` + `Models` |
| `Views` | Faqat XAML tashqi ko'rinishi | `ViewModels` |
| `Themes` | Ranglar va stillar | Yo'q |
| `Behaviors` | Attached property'lar (drag-drop, drop) | WPF |
| `Converters` | XAML uchun qiymat konvertorlari | `Models` |
| `Helpers` | Yordamchi statik sinflar | SkiaSharp, Win32 |
| `Assets` | Ikona va resurslar | — |

---

## 3. `IPdfEngineService` shartnomasi

`IPdfEngineService` — dasturning PDF "dvigateli" va **17 ta vositaning hammasini**
birlashtiruvchi **fasad**. UI qatlami PDFsharp, PdfPig, OpenXML, Tesseract yoki ONNX
Runtime ni umuman bilmaydi: u yoki kerakli sub-servisni oladi, yoki universal
`ExecuteAsync` ni chaqiradi. (Ekran yozuvi bu fasadga kirmaydi — sababi 1-bo'limda.)

```csharp
public interface IPdfEngineService
{
    // ------------------------------------------------------------------
    //  Modul servislari (5 ta)
    // ------------------------------------------------------------------

    /// <summary>Sahifalarni rasterizatsiya qilish, eskiz chizish va sahifa rejasini yozish.</summary>
    IPdfService Pages { get; }

    /// <summary>Birlashtirish, bo'lish, siqish, himoyalash, suv belgisi, raqamlash.</summary>
    IPdfManipulatorService Documents { get; }

    /// <summary>PDF ↔ Word/Excel/PowerPoint/rasm konvertatsiyasi.</summary>
    IDocumentConversionService Conversion { get; }

    /// <summary>Tesseract OCR.</summary>
    IOcrService Ocr { get; }

    /// <summary>u2net (ONNX) bilan rasm fonini olib tashlash.</summary>
    IImageBackgroundRemover BackgroundRemover { get; }

    // ------------------------------------------------------------------
    //  Universal bajarish
    // ------------------------------------------------------------------

    /// <summary>Tanlangan vositani berilgan so'rov bilan bajaradi va natijani qaytaradi.</summary>
    /// <exception cref="PdfServiceException">Har qanday kutilgan nosozlik.</exception>
    /// <exception cref="OperationCanceledException">Foydalanuvchi bekor qilganda.</exception>
    Task<ToolRunResult> ExecuteAsync(
        ToolRequest request,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ishga tushishdan oldin so'rovni tekshiradi; muammo bo'lsa — tushunarli matn, aks holda null.</summary>
    string? Validate(ToolRequest request);

    /// <summary>Tashqi komponent (OCR til fayli, u2net modeli) mavjudmi — UI ogohlantirish ko'rsatadi.</summary>
    string? CheckPrerequisites(ToolId tool, object? options = null);

    /// <summary>Yuqoridagining mashina o'qiy oladigan varianti — UI "Yuklab olish" tugmasini shu asosda chiqaradi.</summary>
    DownloadableComponent GetMissingComponent(ToolId tool, object? options = null);

    /// <summary>Yetishmayotgan komponentni yuklab oladi (None uchun hech narsa qilmaydi).</summary>
    Task DownloadComponentAsync(
        DownloadableComponent component,
        object? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### Fasad a'zolarining roli

| A'zo | Qachon chaqiriladi | Nima qaytaradi | UI da nima bo'ladi |
|---|---|---|---|
| 5 ta sub-servis (`Pages`, `Documents`, `Conversion`, `Ocr`, `BackgroundRemover`) | Maxsus ekranlar (sahifa muharriri, AI fon olib tashlash) to'g'ridan-to'g'ri chaqiradi | Domenga xos natija | Eskizlar, oldin/keyin ko'rinishi |
| `Validate(request)` | "Bajarish" tugmasi bosilishidan oldin | Xato matni yoki `null` | Tugma o'chadi / qizil izoh chiqadi |
| `CheckPrerequisites(tool, options)` | Vosita ochilganda va sozlama o'zgarganda | Ogohlantirish matni yoki `null` | Sariq ogohlantirish paneli ("til fayli yo'q", "model yuklanmagan") |
| `GetMissingComponent(tool, options)` | Shu bilan birga | `DownloadableComponent` | Ogohlantirish yonida "Yuklab olish" tugmasi (`None` bo'lsa — yo'q) |
| `DownloadComponentAsync(component, options, progress, ct)` | "Yuklab olish" bosilgach | — | Progress-bar, so'ng ogohlantirish yo'qoladi |
| `ExecuteAsync(request, progress, ct)` | "Bajarish" bosilgach | `ToolRunResult` | Progress-bar, so'ng "Papkani ochish" tugmasi |

`ExecuteAsync` ichida `request.Tool` bo'yicha `switch` bor: har bir `ToolId` mos sub-servis
metodiga yo'naltiriladi. Barcha kutilgan nosozliklar `PdfServiceException` (ichida
`PdfErrorKind`) ko'rinishida yuqoriga chiqadi, foydalanuvchi bekor qilsa —
`OperationCanceledException`.

---

## 4. Servis interfeyslari jadvali

### `IPdfService` — rasterizatsiya va sahifa rejasi

| Metod | Vazifasi |
|---|---|
| `RenderPdfPagesAsync(filePath, thumbnailWidth, password, progress, ct)` | Barcha sahifalarni eskiz qilib chizadi → `List<PageModel>` |
| `RenderPageAsync(filePath, pageIndex, width, password, ct)` | Bitta sahifani katta o'lchamda chizadi → `BitmapSource` |
| `RenderImageThumbnailAsync(imagePath, thumbnailWidth, ct)` | Rasm fayli uchun eskiz (galereya) |
| `GetPageCountAsync(filePath, password, ct)` | Rasterizatsiyasiz sahifalar sonini oladi |
| `MergePdfFilesAsync(inputFiles, outputPath, progress, ct)` | Fayllarni ketma-ket ulaydi |
| `ReorderAndDeletePagesAsync(sourcePdfPath, keepPageIndicesInOrder, outputPath, …)` | Faqat ko'rsatilgan sahifalarni, ko'rsatilgan tartibda yozadi |
| `ConvertImagesToPdfAsync(imagePaths, outputPath, options, …)` | Har bir rasm — bitta sahifa |
| `BuildPdfAsync(pages, outputPath, progress, ct)` | **Asosiy primitiv**: ixtiyoriy `PageEdit` ro'yxatini bitta PDF ga yozadi |

### `IPdfManipulatorService` — hujjat darajasidagi amallar

| Metod | Vazifasi |
|---|---|
| `MergePdfsAsync(pdfPaths, outputPath, progress, ct)` | Berilgan tartibda birlashtiradi |
| `SplitPdfAsync(pdfPath, outputFolder, SplitOptions, …)` | Rejim bo'yicha bo'ladi → yaratilgan fayllar ro'yxati |
| `SplitPdfAsync(pdfPath, outputFolder, List<(int First, int Last)>, …)` | Oraliqlar to'g'ridan-to'g'ri berilgan sodda variant |
| `CompressPdfAsync(inputPath, outputPath, CompressionLevel, …)` | Rasmlarni qayta kodlab hajmni kichraytiradi → `CompressionResult` |
| `ProtectPdfAsync(inputPath, outputPath, password, …)` | Ochish uchun parol qo'yadi |
| `ProtectPdfAsync(inputPath, outputPath, ProtectOptions, …)` | Egalik paroli, ruxsatlar, shifrlash darajasi bilan |
| `UnlockPdfAsync(inputPath, outputPath, password, …)` | Paroli ma'lum hujjatdan himoyani olib tashlaydi |
| `AddWatermarkAsync(inputPath, outputPath, WatermarkOptions, …)` | Matnli suv belgisi chizadi |
| `AddPageNumbersAsync(inputPath, outputPath, PageNumberOptions, …)` | Sahifalarni raqamlaydi |
| `RotatePagesAsync(inputPath, outputPath, degrees, pageIndices, …)` | Sahifalarni buradi (`null` → barchasi) |
| `IsPasswordProtectedAsync(pdfPath, ct)` | Hujjat parol so'raydimi |

### `IDocumentConversionService` — formatlar orasida

| Metod / xossa | Vazifasi |
|---|---|
| `PdfToWordAsync(pdfPath, docxPath, PdfToWordOptions?, …)` | Tahrirlanadigan `.docx` |
| `WordToPdfAsync(docxPath, pdfPath, WordToPdfOptions?, …)` | Word COM yoki ichki renderer orqali PDF |
| `OcrPdfToWordAsync(scannedPdfPath, docxPath, language, …)` | Skaner-PDF → OCR → `.docx` |
| `PdfToExcelAsync(pdfPath, xlsxPath, PdfToExcelOptions?, …)` | Jadvallar → `.xlsx` |
| `PdfToPowerPointAsync(pdfPath, pptxPath, PdfToPowerPointOptions?, …)` | Har sahifa → slayd `.pptx` |
| `PdfToImagesAsync(pdfPath, outputFolder, PdfToImageOptions?, …)` | Sahifalar → JPG/PNG fayllar |
| `ExtractContentAsync(pdfPath, PdfToWordOptions?, …)` | PDF → oraliq `DocumentContent` modeli |
| `bool IsMicrosoftWordAvailable` | Kompyuterda Word (COM) bormi |

### `IOcrService` — Tesseract qobig'i

| Metod / xossa | Vazifasi |
|---|---|
| `RecognizeAsync(SKBitmap, OcrOptions?, ct)` | Bitta rasm → `ContentPage` (bloklar) |
| `RecognizeTextAsync(imagePath, language, ct)` | Rasm fayli → oddiy matn |
| `RecognizePdfAsync(pdfPath, OcrOptions?, progress, ct)` | Butun skaner-PDF → `DocumentContent` |
| `string TessDataPath` | Til fayllari papkasi (topilgan yoki mo'ljallangan) |
| `GetInstalledLanguages()` | Mavjud til kodlari, masalan `["eng", "rus", "uzb"]` |
| `AreLanguagesInstalled(language, out missing)` | `uzb+eng+rus` ifodasidagi tillar bormi |
| `DownloadLanguagesAsync(languages, progress, ct)` | `tessdata_fast` ombordan yuklab oladi (internet + rozilik kerak) |

### `IImageBackgroundRemover` — AI fon olib tashlash

| Metod / xossa | Vazifasi |
|---|---|
| `RemoveBackgroundAsync(inputImagePath, progress, ct)` | UI ga bog'lash uchun muzlatilgan `BitmapSource` |
| `RemoveBackgroundToBitmapAsync(inputImagePath, BackgroundRemovalOptions?, …)` | To'liq sozlanadigan variant → `SKBitmap` (chaqiruvchi `Dispose` qiladi) |
| `SaveAsPngAsync(image, outputPath, ct)` | Alfa kanali saqlangan holda PNG yozadi |
| `bool IsModelAvailable` | `u2net.onnx` / `u2netp.onnx` topildimi |
| `string ModelPath` | Model kutilayotgan to'liq yo'l (xato xabarida ko'rsatiladi) |
| `string DownloadableModelName` / `DownloadableModelSizeText` | Tasdiqlash oynasida ko'rsatiladigan nom va hajm (`u2net.onnx`, `~168 MB`) |
| `DownloadModelAsync(progress, ct)` | Modelni rasmiy relizdan `%LOCALAPPDATA%\Yordamchi\Models` ga yuklaydi; avval `.tmp` ga yozadi |

### `IScreenRecorderService` — ekran yozuvi

Yagona shartnoma bo'lib, `IPdfEngineService` fasadiga **kirmaydi** (sababi 1-bo'limda).
`IDisposable` dan meros oladi: dastur yopilganda yozuv to'g'ri yakunlanishi kerak.

| A'zo | Nima qaytaradi / qiladi |
|---|---|
| `RecorderState State` | Joriy holat: `Idle` · `Starting` · `Recording` · `Paused` · `Finishing` |
| `bool IsSupported` | Bu kompyuterda yozib olish mumkinmi (`Environment.OSVersion.Version.Build >= 18362`, ya'ni Windows 10 1903) |
| `GetDisplays()` | Monitorlar → `IReadOnlyList<RecordingSourceInfo>` (`Kind = Display`, `Id` — qurilma nomi) |
| `GetWindows()` | Ochiq oynalar → `IReadOnlyList<RecordingSourceInfo>` (`Kind = Window`, `Id` — HWND ning matn ko'rinishi); sarlavhasi bo'sh va kichraytirilganlari chiqarib tashlanadi |
| `GetMicrophones()` / `GetSpeakers()` | `IReadOnlyList<AudioDeviceInfo>`; **birinchi element doim** `(null, "Tizim tanlagan qurilma")` |
| `StartRecording(options)` | Yozuvni boshlaydi va yaratilayotgan `.mp4` faylning to'liq yo'lini **darhol** qaytaradi |
| `StopRecording()` | To'xtatish buyrug'ini beradi va darhol qaytadi — fayl yakunlanishi hodisa bilan xabar qilinadi |
| `PauseRecording()` / `ResumeRecording()` | Vaqtincha to'xtatadi/davom ettiradi; fayl yopilmaydi |
| `event StateChanged` | `RecorderStateChangedEventArgs { State }` |
| `event RecordingCompleted` | `ScreenRecordingCompletedEventArgs { FilePath, Duration }` |
| `event RecordingFailed` | `ScreenRecordingFailedEventArgs { Message, PartialFilePath }` — kutubxona chala faylni saqlab qolgan bo'lsa uning yo'li |

Barcha ro'yxat metodlari xatoni **yutadi** va bo'sh ro'yxat qaytaradi: monitorlar yoki
ovoz qurilmalarini sanab bo'lmasligi dasturni yiqitmasligi kerak — foydalanuvchi
"Yangilash" tugmasi bilan qayta urinib ko'radi.

#### Hodisaga asoslangan hayot sikli

```
GetDisplays() / GetWindows()          manba tanlanadi
        ▼
StartRecording(options)  ──▶  yo'lni qaytaradi (fayl hali tayyor emas)
        │                     StateChanged: Starting → Recording
        ▼
PauseRecording() / ResumeRecording()  StateChanged: Paused ⇄ Recording
        ▼
StopRecording()  ──▶  darhol qaytadi
        │             StateChanged: Finishing → Idle
        ▼
RecordingCompleted(FilePath, Duration)   ← fayl endi ochsa bo'ladi
    yoki
RecordingFailed(Message, PartialFilePath)
```

**Nega natija `Task` emas, hodisa.** `StopRecording` dan keyin kutubxona `moov` atomini
yozib, faylni yopishi kerak — bu bir necha yuz millisekund oladi. Shu sababli to'xtatish
buyrug'i asinxron: UI darhol "Fayl yakunlanmoqda…" holatiga o'tadi va faqat
`RecordingCompleted` kelganda "Oxirgi yozuv" kartochkasini ko'rsatadi.

**Hodisalar UI oqimida ko'tariladi — buni implementatsiya kafolatlaydi.**
ScreenRecorderLib o'z hodisalarini native MTA ishchi oqimida chaqiradi, u yerdan esa WPF
obyektlariga tegib bo'lmaydi. `ScreenRecorderService` shu sababli har bir hodisani
`Application.Current.Dispatcher` orqali o'tkazadi (`CheckAccess()` bo'lsa —
to'g'ridan-to'g'ri, aks holda `BeginInvoke`). Natijada `ScreenRecorderViewModel` da
birorta ham `Dispatcher.Invoke` yo'q.

#### Sozlamalarni tarjima qilish (`ScreenRecordingOptions` → `RecorderOptions`)

| Bizning model | Kutubxonaga nima bo'lib tushadi |
|---|---|
| `Framerate` | `Math.Clamp(…, 15, 60)` |
| `Quality` | Bitrate: `Low` 3 Mbit/s · `Medium` 8 Mbit/s · `High` 16 Mbit/s; 30 fps dan yuqorida `framerate / 30` ga mutanosib ko'tariladi |
| `Encoder` | `H264VideoEncoder` (UnconstrainedVBR, High profil) yoki `H265VideoEncoder` (CBR, Main profil) |
| `UseHardwareEncoding` | `VideoEncoderOptions.IsHardwareEncodingEnabled` |
| `Source` | `WindowRecordingSource(HWND)` · `DisplayRecordingSource(deviceName)` · hech nima tanlanmasa `DisplayRecordingSource.MainMonitor` |
| `RecordSystemAudio` / `RecordMicrophone` | `AudioOptions.IsOutputDeviceEnabled` / `IsInputDeviceEnabled`; ikkalasi birga yoqilsa ovozlar "qirqilib" ketmasligi uchun `OutputVolume = 0.6`, `InputVolume = 0.8` |
| `ShowCursor` / `HighlightClicks` | `MouseOptions.IsMousePointerEnabled` / `IsMouseClicksDetected` |
| `OutputFolder` | Papka yaratiladi, fayl nomi `yozuv-yyyy-MM-dd-HH-mm-ss.mp4` bo'lib qo'shiladi |

Xatolar boshqa servislardagi kabi `PdfServiceException` bo'lib chiqadi:
`MissingComponent` (Windows eski), `OperationFailed` (yozuv allaqachon ketmoqda yoki
kutubxona ishga tushmadi), `InvalidOptions` (papka tanlanmagan), `OutputNotWritable`
(papkani yaratib bo'lmadi).

### Yordamchi UI servislari

| Interfeys | Vazifasi |
|---|---|
| `IDialogService` | Fayl/papka tanlash dialoglari, tasdiq va xato oynalari — `ViewModels` uchun yagona UI eshigi |
| `IThemeService` | Light/Dark almashtirish, tizim mavzusini kuzatish, sarlavha panelini bo'yash |

---

## 5. Ma'lumot oqimi: bitta amal boshdan-oxir

Misol: foydalanuvchi **"PDF → Word"** kartochkasini bosdi.

```
1.  Foydalanuvchi bosh sahifada "PDF → Word" kartochkasini bosadi
        │  (DashboardView.xaml → ToolCardViewModel.SelectCommand)
        ▼
2.  DashboardViewModel.ToolSelected  ─── ToolDescriptor(ToolId.PdfToWord) ──▶
        ▼
3.  MainViewModel  — dashboard'ni yopadi, ishchi oynani ko'rsatadi
        │  CurrentView = ToolWorkspaceViewModel
        ▼
4.  ToolWorkspaceViewModel.Activate(descriptor)
        │  · Input = SinglePdf  → "PDF tanlash" tugmasi
        │  · OutputExtension = ".docx"  → saqlash dialogining filtri
        │  · Options = PdfToWordOptionsViewModel (DataTemplate bilan chiziladi)
        │  · IPdfEngineService.CheckPrerequisites(ToolId.PdfToWord) → ogohlantirish paneli
        ▼
5.  Foydalanuvchi IDialogService orqali faylni tanlaydi, sozlamalarni o'zgartiradi,
    "Bajarish" tugmasini bosadi
        │  IPdfEngineService.Validate(request) → null bo'lsa davom etadi
        ▼
6.  ToolRequest {
          Tool        = ToolId.PdfToWord,
          InputFiles  = ["C:\\...\\hujjat.pdf"],
          OutputPath  = "C:\\...\\hujjat.docx",
          Options     = PdfToWordOptions { … },
          Password    = null
        }
        ▼
7.  IPdfEngineService.ExecuteAsync(request, progress, ct)
        │  PdfEngineService ichidagi switch: case ToolId.PdfToWord →
        ▼
8.  DocumentConversionService.PdfToWordAsync(pdfPath, docxPath, options, progress, ct)
        ▼
9.  Services\Conversion\PdfTextExtractor  (UglyToad.PdfPig)
        │  Sahifa → so'zlar → qatorlar → abzaslar; shrift, o'lcham, qalin/kursiv, koordinatalar
        │  (agar sahifada matn yo'q bo'lsa va rejim ruxsat bersa → OcrService bilan to'ldiriladi)
        ▼
10. DocumentContent  ← oraliq model (ContentPage → ParagraphBlock / TableBlock / ImageBlock)
        │  Bu model formatlardan mustaqil: undan .docx, .xlsx yoki .pptx yozish mumkin
        ▼
11. Services\Conversion\DocxWriter  (DocumentFormat.OpenXml)
        │  Abzaslar, sarlavhalar, jadvallar, rasmlar → WordprocessingDocument
        │  Yozish vaqtinchalik faylga, so'ng atomar ko'chirish
        ▼
12. hujjat.docx  →  ToolRunResult.Ok("Word hujjati tayyor", [docxPath])
        ▼
13. ToolWorkspaceViewModel natijani ko'rsatadi: "Papkani ochish" / "Faylni ochish" tugmalari
```

**Diqqatga sazovor tomon:** 9–11 qadamlar orasida **rasm yo'q**. Matn rasterizatsiya
qilinmaydi — u belgi sifatida o'qiladi va belgi sifatida yoziladi, shuning uchun natijadagi
`.docx` to'liq tahrirlanadi. Rasterizatsiya faqat ikki holatda ishlatiladi: eskizlar va OCR.

---

## 6. Modullar (17 ta vosita) jadvali

Manba: `Models\ToolDescriptor.cs` → `ToolCatalog.All`.

### 1-guruh — Sahifalar bilan ishlash

| # | Vosita (`ToolId`) | Kirish | Chiqish | Qaysi servis bajaradi | Kutubxona |
|---|---|---|---|---|---|
| 1 | PDF birlashtirish (`Merge`) | Bir nechta PDF | `.pdf` | `IPdfManipulatorService.MergePdfsAsync` / `IPdfService.BuildPdfAsync` | PDFsharp |
| 2 | PDF bo'lish (`Split`) | Bitta PDF | Papka | `IPdfManipulatorService.SplitPdfAsync` | PDFsharp |
| 3 | Sahifalarni tartiblash (`Organize`) | Bitta PDF | `.pdf` | `IPdfService.BuildPdfAsync` | PDFsharp + pdfium (eskiz) |
| 4 | Sahifalarni burish (`Rotate`) | Bitta PDF | `.pdf` | `IPdfManipulatorService.RotatePagesAsync` | PDFsharp |

### 2-guruh — Konvertatsiya

| # | Vosita (`ToolId`) | Kirish | Chiqish | Qaysi servis bajaradi | Kutubxona |
|---|---|---|---|---|---|
| 5 | PDF → Word (`PdfToWord`) | Bitta PDF | `.docx` | `IDocumentConversionService.PdfToWordAsync` | PdfPig → OpenXML |
| 6 | Word → PDF (`WordToPdf`) | `.docx` / `.doc` | `.pdf` | `IDocumentConversionService.WordToPdfAsync` | Word COM, aks holda OpenXML + PDFsharp |
| 7 | PDF → Rasm (`PdfToImage`) | Bitta PDF | Papka (JPG/PNG) | `IDocumentConversionService.PdfToImagesAsync` | pdfium + SkiaSharp |
| 8 | Rasm → PDF (`ImageToPdf`) | Rasmlar | `.pdf` | `IPdfService.ConvertImagesToPdfAsync` | PDFsharp |
| 9 | PDF → Excel (`PdfToExcel`) | Bitta PDF | `.xlsx` | `IDocumentConversionService.PdfToExcelAsync` | PdfPig → OpenXML |
| 10 | PDF → PowerPoint (`PdfToPowerPoint`) | Bitta PDF | `.pptx` | `IDocumentConversionService.PdfToPowerPointAsync` | PdfPig → OpenXML |

### 3-guruh — Optimizatsiya va xavfsizlik

| # | Vosita (`ToolId`) | Kirish | Chiqish | Qaysi servis bajaradi | Kutubxona |
|---|---|---|---|---|---|
| 11 | PDF siqish (`Compress`) | Bitta PDF | `.pdf` | `IPdfManipulatorService.CompressPdfAsync` | PDFsharp + SkiaSharp |
| 12 | PDF himoyalash (`Protect`) | Bitta PDF | `.pdf` | `IPdfManipulatorService.ProtectPdfAsync` | PDFsharp |
| 13 | Qulfni ochish (`Unlock`) | Bitta PDF | `.pdf` | `IPdfManipulatorService.UnlockPdfAsync` | PDFsharp |
| 14 | Suv belgisi (`Watermark`) | Bitta PDF | `.pdf` | `IPdfManipulatorService.AddWatermarkAsync` | PDFsharp |
| 15 | Sahifa raqamlari (`PageNumbers`) | Bitta PDF | `.pdf` | `IPdfManipulatorService.AddPageNumbersAsync` | PDFsharp |

### 4-guruh — Sun'iy intellekt

| # | Vosita (`ToolId`) | Kirish | Chiqish | Qaysi servis bajaradi | Kutubxona |
|---|---|---|---|---|---|
| 16 | OCR: skaner → Word (`OcrToWord`) | Bitta PDF | `.docx` | `IDocumentConversionService.OcrPdfToWordAsync` → `IOcrService` | pdfium + Tesseract → OpenXML |
| 17 | Orqa fonni olib tashlash (`BackgroundRemover`) | Rasmlar | `.png` | `IImageBackgroundRemover.RemoveBackgroundToBitmapAsync` | ONNX Runtime (u2net) + SkiaSharp |

> `ToolDescriptor` ikkita hisoblanadigan xossaga ega:
> `ShowsPageThumbnails` (Organize, Rotate, Split, Merge) — ishchi oynada eskizlar to'ri
> ko'rsatiladi; `WritesToFolder` (Split, PdfToImage) — natija bitta fayl emas, papka.

> **Ekran yozuvi bu jadvalda yo'q.** U `ToolCatalog` ga kirmaydi, `ToolId` qiymati ham
> yo'q va `PdfEngineService.ExecuteAsync` ga tegmaydi: yon paneldagi alohida bo'lim
> `ScreenRecorderViewModel` → `IScreenRecorderService` bilan to'g'ridan-to'g'ri ishlaydi
> (1- va 4-bo'limlarga qarang).

---

## 7. Kutubxonalar va litsenziyalar

| Paket | Versiya | Vazifasi | Litsenziya |
|---|---|---|---|
| `PDFsharp` | 6.2.4 | PDF **yozish**: merge, split, rotate, protect, watermark, rasm joylash | MIT |
| `PDFtoImage` (pdfium) | 5.3.0 | PDF sahifalarini **rasmga aylantirish** (eskiz, OCR kirishi, PDF → rasm) | MIT (pdfium — BSD 3-Clause) |
| `SkiaSharp` | 4.150.1 | Rastr grafika: o'lcham o'zgartirish, JPEG/PNG kodlash, alfa kanal | MIT |
| `UglyToad.PdfPig` | 1.7.0-custom-5 | PDF dan **matn, shrift va koordinatalarni o'qish** | Apache-2.0 |
| `DocumentFormat.OpenXml` | 3.5.1 | `.docx` / `.xlsx` / `.pptx` yozish va o'qish | MIT |
| `Tesseract` | 5.2.0 | OCR — skaner qilingan sahifalardan matn tanish | Apache-2.0 |
| `Microsoft.ML.OnnxRuntime` | 1.20.1 | u2net segmentatsiya modelini ishga tushirish | MIT |
| `ScreenRecorderLib` | 6.6.0 | Ekranni videoga yozish: Windows Media Foundation (H.264/H.265) + WASAPI ovozi | MIT |
| `CommunityToolkit.Mvvm` | 8.4.2 | `[ObservableProperty]`, `[RelayCommand]` source generatorlari | MIT |
| `Microsoft.Extensions.DependencyInjection` | 9.0.0 | Composition root, servislarni ro'yxatga olish | MIT |
| .NET 8 runtime (dastur ichida) | 8.x | O'zi-yetarli tarqatish | MIT |

### Nega ikkita PDF kutubxonasi kerak

| Vazifa | PDFsharp | PdfPig | pdfium |
|---|---|---|---|
| PDF yozish / o'zgartirish | ✅ | ❌ | ❌ |
| Matnni koordinatasi bilan o'qish | ❌ | ✅ | qisman |
| Sahifani rasmga chizish | ❌ | ❌ | ✅ |

Uchalasi bir-birini to'ldiradi: **pdfium chizadi, PdfPig o'qiydi, PDFsharp yozadi.**

### `ScreenRecorderLib` — ikkita paketlash nozikligi

Bu paket boshqalar kabi "qo'shdim va ketdi" emas. `Yordamchi.csproj` dagi ikkita
qo'shimcha xususiyat aynan shu sababli turibdi:

```xml
<PackageReference Include="ScreenRecorderLib" Version="6.6.0"
                  GeneratePathProperty="true"
                  ExcludeAssets="build;buildTransitive" />
<Reference Include="ScreenRecorderLib">
  <HintPath>$(PkgScreenRecorderLib)\build\x64\ScreenRecorderLib.dll</HintPath>
  <Private>true</Private>
</Reference>
```

**1. Paketda `lib\` papkasi yo'q.** Odatdagi NuGet paketi yig'ilmani `lib\<tfm>\` dan
beradi; bu paket esa `build\ScreenRecorderLib.targets` orqali havolani `$(Platform)`
qiymatiga qarab qo'shadi va `AnyCPU` da ochiqchasiga xato beradi. Loyihada
`$(Platform)` = `AnyCPU` (x64 lik `PlatformTarget` bilan ta'minlanadi), shuning uchun:

- `ExcludeAssets="build;buildTransitive"` — paketning `.targets` fayli umuman
  ulanmaydi, ya'ni `$(Platform)` tekshiruvi ishga tushmaydi;
- `GeneratePathProperty="true"` — MSBuild `$(PkgScreenRecorderLib)` o'zgaruvchisini
  yasaydi (paketning diskdagi yo'li);
- `<Reference>` — x64 yig'ilmasiga to'g'ridan-to'g'ri havola, `<Private>true</Private>`
  bilan chiqishga ko'chiriladi.

Shu tufayli `.sln` dagi "Any CPU" konfiguratsiyasiga umuman tegish shart emas.

**2. `ScreenRecorderLib.dll` — C++/CLI aralash (IJW) yig'ilma.** Uni
`PublishSingleFile` to'plamiga **joylashtirib bo'lmaydi**: single-file host aralash
rejimli yig'ilmani xotiradan yuklay olmaydi va `BadImageFormatException` chiqadi.

Hozirgi tarqatish sxemasi bunga tegmaydi: `build-installer.ps1` **papkali**
self-contained publish qiladi (`-r win-x64 --self-contained true
-p:PublishReadyToRun=true`), ya'ni barcha DLL lar yonma-yon yotadi. Agar kelajakda
single-file ga o'tilsa, bu fayl to'plamdan ochiq holda chiqarib qo'yilishi shart
(`ExcludeFromSingleFile`).

**3. Ish vaqti bog'liqligi.** C++/CLI kodi Visual C++ 2015–2022 ish vaqtiga
(`MSVCP140.dll`, `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`, `CONCRT140.dll`) tayanadi,
u esa Windows tarkibida kelmaydi. Shu sababli `installer\Bundle.wxs` ga zanjir qo'shildi:

| Bosqich | Nima bo'ladi |
|---|---|
| `util:RegistrySearch` | `HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64` → `Installed` o'qiladi ("14.0" — 2015/2017/2019/2022 uchun umumiy kalit) |
| `ExePackage VCRedistX64` | `DetectCondition="VCRedistX64Installed = 1"` — faqat yo'q bo'lsagina `/install /quiet /norestart` bilan o'rnatiladi |
| `Permanent="yes"` | Yordamchi o'chirilganda ish vaqti olib tashlanmaydi — u boshqa dasturlarga ham kerak |
| `Compressed="yes"` | `vc_redist.x64.exe` bundle ichiga joylanadi: o'rnatuvchi internetsiz ishlaydi |
| `MsiPackage` | Shundan keyin asosiy MSI o'rnatiladi |

`build-installer.ps1` `vc_redist.x64.exe` ni `https://aka.ms/vs/17/release/vc_redist.x64.exe`
dan **bir marta** `artifacts\` ga yuklab oladi va keyingi yig'ilishlarda qayta ishlatadi.
Bundle uchun `WixToolset.Util.wixext` kengaytmasi ham talab qilinadi.

---

## 8. Tashqi resurslar: OCR tillari va AI modeli

Ikkala resurs ham hajmi katta bo'lgani uchun o'rnatuvchiga **qo'shilmaydi** — birinchi
foydalanishda yuklab olinadi yoki qo'lda joylashtiriladi. Ular yo'q bo'lsa,
`CheckPrerequisites` ogohlantirish qaytaradi va tegishli vosita ishga tushmaydi
(`PdfErrorKind.MissingComponent`), dasturning qolgan 15 ta vositasi esa normal ishlayveradi.

Ikkalasi ham **dastur ichidan** yuklab olinadi — bir xil naqsh bo'yicha:

| Bosqich | Kim bajaradi |
|---|---|
| Nima yetishmayotganini aniqlash | `IPdfEngineService.GetMissingComponent` → `DownloadableComponent` |
| Ogohlantirish matni | `IPdfEngineService.CheckPrerequisites` (o'sha natijadan kelib chiqadi) |
| "Yuklab olish" tugmasi | `ToolWorkspaceViewModel.DownloadMissingComponentCommand`, shuningdek `AboutViewModel` va `BackgroundRemoverViewModel` da |
| Yuklashning o'zi | `IOcrService.DownloadLanguagesAsync` / `IImageBackgroundRemover.DownloadModelAsync` |

Uchinchi tashqi bog'liqlik — **Microsoft Word** — ataylab bu ro'yxatga kirmaydi:
u foydalanuvchi o'zi o'rnatadigan dastur, shuning uchun `GetMissingComponent` u yerda doim
`None` qaytaradi va faqat ogohlantirish matni ko'rsatiladi.

### 8.1. Tesseract til fayllari (`tessdata`)

| Xususiyat | Qiymat |
|---|---|
| Fayl nomlari | `uzb.traineddata`, `eng.traineddata`, `rus.traineddata` |
| Ombor | `tessdata_fast` — tez va aniqligi kundalik ish uchun yetarli |
| Yuklab olish manzili | `https://github.com/tesseract-ocr/tessdata_fast/raw/main/<til>.traineddata` |
| Taxminiy hajm | Har biri ~1–5 MB |
| Til ifodasi | `uzb+eng+rus` ko'rinishida bir nechta til birga beriladi |

Papka qidirish tartibi (`OcrService`):

1. `TESSDATA_PREFIX` muhit o'zgaruvchisi (papkaning o'ziga yoki otasiga ishora qilishi mumkin);
2. `<dastur papkasi>\tessdata` — masalan `C:\Program Files\Yordamchi\tessdata`;
3. `%LOCALAPPDATA%\Yordamchi\tessdata`.

> **Muhim:** avtomatik yuklab olish **doim** `%LOCALAPPDATA%\Yordamchi\tessdata` ga yoziladi,
> chunki `Program Files` ga yozish uchun administrator huquqi kerak. Fayl avval vaqtinchalik
> nomga yuklanadi — ulanish uzilsa, buzuq `.traineddata` qolib ketmaydi.

### 8.2. AI modeli (`u2net.onnx`)

| Xususiyat | Qiymat |
|---|---|
| Fayl nomlari | `u2net.onnx` (~168 MB) yoki yengilroq `u2netp.onnx` (~4,7 MB) |
| Model kirishi | 320×320 RGB, ImageNet normalizatsiyasi |
| Manba | `https://github.com/danielgatis/rembg` (releases: `v0.0.0/u2net.onnx`, `v0.0.0/u2netp.onnx`) |
| Dastur nimani yuklaydi | Faqat to'liq `u2net.onnx` — `u2netp` sochlar kabi nozik chekkalarda sezilarli yomonroq |
| Litsenziya | MIT |

Qidirish tartibi (`OnnxBackgroundRemover`):

1. `<dastur papkasi>\Models\u2net.onnx`
2. `<dastur papkasi>\Models\u2netp.onnx`
3. `%LOCALAPPDATA%\Yordamchi\Models\u2net.onnx`
4. `%LOCALAPPDATA%\Yordamchi\Models\u2netp.onnx`

Hech biri topilmasa, `ModelPath` foydalanuvchi yozish huquqiga ega bo'lgan
`%LOCALAPPDATA%\Yordamchi\Models\u2net.onnx` yo'lini qaytaradi va xato xabarida aynan shu
manzil ko'rsatiladi.

> **Muhim:** OCR til fayllari kabi, model ham avval `.tmp` nomiga yoziladi va faqat to'liq
> yuklangach asl nomiga ko'chiriladi. Bu 168 MB lik yuklashda ayniqsa muhim: ulanish uzilsa
> yoki foydalanuvchi bekor qilsa, yarim fayl "model bor" deb hisoblanib qolmaydi.

> `ModelPath` har chaqiruvda qaytadan qidiradi, `InferenceSession` esa kech (lazy) ochiladi
> va qaysi fayl uchun ochilgani `_sessionModelPath` da eslab qolinadi. Shu ikkisi tufayli
> model yuklab olingandan keyin **dasturni qayta ishga tushirish shart emas**.

> ONNX sessiyasini yaratish bir necha soniya oladi, shuning uchun u **birinchi chaqiruvda**
> yaratiladi va keyin qayta ishlatiladi (lazy singleton).

---

## 9. Kengaytirish qo'llanmasi: yangi vosita qo'shish

Yangi vosita qo'shish uchun **4 qadam** kifoya. Ekran tuzilishi (`DashboardView`,
`ToolWorkspaceView`) o'zgartirilmaydi — kartochka ham, ishchi oyna ham katalogdan avtomatik
quriladi; qo'lda yoziladigan yagona UI qismi — sozlamalar panelining `DataTemplate` i.

### 1-qadam — `ToolCatalog` ga yozuv

`Models\ToolDescriptor.cs`:

```csharp
// a) enum ga yangi qiymat qo'shing (oxiriga — eski qiymatlar tartibi buzilmasin)
public enum ToolId
{
    …
    RedactText   // ← yangi
}

// b) ToolCatalog.All ro'yxatiga tavsif qo'shing
new(ToolId.RedactText, "Matnni yashirish",
    "Hujjatdagi maxfiy so'zlarni qora to'rtburchak bilan yoping.",
    "\uE77A", ToolCategory.Optimize, ToolInputKind.SinglePdf, "#12A594", ".pdf"),
```

Shu bilan bosh sahifada kartochka **avtomatik** paydo bo'ladi.
Kerak bo'lsa `ShowsPageThumbnails` / `WritesToFolder` xossalariga ham yangi `ToolId` ni
qo'shing.

### 2-qadam — sozlamalar modeli

`Models\OperationOptions.cs` (yoki `ConversionOptions.cs`) ga oddiy POCO qo'shing:

```csharp
/// <summary>"Matnni yashirish" vositasi sozlamalari.</summary>
public sealed class RedactOptions
{
    /// <summary>Yashiriladigan so'zlar ro'yxati.</summary>
    public List<string> Terms { get; set; } = [];

    /// <summary>Katta-kichik harf farqlansinmi.</summary>
    public bool CaseSensitive { get; set; }
}
```

Bu obyekt `ToolRequest.Options` maydoniga tushadi.

### 3-qadam — sozlamalar ViewModel'i va `DataTemplate`

`ViewModels\ToolOptionsViewModels.cs` ga VM qo'shing. Asos sinf ikkita a'zoni beradi:
majburiy `ToModel()` (sozlamalarni servis qatlami tushunadigan POCO ga aylantiradi) va
ixtiyoriy `Validate()` (faqat UI da ko'rinadigan tekshiruvlar — masalan "parolni tasdiqlash"
maydoni modelga tushmagani uchun uni dvigatel ko'ra olmaydi):

```csharp
public sealed partial class RedactOptionsViewModel : ToolOptionsViewModel
{
    [ObservableProperty] private string _terms = string.Empty;
    [ObservableProperty] private bool _caseSensitive;

    public override object ToModel() => new RedactOptions
    {
        Terms = Terms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        CaseSensitive = CaseSensitive
    };

    // Ixtiyoriy: "Bajarish" bosilganda ishlaydi, xato matni qaytsa amal boshlanmaydi.
    public override string? Validate()
        => string.IsNullOrWhiteSpace(Terms) ? "Kamida bitta so'z kiriting." : null;
}
```

Sozlama panellarining barcha `DataTemplate` lari `Views\ToolOptionTemplates.xaml` da turadi
(`ToolWorkspaceView.xaml` faqat ularni `ResourceDictionary` sifatida ulaydi). Yangi shablonni
o'sha faylga qo'shing — u VM turiga qarab avtomatik tanlanadi. Fayldagi alias `viewModels:`:

```xml
<DataTemplate DataType="{x:Type viewModels:RedactOptionsViewModel}">
    <StackPanel>
        <TextBlock Text="Yashiriladigan so'zlar" Style="{StaticResource OptionLabelStyle}" />
        <TextBox Style="{DynamicResource AppTextBoxStyle}"
                 Text="{Binding Terms, UpdateSourceTrigger=PropertyChanged}" />
        <CheckBox Style="{DynamicResource AppCheckBoxStyle}"
                  Content="Katta-kichik harf farqlansin" IsChecked="{Binding CaseSensitive}" />
    </StackPanel>
</DataTemplate>
```

Nihoyat, `ToolWorkspaceViewModel.CreateOptions` ichidagi `switch` ga yangi `ToolId` uchun
VM ni qaytaradigan qator qo'shing — aks holda panel bo'sh qoladi.

### 4-qadam — `PdfEngineService.ExecuteAsync` ga `case`

`Services\PdfEngineService.cs`:

```csharp
case ToolId.RedactText:
{
    var options = request.Options as RedactOptions ?? new RedactOptions();
    await Documents.RedactAsync(
        request.InputFiles[0], request.OutputPath!, options, progress, cancellationToken);

    return ToolRunResult.Ok("Matn muvaffaqiyatli yashirildi", request.OutputPath!);
}
```

Zarur bo'lsa `Validate` va `CheckPrerequisites` ga ham tegishli tekshiruvlarni qo'shing
(masalan: "kamida bitta so'z kiriting").

### Tekshiruv ro'yxati

- [ ] `ToolId` ga qiymat qo'shildi
- [ ] `ToolCatalog.All` ga `ToolDescriptor` qo'shildi (Glyph — Segoe Fluent Icons kodi)
- [ ] Kerak bo'lsa `ShowsPageThumbnails` / `WritesToFolder` yangilandi
- [ ] Sozlamalar modeli (POCO) yozildi
- [ ] Options VM (`ToModel`, kerak bo'lsa `Validate`) yozildi
- [ ] `DataTemplate` `Views\ToolOptionTemplates.xaml` ga qo'shildi
- [ ] `ToolWorkspaceViewModel.CreateOptions` ga yangi `case` qo'shildi
- [ ] Servis metodi va interfeysi yozildi
- [ ] `ExecuteAsync` ga `case` qo'shildi
- [ ] `Validate` / `CheckPrerequisites` yangilandi
- [ ] Xatolar `PdfServiceException` orqali qaytadi

---

## 10. Ko'ndalang qarorlar

**Asosiy primitiv — `BuildPdfAsync(IReadOnlyList<PageEdit>, …)`.** To'rtta amal ham bitta
metodga keltiriladi:

| Amal | `PageEdit` ro'yxati qanday bo'ladi |
|---|---|
| Tartiblash | O'sha sahifalar, boshqa tartibda |
| O'chirish | Ba'zi sahifalar ro'yxatga kirmaydi |
| Burish | `Rotation` maydoni o'zgaradi |
| Birlashtirish | Ro'yxatda bir nechta `SourceFilePath` bo'ladi |

Shuning uchun tahrirlash **destruktiv emas**: foydalanuvchi saqlamaguncha diskda hech narsa
o'zgarmaydi, va ekrandagi kolleksiyaning o'zi yagona haqiqat manbai.

**Yozish atomar.** Har bir yozish yonidagi `.tmp-<guid>` fayliga boradi va faqat
muvaffaqiyatdan keyin nishonga ko'chiriladi. Manba avval xotiraga o'qiladi — shuning uchun
**ochilgan faylning ustiga saqlash ishlaydi**, va muvaffaqiyatsiz amal nishon faylni
buzmaydi.

**Barcha nosozlik — bitta tur.** Har qanday kutilgan xato `PdfServiceException` +
`PdfErrorKind` (masalan `PasswordProtected`, `InvalidPassword`, `MissingComponent`,
`CorruptFile`) ko'rinishida yuqoriga chiqadi; `ViewModelBase` uni foydalanuvchiga tushunarli
matnga aylantiradi.

**Barcha uzoq amal asinxron va bekor qilinadi.** CPU ni band qiladigan ish thread pool da
bajariladi, `IProgress<PdfProgress>` orqali holat xabar qilinadi,
`CancellationToken` esa "Bekor qilish" tugmasiga bog'langan.

**Eskizlar `Freeze()` qilinadi.** Rasterizatsiya thread pool da bo'ladi; muzlatilgan
`BitmapImage` UI oqimiga marshalling'siz uzatiladi.

**Burish ikki joyda.** UI da `LayoutTransform` (bir zumda, qayta render qilinmaydi),
eksportda esa PDF sahifasining `/Rotate` qiymati. Piksel qayta ishlanmaydi.

**Mavzu almashtirish jonli.** `ThemeService` faqat `MergedDictionaries[0]` ni almashtiradi;
`Controls.xaml` ranglarga faqat `DynamicResource` orqali murojaat qilgani uchun butun oyna
bitta ham kontrol qayta yaratilmasdan bo'yaladi. Sarlavha paneli `DwmSetWindowAttribute`
bilan moslashadi.

**Mica xavfsiz degradatsiya qiladi.** Windows 10 da yoki DWM rad etsa,
`WindowBackdrop.TryApplyMica` `false` qaytaradi va oyna oddiy fon rangiga qaytadi.

**Drag-drop tugmalarni o'g'irlamaydi.** Sichqoncha bosilganda vizual daraxt yuqoriga
yuriladi; agar karta konteyneridan oldin `ButtonBase` topilsa, drag boshlanmaydi — shuning
uchun kartadagi "o'chirish"/"burish" tugmalari ishlayveradi.

**Ekran yozuvi — holatga asoslangan, "band" qoplamasisiz.** Boshqa barcha sahifalar
`ViewModelBase.RunAsync` bilan ishlaydi: amal boshlanadi, UI bloklanadi, natija qaytadi.
Ekran yozuvi boshqacha — u soatlab davom etishi mumkin, shu vaqt ichida foydalanuvchi
dastur bilan (ko'pincha esa umuman boshqa dastur bilan) ishlashda davom etadi. Shuning
uchun `ScreenRecorderViewModel` `RunAsync` ni ishlatmaydi: sahifa holati
`RecorderState` ga bog'langan, sozlamalar bloklari esa `IsIdleState` orqali
o'chiriladi. O'tgan vaqt `PeriodicTimer` bilan sekundiga bir marta yangilanadi
(`ConfigureAwait(true)` — davomi baribir UI oqimida), pauza vaqti umumiy hisobdan
chiqarib tashlanadi.

**Oynani kichraytirish — code-behind da.** `WindowState` — `Window` ustidagi amal,
ViewModel undan bexabar bo'lishi kerak. Shu sababli `ScreenRecorderViewModel` faqat
`MinimizeRequested` hodisasini ko'taradi, `MainWindow` esa unga obuna bo'lib
`WindowState = WindowState.Minimized` qiladi va oyna yopilganda obunani bekor qiladi.
Aks holda videoning boshida dasturning o'z oynasi ko'rinib qolardi.

**Yozuv dastur yopilganda ham to'g'ri yakunlanadi.** `ScreenRecorderService` singleton va
`IDisposable`; `ServiceProvider.Dispose` (`App.OnExit`) uni tozalaganda hali ketayotgan
yozuv `Stop()` bilan yakunlanadi. Bu majburiy: yakunlanmagan `.mp4` da `moov` atomi
yozilmay qoladi va fayl umuman ochilmaydi.

**Kelajakdagi ajratish.** `Models` + `Services` ni alohida `Yordamchi.Core` kutubxonasiga
ko'chirish mumkin; to'siqlar — `PageModel.Thumbnail` (`BitmapSource`),
`IImageBackgroundRemover.RemoveBackgroundAsync` ning `BitmapSource` qaytarishi va
`ScreenRecorderService` ning `Application.Current.Dispatcher` ga murojaati. Birinchi
ikkitasini `byte[]` yoki `SKBitmap` ga, uchinchisini esa `SynchronizationContext` ga
almashtirsangiz, Core sof `net8.0-windows` (WPF siz) bo'ladi. To'liq `net8.0` esa baribir
chiqmaydi: ekran yozuvi Windows Media Foundation ga bog'langan.

---

## Ma'lum cheklovlar

- **Virtualizatsiya yo'q.** Eskizlar `WrapPanel` da chiziladi; ~500+ sahifali hujjatda
  xotira va birinchi ochilish vaqti sezilarli bo'ladi.
- **Word → PDF sifati Word mavjudligiga bog'liq.** Microsoft Word o'rnatilgan bo'lsa
  (`OfficeWordInterop`) natija asl nusxaga juda yaqin; aks holda ichki
  `WordToPdfRenderer` ishlatiladi va murakkab formatlash soddalashtiriladi.
- **PDF → Excel jadval aniqlash evristik.** Chegara chiziqlari yo'q jadvallar ustun
  koordinatalari bo'yicha taxmin qilinadi.
- **Bookmark/outline va formalar saqlanmaydi** — PDFsharp sahifalarni import qilganda
  hujjat darajasidagi bu tuzilmalar ko'chirilmaydi.
- **OCR aniqligi manba sifatiga bog'liq.** 300 dpi va undan yuqori skanlar uchun natija
  yaxshi; qiyshiq yoki shovqinli rasmlarda xatolar bo'lishi mumkin.
- **Ekran yozuvi Windows 10 1903 (build 18362) dan boshlab ishlaydi.** Windows Graphics
  Capture aynan shu versiyada paydo bo'lgan. `IsSupported` shuni tekshiradi; `false`
  bo'lsa sahifa ochiladi, lekin qizil ogohlantirish ko'rsatiladi va `StartCommand`
  bajarilmaydi (`CanStart`). PDF vositalari bunga bog'liq emas.
- **Windows Media Foundation majburiy.** Kodlash (H.264/H.265) va ovoz uni talab qiladi.
  Windows ning **N/KN** nashrlarida Media Foundation yo'q — "Media Feature Pack" ni
  qo'shimcha o'rnatmasdan yozuv boshlanmaydi.
- **Visual C++ 2015–2022 (x64) ish vaqti — oldindan shart.** `ScreenRecorderLib.dll`
  C++/CLI yig'ilma bo'lgani uchun `MSVCP140.dll` / `VCRUNTIME140*.dll` ga tayanadi. Uni
  `YordamchiSetup.exe` zanjiri yo'q bo'lsa o'zi o'rnatadi (7-bo'limga qarang), lekin
  `dotnet run` yoki qo'lda ko'chirilgan publish papkasi bilan ishlaganda buni o'zingiz
  ta'minlashingiz kerak.
- **Bir vaqtda bitta manba.** `RecorderOptions.SourceOptions.RecordingSources` ga bitta
  element beriladi: bir nechta monitor yoki oynani birga yozish, hamda kamera (PiP)
  qo'shish qo'llab-quvvatlanmaydi.
- **Yozuv davomida sozlamalar o'zgarmaydi.** Manba, kodek, sifat va ovoz bloklari
  `IsIdleState` ga bog'langan; ularni almashtirish uchun yozuvni to'xtatish kerak.
- **Oyna rejimida oynani yopish/kichraytirish kadrlar oqimini to'xtatadi** — bu Windows
  Graphics Capture ning cheklovi, dastur uni aylanib o'ta olmaydi.
- **`ScreenRecorderLib.dll` `PublishSingleFile` ga sig'maydi.** Aralash rejimli (IJW)
  yig'ilma single-file to'plamdan yuklanmaydi; hozirgi papkali publish uchun muammo yo'q.

---

© 2026 Abduxalil Voxidjonov — [@abduxalilvoxidjonov](https://t.me/abduxalilvoxidjonov)
