<div align="center">

# Yordamchi 2.1.0

**Windows uchun to'liq funksional PDF vositalari to'plami — 17 ta vosita, arxivlash va ekran yozuvi, bitta dastur.**

WPF (.NET 8) · MVVM · Fluent Design · Light/Dark rejim · to'liq o'zbek tilida

</div>

---

Yordamchi — kundalik ishda kerak bo'ladigan barcha PDF amallarini bitta oynada jamlagan
dastur: sahifalarni tartiblash va birlashtirishdan tortib, Word/Excel/PowerPoint ga
konvertatsiya, OCR va sun'iy intellekt bilan rasm fonini olib tashlashgacha. Bularga
qo'shimcha — **arxivlash** va **ekranni ovoz bilan videoga yozib olish** modullari.

**Hech qanday internet talab qilinmaydi** (OCR til fayllari va AI modelini birinchi marta
yuklab olishdan tashqari), fayllaringiz hech qayerga jo'natilmaydi — barcha ish sizning
kompyuteringizda bajariladi.

Chap yon paneldagi navigatsiya to'rt bo'limdan iborat:

| Bo'lim | Nima ochiladi |
|---|---|
| **PDF vositalari** | Bosh sahifa: 17 ta vositaning kartochkalari, kategoriyalar va qidiruv |
| **Arxiv** | Fayllarni ZIP ga jamlash va arxivlarni ochish (parolli arxivlar ham) |
| **Ekran yozuvi** | Ekranni videoga yozib olish sahifasi |
| **Dastur haqida** | Versiya, muallif va qo'shimcha komponentlar holati |

<!-- screenshot -->
<!--
    Bu yerga dastur skrinshotini qo'ying:
    ![Yordamchi — PDF vositalari](docs/images/dashboard.png)
    ![Ishchi oyna](docs/images/workspace.png)
    ![Arxiv](docs/images/archive.png)
    ![Ekran yozuvi](docs/images/screen-recorder.png)
-->

---

## Imkoniyatlar — 17 ta vosita

### 📄 Sahifalar bilan ishlash

| Vosita | Nima qiladi |
|---|---|
| **PDF birlashtirish** | Bir nechta PDF faylni kerakli tartibda bitta hujjatga jamlaydi |
| **PDF bo'lish** | Hujjatni sahifalarga yoki belgilangan oraliqlarga ajratadi |
| **Sahifalarni tartiblash** | Sahifalarni sichqoncha bilan surib joyini almashtirasiz, keraksizini o'chirasiz |
| **Sahifalarni burish** | Barcha yoki tanlangan sahifalarni 90° ga buradi |

### 🔄 Konvertatsiya

| Vosita | Nima qiladi |
|---|---|
| **PDF → Word** | Matn, sarlavha va jadvallarni **tahrirlanadigan** `.docx` hujjatga aylantiradi |
| **Word → PDF** | Shrift va formatlashni saqlagan holda `.docx` ni PDF ga o'tkazadi |
| **PDF → Rasm** | Har bir sahifani JPG yoki PNG rasm sifatida saqlaydi |
| **Rasm → PDF** | JPG, PNG va boshqa rasmlardan bitta PDF hujjat yig'adi |
| **PDF → Excel** | Hujjatdagi jadvallarni `.xlsx` kitobiga chiqaradi |
| **PDF → PowerPoint** | Har bir sahifadan matnli slayd tayyorlaydi |

### 🛡 Optimizatsiya va xavfsizlik

| Vosita | Nima qiladi |
|---|---|
| **PDF siqish** | Rasmlarni optimallashtirib fayl hajmini 30–70% ga kichraytiradi |
| **PDF himoyalash** | Ochish uchun parol qo'yadi, chop etish/nusxalashni cheklaydi |
| **Qulfni ochish** | Parol ma'lum bo'lsa, hujjatdan himoyani olib tashlaydi |
| **Suv belgisi** | Har bir sahifaga matnli suv belgisi qo'shadi |
| **Sahifa raqamlari** | Sahifalarni tanlangan joyda avtomatik raqamlaydi |

