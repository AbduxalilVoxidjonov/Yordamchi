# Yordamchi — arxitektura hujjati

**Versiya:** 2.3.0
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
11. [Testlar](#11-testlar)

---

## 1. Umumiy qarash

Yordamchi **Clean Architecture** tamoyillari ustiga qurilgan **MVVM** dasturi. Bog'liqlik
yo'nalishi faqat bitta tomonga — ichkariga (abstraksiyalar va modellar tomon) qaraydi.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Views (XAML)                                                            │
│  MainWindow, DashboardView, ToolWorkspaceView, BackgroundRemoverView,    │
│  ArchiveView, ScreenRecorderView, TransliterationView,                   │
│  NumberSystemView, RecordingOverlayWindow…                               │
│  Code-behind faqat InitializeComponent va HWND (Mica, oynani             │
│  kichraytirish/qaytarish, panelni yozuvdan yashirish) ishlari uchun.     │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  DataBinding / Command
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  ViewModels (CommunityToolkit.Mvvm)                                      │
│  MainViewModel, DashboardViewModel, ToolWorkspaceViewModel,              │
│  ArchiveViewModel, ScreenRecorderViewModel, TransliterationViewModel,    │
│  NumberSystemViewModel, …                                                │
│  WPF dialoglarini bilmaydi; faqat abstraksiyalarga tayanadi.             │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  interfeyslar
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Services.Abstractions (shartnomalar)                                    │
│  IPdfEngineService · IPdfService · IPdfManipulatorService ·              │
│  IDocumentConversionService · IOcrService · IImageBackgroundRemover ·    │
│  IArchiveService · IScreenRecorderService · IUpdateService ·             │
│  ITransliterationService · INumberSystemService ·                        │
│  IDialogService · IThemeService                                          │
└───────────────────────────────┬──────────────────────────────────────────┘
                                │  DI (Microsoft.Extensions.DependencyInjection)
                                ▼
┌──────────────────────────────────────────────────────────────────────────┐
│  Services (implementatsiya)                                              │
│  PdfEngineService (fasad) → PdfService, PdfManipulatorService,           │
│  DocumentConversionService, OcrService, OnnxBackgroundRemover            │
│  Services\Conversion\… — past darajali yordamchi yozuvchi/o'quvchilar    │
│  ScreenRecorderService, ArchiveService, UpdateService,                   │
│  TransliterationService, NumberSystemService — fasaddan TASHQARIDA       │
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

### Nega ekran yozuvi, arxiv, yangilanish va o'girish fasadga kirmaydi

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

// Ekran yozuvi va arxivlash PDF quvuriga umuman aloqador emas, shuning uchun ular
// fasadga qo'shilmaydi va o'z sahifalari bilan to'g'ridan-to'g'ri ishlaydi.
services.AddSingleton<IScreenRecorderService, ScreenRecorderService>();
services.AddSingleton<IArchiveService, ArchiveService>();

// Yangilanish tekshiruvi ham PDF quvuriga aloqador emas: uning kirishi — GitHub relizi.
// Singleton — muvaffaqiyatli tekshiruv natijasi shu nusxada keshlanadi.
services.AddSingleton<IUpdateService, UpdateService>();

// Kirill ↔ lotin o'girish ham shu qatorda: kirishi — oddiy matn yoki Word hujjati.
services.AddSingleton<ITransliterationService, TransliterationService>();

// Sanoq sistemalari — sof hisob: na fayl, na PDF.
services.AddSingleton<INumberSystemService, NumberSystemService>();
```

**`IArchiveService` ham xuddi shu sababdan tashqarida.** U PDF ni umuman bilmaydi, boshqa
kutubxonalarga (SharpCompress, SharpZipLib) tayanadi va o'z xato holatlariga ega (parol
noto'g'ri, format tanilmadi, arxiv xavfli yozuv saqlaydi). Uni fasadga qo'shish
`IPdfEngineService` ni "PDF dvigateli" dan "hamma narsaning ro'yxati" ga aylantirib
yuborardi. Farqi ekran yozuvidan bittasi bor: arxivlash — <b>bir martalik amal</b>, ya'ni
ekran yozuvi kabi holatga asoslangan seans emas; shuning uchun u `ViewModelBase.RunAsync`
ning odatdagi progress/bekor qilish oqimidan foydalanadi.

**`ITransliterationService` ham xuddi shu sababdan tashqarida.** Uning kirishi — foydalanuvchi
yozgan matn yoki `.docx`/`.txt` fayl, chiqishi ham shunday; PDF ga hech qanday aloqasi yo'q.
Bitta farqi bor: **matn rejimi umuman asinxron emas.** `ConvertText` — sof, tez va sinxron
metod, chunki har bosishda natija darhol ko'rinishi kerak; bu yerda `Task` qaytarish faqat
keraksiz kontekst almashinuvi bo'lardi. Fayllar bilan ishlash esa odatdagidek asinxron va
`ViewModelBase.RunAsync` ning progress/bekor qilish oqimidan foydalanadi.

**`IUpdateService` — uchinchi shunday shartnoma.** Uning kirishi internetdagi reliz,
chiqishi esa foydalanuvchiga ko'rsatiladigan xabar: bu yerda na `ToolRequest`,
na `ToolRunResult` ning ma'nosi bor. Uni fasadga qo'shish "PDF dvigateli" ga PDF ga
umuman aloqasi yo'q to'rtinchi mas'uliyat qo'shgan bo'lardi. Xatolarni bir xil qilish
uchun u ham `PdfServiceException` tashlaydi, ya'ni `ViewModelBase.RunAsync` uni odatdagidek
tushunarli xabarga aylantiradi. Singleton bo'lishining o'z sababi bor: muvaffaqiyatli
tekshiruv natijasi shu nusxada keshlanadi, shuning uchun takroriy tekshiruvda GitHub ga
ikkinchi so'rov ketmaydi.

Singleton tanlanishining amaliy sababi ham bor: `IScreenRecorderService` —
`IDisposable`, va `ServiceProvider` dastur yopilganda uni `Dispose` qiladi. `Dispose`
esa hali yozilayotgan faylni to'g'ri yakunlaydi, aks holda `.mp4` da `moov` atomi
yozilmay qoladi va fayl umuman ochilmaydi.

Yon paneldagi yettita bo'lim (`MainViewModel.NavigationItems`):

| # | Bo'lim | Sahifa (`ViewModelBase.Title`) |
|---|---|---|
| 0 | PDF vositalari | `DashboardViewModel` |
| 1 | Arxiv | `ArchiveViewModel` |
| 2 | Ekran yozuvi | `ScreenRecorderViewModel` |
| 3 | Kirill ↔ Lotin | `TransliterationViewModel` |
| 4 | Sanoq sistemasi | `NumberSystemViewModel` |
| 5 | Kompyuterlarni boshqarish | `RemoteControlViewModel` |
| 6 | Dastur haqida | `AboutViewModel` |

Yangi versiya topilganda "Dastur haqida" bandi yonida kichik nuqta ko'rinadi
(`NavigationItemViewModel.HasNotification`). Tekshiruvning o'zi `AboutViewModel` da qoladi,
`MainViewModel` esa faqat uning natijasini yon panelga ko'zgu qiladi — shu tufayli GitHub
ga so'rov yuboradigan joy bitta bo'lib qolaveradi.

#### Yon panelni yig'ish

`MainViewModel.IsNavigationCollapsed` — sof ko'rinish holati, `ToggleNavigationCommand`
uni almashtiradi. Yig'ilgan holatda panel **264 → 68** nuqtaga torayadi: nomlar, brend
matni va quyi imzo yashirinadi, nishonlar markazga tushadi.

| Qaror | Nega |
|---|---|
| Kenglik `Border` da o'zgaradi, `ColumnDefinition` esa `Auto` | `GridLength` ni standart animatsiya bilan o'zgartirib bo'lmaydi — buning uchun maxsus `GridLengthAnimation` yozish kerak bo'lardi |
| 0,18 s `DoubleAnimation` (`CubicEase`) | Kenglik sakrab o'zgarsa, kontent ham sakraydi |
| Nishonlar `Grid.ColumnSpan="3"` bilan markazga tushadi | Ustunlar kengligini triggerdan o'zgartirib bo'lmaydi; nishonni butun kenglikka yoyish soddaroq |
| Yangi versiya nuqtasi nishon burchagiga ko'chadi | Yig'ilganda uni butunlay yashirish yangi versiya haqidagi yagona ishorani yo'qotardi |
| Holat diskka yozilmaydi | Dasturda umuman sozlama saqlaydigan joy yo'q (mavzu ham shunday); bitta bayroq uchun yangi mexanizm o'ylab topish nomutanosib bo'lardi |

---

## 2. Papkalar va sinflar xaritasi

> Quyidagi daraxt `src\Yordamchi` papkasining haqiqiy tarkibi. Ba'zi fayllar 2.x ishlab
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
   │  ├─ ArchiveModels.cs        Arxiv: ArchiveFormat / ArchiveCompressionLevel / ZipEncryption
   │  │                          enum'lari + ArchiveEntryInfo / ArchiveInfo / CreateArchiveOptions
   │  ├─ ScreenRecording.cs       Ekran yozuvi: RecordingSourceKind / RecordingSourceInfo /
   │  │                           AudioDeviceInfo / VideoEncoderKind / RecordingQuality /
   │  │                           RecorderState + ScreenRecordingOptions
   │  ├─ NumberSystem.cs          Sanoq sistemalari: NumberConversionResult +
   │  │                           ConversionExplanationSection (qadam-baqadam yechim bo'limi)
   │  ├─ TransliterationOptions.cs  O'girish: TransliterationDirection / ApostropheStyle
   │  │                           enum'lari + TransliterationOptions / TransliterationFileResult
   │  ├─ UpdateInfo.cs            Topilgan yangilanish: versiya, teg, reliz nomi, havola, hajm
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
   │  │  ├─ IArchiveService.cs          Arxivlarni o'qish/ochish/yaratish (fasadga kirmaydi)
   │  │  ├─ IScreenRecorderService.cs   Ekran yozuvi (fasadga kirmaydi) + hodisa argumentlari
   │  │  ├─ IUpdateService.cs           Yangi versiya bor-yo'qligini tekshirish (fasadga kirmaydi)
   │  │  ├─ ITransliterationService.cs  Kirill ↔ lotin o'girish (fasadga kirmaydi)
   │  │  ├─ INumberSystemService.cs     Sanoq sistemalari (fasadga kirmaydi)
   │  │  ├─ IRemoteControlService.cs    Boshqaruv agentini GitHub'dan yuklab olish (fasadga kirmaydi)
   │  │  ├─ IDialogService.cs           Fayl/papka dialoglari, xabar oynalari
   │  │  └─ IThemeService.cs            Light/Dark almashtirish + tizim sozlamasini kuzatish
   │  │
   │  ├─ PdfEngineService.cs      Fasad: ToolId → mos servis; Validate va CheckPrerequisites shu yerda
   │  ├─ PdfService.cs            pdfium bilan rasterizatsiya + PDFsharp bilan sahifa rejasini yozish
   │  ├─ PdfManipulatorService.cs Hujjat darajasidagi amallar (PDFsharp): merge, split, compress, protect…
   │  ├─ DocumentConversionService.cs  Konvertatsiya orkestratori: extractor → model → writer
   │  ├─ OcrService.cs            Tesseract; tessdata papkasini topadi va til fayllarini yuklab oladi
   │  ├─ OnnxBackgroundRemover.cs u2net/u2netp ONNX modeli, maska → alfa kanal
   │  ├─ ArchiveService.cs        SharpCompress bilan o'qish, SharpZipLib bilan parolli ZIP
   │  │                           yozish; chiqarishda "Zip Slip" tekshiruvi
   │  ├─ ScreenRecorderService.cs ScreenRecorderLib (Media Foundation) qobig'i; hodisalarni
   │  │                           UI oqimiga o'tkazadi, sifat → bitrate ni o'zi hisoblaydi
   │  ├─ UpdateService.cs         GitHub relizlari API si; aktiv nomi va xost tekshiruvlari,
   │  │                           natijani jarayon davomida keshlash
   │  ├─ NumberSystemService.cs   NumberBaseConverter ustidagi yupqa qobiq (shartnoma uchun)
   │  ├─ RemoteControlService.cs  Boshqaruv agentini GitHub'dan yuklab olish (host tekshiruvi,
   │  │                           progress bilan oqim, .tmp → ko'chirish); faylni ishga tushirmaydi
   │  ├─ TransliterationService.cs Matn va fayl o'girish orkestratori: nom tanlash, kodlashni
   │  │                           aniqlash, vaqtinchalik faylga yozib so'ng o'z o'rniga ko'chirish
   │  ├─ DialogService.cs         Win32 fayl dialoglari, MessageBox, clipboard — UI ning
   │  │                           yagona kirish nuqtasi
   │  ├─ ThemeService.cs          MergedDictionaries[0] ni almashtirish, DWM sarlavha rangi
   │  │
   │  └─ Conversion/              ── Past darajali o'quvchi/yozuvchilar (bitta format = bitta fayl)
   │     ├─ PdfTextExtractor.cs   PdfPig: matn, shrift, o'lcham, koordinata → DocumentContent
   │     ├─ DocxWriter.cs         OpenXML: DocumentContent → .docx (abzas, jadval, sarlavha)
   │     ├─ WordToPdfRenderer.cs  Word o'rnatilmagan holat uchun OpenXML → PDF (PDFsharp) renderer
   │     ├─ OfficeWordInterop.cs  Microsoft Word COM (late binding) orqali eng aniq .docx → PDF
   │     ├─ XlsxWriter.cs         OpenXML: jadvallar → .xlsx kitob
   │     ├─ PptxWriter.cs         OpenXML: har bir sahifa matni → .pptx slayd
   │     ├─ UzbekTransliterator.cs  Kirill ↔ lotin qoidalari (sof mantiq, kutubxonasiz)
   │     ├─ DocxTransliterator.cs   Mavjud .docx dagi w:t tugunlarini joyida almashtirish
   │     └─ NumberBaseConverter.cs  Sanoq sistemalari: BigInteger va ratsional arifmetika,
   │                                qadam-baqadam yechim (sof mantiq, kutubxonasiz)
   │
   ├─ ViewModels/
   │  ├─ ViewModelBase.cs             IsBusy / Progress / Cancel / xatolarni ko'rsatish uchun asos
   │  ├─ MainViewModel.cs             Shell: navigatsiya, dashboard ↔ workspace almashinuvi, mavzu
   │  │                               tugmasi; "Dastur haqida" bandidagi nuqtani ko'zguga oladi
   │  ├─ DashboardViewModel.cs        Bosh sahifa: kategoriyalar bo'yicha kartochkalar, qidiruv, ToolSelected
   │  ├─ ToolCardViewModel.cs         Bitta vosita kartochkasi (ToolDescriptor ustidagi qobiq)
   │  ├─ ToolWorkspaceViewModel.cs    Universal ishchi oyna: fayl tanlash → ToolRequest → ExecuteAsync
   │  ├─ ToolOptionsViewModels.cs     Har bir vosita sozlamalari uchun VM'lar (DataTemplate bilan tanlanadi)
   │  ├─ BackgroundRemoverViewModel.cs AI fon olib tashlash: oldin/keyin ko'rinishi, saqlash
   │  ├─ ArchiveViewModel.cs          Arxiv sahifasi: arxivlash / arxivdan ochish rejimlari
   │  ├─ ArchiveItemViewModels.cs     ArchiveSourceViewModel (manba fayl/papka) +
   │  │                               ArchiveEntryViewModel (arxiv ichidagi yozuv)
   │  ├─ TransliterationViewModel.cs  Kirill ↔ lotin sahifasi: matn rejimi (jonli o'girish,
   │  │                               almashtirish, nusxa olish) va fayl rejimi
   │  ├─ TransliterationFileViewModel.cs  Ro'yxatdagi bitta fayl va uning holati
   │  ├─ NumberSystemViewModel.cs     Sanoq sistemasi sahifasi: jonli hisob, barcha asoslar
   │  │                               jadvali, tanlov va qadam-baqadam yechim
   │  ├─ NumberBaseRowViewModel.cs    Jadvaldagi bitta asos va undagi natija
   │  ├─ RemoteControlViewModel.cs    Kompyuterlarni boshqarish sahifasi: agentni yuklab olish
   │  │                               + o'rnatish tartibi (InstallStep ro'yxati)
   │  ├─ ScreenRecorderViewModel.cs   Ekran yozuvi sahifasi: manba/video/ovoz sozlamalari,
   │  │                               boshlash-pauza-to'xtatish, taymer, MinimizeRequested /
   │  │                               RestoreRequested / OverlayVisibilityChanged
   │  ├─ AboutViewModel.cs            Versiya, muallif, Telegram havolasi, litsenziyalar,
   │  │                               yangilanish kartochkasi (tekshirish + relizlar sahifasi)
   │  ├─ WorkspaceFileViewModel.cs    Ishchi oynadagi bitta tanlangan fayl (nom, hajm, holat)
   │  ├─ PageItemViewModel.cs         Bitta sahifa kartasi (eskiz + burilish + tanlov)
   │  ├─ ImageItemViewModel.cs        Galereyadagi bitta rasm
   │  └─ NavigationItemViewModel.cs   Yon paneldagi bitta bo'lim (Glyph + Content + HasNotification)
   │
   ├─ Views/
   │  ├─ MainWindow.xaml(.cs)         Shell: yon panel + kontent hosti
   │  │                               (code-behind: Mica; MinimizeRequested / RestoreRequested →
   │  │                               WindowState; suzuvchi panelni ochish-yopish)
   │  ├─ DashboardView.xaml(.cs)      PDF vositalari: 4 kategoriya, 17 kartochka, qidiruv maydoni
   │  ├─ ToolWorkspaceView.xaml(.cs)  Universal ishchi oyna: fayl ro'yxati, sozlamalar paneli, natija
   │  ├─ ToolOptionTemplates.xaml     Har bir Options VM uchun DataTemplate lar (ResourceDictionary)
   │  ├─ BackgroundRemoverView.xaml(.cs)  Oldin/keyin taqqoslash, shaffoflik shaxmat foni
   │  ├─ ArchiveView.xaml(.cs)        Arxiv: rejim kaliti, manbalar/yozuvlar ro'yxati, sozlamalar
   │  ├─ ScreenRecorderView.xaml(.cs) Ekran yozuvi: boshqaruv paneli, manba, video, ovoz, saqlash
   │  ├─ TransliterationView.xaml(.cs) Kirill ↔ lotin: ikkita matn maydoni yoki fayllar ro'yxati,
   │  │                               umumiy sozlamalar paneli
   │  ├─ NumberSystemView.xaml(.cs) Sanoq sistemasi: tepada kiritish, pastda barcha asoslar
   │  │                               jadvali, o'ngda qadam-baqadam yechim
   │  ├─ RecordingOverlayWindow.xaml(.cs)  Yozuv davomidagi suzuvchi boshqaruv paneli:
   │  │                               taymer, pauza, to'xtatish; yozuvdan yashirilgan alohida oyna
   │  └─ AboutView.xaml(.cs)          Dastur haqida: versiya, muallif, Telegram, yangilanish
   │
   ├─ Behaviors/                      ── XAML'dan ulanadigan attached behavior'lar
   │  ├─ DragDropReorder.cs           Kolleksiyani o'z joyida qayta tartiblash + auto-scroll
   │  ├─ InsertionAdorner.cs          Qo'yiladigan joyni ko'rsatuvchi accent chiziq
   │  └─ FileDrop.cs                  Explorer'dan fayl tashlab yuborish (drop); IncludeFolders
   │                                     yoqilganda papka papka bo'yicha o'tadi (arxiv sahifasi)
   │
   ├─ Converters/                     ── IValueConverter'lar (XAML uchun)
   │  ├─ UiConverters.cs                      2.x UI uchun umumiy konvertorlar to'plami
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
   │  ├─ CaptureExclusion.cs          Oynani yozuv va skrinshotlardan yashirish
   │  │                               (SetWindowDisplayAffinity / WDA_EXCLUDEFROMCAPTURE)
   │  └─ WindowBackdrop.cs            Mica/Acrylic backdrop, yumaloq burchak (DwmSetWindowAttribute),
   │                                  xavfsiz degradatsiya
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

### `IArchiveService` — arxivlar

`IPdfEngineService` fasadiga **kirmaydi** (sababi 1-bo'limda). Xatolarni dastur bo'ylab
yagona qilish uchun `PdfServiceException` tashlaydi — nomi "Pdf" bilan boshlansa ham, u
dasturning umumiy xato turi va uni `ViewModelBase` tushunarli xabarga aylantiradi.

| A'zo | Vazifasi |
|---|---|
| `IReadOnlyList<string> SupportedReadExtensions` | O'qish mumkin bo'lgan kengaytmalar |
| `string OpenFilter` | Fayl dialogi uchun tayyor filtr satri |
| `bool LooksLikeArchive(path)` | Kengaytmasiga qarab tez tekshiruv (faylni ochmaydi) |
| `ReadAsync(archivePath, password, ct)` | Ichidagi ro'yxat → `ArchiveInfo` (format, yozuvlar, umumiy hajm, shifrlanganmi) |
| `ExtractAsync(archivePath, targetFolder, password, entryPaths, progress, ct)` | Chiqaradi; `entryPaths` berilsa faqat o'sha yozuvlar. Chiqarilgan fayllar sonini qaytaradi |
| `CreateZipAsync(sourcePaths, archivePath, options, progress, ct)` | Fayl va papkalardan `.zip` yig'adi (papkalar rekursiv). Yozilgan fayllar sonini qaytaradi |

#### Nega bu yerda ham ikkita kutubxona

| Kutubxona | Vazifasi | Nega yolg'iz yetmaydi |
|---|---|---|
| **SharpCompress** | ZIP, RAR (RAR5 ham), 7z, TAR, GZip **o'qish** | ZIP ni **shifrlab yoza olmaydi** |
| **SharpZipLib** | Parolli ZIP **yozish** (WinZip AES-256 yoki ZipCrypto) | RAR va 7z ni umuman o'qiy olmaydi |

#### "Zip Slip" himoyasi

Arxivga `..\..\Windows\System32\...` kabi yo'lli yozuv qo'yish mumkin — chiqarishda u
tanlangan papkadan tashqariga yozib yuboradi. `ArchiveService` bunda kutubxonaga
ishonmaydi: har bir yozuvning natija yo'li `Path.GetFullPath` bilan to'liq yechiladi va
maqsad papkasi ichida qolishi alohida tekshiriladi. Chiqib ketmoqchi bo'lgan arxivda
amal butunlay to'xtatiladi (`PdfErrorKind.CorruptedDocument`), chunki bunday fayl allaqachon
ishonchsiz. Disk harfi (`C:\...`) va UNC (`\server\...`) yo'llari ham xuddi shu yerda
zararsizlantiriladi.

#### Parol xatosini tanish

SharpCompress arxiv formatini aniqlashda ham deshifrlaydi, shuning uchun **noto'g'ri
parol** "oqim turini aniqlab bo'lmadi" degan mutlaqo boshqa xato ko'rinishida chiqadi.
Buni ajratish uchun ochish muvaffaqiyatsiz bo'lganda `ArchiveFactory.IsArchive` bilan
faylning imzosi parolsiz tekshiriladi:

| Imzo to'g'ri | Parol kiritilgan | Xulosa |
|---|---|---|
| ✔ | ✔ | `InvalidPassword` — parol to'g'ri kelmadi |
| ✔ | ✘ | `PasswordProtected` — parol so'raladi |
| ✘ | — | `UnsupportedFormat` — bu umuman arxiv emas |

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

#### Suzuvchi boshqaruv paneli (`RecordingOverlayWindow`)

**Muammo.** Yozuv boshlanganda dastur oynasi kichraytiriladi — aks holda u videoning
birinchi kadrlarida ko'rinib qoladi. Lekin shunda foydalanuvchida yozuvni to'xtatadigan
tugma ham qolmaydi, oynani qaytarish esa uni aynan videoga tushirib yuboradi. Ya'ni
boshqaruvni sahifada qoldirishning imkoni yo'q.

**Yechim.** Boshqaruv (taymer, pauza, to'xtatish) ekranning pastidagi alohida kichik
oynaga chiqariladi va bu oyna **yozuvdan chiqarib tashlanadi**:
`CaptureExclusion.TryExclude` → `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`.
Natijada panel monitorda ko'rinadi, kadrlarda esa umuman yo'q. Chaqiruv
`OnSourceInitialized` da — HWND paydo bo'lgandan keyin, lekin oyna ko'rsatilishidan
oldin: teskari tartibda panel bir necha kadr davomida yozuvga tushib ulgurardi.

| Qaror | Sabab |
|---|---|
| Alohida `Window`, egasi (`Owner`) ko'rsatilmagan | Egali oyna asosiy oyna kichraytirilganda u bilan birga yashirinadi — panelning butun ma'nosi yo'qolardi |
| Panel har seansda qaytadan yaratiladi | Yashirish `SourceInitialized` da qo'llanadi; oynani "berkitib qo'yish" bu himoyani keyingi seansda qayta qo'llash imkonini qoldirmasdi |
| `AllowsTransparency` **ishlatilmaydi** | Qatlamli (layered) oynada `SetWindowDisplayAffinity` ishonchsiz, panelning yozuvdan yashirilishi esa uning mavjudlik sababi. Burchaklar shu sababli DWM orqali yumaloqlanadi (`WindowBackdrop.TryRoundCorners`) |
| Ranglar qat'iy (qorong'i), mavzuga bog'lanmagan | Panel dastur oynasining emas, foydalanuvchining ish stoli ustida turadi: yorug' panel oq fonli hujjat ustida ko'rinmay qolardi |
| Joyi `static` maydonda | Panel har yozuvda qaytadan yaratiladi, foydalanuvchi tanlagan joy esa seans davomida esda qolishi kerak. Joy `WorkArea` ichiga `Clamp` qilinadi — monitorlar tarkibi o'zgargan bo'lishi mumkin |
| Panel oynadan **oldin** ochiladi | Kichraytirish animatsiyasi tugagunicha ekranda boshqaruvsiz bo'shliq qolmasligi uchun |

**Eski Windows dagi zaxira yo'l.** `WDA_EXCLUDEFROMCAPTURE` Windows 10 **2004**
(build 19041) da paydo bo'lgan. Undan eskiroq tizimda panelni yashirib bo'lmaydi, ya'ni u
har kadrda ko'rinib qolardi — shuning uchun u yerda panel **umuman ochilmaydi** va
tugmalar sahifaning o'zida qoladi:

| Xossa (`ScreenRecorderViewModel`) | Ma'nosi |
|---|---|
| `UsesFloatingControls` | Boshqaruv suzuvchi panelda. Standart qiymat — `CaptureExclusion.IsSupported`, lekin xossa `init`: aks holda sinov natijasi u ishlayotgan Windows versiyasiga bog'lanib qolardi va ikkala tarmoqni tekshirib bo'lmasdi |
| `ShowsInlineControls` | Buning teskarisi — sahifadagi "Pauza"/"To'xtatish" tugmalari |
| `ShowsInlineStopControls` | Sahifadagi tugmalar aynan hozir ko'rinishi kerakmi (`ShowsInlineControls && !IsIdleState`) |
| `ShowsOverlayHint` | Yozuv ketayotganda sahifadagi "Boshqaruv ekrandagi suzuvchi panelda" eslatmasi |

**Seansning tugash nuqtasi bitta.** `RecorderState.Idle` — servis uni muvaffaqiyatli
yakunda ham, xatoda ham, yozuv umuman boshlanmaganda ham o'rnatadi. Shu sababli tozalash
aynan shu yerda bajariladi: taymer to'xtatiladi, panel yopiladi va oyna qaytariladi.
Oyna **faqat uni biz kichraytirgan bo'lsak** qaytariladi (`_minimizedForRecording`) —
foydalanuvchi oynani o'zi kichraytirgan bo'lsa, uni "qaytarish" begona aralashuv bo'lardi.

Oynani boshqarish ViewModel dan tashqarida: `ScreenRecorderViewModel` faqat hodisa
ko'taradi (`MinimizeRequested`, `RestoreRequested`, `OverlayVisibilityChanged`),
`MainWindow` esa `WindowState` ni o'zgartiradi va panelni ochib-yopadi.

### `IUpdateService` — yangi versiyadan xabar berish

Uchinchi shartnoma, `IPdfEngineService` fasadiga **kirmaydi** (sababi 1-bo'limda): kirishi
— GitHub dagi reliz, chiqishi — "Dastur haqida" sahifasida ko'rsatiladigan xabar.

| A'zo | Nima qaytaradi / qiladi |
|---|---|
| `Version CurrentVersion` | Ishlab turgan yig'ilma versiyasi (`Assembly.GetName().Version`) |
| `string ReleasesPageUrl` | Brauzerda ochiladigan relizlar sahifasi ("Relizlar sahifasi" tugmasi) |
| `CheckForUpdateAsync(ct)` | Eng so'nggi relizni so'raydi; **joriy versiyadan yangi** bo'lsa `UpdateInfo`, aks holda `null`. Muvaffaqiyatli natija jarayon davomida keshlanadi (GitHub API so'rovlari cheklangan), xato esa keshlanmaydi — internet tiklangach qayta urinish ishlaydi. `SemaphoreSlim` tufayli ikkita chaqiruv bir vaqtda kelsa ham tarmoqqa bitta so'rov ketadi |

**Servis hech nima yuklab olmaydi va hech nimani ishga tushirmaydi** — bu ataylab
qilingan qaror. Dastur internetdan olingan faylni administrator huquqi bilan ishga
tushirsa, GitHub hisobi buzilgan yoki fayl yo'lda/mahalliy almashtirilgan holatda bu
huquq oshirish (privilege escalation) yo'liga aylanadi. O'rnatgich kod bilan imzolanmagan
ekan (Authenticode), faylning haqiqiyligini ishonchli tasdiqlab bo'lmaydi; shuning uchun
bu xavfni olish o'rniga oxirgi qadam foydalanuvchiga qoldirildi — `AboutViewModel` faqat
relizlar sahifasini brauzerda ochadi, yuklab olish va o'rnatish esa foydalanuvchining
qo'lida qoladi.

#### Qabul qilish qoidalari

Reliz haqidagi xabar UI ga chiqishidan oldin bir necha shart tekshiriladi — nomuvofiqlik
hech qachon "ehtimol to'g'ridir" deb o'tkazilmaydi:

| Tekshiruv | Qoida |
|---|---|
| Aktiv nomi | `^YordamchiSetup-\d+(\.\d+){1,3}\.exe$` — `.msi`, `.zip` yoki boshqa nomdagi aktiv umuman ko'rilmaydi |
| Protokol va xost | Faqat `https` va faqat `github.com` / `objects.githubusercontent.com` (aktivlar shu ikkinchi xostga yo'naltiriladi). Boshqa xost — biz nazorat qilmaydigan server, demak havola ko'rsatilmaydi |
| Reliz turi | `draft` yoki `prerelease` bo'lsa rad etiladi |
| Versiya | Teg (`v2.3.0` ham, `2.3.0` ham) uch qismga keltirilib solishtiriladi. **Teng versiya ham rad etiladi**: `Version` ko'rsatilmagan qismni `-1` deb sanaydi, ya'ni normalizatsiyasiz `2.3.0` va `2.3.0.0` teng bo'lmay qolardi va "qayta o'rnatish" taklifi chiqaverardi |

`ParseRelease` va `IsTrustedDownloadUrl` — `static` va tarmoqqa chiqmaydigan metodlar,
shuning uchun barcha qabul qilish qoidalari haqiqiy so'rovsiz to'liq sinaladi.

### `ITransliterationService` — kirill ↔ lotin

To'rtinchi shartnoma, `IPdfEngineService` fasadiga **kirmaydi** (sababi 1-bo'limda).

| A'zo | Nima qaytaradi / qiladi |
|---|---|
| `IReadOnlyList<string> SupportedExtensions` | `.docx` va `.txt` |
| `string OpenFilter` | Fayl dialogi uchun tayyor filtr satri |
| `bool IsSupported(path)` | Kengaytmasiga qarab tez tekshiruv (faylni ochmaydi) |
| `ConvertText(text, options)` | **Sinxron**: matn har bosishda darhol o'giriladi |
| `DetectDirection(text)` | Matn qaysi alifboda; harf topilmasa `null` |
| `SuggestOutputPath(sourcePath, outputFolder, direction)` | `hujjat.docx` → `hujjat-lotin.docx`; nom band bo'lsa raqam qo'shiladi |
| `ConvertFileAsync(sourcePath, outputFolder, options, progress, ct)` | Faylni o'girib **yangi** faylga yozadi; yakuniy yo'l natijada qaytadi |

#### Nega natija nomini servis tanlaydi

`ConvertFileAsync` ga natija **yo'li** emas, natija **papkasi** beriladi. Sababi avtomatik
aniqlashda: yo'nalish faqat hujjat ochilib, ichidagi matn sanab chiqilgandan keyin ma'lum
bo'ladi. Nomni chaqiruvchi oldindan tanlasa, kirillcha deb o'ylangan hujjat lotincha bo'lib
chiqqanda fayl `-lotin` qo'shimchasi bilan yozilar, ichida esa kirill matn turardi. Shuning
uchun tartib teskari: avval o'giriladi (vaqtinchalik nomda), so'ng aniqlangan yo'nalishga
qarab nom beriladi.

#### O'girish qoidalari qayerda

`Services\Conversion\UzbekTransliterator` — **sof mantiq**: hech qanday kutubxonaga,
faylga yoki UI ga bog'liq emas, shuning uchun to'liq sinovdan o'tkaziladi. Harflarning
ko'pchiligi atrofidagi harflarga qarab hal qilinadi:

| Qoida | Misol |
|---|---|
| `е` so'z boshida, unlidan yoki `ъ`/`ь` dan keyin — `ye` | `ердан` → `yerdan`, `поезд` → `poyezd` |
| `ц` unlidan keyin — `ts`, aks holda `s` | `революция` → `revolyutsiya`, `лекция` → `leksiya` |
| `ъ` — tutuq belgisi, lekin `е ё ю я` dan oldin tushadi | `маъно` → `ma'no`, `объект` → `obyekt` |
| `ь` butunlay tushadi | `фильм` → `film` |
| `y` + unli birga o'qiladi, lekin `yo'` — `й` + `ў` | `yo'l` → `йўл`, `yog'och` → `ёғоч` |
| Katta harf yonidagi harfga qarab shaklini saqlaydi | `Шаҳар` → `Shahar`, `ШАҲАР` → `SHAHAR` |
| Havola va e-pochta manzillari o'girilmaydi | `www.google.com` o'z holida qoladi |

> **Bilib turib qilingan chekinish.** Lotindan kirillga o'girishda `-siya` bilan tugaydigan
> o'zlashma so'zlarni qoida bilan ajratib bo'lmaydi: `funksiya` → `функция`, lekin
> `pensiya` → `пенсия` — ikkalasi ham bir xil ko'rinadi va farqni faqat lug'at biladi.
> Shuning uchun bu yerda faqat `ts` qatnashgan ishonchli holat (`revolyutsiya` →
> `революция`, so'z boshidagi `tsex` → `цех`) o'giriladi, qolgani `с` bo'lib qolaveradi.
> Lug'at qo'shish — kelajakdagi ish, lekin u qoidalar bilan emas, ma'lumot fayli bilan
> hal qilinishi kerak.

#### Word hujjati abzas bo'yicha o'giriladi

`DocxTransliterator` hujjatni qaytadan qurmaydi: nusxasini ochib, faqat `w:t` (va shakllar
ichidagi `a:t`) tugunlaridagi matnni almashtiradi. Shu tufayli shrift, jadval, rasm,
ro'yxat, kolontitul va sahifa sozlamalari qanday bo'lsa shundayligicha qoladi.

Lekin tugunlarni **alohida** o'girib bo'lmaydi. Word bitta so'zni bir necha `w:t` ga bo'lib
tashlashi odatiy hol (imlo tekshiruvi, tahrir izlari), ya'ni `Ўз` + `бекистон` ikkita
alohida so'zdek ko'rinardi va natijada `O'z` dan keyin `Bekiston` chiqardi. Shuning uchun:

| Qadam | Nima bo'ladi |
|---|---|
| 1 | Abzasdagi barcha matn tugunlari yig'ib, bitta satrga ulanadi |
| 2 | Satr yaxlit o'giriladi; `UzbekTransliterator` har bir bo'lak manbada **qayerdan** boshlanganini ham xabar qiladi |
| 3 | Har bir bo'lak o'zi boshlangan tugunga qaytariladi — natija uzunligi boshqacha bo'lsa ham |

Uchinchi qadam tufayli ikki tugun chegarasida turgan `ў` yoki `ye` ikkiga bo'linib ketmaydi,
formatlash esa manbadagidek taqsimlanadi.

> **Maydon kodlariga tegilmaydi.** `w:instrText` — ko'rinadigan matn emas, Word uchun
> buyruq (`PAGE`, `TOC`, `DATE`). U `Descendants<W.Text>()` ga tushmaydi, ya'ni o'girilmaydi:
> aks holda avtomatik mundarija va sana maydonlari ishlamay qolardi.

### `INumberSystemService` — sanoq sistemalari

Beshinchi shartnoma, `IPdfEngineService` fasadiga **kirmaydi**: bu yerda na fayl bor, na PDF —
kirish ham, chiqish ham oddiy satr.

| A'zo | Nima qaytaradi / qiladi |
|---|---|
| `MinBase` / `MaxBase` | 2 va 256 |
| `SupportedBases` / `PopularBases` | To'qqizta asos (2, 4, 8, 10, 16, 32, 64, 128, 256) va eng ko'p ishlatiladigan to'rttasi (2, 8, 10, 16) |
| `IsSupportedBase(radix)` | Ro'yxat qat'iy: oraliqdagi asoslar (3, 5, 6, …) yo'q |
| `UsesDigitGroups(radix)` | Shu asosda raqamlar «:» bilan ajratiladimi (64, 128, 256) |
| `DescribeBase(radix)` / `LabelBase(radix)` | "o'n oltilik" / "16-lik — o'n oltilik" |
| `DigitsOf(radix)` | "0–9 va A–F" — kiritish maydoni ostidagi eslatma uchun |
| `Validate(text, fromBase)` | Kiritilgan son shu asosga mos keladimi; xato matni yoki `null` |
| `Convert(text, fromBase, toBase, fractionDigits)` | `NumberConversionResult` — qiymat va u aniqmi |
| `Explain(text, fromBase, toBase, fractionDigits)` | Qadam-baqadam yechim bo'limlari |
| `Group(value, radix)` | Uzun natijani o'qishga qulay ajratish (faqat ko'rsatish uchun) |

