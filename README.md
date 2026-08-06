# PdfEdit

Windows 11 uslubidagi PDF sahifa tashkilotchisi — WPF (.NET 8) + MVVM.

Sahifalarni drag-and-drop bilan qayta tartiblash, o'chirish va burish; bir nechta PDF'ni birlashtirish;
rasmlardan PDF yasash. Fluent Design, Mica backdrop, jonli Light/Dark rejim.

---

## O'rnatish (oddiy foydalanuvchi uchun)

[**Releases**](https://github.com/AbduxalilVoxidjonov/PdfEditor/releases/latest) bo'limidan
`PdfEditSetup-1.0.0.exe` ni yuklab oling va ishga tushiring.

- **`.NET` o'rnatish shart emas** — dastur self-contained, kerakli hamma narsa ichida.
- O'rnatuvchi Windows 11/10 (x64) uchun; `Program Files` ga o'rnatadi, shuning uchun bir marta
  administrator ruxsati (UAC) so'raydi.
- Start menyu va ish stolida yorliq yaratadi, "Ilovalar va imkoniyatlar" (Add or remove programs)
  ro'yxatiga tushadi — u yerdan o'chirsa ham bo'ladi.
- Diskda ~169 MB joy egallaydi.

Korporativ tarqatish uchun `PdfEdit-1.0.0-x64.msi` ham bor:
```powershell
msiexec /i PdfEdit-1.0.0-x64.msi /qn INSTALLFOLDER="C:\Apps\PdfEdit"
```

## Ishga tushirish (dasturchi uchun)

```bash
dotnet build PdfEdit.sln -c Release
dotnet run --project src/PdfEdit
```

Talab: .NET 8 SDK (yoki undan yuqori) va Windows. `pdfium` mahalliy kutubxonasi tufayli loyiha
`x64` uchun quriladi.

## O'rnatuvchini yig'ish

```powershell
dotnet tool install --global wix --version 5.*
wix extension add -g WixToolset.UI.wixext/5.0.2
wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2

.\build-installer.ps1                 # yoki: .\build-installer.ps1 -Version 1.1.0.0
```

Skript uch bosqichni bajaradi va natijani `artifacts/` ga qo'yadi:

1. `dotnet publish -r win-x64 --self-contained -p:PublishReadyToRun=true` → 259 fayl, 168.6 MB
2. `PdfEdit-<ver>-x64.msi` — LZX:high siqish bilan 59.9 MB
3. `PdfEditSetup-<ver>.exe` — MSI'ni o'rab turgan WiX Burn bootstrapper, 60.5 MB

> `PdfEdit.csproj` dagi `StripSymbolsFromPublish` target'i publish'dan barcha `.pdb` fayllarni
> olib tashlaydi. Bu bezak emas: SkiaSharp'ning `libSkiaSharp.pdb` fayli **85 MB** — butun
> yukning uchdan biri.

## Kutubxonalar

| Paket | Vazifa | Litsenziya |
|---|---|---|
| `PDFsharp` 6.2.4 | PDF yozish: merge, reorder, rotate, rasm joylash | MIT |
| `PDFtoImage` 5.3.0 (pdfium + SkiaSharp) | Sahifalarni rasmga aylantirish (thumbnail) | MIT |
| `CommunityToolkit.Mvvm` 8.4.2 | `[ObservableProperty]`, `[RelayCommand]` | MIT |
| `Microsoft.Extensions.DependencyInjection` 9.0 | Composition root | MIT |

> `PDFsharp` va `PdfPig` sahifani **rasmga aylantira olmaydi** — shu sababli rasterizatsiya uchun
> alohida `PDFtoImage` (pdfium) ishlatilgan. Bu ikkisi bir-birini to'ldiradi: pdfium o'qiydi va chizadi,
> PDFsharp yozadi.

---

## Papkalar tuzilmasi

```
PdfEdit.sln
└─ src/PdfEdit/
   ├─ App.xaml(.cs)              Composition root: DI konteyner, mavzu, shell oynasi
   ├─ app.manifest               Per-monitor V2 DPI
   │
   ├─ Models/                    Sof ma'lumot — UI ham, servis ham bog'liq emas
   │  ├─ PageModel.cs            Bitta manba sahifa + render qilingan thumbnail
   │  ├─ PageEdit.cs             "Shu fayldan shu sahifani shu burchak bilan ol"
   │  ├─ PageRotation.cs         enum + Add/RotateClockwise kengaytmalari
   │  ├─ PdfProgress.cs          IProgress<T> yuki
   │  ├─ PdfServiceException.cs  PdfErrorKind bilan yagona xato turi
   │  └─ ImageToPdfOptions.cs    Sahifa o'lchami, chekka, downscale chegarasi
   │
   ├─ Services/
   │  ├─ Abstractions/           IPdfService, IDialogService, IThemeService
   │  ├─ PdfService.cs           Butun PDF biznes-mantiq (pdfium + PDFsharp)
   │  ├─ DialogService.cs        Fayl dialoglari, MessageBox
   │  └─ ThemeService.cs         Light/Dark almashtirish + tizim sozlamasini kuzatish
   │
   ├─ ViewModels/
   │  ├─ ViewModelBase.cs        IsBusy / Progress / Cancel / xatolarni ko'rsatish
   │  ├─ MainViewModel.cs        Navigatsiya + mavzu tugmasi
   │  ├─ PageEditorViewModel.cs  Sahifalarni tartiblash/o'chirish/burish
   │  ├─ PdfBuilderViewModel.cs  Merge
   │  ├─ ImageToPdfViewModel.cs  Rasm -> PDF
   │  └─ *ItemViewModel.cs       Bitta karta (sahifa / fayl / rasm)
   │
   ├─ Views/
   │  ├─ MainWindow.xaml         Sidebar + workspace host (+ Mica code-behind)
   │  ├─ PageEditorView.xaml     Thumbnail grid, drag-drop, hover tugmalari
   │  ├─ PdfBuilderView.xaml     Merge kartalari
   │  └─ ImageToPdfView.xaml     Rasm galereyasi + sozlamalar paneli
   │
   ├─ Behaviors/                 XAML'dan ulanadigan attached behavior'lar
   │  ├─ DragDropReorder.cs      Kolleksiyani o'z joyida qayta tartiblash + auto-scroll
   │  ├─ InsertionAdorner.cs     Qo'yiladigan joyni ko'rsatuvchi accent chiziq
   │  └─ FileDrop.cs             Explorer'dan fayl tashlash
   │
   ├─ Converters/                9 ta IValueConverter
   ├─ Helpers/                   SkiaImageHelper (SKBitmap -> frozen BitmapImage), WindowBackdrop (Mica)
   └─ Themes/
      ├─ Colors.Light.xaml       44 ta kalit — MergedDictionaries[0]
      ├─ Colors.Dark.xaml        Aynan o'sha 44 ta kalit
      └─ Controls.xaml           50 ta style; ranglarga faqat DynamicResource orqali murojaat
```

### Qatlamlar orasidagi qoidalar

- `Views` → faqat `ViewModels` (code-behind'da faqat `InitializeComponent` va HWND ishlari).
- `ViewModels` → faqat `Services.Abstractions` + `Models`. `MessageBox` yoki `OpenFileDialog` yo'q.
- `Services` → `Models` + tashqi kutubxonalar. WPF'dan faqat `BitmapSource` ishlatiladi.
- `Models` → hech kimga bog'liq emas.

Kelajakda `Models` + `Services` ni alohida `PdfEdit.Core` kutubxonasiga ajratish mumkin: yagona to'siq —
`PageModel.Thumbnail` (`BitmapSource`), uni `byte[]` ga almashtirsangiz Core sof `net8.0` bo'ladi.

---

## Arxitekturaning asosiy g'oyasi

To'rtala amal ham bitta primitivga keltiriladi:

```csharp
Task BuildPdfAsync(IReadOnlyList<PageEdit> pages, string outputPath, ...)
```

`PageEdit(SourceFilePath, SourcePageIndex, Rotation)` ro'yxati — bu:

| Amal | `PageEdit` ro'yxati qanday bo'ladi |
|---|---|
| Reorder | o'sha sahifalar, boshqa tartibda |
| Delete | ba'zi sahifalar ro'yxatga kirmaydi |
| Rotate | `Rotation` maydoni o'zgaradi |
| Merge | ro'yxatda bir nechta `SourceFilePath` bo'ladi |

Shuning uchun tahrirlash **destruktiv emas**: foydalanuvchi saqlamaguncha diskda hech narsa
o'zgarmaydi, va `PageEditorViewModel` ichida hech qanday "o'chirilgan sahifalar" holati saqlanmaydi —
ekrandagi kolleksiyaning o'zi yagona haqiqat manbai.

## E'tiborga loyiq texnik yechimlar

**Thumbnail'lar `Freeze()` qilinadi.** Rasterizatsiya thread pool'da bo'ladi; muzlatilgan
`BitmapImage` UI thread'ga marshalling'siz uzatiladi.

**Yozish atomar.** Har bir yozish yonidagi `.tmp-<guid>` fayliga boradi va faqat muvaffaqiyatdan keyin
nishonga ko'chiriladi. Manba avval xotiraga o'qiladi — shuning uchun **ochilgan faylning ustiga saqlash
ishlaydi**, va muvaffaqiyatsiz amal nishon faylni buzmaydi.

**Burish ikki joyda.** UI'da `LayoutTransform` (bir zumda, qayta render qilinmaydi), eksportda esa
PDF sahifasining `/Rotate` qiymati. Piksel qayta ishlanmaydi.

**Mavzu almashtirish jonli.** `ThemeService` faqat `MergedDictionaries[0]` ni almashtiradi;
`Controls.xaml` ranglarga faqat `DynamicResource` orqali murojaat qilgani uchun butun oyna
bitta ham kontrol qayta yaratilmasdan bo'yaladi. Sarlavha paneli `DwmSetWindowAttribute` bilan
moslashadi.

**Mica xavfsiz degradatsiya qiladi.** Windows 10'da yoki DWM rad etsa, `WindowBackdrop.TryApplyMica`
`false` qaytaradi va oyna oddiy `AppBackgroundBrush` foniga qaytadi.

**Drag-drop tugmalarni o'g'irlamaydi.** Sichqoncha bosilganda vizual daraxt yuqoriga yuriladi; agar
karta konteyneridan oldin `ButtonBase` topilsa, drag boshlanmaydi — shuning uchun kartadagi
"o'chirish"/"burish" tugmalari ishlayveradi.

---

## Ma'lum cheklovlar

- **Virtualizatsiya yo'q.** `WrapPanel` virtualizatsiya qilmaydi, shuning uchun ~500+ sahifali hujjatda
  xotira va birinchi ochilish vaqti sezilarli bo'ladi. Kerak bo'lsa virtualizatsiya qiluvchi wrap panel
  qo'shish kerak.
- **Parolli PDF'lar o'qilmaydi.** `IPdfService` `password` parametrini qabul qiladi, lekin UI hali parol
  so'ramaydi — shunday fayl `PdfErrorKind.PasswordProtected` bilan tushuntirilib rad etiladi.
- **Bookmark/outline va formalar saqlanmaydi** — PDFsharp sahifalarni import qilganda hujjat darajasidagi
  bu tuzilmalar ko'chirilmaydi.
- **`ImageToPdfOptions.ImageDpi` amalda kamdan-kam ishlaydi.** `FitToImage` rejimida sahifa o'lchami
  `XImage.HorizontalResolution` dan olinadi, PDFsharp esa DPI metama'lumoti yo'q JPEG uchun `0` emas,
  `72` qaytaradi — ya'ni "metama'lumot yo'q" holati aniqlanmaydi va `ImageDpi` ishlatilmaydi. Natijada
  metama'lumotsiz JPEG 72 dpi, `pHYs`siz PNG esa 96 dpi bo'yicha o'lchanadi. Buni tuzatish uchun DPI'ni
  WIC orqali o'qish kerak.