### 🤖 Sun'iy intellekt

| Vosita | Nima qiladi |
|---|---|
| **OCR: skaner → Word** | Skaner qilingan rasm-PDF dan matnni tanib olib Word ga yozadi (o'zbek, ingliz, rus) |
| **Orqa fonni olib tashlash** | AI (u2net) yordamida rasmlar fonini bir soniyada shaffof qiladi |

---

## 🗜 Arxiv

Yon paneldagi **"Arxiv"** bo'limi ikkita rejimga ega. PDF vositalaridan alohida sahifa —
bosh sahifadagi kartochkalar orasida turmaydi.

### Arxivlash

Fayl va papkalarni tanlang (yoki oynaga sudrab tashlang) va **`.zip`** arxiv yig'ing.
Papkalar ichidagi barcha fayllari bilan, tuzilishi saqlangan holda qo'shiladi.

| Sozlama | Tanlovlar | Standart |
|---|---|---|
| Siqish darajasi | Siqishsiz · Tez · Oddiy · Maksimal | Oddiy |
| Papkalar tuzilishi | Saqlanadi / fayllar tekis yoziladi | Saqlanadi |
| Parol | Yoqilgan / o'chirilgan | O'chirilgan |
| Shifrlash usuli | **AES-256** · **ZipCrypto** | AES-256 |

> **Qaysi shifrlashni tanlash kerak.** **AES-256** kuchli va zamonaviy, lekin Windows
> Explorer ning ichki ZIP ochuvchisi uni tushunmaydi — qabul qiluvchida 7-Zip yoki WinRAR
> bo'lishi kerak. **ZipCrypto** esa deyarli hamma joyda, jumladan Explorer da ham ochiladi,
> lekin himoyasi zaif. Muhim ma'lumot uchun AES-256 ni tanlang.

Parol ikki marta so'raladi: xato yozib qo'yib, o'z arxivingizni ocholmay qolmasligingiz
uchun. Yarim yozilgan arxiv qolmaydi — fayl avval vaqtinchalik nomga yoziladi va faqat
to'liq tugagach o'z nomiga o'tadi.

### Arxivdan ochish

Arxivni tanlang — ichidagi fayllar ro'yxati nomi, hajmi va sanasi bilan ko'rinadi.
Keraklilarini belgilab, faqat o'shalarni chiqarish mumkin.

| Xususiyat | Qiymat |
|---|---|
| O'qiladigan formatlar | `.zip` · `.rar` (RAR5 ham) · `.7z` · `.tar` · `.gz` · `.bz2` · `.cbz` · `.cbr` |
| Parolli arxivlar | Qo'llab-quvvatlanadi — parolni o'ng paneldagi maydonga kiriting |
| Chiqariladigan papka | Arxiv yonidagi, uning nomi bilan atalgan papka taklif qilinadi |

> **Xavfsizlik.** Arxiv ichidagi yozuv `..\..\Windows\...` kabi yo'l bilan tanlangan
> papkadan tashqariga yozishga urinsa ("Zip Slip" hujumi), dastur chiqarishni to'xtatadi va
> ogohlantiradi. Bu tekshiruv kutubxonaga ishonib qo'yilmagan — har bir yozuvning natija
> yo'li to'liq yechilib, papka ichida qolishi alohida tasdiqlanadi.

---

## 🎥 Ekran yozuvi

Yon paneldagi **"Ekran yozuvi"** bo'limi ekranni yoki bitta oynani ovoz bilan `.mp4`
videoga yozib oladi. Bu PDF vositalaridan alohida sahifa — bosh sahifadagi kartochkalar
orasida turmaydi.

### Nimani yozish mumkin

| Manba | Izoh |
|---|---|
| **Butun ekran (monitor)** | Ulangan monitorlardan biri tanlanadi; hech nima tanlanmasa — asosiy monitor |
| **Bitta oyna** | Ochiq oynalar ro'yxatidan tanlanadi. Yozuv davomida oynani yopmang va kichraytirmang |

Ro'yxatlar **"Yangilash"** tugmasi bilan qayta o'qiladi (yozuv davomida manbani
o'zgartirib bo'lmaydi).

### Video sozlamalari

| Sozlama | Tanlovlar | Standart |
|---|---|---|
| Kadrlar chastotasi (FPS) | 15 · 24 · 30 · 60 | 30 |
| Sifat | Past · O'rtacha · Yuqori | O'rtacha |
| Kodek | **H.264** (hamma joyda ochiladi) · **H.265** (fayl ~30% kichik) | H.264 |
| Apparat (GPU) kodlash | Yoqilgan / o'chirilgan | Yoqilgan |
| Sichqoncha ko'rsatkichi | Ko'rsatiladi / yashiriladi | Ko'rsatiladi |
| Bosilganda halqa chizish | Yoqilgan / o'chirilgan | O'chirilgan |

> **Apparat kodlash** yoqilganda videoni videokarta kodlaydi va protsessor deyarli bo'sh
> qoladi. Eski kompyuterlarda muammo chiqsa uni o'chirib ko'ring — u holda protsessor
> ishlaydi: sekinroq, lekin mosroq.

### Ovoz

- **Tizim ovozi** — dinamikdan chiqayotgan hamma narsa (standart holatda yoqilgan);
- **Mikrofon** — o'z ovozingiz (standart holatda o'chirilgan).