**Barcha metodlar sinxron** — bu ataylab qilingan qaror. Natija har bosishda yangilanadi;
`Task` qaytarish bu yerda faqat keraksiz kontekst almashinuvi bo'lardi. To'qqizta asos uchun
o'tkazish mikrosoniyalarda tugaydi, shuning uchun sahifa `ViewModelBase.RunAsync` ni ham,
"band" qoplamasini ham ishlatmaydi.

#### Nega faqat ikkining darajalari va 10

Ro'yxat qat'iy: `2, 4, 8, 10, 16, 32, 64, 128, 256`. Oraliqdagi asoslar (3, 5, 6, 7, 9, 11 …)
amalda ishlatilmaydi, jadvalda esa har biri bitta qator egallab, kerakli asosni ko'z bilan
qidirishga majbur qilardi. To'qqizta qator bir ekranga sig'adi — shu sababli "faqat mashhur
asoslar" filtri ham kerak bo'lmay qoldi.

#### Ikki xil raqam yozuvi

| Asos | Bitta raqam qanday yoziladi | Misol |
|---|---|---|
| 2 … 32 | Bitta belgi: `0–9`, so'ng `A–V` | `255₁₀ = FF₁₆` |
| 64, 128, 256 | O'nlikdagi son, raqamlar `:` bilan ajratiladi | `12345678₁₀ = 188:97:78₂₅₆` |

64 tagacha belgi topish mumkin edi (Base64 alifbosi), lekin 128 va 256 uchun bunday alifbo
yo'q. Ikki xil qoida o'rniga bitta: `MaxSymbolBase` (32) dan katta asoslarning hammasi
guruh yozuvidan foydalanadi. Shu bitta chegara `DigitSymbol`, `JoinDigits`, kiritishni
o'qish va qadam-baqadam yechim — hammasini boshqaradi.

O'qishda bo'sh joy ham ajratkich sanaladi (`188 97 78`), ketma-ket ajratkichlar bittadek
qaraladi, oxiridagi ajratkich esa xato emas — foydalanuvchi hali yozayotgan bo'lishi
mumkin va har bosishda qizil xabar chiqarish xalaqit berardi.

#### Nega `double` emas

Bu sinfning butun qiymati aniqlikda, shuning uchun hisob ikki qismga bo'lingan:

| Qism | Qanday saqlanadi | Nega |
|---|---|---|
| Butun qism | `BigInteger` | `long` 19 xonadan keyin to'lib ketadi; foydalanuvchi esa 100 xonali son kiritishi mumkin |
| Kasr qism | `surat / maxraj` (ikkalasi ham `BigInteger`) | `0.1` ni `double` allaqachon taqribiy saqlaydi; uzun kasrda bu xato to'planib, oxirgi raqamlarni buzib yuborardi |