Har biri uchun qurilmani ro'yxatdan tanlash mumkin; birinchi element — *"Tizim tanlagan
qurilma"*. Ikkalasi birga yoqilsa ovozlar bitta yo'lakka aralashtiriladi.

### Saqlash

| Xususiyat | Qiymat |
|---|---|
| Standart papka | `Videolar\Yordamchi` (yo'q bo'lsa avtomatik yaratiladi) |
| Papkani almashtirish | "Saqlash" bo'limidagi **"Tanlash…"** tugmasi |
| Fayl nomi | `yozuv-yyyy-MM-dd-HH-mm-ss.mp4`, masalan `yozuv-2026-08-06-21-15-40.mp4` |
| Format | MP4: H.264 yoki H.265 video + 128 kbit/s stereo ovoz |

### Boshqarish

1. **"Yozishni boshlash"** — sozlamalar bloklanadi, taymer yurishni boshlaydi.
2. **"To'xtatib turish" / "Davom ettirish"** — fayl yopilmaydi, to'xtab turgan vaqt
   taymerga qo'shilmaydi.
3. **"To'xtatish"** — fayl yakunlanadi (bu bir necha yuz millisekund oladi), so'ng
   sahifada **"Oxirgi yozuv"** kartochkasi va **"Papkada ko'rsatish"** tugmasi paydo
   bo'ladi.

**"Yozish boshlanganda dastur oynasi kichraytirilsin"** belgisi (standart holatda
yoqilgan) yozuv boshlanishi bilan Yordamchi oynasini kichraytiradi — aks holda videoning
boshida shu oynaning o'zi ko'rinib qoladi.

---

## Tizim talablari