Kasr yangi asosga ratsional arifmetika bilan o'tkaziladi: qiymat asosga ko'paytiriladi,
chiqqan butun qism navbatdagi raqam bo'ladi, qoldiq esa aniq saqlanadi. Shu tufayli
`123456.75` ni istalgan juft asosga o'tkazib qaytarganda aynan o'sha son chiqadi.

**Kesish, yaxlitlash emas.** Kasr yangi asosda cheksiz davom etsa, u tanlangan xonada
shunchaki kesiladi. Bu darslikdagi "ketma-ket ko'paytirish" algoritmi qiladigan ish, ya'ni
natija qadam-baqadam yechim bilan **bir xil** chiqadi — yaxlitlansa, oxirgi raqam
tushuntirishdagi raqamdan farq qilib qolardi. Bunday natija `IsExact = false` bilan
belgilanadi va UI da `≈` ko'rinadi.

> **Aylanma har doim ham asl sonni qaytarmaydi.** `0.75₁₀` uchlik sanoq sistemasida cheksiz
> kasr; uni 3-likka o'tkazib qaytarsangiz `0.7499…` chiqadi. Bu xato emas — ma'lumot
> allaqachon kesilgan bo'ladi. Shuning uchun "Almashtirish" tugmasi kesilgan natijani
> qaytarganda foydalanuvchini ogohlantiradi.