| Talab | Qiymat |
|---|---|
| Operatsion tizim | **Windows 10 (1809+) yoki Windows 11**, 64-bit (x64) |
| Ekran yozuvi uchun | **Windows 10 1903 (build 18362)** yoki undan yangisi |
| .NET | **Talab qilinmaydi** — dastur o'zi-yetarli (self-contained), kerakli hamma narsa ichida |
| Visual C++ ish vaqti | **Microsoft Visual C++ 2015–2022 (x64)** — o'rnatuvchi yo'q bo'lsa o'zi qo'yadi |
| Diskda joy | ~170 MB (o'rnatilgandan keyin) |
| Operativ xotira | 4 GB (AI vositasi uchun 8 GB tavsiya etiladi) |
| Ixtiyoriy | Microsoft Word — "Word → PDF" natijasini yanada aniqroq qiladi |

> Mica (shaffof fon) effekti Windows 11 da ishlaydi; Windows 10 da dastur oddiy fonga
> xavfsiz qaytadi.

> Ekran yozuvi moduli Visual C++ 2015–2022 (x64) ish vaqtiga tayanadi. U ko'p
> kompyuterda boshqa dasturlar orqali allaqachon o'rnatilgan bo'ladi; bo'lmasa
> `YordamchiSetup.exe` uni **o'zi o'rnatadi** — ish vaqti o'rnatuvchi ichiga joylangan,
> shuning uchun **internetsiz ham** o'rnatish ishlayveradi. Windows 10 1903 dan eski
> tizimda esa faqat ekran yozuvi ishlamaydi, 17 ta PDF vositasi normal ishlayveradi.

---

## O'rnatish

1. [**Releases**](https://github.com/AbduxalilVoxidjonov/PdfEditor/releases/latest) bo'limidan
   `YordamchiSetup-2.1.0.exe` faylini yuklab oling va ishga tushiring.
2. Litsenziyani qabul qiling, kerak bo'lsa o'rnatish papkasini o'zgartiring.
3. "O'rnatish" tugmasini bosing — bir marta administrator ruxsati (UAC) so'raladi.

O'rnatuvchi:

- dasturni `C:\Program Files\Yordamchi` papkasiga joylaydi;
- Start menyu va ish stolida yorliq yaratadi;
- "Ilovalar va imkoniyatlar" ro'yxatiga yozuv qo'shadi — u yerdan o'chirish mumkin;
- eski **1.0.0 ("PDF Suite")** o'rnatilgan bo'lsa, uni ikkinchi nusxa qilib qo'ymay,
  o'rniga yangilaydi (`UpgradeCode` ataylab o'zgartirilmagan);
- **Microsoft Visual C++ 2015–2022 (x64)** ish vaqti yo'q bo'lsa — uni ham o'rnatadi.
  Tekshiruv reyestr orqali bo'ladi (`SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64`),
  ya'ni allaqachon bor bo'lsa bosqich o'tkazib yuboriladi. Ish vaqti dastur o'chirilganda
  olib tashlanmaydi — u boshqa dasturlarga ham kerak.

### Korporativ (jimgina) o'rnatish

MSI ham mavjud — `artifacts\Yordamchi-2.1.0-x64.msi`:

```powershell
msiexec /i Yordamchi-2.1.0-x64.msi /qn INSTALLFOLDER="C:\Apps\Yordamchi"
```

O'chirish:

```powershell
msiexec /x Yordamchi-2.1.0-x64.msi /qn
```

---

## Manbadan yig'ish

### Talablar

- **.NET 8 SDK** (yoki undan yuqori)
- Windows x64 (`pdfium`, `Tesseract`, `ONNX Runtime`, `ScreenRecorderLib` — hammasi x64
  native kutubxonalar)

### Ishga tushirish (dasturchi uchun)

```powershell
dotnet build Yordamchi.sln -c Release
dotnet run --project src\Yordamchi
```

### Testlar

```powershell
dotnet test Yordamchi.sln -c Release
```

Testlar `tests\Yordamchi.Tests` da: **xUnit** + **NSubstitute**. Ular internetga chiqmaydi
va foydalanuvchining fayllariga tegmaydi — har bir test o'z vaqtinchalik papkasida
ishlaydi va tugagach o'zidan keyin tozalab ketadi.

| Nima sinaladi | Qanday |
|---|---|
| `ArchiveService` | Haqiqiy fayllar bilan: yaratish → o'qish → chiqarish aylanmasi, parol, Zip Slip |
| `PdfManipulatorService` | Test ichida PDFsharp bilan yaratilgan haqiqiy PDF lar ustida |
| `IPdfEngineService` qarorlari | Sub-servislar o'rniga substitute; `Validate` / `CheckPrerequisites` mantiqi |
| ViewModel qoidalari | Tugma qachon faol, servisga nima uzatiladi, xatodan keyin sahifa holati |
| Konvertorlar va `ToolCatalog` | Chegaraviy qiymatlar va katalog butunligi |

> Xavfsizlik tekshiruvlari "yashil bo'lgani uchun" emas, **haqiqatan ushlagani uchun**
> ishonchli: Zip Slip himoyasi ataylab o'chirib ko'rilgan va aynan o'sha to'rtta test
> yiqilgan. Yangi himoya qo'shganda shu usulni takrorlang — test yiqilmasa, u hech narsani
> tekshirmayotgan bo'ladi.

### O'zi-yetarli build chiqarish

```powershell
dotnet publish src\Yordamchi\Yordamchi.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true `
    -p:Version=2.1.0 -p:FileVersion=2.1.0.0 -p:AssemblyVersion=2.1.0.0 `
    -o publish\win-x64
```

### O'rnatuvchini yig'ish

Avval WiX v5 ni bir marta o'rnating:

```powershell
dotnet tool install --global wix --version 5.*
wix extension add -g WixToolset.UI.wixext/5.0.2
wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2
wix extension add -g WixToolset.Util.wixext/5.0.2
```

> `WixToolset.Util.wixext` — Visual C++ ish vaqti o'rnatilganini reyestrdan tekshirish
> (`util:RegistrySearch`) uchun kerak.

So'ng bitta buyruq:

```powershell
.\build-installer.ps1                        # standart versiya: 2.1.0.0
.\build-installer.ps1 -Version 2.2.0.0       # boshqa versiya bilan
.\build-installer.ps1 -SkipPublish           # mavjud publish papkasini qayta ishlatish
```

Skript to'rt bosqichni bajaradi va natijani `artifacts\` ga qo'yadi:

| # | Bosqich | Natija |
|---|---|---|
| 1 | `dotnet publish -r win-x64 --self-contained -p:PublishReadyToRun=true` | `publish\win-x64\` (~168 MB) |
| 2 | `wix build installer\Package.wxs` | `artifacts\Yordamchi-2.1.0-x64.msi` (LZX:high siqish bilan ~60 MB) |
| 3 | `vc_redist.x64.exe` ni `https://aka.ms/vs/17/release/vc_redist.x64.exe` dan olish | `artifacts\vc_redist.x64.exe` — **bir martalik**, keyingi yig'ilishlarda qayta ishlatiladi |
| 4 | `wix build installer\Bundle.wxs` | `artifacts\YordamchiSetup-2.1.0.exe` — MSI va VC++ ish vaqti ichiga joylangan (`Compressed="yes"`) |

> 3-bosqich — yagona joy, u yerda skript internetga chiqadi. Fayl allaqachon
> `artifacts\vc_redist.x64.exe` da bo'lsa, u yuklab olinmaydi; qo'lda ham qo'yish mumkin.
> Tayyor `YordamchiSetup.exe` esa hech qachon internetga chiqmaydi.

> `Yordamchi.csproj` dagi `StripSymbolsFromPublish` target'i publish'dan barcha `.pdb`
> fayllarni olib tashlaydi. Bu bezak emas: SkiaSharp'ning `libSkiaSharp.pdb` fayli
> **85 MB** — butun yukning uchdan biri.

---

## OCR va AI modelini sozlash

Ikkala resurs ham hajmi katta bo'lgani uchun o'rnatuvchiga qo'shilmaydi. Ular yo'q bo'lsa
dastur ogohlantirish ko'rsatadi, qolgan 15 ta vosita va ekran yozuvi esa normal
ishlayveradi.

### OCR til fayllari (`tessdata`)

**"OCR: skaner → Word"** vositasi Tesseract til fayllarini talab qiladi:
`uzb.traineddata`, `eng.traineddata`, `rus.traineddata`.

**Eng oson yo'l:** vositani ochganingizda dastur yetishmayotgan tillarni aniqlaydi va
ogohlantirish yonida **"Yuklab olish"** tugmasini ko'rsatadi — u fayllarni rasmiy
`tessdata_fast` omboridan olib, `%LOCALAPPDATA%\Yordamchi\tessdata` papkasiga joylaydi.
Xuddi shu tugma "Dastur haqida" sahifasida ham bor.

**Qo'lda:** fayllarni
[tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) dan yuklab olib, quyidagi
papkalardan biriga qo'ying:

| Papka | Izoh |
|---|---|
| `%LOCALAPPDATA%\Yordamchi\tessdata` | Tavsiya etiladi — administrator huquqi kerak emas |
| `C:\Program Files\Yordamchi\tessdata` | Dastur papkasi yonida |
| `TESSDATA_PREFIX` ko'rsatgan papka | Tesseract allaqachon o'rnatilgan bo'lsa |

Bir nechta tilni birga ishlatish mumkin: sozlamalarda `uzb+eng+rus` deb ko'rsating.

### AI modeli (`u2net.onnx`)

**"Orqa fonni olib tashlash"** vositasi u2net segmentatsiya modelini talab qiladi.

**Eng oson yo'l:** vositani ochganingizda dastur modelni topolmasa ogohlantirish va
**"Yuklab olish"** tugmasini ko'rsatadi. Tugma `u2net.onnx` (~168 MB) faylini rasmiy
[rembg](https://github.com/danielgatis/rembg) relizidan olib
`%LOCALAPPDATA%\Yordamchi\Models` papkasiga joylaydi. Yuklash foizi ko'rinib turadi va uni
istalgan payt bekor qilish mumkin; yarim yuklangan fayl saqlanmaydi. Tugagach vosita
darhol ishlaydi — **dasturni qayta ishga tushirish shart emas**. Xuddi shu tugma
"Dastur haqida" sahifasida ham bor.

**Qo'lda** (masalan sekin ulanishda yengilroq variantni tanlash uchun): modelni
[rembg](https://github.com/danielgatis/rembg) dan yuklab oling —

- `u2net.onnx` — ~168 MB, eng yaxshi sifat (dastur shuni yuklab oladi);
- `u2netp.onnx` — ~4,7 MB, tezroq va yengil, sochlar kabi nozik chekkalarda sifati pastroq.

So'ng faylni quyidagi papkalardan biriga qo'ying:

| Papka |
|---|
| `%LOCALAPPDATA%\Yordamchi\Models\` (tavsiya etiladi — administrator huquqi kerak emas) |
| `C:\Program Files\Yordamchi\Models\` |

Ikkala nom ham (`u2net.onnx` va `u2netp.onnx`) qabul qilinadi — dastur qaysi biri
mavjud bo'lsa, o'shani ishlatadi.

---

## Arxitektura

Dastur **Clean Architecture + MVVM** ustiga qurilgan; barcha PDF modullari bitta fasad —
`IPdfEngineService` orqali birlashtirilgan. Ekran yozuvi PDF quvuriga aloqador emas,
shuning uchun u fasadga qo'shilmagan: `IScreenRecorderService` alohida shartnoma bo'lib,
o'z sahifasi bilan to'g'ridan-to'g'ri ishlaydi.

```
Views → ViewModels → Services.Abstractions → Services → tashqi kutubxonalar
                                                  ↓
                                               Models
```

To'liq tavsif, papkalar xaritasi, ma'lumot oqimi va yangi vosita qo'shish qo'llanmasi:
**[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)**

### Asosiy kutubxonalar

| Paket | Vazifasi | Litsenziya |
|---|---|---|
| `PDFsharp` 6.2.4 | PDF yozish: merge, split, rotate, protect, watermark | MIT |
| `PDFtoImage` 5.3.0 (pdfium) | Sahifalarni rasmga aylantirish | MIT / BSD 3-Clause |
| `SkiaSharp` 4.150.1 | Rastr grafika, JPEG/PNG kodlash | MIT |
| `UglyToad.PdfPig` 1.7.0 | PDF dan matn va koordinatalarni o'qish | Apache-2.0 |
| `DocumentFormat.OpenXml` 3.5.1 | `.docx` / `.xlsx` / `.pptx` yozish | MIT |
| `Tesseract` 5.2.0 | OCR | Apache-2.0 |
| `Microsoft.ML.OnnxRuntime` 1.20.1 | u2net modelini ishga tushirish | MIT |
| `SharpCompress` 0.50.4 | Arxivlarni o'qish: ZIP, RAR, 7z, TAR, GZip | MIT |
| `SharpZipLib` 1.4.2 | Parolli (AES-256 / ZipCrypto) ZIP yozish | MIT |
| `ScreenRecorderLib` 6.6.0 | Ekran yozuvi: Windows Media Foundation (H.264/H.265) + WASAPI ovozi | MIT |
| `CommunityToolkit.Mvvm` 8.4.2 | MVVM source generatorlari | MIT |

---

## Ma'lum cheklovlar

- ~500+ sahifali hujjatlarda eskizlar to'ri sekinroq ochiladi (virtualizatsiya yo'q).
- "Word → PDF" natijasi Microsoft Word o'rnatilgan bo'lsa ancha aniqroq bo'ladi.
- PDF dagi jadvallarni aniqlash evristik — chegara chiziqlari yo'q jadvallarda xatolar
  bo'lishi mumkin.
- Bookmark (outline) va PDF formalari konvertatsiyada saqlanmaydi.
- Arxiv **yaratish** faqat `.zip` formatida: 7z va RAR yozuvchisi ochiq kutubxonalarda yo'q
  (RAR formatining o'zi yopiq). O'qish esa sanab o'tilgan barcha formatlar uchun ishlaydi.
- `.tar.gz` va `.tar.bz2` ikki qavatli arxivlar bitta qadamda ochilmaydi: ro'yxatda ichki
  `.tar` fayli ko'rinadi, uni chiqarib, so'ng yana ochish kerak.
- OCR aniqligi skanning sifatiga bog'liq; 300 dpi va undan yuqori tavsiya etiladi.
- Ekran yozuvi Windows 10 1903 (build 18362) va undan yangi tizimlarni talab qiladi;
  eskirog'ida sahifa ochiladi, lekin "Yozishni boshlash" tugmasi ishlamaydi.
- Ekran yozuvi Windows Media Foundation ga tayanadi. Windows ning **N/KN** nashrlarida u
  yo'q — "Media Feature Pack" ni qo'shimcha o'rnatish kerak.
- Bir vaqtning o'zida bitta manba (bitta monitor yoki bitta oyna) yoziladi; yozuv
  davomida manbani va sozlamalarni o'zgartirib bo'lmaydi.
- Oyna yozib olinayotganda uni yopish yoki kichraytirish kadrlar oqimini to'xtatadi.

---

## Litsenziya

Loyiha **MIT** litsenziyasi asosida tarqatiladi — erkin ishlatish, nusxalash, o'zgartirish
va tarqatish mumkin. To'liq matn: [LICENSE](LICENSE).

Uchinchi tomon komponentlarining litsenziyalari ham `LICENSE` faylida keltirilgan.

---

## Muallif
**Abduxalil Voxidjonov**
Telegram: [@abduxalilvoxidjonov](https://t.me/abduxalilvoxidjonov)

© 2026 Abduxalil Voxidjonov