#### Qadam-baqadam yechim

`Explain` uchta bosqichni qaytaradi (keraksizi tushib qoladi):

| Bosqich | Qachon bor | Nima ko'rsatadi |
|---|---|---|
| Pozitsion yoyilma | Manba 10-lik emas | `1 × 16¹ = 16`, `A (10) × 16⁰ = 10` … |
| Ketma-ket bo'lish | Nishon 10-lik emas | `26 ÷ 2 = 13, qoldiq 0` … |
| Ketma-ket ko'paytirish | Nishon 10-lik emas va kasr bor | `0.5 × 2 = 1 → 1` … |

Manba va nishon bir xil bo'lsa, yagona "o'tkazish kerak emas" bo'limi qaytadi. Juda uzun
sonda yoyilma umuman ko'rsatilmaydi (20 ta raqamdan oshsa), uzun ro'yxatlar esa boshi va
oxiri qoldirilib qisqartiriladi — aks holda o'ng panel o'qib bo'lmas holga kelardi.

### `IRemoteControlService` — kompyuterlarni boshqarish

"Kompyuterlarni boshqarish" bo'limi — boshqa kompyuterlarni masofadan boshqarish uchun
**tarqatish markazi**. Agentning o'zi (DXGI ekran uzatish, kirish yuborish, SYSTEM xizmati,
TCP/UDP tarmoq) — bu dasturga kirmaydigan **alohida katta loyiha**; u GitHub relizlariga
qo'yiladi va shu bo'limdan yuklab olinadi. Bu yerdagi xizmat faqat **yuklab oladi**.

| A'zo | Nima qiladi |
|---|---|
| `DefaultDownloadUrl` / `ExampleDownloadUrl` | Sozlangan manzil (hozircha bo'sh placeholder) va namuna GitHub havolasi |
| `DownloadFolder` / `AgentFilePath` / `AgentFileName` | `%LOCALAPPDATA%\Yordamchi\RemoteControl\` va undagi fayl |
| `IsAgentDownloaded` | Fayl allaqachon bormi |
| `IsDownloadUrlReady(url)` | Manzil bo'sh emas, `https` va faqat GitHub xostida — `UpdateService.IsTrustedDownloadUrl` bilan |
| `DownloadAgentAsync(...)` | Faylni oqim orqali (progress bilan) yuklaydi; **ishga tushirmaydi** |

**Bir nechta ataylab qilingan qaror:**

| Qaror | Nega |
|---|---|
| Yuklab olish faqat GitHub xostlaridan | Boshqa kompyuterlarga o'rnatiladigan dasturni begona serverdan tortib olish xavfli; ishonch ro'yxati butun dasturda bitta (`UpdateService`) |
| Dastur faylni ishga tushirmaydi | Faqat yuklab oladi; o'rnatishni foydalanuvchi maqsadli kompyuterda administrator huquqida o'zi bajaradi |
| O'rnatish qadamlarida "ko'rinadigan belgi" ochiq yozilgan | Masofaviy boshqaruv qonuniy bo'lishi uchun — faqat o'zing administratsiya qiladigan kompyuter, foydalanuvchi xabardor; agent yashirin ishlamaydi |
| Manzil doimiy emas, UI maydonidan olinadi | Placeholder holatda real fayl hali yo'q; manzilni qayta yig'masdan kiritish mumkin (holat faqat shu seansda) |

### Yordamchi UI servislari

| Interfeys | Vazifasi |
|---|---|
| `IDialogService` | Fayl/papka tanlash dialoglari, tasdiq va xato oynalari, clipboard — `ViewModels` uchun yagona UI eshigi |
| `IThemeService` | Light/Dark almashtirish, tizim mavzusini kuzatish, sarlavha panelini bo'yash |

> **Clipboard ham shu eshikdan o'tadi.** `System.Windows.Clipboard` — WPF ning bir qismi;
> uni ViewModel'dan to'g'ridan-to'g'ri chaqirish "ViewModels `MessageBox` ni bilmaydi"
> qoidasini buzardi va sinovda haqiqiy oyna talab qilardi. Shu sababli
> `IDialogService.SetClipboardText` qo'shilgan; implementatsiya nosozlikni yutadi — clipboard
> ni boshqa jarayon band qilib turgani foydalanuvchining ishini to'xtatmasligi kerak.

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

> **Ekran yozuvi, arxiv, kirill ↔ lotin o'girish va sanoq sistemalari bu jadvalda yo'q.** Ular `ToolCatalog`
> ga kirmaydi, `ToolId` qiymati ham yo'q va `PdfEngineService.ExecuteAsync` ga tegmaydi:
> yon paneldagi alohida bo'limlar o'z servislari bilan to'g'ridan-to'g'ri ishlaydi
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
| `SharpCompress` | 0.50.4 | Arxivlarni **o'qish**: ZIP, RAR, 7z, TAR, GZip | MIT |
| `SharpZipLib` | 1.4.2 | Parolli (AES-256 / ZipCrypto) ZIP **yozish** | MIT |
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
(`ConfigureAwait(false)` — sikl hech qanday kontekstni ushlab qolmasligi kerak, sababi
11-bo'limda), pauza vaqti umumiy hisobdan chiqarib tashlanadi.

**Oyna holati va suzuvchi panel — code-behind da.** `WindowState` va yangi oyna ochish —
`Window` ustidagi amallar, ViewModel ulardan bexabar bo'lishi kerak. Shu sababli
`ScreenRecorderViewModel` faqat hodisa ko'taradi (`MinimizeRequested`,
`RestoreRequested`, `OverlayVisibilityChanged`), `MainWindow` esa ularga obuna bo'lib
oynani kichraytiradi/qaytaradi va `RecordingOverlayWindow` ni ochib-yopadi; oyna
yopilganda obunalar bekor qilinadi. Kichraytirish kerak, aks holda videoning boshida
dasturning o'z oynasi ko'rinib qolardi; panel esa shundan keyin boshqaruvsiz qolmaslik
uchun (4-bo'limga qarang).

**Yozuv dastur yopilganda ham to'g'ri yakunlanadi.** `ScreenRecorderService` singleton va
`IDisposable`; `ServiceProvider.Dispose` (`App.OnExit`) uni tozalaganda hali ketayotgan
yozuv `Stop()` bilan yakunlanadi. Bu majburiy: yakunlanmagan `.mp4` da `moov` atomi
yozilmay qoladi va fayl umuman ochilmaydi.

**Jonli o'girish — sinxron, ataylab.** "Kirill ↔ Lotin" bo'limining matn rejimida natija
har bosishda yangilanadi. `ConvertText` sof va O(n): bir necha yuz ming belgigacha matn
sezilmasdan o'giriladi, shuning uchun bu yerda `Task`, `RunAsync` yoki "band" qoplamasi yo'q
— ular faqat kechikish qo'shgan bo'lardi. Chegara baribir bor: `LiveConversionLimit`
(100 000 belgi) dan katta matnda jonli o'girish o'chadi va tugma bosish talab qilinadi, aks
holda juda katta hujjatni qo'yganda har bosish sezilib qolardi.

**Jonli hisob — sinxron, ataylab (ikkinchi holat).** "Sanoq sistemasi" sahifasi ham
`RunAsync` ni ishlatmaydi: to'qqizta o'tkazish har bosishda qaytadan bajariladi va bu
mikrosoniyalarda tugaydi. Jadval qatorlari esa **bir marta** yaratilib, keyin faqat qiymati
yangilanadi — har bosishda yangi obyektlar yasash WPF ni ro'yxatni qaytadan qurishga
majbur qilardi va foydalanuvchining tanlovi ham yo'qolib ketardi.

**Kelajakdagi ajratish.** `Models` + `Services` ni alohida `Yordamchi.Core` kutubxonasiga
ko'chirish mumkin; to'siqlar — `PageModel.Thumbnail` (`BitmapSource`),
`IImageBackgroundRemover.RemoveBackgroundAsync` ning `BitmapSource` qaytarishi va
`ScreenRecorderService` ning `Application.Current.Dispatcher` ga murojaati. Birinchi
ikkitasini `byte[]` yoki `SKBitmap` ga, uchinchisini esa `SynchronizationContext` ga
almashtirsangiz, Core sof `net8.0-windows` (WPF siz) bo'ladi. To'liq `net8.0` esa baribir
chiqmaydi: ekran yozuvi Windows Media Foundation ga bog'langan.

---

---

## 11. Testlar

Testlar `tests\Yordamchi.Tests` da yashaydi va **`Yordamchi.sln`** ga qo'shilgan, ya'ni
`dotnet test Yordamchi.sln -c Release` hammasini ishga tushiradi. Hozirda **801 ta
sinov** bor: ular orasida yangilanish xizmatining qabul qilish qoidalari, yozuv seansining
hayot sikli — panel qachon ochiladi/yopiladi, oyna qachon qaytariladi — kirill ↔ lotin
o'girishning har bir qoidasi hamda sanoq sistemalarining 9 × 9 asos juftligi bo'yicha
aylanmasi ham bor.

| Vosita | Nima uchun |
|---|---|
| **xUnit** | .NET dagi odatiy tanlov; `[Theory]` bilan chegaraviy qiymatlarni ixcham yozish mumkin |
| **NSubstitute** | Sub-servislarni qo'lda o'nlab metodli stub sifatida yozmaslik uchun (BSD-3) |

> Assert kutubxonasi ataylab qo'shilmagan — xUnit ning o'z `Assert` i yetarli. Mashhur
> `FluentAssertions` ning 8-versiyasi tijorat litsenziyasiga o'tgan, shuning uchun uni
> loyihaga kiritish keraksiz huquqiy bog'liqlik bo'lardi.

### Nima soxtalashtiriladi, nima yo'q

Bu yerdagi asosiy qaror — **qayerda haqiqiy fayl ishlatish**:

| Qatlam | Yondashuv | Sabab |
|---|---|---|
| `ArchiveService`, `PdfManipulatorService`, `TransliterationService` | **Haqiqiy fayllar** (vaqtinchalik papkada) | Bu sinflarning butun qiymati tashqi kutubxona bilan kelishuvda. Soxta ZIP ustidagi sinov SharpZipLib va SharpCompress orasidagi moslikni umuman tekshirmagan bo'lardi; soxta `.docx` esa "matn bir necha `w:t` ga bo'linib ketgan" degan eng muhim holatni |
| `UzbekTransliterator`, `NumberBaseConverter` | **Hech narsa soxtalashtirilmaydi** | Sof mantiq: kirish — satr, chiqish — satr. Har bir qoida aynan misol bilan qulflanadi, chunki qoidalar bir-biriga bog'liq va bittasini o'zgartirish jimgina boshqasini buzishi mumkin |
| `NumberSystemViewModel` | **Haqiqiy servis** | Servis sof va tez; uni soxtalashtirish aynan tekshirilishi kerak bo'lgan narsani — jadval to'g'ri to'ldiriladimi — yashirib qo'yardi |
| `PdfEngineService` ning qaror mantiqi | **Substitute** sub-servislar | Bu yerda tekshiriladigan narsa — fayl emas, qoida: qaysi holatda qanday ogohlantirish chiqadi |
| ViewModel'lar | **Substitute** servis + soxta dialog | UI oynasi ochilmasligi kerak; tekshiriladigani — tugmalar qoidasi va servisga uzatilgan qiymatlar |

`TestSupport` papkasida ikkita yordamchi bor: `TempWorkspace` (har bir sinovga alohida
vaqtinchalik papka, tugagach o'chiriladi) va `FakeDialogService` (javoblari oldindan
beriladigan, chaqiruvlarni yozib boradigan dialog qobig'i).

### Testlar uchun qoidalar

- **Tarmoq yo'q.** Hech bir test haqiqiy yuklab olishni boshlamaydi. Yuklab olish mantiqi
  faqat tarmoqqa chiqishdan *oldin* rad etiladigan holatlar bo'yicha sinaladi.
- **Global holatga tegilmaydi.** Foydalanuvchining `%LOCALAPPDATA%` papkasi va muhit
  o'zgaruvchilari o'zgartirilsa, `try/finally` bilan tiklanadi.
- **Kutish yo'q.** `Thread.Sleep` o'rniga shart bo'yicha kutish; tasodifiy qiymat yo'q.
- **Freymvork sinalmaydi.** `ObservableCollection` ishlashini tekshirish keraksiz — faqat
  dasturning o'z qoidalari sinaladi.
- **Uzluksiz sikl `async void` da bo'lmaydi.** Sababi quyida.

### `async void` sinov jarayonini qulatadi

Yozuv taymeri dastlab `async void` metod edi va bu sinovlarni yiqitmadi — u butun sinov
**jarayonini** qulatdi. Sabab: `async void` metod boshlanganda sinxronizatsiya kontekstida
"tugallanmagan amal" sifatida ro'yxatga olinadi va kontekst uning tugashini kutishga
majbur bo'ladi. Taymer esa cheksiz sikl — u hech qachon tugamaydi. Ustiga-ustak, `async
void` dan chiqqan istisno hech qayerga ilinmaydi: u to'g'ridan-to'g'ri kontekstga uzatiladi
va jarayonni tugatadi (davomni yetkazish `try` blokidan tashqarida bo'lgani uchun uni ushlab
ham bo'lmaydi).

`async Task` da bunday ro'yxatga olish **yo'q**: `Task` ni kim kutishni o'zi hal qiladi.
Shu sababli hozirgi tuzilma quyidagicha va uni buzmaslik kerak:

| Qoida | Nima uchun |
|---|---|
| Sikl `async Task` metodda (`RunTimerAsync`), chaqiruvchi uni `_ = …` bilan boshlaydi | Kontekst uni "tugallanmagan amal" deb hisoblamaydi |
| `ConfigureAwait(false)` | Sikl hech qanday kontekstni ushlab qolmaydi va tugatilgan kontekstga ish yubormaydi. WPF oddiy xossaning `PropertyChanged` xabarini fon oqimidan kelganda o'zi dispetcherga o'tkazadi, shuning uchun UI uchun xavfsiz |
| Butun sikl `try/catch (Exception)` ichida | Taymer faqat ekrandagi soatni yuritadi; uning nosozligi dastur qulashiga sabab bo'lmasligi kerak |
| ViewModel — `IDisposable`, `Dispose` taymerni to'xtatadi | Ishlab turgan `PeriodicTimer` egasi tashlab yuborilgandan keyin ham tikillashda davom etadi |

`async void` ning yagona qonuniy o'rni — hodisa ishlovchilari (event handler), chunki
ularning imzosini biz tanlamaymiz. Boshqa hamma joyda `async Task`.

### Himoya testini mutatsiya bilan tekshirish

Xavfsizlik tekshiruvi "yashil" bo'lgani uchun emas, **haqiqatan ushlagani uchun** ishonchli.
Zip Slip himoyasi qo'shilganda `ResolveSafeDestination` dagi shart ataylab `if (false)` ga
almashtirildi va sinovlar qayta ishga tushirildi: aynan o'sha to'rtta test yiqildi, qolgan
207 tasi yashil qoldi. Bu ikki narsani isbotlaydi — test haqiqatan shu himoyani tekshiradi
va u boshqa hech narsaga taalluqli emas.

Yangi himoya qo'shganda shu qadamni takrorlang: **himoyani vaqtincha buzib ko'ring**. Test
yiqilmasa, u hech narsani tekshirmayapti.

## Ma'lum cheklovlar

- **Virtualizatsiya yo'q.** Eskizlar `WrapPanel` da chiziladi; ~500+ sahifali hujjatda
  xotira va birinchi ochilish vaqti sezilarli bo'ladi.
- **Word → PDF sifati Word mavjudligiga bog'liq.** Microsoft Word o'rnatilgan bo'lsa
  (`OfficeWordInterop`) natija asl nusxaga juda yaqin; aks holda ichki
  `WordToPdfRenderer` ishlatiladi va murakkab formatlash soddalashtiriladi.
- **PDF → Excel jadval aniqlash evristik.** Chegara chiziqlari yo'q jadvallar ustun
  koordinatalari bo'yicha taxmin qilinadi.
- **Arxiv yaratish faqat `.zip`.** 7z va RAR yozuvchisi ochiq kutubxonalarda yo'q (RAR
  formatining o'zi yopiq), TAR/GZip esa Windows foydalanuvchisiga deyarli kerak emas.
  O'qish sanab o'tilgan barcha formatlar uchun ishlaydi.
- **`.tar.gz` / `.tar.bz2` bitta qadamda ochilmaydi.** `ArchiveFactory` tashqi qobiqni
  ochadi va ro'yxatda ichki `.tar` fayli ko'rinadi; uni chiqarib, so'ng qayta ochish kerak.
- **AES-256 bilan shifrlangan ZIP Windows Explorer da ochilmaydi.** Bu formatning o'zi
  emas, Explorer ning cheklovi. Shu sababli UI da `ZipCrypto` muqobili ham beriladi va
  tanlov oqibati sahifada matn bilan tushuntiriladi.
- - **Bookmark/outline va formalar saqlanmaydi** — PDFsharp sahifalarni import qilganda
  hujjat darajasidagi bu tuzilmalar ko'chirilmaydi.
- **OCR aniqligi manba sifatiga bog'liq.** 300 dpi va undan yuqori skanlar uchun natija
  yaxshi; qiyshiq yoki shovqinli rasmlarda xatolar bo'lishi mumkin.
- **`ц` harfi qoida bilan tiklanmaydi.** Lotindan kirillga o'girishda `funksiya` →
  `функция` va `pensiya` → `пенсия` bir xil ko'rinadi; faqat `ts` qatnashgan ishonchli
  holat o'giriladi (4-bo'limga qarang).
- **O'girishda `.txt` UTF-8 bo'lishi shart.** .NET Core tarkibida Windows-1251 kodlashi
  yo'q; uni qo'shish uchun `System.Text.Encoding.CodePages` paketi kerak bo'lardi. Taxmin
  qilib o'qish o'rniga fayl rad etiladi va aniq xabar beriladi.
- **Eski `.doc` o'girilmaydi.** OpenXML faqat `.docx` (OPC) bilan ishlaydi; binar `.doc`
  formati uchun butunlay boshqa o'quvchi kerak bo'lardi.
- **Sanoq sistemasi asoslari ro'yxati qat'iy: 2, 4, 8, 10, 16, 32, 64, 128, 256.** Oraliqdagi
  asoslar amalda ishlatilmaydi va jadvalni uzaytirib, kerakligini qidirishga majbur qilardi.
- **64, 128 va 256-likda raqamlar `:` bilan ajratiladi.** Bitta belgili raqamlar 32 tada
  tugaydi (`0–9`, `A–V`); undan keyin umumiy kelishilgan alifbo yo'q, shuning uchun raqam
  o'nlikda yoziladi. Ajratkichsiz yozilgan son bitta raqam deb o'qiladi.
- **Kiritilgan son 512 belgidan oshmasligi kerak.** Undan uzun sonda har bosishdagi
  o'tkazishlar sezilarli vaqt olardi; chegara oshib ketsa aniq xabar beriladi.
- **Cheksiz kasr kesiladi, yaxlitlanmaydi.** Bu ataylab: yaxlitlangan natija qadam-baqadam
  yechimdagi raqamlar bilan mos kelmay qolardi. Kesilgan natija `≈` bilan belgilanadi.
- **Aylanma o'girish har doim ham asl matnni qaytarmaydi.** `объект` → `obyekt` → `обект`:
  `ъ` ning qayerda turishi lotin yozuvida saqlanmaydi. Lotindan boshlangan aylanma
  (lotin → kirill → lotin) esa asl matnni qaytaradi va sinovlar aynan shuni tekshiradi.
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
- **Suzuvchi boshqaruv paneli Windows 10 2004 (build 19041) dan boshlab.**
  `WDA_EXCLUDEFROMCAPTURE` aynan shu versiyada paydo bo'lgan. Eskiroq tizimda
  `CaptureExclusion.IsSupported` `false` qaytaradi, panel umuman ochilmaydi (aks holda u
  har kadrda ko'rinardi) va boshqaruv sahifada qoladi. Yozuvning o'zi ishlayveradi.
- **Dastur o'zini o'zi yangilamaydi.** U faqat yangi versiya chiqqanini aytadi; o'rnatgichni
  foydalanuvchi relizlar sahifasidan o'zi yuklab oladi va o'zi ishga tushiradi (sababi
  4-bo'limda).
- **Yangilanish haqidagi xabar faqat GitHub relizlaridan.** Boshqa manba, boshqa xost yoki
  boshqa nomdagi aktiv qo'llab-quvvatlanmaydi va sozlama orqali ham o'zgartirilmaydi.

---

© 2026 Abduxalil Voxidjonov — [@abduxalilvoxidjonov](https://t.me/abduxalilvoxidjonov)
