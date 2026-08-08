<div align="center">

# Yordamchi 2.3.0

**Windows uchun to'liq funksional ish vositalari to'plami — 17 ta PDF vositasi, arxivlash, ekran yozuvi, kirill ↔ lotin o'girish va sanoq sistemalari kalkulyatori, bitta dastur.**

WPF (.NET 8) · MVVM · Fluent Design · Light/Dark rejim · to'liq o'zbek tilida

</div>

---

Yordamchi — kundalik ishda kerak bo'ladigan barcha PDF amallarini bitta oynada jamlagan
dastur: sahifalarni tartiblash va birlashtirishdan tortib, Word/Excel/PowerPoint ga
konvertatsiya, OCR va sun'iy intellekt bilan rasm fonini olib tashlashgacha. Bularga
qo'shimcha — **arxivlash**, **ekranni ovoz bilan videoga yozib olish**, **kirill ↔ lotin
o'girish**, **sanoq sistemalari kalkulyatori** va **kompyuterlarni masofadan boshqarish**
modullari. Yozuv
davomida boshqaruv ekrandagi kichik **suzuvchi panelga** chiqadi: u monitorda ko'rinadi,
lekin videoga tushmaydi. Yangi versiya chiqqanini dastur o'zi sezadi va **"Dastur haqida"**
sahifasida xabar beradi — o'rnatgichni esa siz o'zingiz yuklab olasiz.

**Fayllaringiz hech qayerga jo'natilmaydi** — hujjat, rasm va videolar ustidagi barcha ish
sizning kompyuteringizda bajariladi. Dastur internetga faqat uch holatda chiqadi: OCR til
fayllarini va AI modelini birinchi marta yuklab olishda hamda ochilishda "eng so'nggi
versiya qaysi" degan bitta so'rovni GitHub ga yuborganda. Bu so'rovda foydalanuvchi
haqidagi hech qanday ma'lumot yo'q — unda faqat dastur nomi va versiyasi ko'rsatiladi
(batafsil: [🔄 Yangilanish](#-yangilanish)).

Chap yon paneldagi navigatsiya yetti bo'limdan iborat (yuqoridagi **≡** tugmasi panelni yig'adi):

| Bo'lim | Nima ochiladi |
|---|---|
| **PDF vositalari** | Bosh sahifa: 17 ta vositaning kartochkalari, kategoriyalar va qidiruv |
| **Arxiv** | Fayllarni ZIP ga jamlash va arxivlarni ochish (parolli arxivlar ham) |
| **Ekran yozuvi** | Ekranni videoga yozib olish sahifasi |
| **Kirill ↔ Lotin** | Matn yoki Word hujjatini bir alifbodan ikkinchisiga o'girish |
| **Sanoq sistemasi** | Sonni 2, 4, 8, 10, 16, 32, 64, 128 va 256-lik asoslarga o'tkazish, qadam-baqadam yechim bilan |
| **Kompyuterlarni boshqarish** | Boshqa kompyuterlarni masofadan boshqarish agentini GitHub'dan yuklab olish va o'rnatish tartibi |
| **Dastur haqida** | Versiya, muallif, qo'shimcha komponentlar holati va loyihani qo'llab-quvvatlash |

Panelning tepasidagi **burger tugmasi (≡)** uni yig'ib qo'yadi: nomlar yashirinadi, faqat
nishonlar qoladi va ishchi hudud ~200 nuqtaga kengayadi. Yig'ilgan holatda nishon ustiga
sichqonchani olib borsangiz bo'lim nomi va tavsifi chiqadi, yangi versiya haqidagi nuqta esa
nishonning burchagida ko'rinib turadi. Holat shu seans davomida saqlanadi.

<!-- screenshot -->
<!--
    Bu yerga dastur skrinshotini qo'ying:
    ![Yordamchi — PDF vositalari](docs/images/dashboard.png)
    ![Ishchi oyna](docs/images/workspace.png)
    ![Arxiv](docs/images/archive.png)
    ![Ekran yozuvi](docs/images/screen-recorder.png)
    ![Kirill ↔ Lotin](docs/images/transliteration.png)
    ![Sanoq sistemasi](docs/images/number-system.png)
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

1. **"Yozishni boshlash"** — sozlamalar bloklanadi, taymer yurishni boshlaydi va ekranning
   pastida kichik **suzuvchi boshqaruv paneli** ochiladi.
2. Panelda: holat nishoni (yozilayotganda qizil, pauzada sariq), o'tgan vaqt,
   **"Pauza" / "Davom"** va **"To'xtatish"** tugmalari.
3. **"Pauza"** — fayl yopilmaydi, to'xtab turgan vaqt taymerga qo'shilmaydi.
4. **"To'xtatish"** — fayl yakunlanadi (bu bir necha yuz millisekund oladi), so'ng
   sahifada **"Oxirgi yozuv"** kartochkasi va **"Papkada ko'rsatish"** tugmasi paydo
   bo'ladi.

> **Panel videoga tushmaydi.** U monitorda ko'rinib turadi, lekin yozuvga ham,
> skrinshotlarga ham kirmaydi — Windows ning `WDA_EXCLUDEFROMCAPTURE` imkoniyati shuni
> ta'minlaydi. Shuning uchun yozuv davomida "To'xtatish" tugmasi doim ko'z oldingizda
> turadi va videoda uning izi qolmaydi.

Panelni bo'sh joyidan ushlab **istalgan joyga surish** mumkin — masalan yozilayotgan
oynaning ustidan olib qo'yish uchun. Tanlangan joy dastur yopilgunicha esda qoladi:
keyingi yozuvda panel o'sha yerdan ochiladi. Vazifalar panelida u ko'rinmaydi va boshqa
oynalarning ustida turadi.

**"Yozish boshlanganda dastur oynasi kichraytirilsin"** belgisi (standart holatda
yoqilgan) yozuv boshlanishi bilan Yordamchi oynasini kichraytiradi — aks holda videoning
boshida shu oynaning o'zi ko'rinib qoladi. Yozuv tugagach oyna **o'zi qaytariladi**, lekin
faqat uni dastur kichraytirgan bo'lsa: agar oynani siz qo'lda kichraytirgan bo'lsangiz, u
o'z holida qoladi.

> **Windows 10 2004 (build 19041) dan eski tizimda.** U yerda oynani yozuvdan yashirib
> bo'lmaydi, ya'ni panel har kadrda ko'rinib qolardi. Shuning uchun u umuman ochilmaydi va
> "Pauza" bilan "To'xtatish" odatdagidek **sahifaning o'zida** qoladi — bunday tizimda
> oynani kichraytirmaslik yoki uni vazifalar panelidan qaytarish kerak bo'ladi.

---

## 🔤 Kirill ↔ Lotin

O'zbek matnini kirilldan lotinga va aksincha o'giradigan bo'lim. Ikkita rejimi bor:
matnni to'g'ridan-to'g'ri yozib (yoki qo'yib) o'girish va tayyor **Word hujjatlarini**
o'girish.

### Matn rejimi

Chapdagi maydonga matn yoziladi yoki qo'yiladi — natija o'ng maydonda **darhol**, tugma
bosmasdan paydo bo'ladi. Ikki maydon orasidagi tugma natijani manba o'rniga qo'yib,
yo'nalishni teskarisiga buradi: o'girilgan matnni bir bosishda qaytarib tekshirish
mumkin. **"Nusxa olish"** natijani vaqtinchalik xotiraga ko'chiradi.

### Word va matn fayllari rejimi

Fayllarni oynaga tashlang yoki **"Fayl qo'shish"** orqali tanlang.

| Xususiyat | Qiymat |
|---|---|
| Qabul qilinadigan formatlar | `.docx` (Word 2007 va undan yangi) · `.txt` |
| Manba fayl | **Umuman o'zgartirilmaydi** — natija doim yangi faylga yoziladi |
| Natija nomi | `hujjat.docx` → `hujjat-lotin.docx` (yoki `-kirill`). Bunday nom band bo'lsa raqam qo'shiladi, ya'ni oldingi natija o'chib ketmaydi |
| Natija papkasi | Standart holatda manba fayl yonida; o'ng paneldan boshqa papka tanlash mumkin |
| Bir vaqtda | Istalgancha fayl — biri xato bersa qolganlari to'xtamaydi, sabab o'sha qatorda ko'rinadi |

> **Formatlash saqlanadi.** Hujjat qaytadan yig'ilmaydi: uning nusxasi ochilib, faqat
> matn tugunlari almashtiriladi. Shrift, rang, jadval, rasm, ro'yxat, kolontitul va
> sahifa sozlamalari qanday bo'lsa shundayligicha qoladi. Avtomatik mundarija va sana
> kabi **maydon kodlariga tegilmaydi** — ular o'girilsa ishlamay qolardi.

### Sozlamalar

Ikkala sozlama ham sahifaning **tepasida**, rejim tugmalari ostidagi bitta qatorda turadi —
alohida sozlamalar paneli yo'q, shuning uchun manba va natija maydonlari butun kenglikni
oladi.

| Sozlama | Tanlovlar | Standart |
|---|---|---|
| Yo'nalish | **Avtomatik** · Kirill → Lotin · Lotin → Kirill | Avtomatik |
| Apostrof belgisi | **Oddiy** (`o'`, `g'`) · **Rasmiy** (`oʻ`, `gʻ`, `ʼ`) | Oddiy |

Yo'nalish bitta tugma: unda aynan **qo'llanayotgan** yo'nalish yozilgan, bosilsa esa
teskarisiga buriladi va **"Avtomatik"** belgisi o'chadi — aks holda keyingi harfda aniqlagich
tanlovingizni bekor qilardi. Avtomatik holatda matndagi harflar sanab chiqiladi; fayl
rejimida bu **har bir hujjat uchun alohida** hal qilinadi, ya'ni bitta ro'yxatda kirill va
lotin hujjatlari aralash bo'lishi mumkin.

Fayl rejimida **natija papkasi** fayllar ro'yxatining ostida — u faqat o'sha ro'yxatga
tegishli.

### Qoidalar haqida

O'girish harflarni ko'r-ko'rona almashtirmaydi — ko'p harf atrofidagi harflarga qarab
hal qilinadi:

| Holat | Misol |
|---|---|
| `е` so'z boshida va unlidan keyin | `ердан` → `yerdan`, `поезд` → `poyezd` |
| `е` undoshdan keyin | `мен келдим` → `men keldim` |
| `ц` unlidan keyin — `ts`, aks holda `s` | `революция` → `revolyutsiya`, `лекция` → `leksiya` |
| `ъ` — tutuq belgisi, lekin `е ё ю я` dan oldin tushadi | `маъно` → `ma'no`, `объект` → `obyekt` |
| `ь` butunlay tushadi | `фильм` → `film` |
| `yo'` — bu `ё` emas, `й` + `ў` | `yo'l` → `йўл`, lekin `yog'och` → `ёғоч` |
| Katta harf so'z shaklini saqlaydi | `Шаҳар` → `Shahar`, `ШАҲАР` → `SHAHAR` |
| Havola va e-pochta manzillari o'girilmaydi | `www.google.com` o'z holida qoladi |

Lotinda yozilgan `o'` va `g'` uchun klaviaturadan kiritiladigan **barcha** apostrof
ko'rinishlari (`'`, `‘`, `’`, `ʻ`, `` ` ``) tushuniladi.

> **Bir joyda qoida yetarli emas.** `ц` harfi o'zlashma so'zlarda faqat lug'at bilan
> aniqlanadi: `funksiya` → `функция`, lekin `pensiya` → `пенсия` — ikkalasi ham bir xil
> ko'rinadi. Shuning uchun lotindan kirillga o'girishda faqat **ishonchli** holat
> (`revolyutsiya` → `революция`, so'z boshidagi `tsex` → `цех`) o'giriladi, qolgani `с`
> bo'lib qolaveradi. Noto'g'ri taxmin qilgandan ko'ra tegmagan ma'qul, lekin natijani
> ko'zdan kechirish foydali.

> **`.txt` fayllar UTF-8 bo'lishi kerak.** Eski Windows-1251 kodlashidagi fayl o'qilmaydi
> va bu haqda aniq xabar beriladi — bunday faylni Bloknotda ochib "UTF-8" ko'rinishida
> saqlang yoki matnni to'g'ridan-to'g'ri "Matn" rejimiga qo'ying.

---

## 🔢 Sanoq sistemasi

Sonni **2, 4, 8, 10, 16, 32, 64, 128 va 256-lik** sanoq sistemalari orasida o'tkazadigan
kalkulyator — ikkining darajalari va kundalik o'nlik. Ishlash tartibi sodda: tepaga son
kiritiladi va uning asosi tanlanadi — pastda natija **hamma asosda bir vaqtning o'zida**
paydo bo'ladi. Tugma bosish shart emas, har bir belgidan keyin jadval o'zi yangilanadi.

### Kiritish

| Xususiyat | Qiymat |
|---|---|
| Asoslar | **2, 4, 8, 10, 16, 32, 64, 128, 256**; tepada 2 · 8 · 10 · 16 uchun tezkor tugmalar, qolgani ro'yxatdan |
| Butun son | Uzunligi cheklanmagan — 100 xonali son ham aniq o'tkaziladi |
| Kasr son | Nuqta ham, vergul ham qabul qilinadi: `25.5` va `25,5` bir xil |
| Manfiy son | `-` bilan boshlanadi |
| Katta-kichik harf | Farqi yo'q: `ff` ham, `FF` ham — 255 |
| Bo'shliqlar | E'tiborsiz qoldiriladi, ya'ni jadvaldan nusxa olingan `1111 1111` ni qaytarib qo'yish ishlaydi |

### Ikki xil yozuv

32-likkacha har bir raqam bitta belgi bilan yoziladi: `0–9`, so'ng `A–V`. **64, 128 va
256-lik** uchun bunday belgi yetmaydi, shuning uchun bu asoslarda har bir raqam o'nlikda
yoziladi va `:` bilan ajratiladi:

```
12345678₁₀ = 47:6:5:14₆₄ = 5:113:66:78₁₂₈ = 188:97:78₂₅₆

255.5₁₀ = 255.128₂₅₆        (nuqtadan keyin ham xuddi shunday)
```

Kiritishda bo'sh joy ham ajratkich bo'lib ishlaydi (`188 97 78`), ya'ni ekrandagi natijani
nusxa olib qaytarib qo'yish mumkin. Ajratkichsiz yozilgan son — bitta raqam: 256-likda
`255` bu 255, `2:5:5` emas.

> Base64 alifbosi 64-lik uchun ixchamroq bo'lardi, lekin 128 va 256-likka baribir yetmaydi —
> uchala asos uchun bitta qoida ishlagani ma'qul.

Kiritilgan belgi tanlangan asosga mos kelmasa, maydonning ostida aniq xabar chiqadi —
masalan ikkilik sistemada `2` yozilsa: *«2» — 2-lik sanoq sistemasining raqami emas.
Ruxsat etilgan belgilar: 0 va 1.*

### Natijalar jadvali

Har bir qatorda asos raqami, uning nomi va natija turadi. To'qqizta qator bir ekranga
sig'adi; 2 · 8 · 10 · 16 — eng ko'p ishlatiladigan asoslar — rangli nishon bilan ajratilgan.

| Tugma | Nima qiladi |
|---|---|
| Qatorning o'zi | O'ng paneldagi qadam-baqadam yechimni shu sanoq sistemasi uchun ochadi |
| Qator oxiridagi nishon | O'sha qiymatni vaqtinchalik xotiraga ko'chiradi |
| **Almashtirish** | Tanlangan natijani kiritish maydoniga qo'yib, asoslarni o'rin almashtiradi |
| **Natijadan nusxa olish** | Tanlangan sanoq sistemasidagi natijani ko'chiradi |

### Qadam-baqadam yechim

O'ng panelda o'tkazish bosqichma-bosqich yoziladi — aynan darslikdagi kabi:

```
1-qadam — 10-lik sanoq sistemasiga o'tkazish
    1 × 16¹ = 16
    A (10) × 16⁰ = 10
    8 × 16⁻¹ = 0.5
  = 1A.8₁₆ = 26.5₁₀

2-qadam — butun qismni 2 ga ketma-ket bo'lish
    26 ÷ 2 = 13, qoldiq 0
    13 ÷ 2 = 6, qoldiq 1
    6 ÷ 2 = 3, qoldiq 0
    3 ÷ 2 = 1, qoldiq 1
    1 ÷ 2 = 0, qoldiq 1
  = Qoldiqlarni oxiridan boshiga qarab o'qiymiz: 11010

3-qadam — kasr qismini 2 ga ketma-ket ko'paytirish
    0.5 × 2 = 1 → 1
  = Butun qismlarni tartib bilan yozamiz: 0.1

Natija: 1A.8₁₆ = 11010.1₂
```

Manba yoki natija allaqachon 10-lik bo'lsa, tegishli bosqich tushib qoladi.

### Sozlamalar

| Sozlama | Tanlovlar | Standart |
|---|---|---|
| Kasr xonalari | 8 · 12 · 16 · 24 · 32 | 16 |
| Raqamlarni guruhlash | Yoqilgan / o'chirilgan | Yoqilgan |

Guruhlash **faqat ko'rinishga** ta'sir qiladi: ikkilik va o'n oltilikda 4 talab, sakkizlik
va o'nlikda 3 talab ajratiladi (`11111111` → `1111 1111`), nusxa olishda esa bo'shliqsiz
qiymat ketadi.

### Aniqlik haqida

Hisob `double` ustida emas, **butun sonlar va oddiy kasrlar** ustida olib boriladi:

- **Butun qism doim aniq** — uzunligidan qat'i nazar. 40 xonali sonni 16-likka o'tkazib,
  qaytarib olsangiz aynan o'sha son chiqadi; `double` bunday sonni allaqachon buzib qo'yardi.
- **Kasr qism** `surat/maxraj` ko'rinishida saqlanadi (`0.1₁₀` = `1/10`), shuning uchun
  yaxlitlash xatosi to'planmaydi.
- Kasr yangi asosda cheksiz davom etsa, u tanlangan xonada **kesiladi** (yaxlitlanmaydi —
  ketma-ket ko'paytirish algoritmi aynan shunday ishlaydi) va bunday natija yonida **≈**
  belgisi turadi. Masalan `0.1₁₀` ikkilikda `0.0001100110011001…` — hech qachon tugamaydi.

> **Kesilgan natijani orqaga o'tkazish.** `≈` bilan belgilangan qiymatni "Almashtirish"
> orqali qaytarsangiz, asl sondan bir oz farq qilishi mumkin — ma'lumot allaqachon
> kesilgan bo'ladi. Dastur bu haqda pastki panelda ogohlantiradi.

---

## 🖥 Kompyuterlarni boshqarish

Boshqa kompyuterlarni **masofadan kuzatish va boshqarish** uchun mo'ljallangan bo'lim. Bu
sahifa — **tarqatish markazi**: unda boshqariladigan kompyuterlarga o'rnatiladigan **agent
(server)** faylini GitHub relizidan yuklab olish va uni o'rnatish tartibi bor. Agentning o'zi
(ekran uzatish, boshqaruv, tarmoq) — alohida katta loyiha; u GitHub'ga qo'yiladi va shu
sahifadan yuklab olinadi.

> **Ruxsat va shaffoflik.** Masofaviy boshqaruvni faqat **o'zingiz administratsiya qiladigan**
> kompyuterlarga (sinf, laboratoriya, ofis) va **foydalanuvchilar xabardor** holatda o'rnating.
> Agent maqsadli kompyuterda **ko'rinadigan belgi** qoldiradi — kuzatuv yashirin emas. Bu
> NetSupport School / Veyon kabi qonuniy sinf boshqaruvi dasturlari ishlaydigan yo'l.

### Yuklab olish

| Xususiyat | Qiymat |
|---|---|
| Manba | Faqat **GitHub** (`https`) — begona serverdan yuklab olishga yo'l qo'yilmaydi |
| Manzil | Agent fayli GitHub relizining to'g'ridan-to'g'ri havolasi; hozircha **placeholder**, real fayl chiqqach kiritiladi |
| Saqlanadigan joy | `%LOCALAPPDATA%\Yordamchi\RemoteControl\` |
| Ishga tushirish | Dastur faylni **faqat yuklab oladi** — o'zi ishga tushirmaydi |

Manzil maydoniga real havola kiritilgunga qadar "Yuklab olish" tugmasi faol bo'lmaydi.
Kiritilgan manzil `https` va GitHub xostida bo'lishi shart, aks holda rad etiladi.

### O'rnatish tartibi

1. **Agentni yuklab oling** — shu sahifadan agent faylini oling.
2. **Maqsadli kompyuterga ko'chiring** — USB, umumiy tarmoq papkasi yoki guruh siyosati (GPO).
3. **Administrator huquqida o'rnating** — agent Windows xizmati sifatida o'rnatiladi va tizim
   majmuasida (tray) ko'rinadigan belgi qoldiradi.
4. **Tarmoq va portni tekshiring** — ikkala kompyuter bir lokal tarmoqda, kerakli port
   (masalan 5405) brandmauerda ochiq.
5. **Ro'yxatda paydo bo'ladi** — o'rnatilgach kompyuter boshqaruv oynasida ko'rinadi.
   *(Boshqaruv oynasi keyingi bosqichda qo'shiladi.)*

---

## 🔄 Yangilanish

Dastur yangi versiya chiqqanini o'zi sezadi va sizga aytadi. **Yuklab olish va o'rnatishni
esa siz bajarasiz** — dastur hech qanday fayl yuklab olmaydi va hech nimani ishga
tushirmaydi.

### Qanday ishlaydi

1. **Ochilishda jimgina tekshiruv.** Dastur GitHub relizlariga bitta so'rov yuborib, eng
   so'nggi versiyani so'raydi. Internet yo'q bo'lsa yoki server javob bermasa — hech qanday
   xato oynasi chiqmaydi, siz buni umuman sezmaysiz.
2. **Yon panelda kichik nuqta.** Yangi versiya bo'lsa chap yon paneldagi
   **"Dastur haqida"** bandi yonida kichik nuqta paydo bo'ladi. U hech narsani to'sib
   qo'ymaydi va hech narsa yuklamaydi — shunchaki sahifaga ishora qiladi.
3. **Batafsili "Dastur haqida" sahifasida.** *"Dastur yangilanishi"* kartochkasi holatni
   matn bilan aytadi — *"Eng so'nggi versiya o'rnatilgan"* yoki *"Yangi versiya mavjud:
   2.3.0 (103 MB)"*.
4. **Ikkita tugma.** **"Tekshirish"** — qo'lda qayta tekshirish; **"Relizlar sahifasi"** —
   GitHub dagi relizni brauzerda ochadi, u yerda o'zgarishlar ro'yxati va o'rnatgich fayli
   turadi.
5. **O'rnatish — odatdagidek.** Yuklab olingan `YordamchiSetup-<versiya>.exe` ni ishga
   tushirasiz; u eski versiyani o'rniga yangilaydi, avval o'chirish shart emas.

> **Nega dastur o'zi yuklab olib o'rnatmaydi.** Bu ataylab qilingan qaror. O'zini o'zi
> yangilaydigan dastur internetdan olingan faylni administrator huquqi bilan ishga
> tushiradi; o'rnatgich esa kod bilan imzolanmagan, ya'ni faylning haqiqiyligini
> ishonchli tasdiqlab bo'lmaydi. Shuning uchun oxirgi qadam — nimani yuklab olish va
> nimani ishga tushirish — foydalanuvchining o'zida qoldirilgan.

| Xususiyat | Qiymat |
|---|---|
| Nima yuboriladi | GitHub relizlar API siga bitta `GET` so'rovi; foydalanuvchi haqidagi ma'lumot yo'q, faqat dastur nomi va versiyasi (`Yordamchi/2.3.0`) — GitHub API `User-Agent` siz so'rovlarni rad etadi |
| Qachon xabar beriladi | Faqat joriy versiyadan **yangi** reliz uchun; qoralama (draft) va sinov (prerelease) relizlari e'tiborga olinmaydi |
| Dastur nima yuklab oladi | **Hech nima.** Havola brauzerda ochiladi, qolgani sizning qo'lingizda |
| Tekshiruvni o'tkazib yuborish | Internet yo'q bo'lsa dastur baribir normal ishlaydi — tekshiruv jimgina o'tkazib yuboriladi |

---

## Tizim talablari

| Talab | Qiymat |
|---|---|
| Operatsion tizim | **Windows 10 (1809+) yoki Windows 11**, 64-bit (x64) |
| Ekran yozuvi uchun | **Windows 10 1903 (build 18362)** yoki undan yangisi |
| Suzuvchi boshqaruv paneli uchun | **Windows 10 2004 (build 19041)** yoki undan yangisi — oynani yozuvdan yashirish shu versiyadan mumkin |
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

1. [**Releases**](https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/latest) bo'limidan
   `YordamchiSetup-2.3.0.exe` faylini yuklab oling va ishga tushiring.
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

> **Keyingi versiyalar ham xuddi shunday o'rnatiladi.** Dastur o'zini o'zi yangilamaydi:
> u faqat yangi versiya chiqqanini "Dastur haqida" sahifasida aytadi, o'rnatgichni esa siz
> [Releases](https://github.com/AbduxalilVoxidjonov/Yordamchi/releases/latest) dan yuklab
> olib ishga tushirasiz (batafsil: [🔄 Yangilanish](#-yangilanish)).

### Korporativ (jimgina) o'rnatish

MSI ham xuddi shu relizda — `Yordamchi-2.3.0-x64.msi`:

```powershell
msiexec /i Yordamchi-2.3.0-x64.msi /qn INSTALLFOLDER="C:\Apps\Yordamchi"
```

O'chirish:

```powershell
msiexec /x Yordamchi-2.3.0-x64.msi /qn
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
| `UzbekTransliterator` | Har bir o'girish qoidasi alohida misol bilan; lotin → kirill → lotin aylanmasi |
| `NumberBaseConverter` | Butun va kasr sonlar, 31 × 31 asos juftligi bo'yicha aylanma, qadam-baqadam yechim |
| `TransliterationService` | Sinov ichida yasalgan haqiqiy `.docx` ustida: bo'lingan run'lar, jadval, kolontitul, maydon kodi |
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
    -p:Version=2.3.0 -p:FileVersion=2.3.0.0 -p:AssemblyVersion=2.3.0.0 `
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
.\build-installer.ps1                        # standart versiya: 2.3.0.0
.\build-installer.ps1 -Version 2.4.0.0       # boshqa versiya bilan
.\build-installer.ps1 -SkipPublish           # mavjud publish papkasini qayta ishlatish
```

Skript to'rt bosqichni bajaradi va natijani `artifacts\` ga qo'yadi:

| # | Bosqich | Natija |
|---|---|---|
| 1 | `dotnet publish -r win-x64 --self-contained -p:PublishReadyToRun=true` | `publish\win-x64\` (~168 MB) |
| 2 | `wix build installer\Package.wxs` | `artifacts\Yordamchi-2.3.0-x64.msi` (LZX:high siqish bilan ~60 MB) |
| 3 | `vc_redist.x64.exe` ni `https://aka.ms/vs/17/release/vc_redist.x64.exe` dan olish | `artifacts\vc_redist.x64.exe` — **bir martalik**, keyingi yig'ilishlarda qayta ishlatiladi |
| 4 | `wix build installer\Bundle.wxs` | `artifacts\YordamchiSetup-2.3.0.exe` — MSI va VC++ ish vaqti ichiga joylangan (`Compressed="yes"`) |

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
o'z sahifasi bilan to'g'ridan-to'g'ri ishlaydi. Arxivlash (`IArchiveService`), yangilanish
(`IUpdateService`), kirill ↔ lotin o'girish (`ITransliterationService`) va sanoq
sistemalari (`INumberSystemService`) ham xuddi shu sababdan fasaddan tashqarida.

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
- Kirill ↔ lotin o'girishda `ц` harfi o'zlashma so'zlarda qoida bilan aniqlanmaydi
  (`funksiya` → `функция`, lekin `pensiya` → `пенсия`) — faqat `ts` qatnashgan ishonchli
  holat o'giriladi, natijani ko'zdan kechirish kerak.
- O'girish uchun `.txt` fayllar UTF-8 kodlashida bo'lishi shart; Windows-1251 dagi eski
  fayllar o'qilmaydi (bu haqda aniq xabar beriladi).
- O'girishda `.doc` (eski Word) qo'llab-quvvatlanmaydi — hujjatni avval `.docx` ga saqlash kerak.
- Sanoq sistemalari kalkulyatorida asoslar ro'yxati qat'iy: **2, 4, 8, 10, 16, 32, 64, 128,
  256**. Oraliqdagi asoslar (3, 5, 6, 7, …) yo'q. Kiritilgan son 512 belgidan oshmasligi kerak.
- Cheksiz kasr tanlangan xonada kesiladi (yaxlitlanmaydi) — bunday natija `≈` bilan
  belgilanadi va uni orqaga o'tkazganda kichik farq bo'lishi mumkin.
- Ekran yozuvi Windows 10 1903 (build 18362) va undan yangi tizimlarni talab qiladi;
  eskirog'ida sahifa ochiladi, lekin "Yozishni boshlash" tugmasi ishlamaydi.
- Ekran yozuvi Windows Media Foundation ga tayanadi. Windows ning **N/KN** nashrlarida u
  yo'q — "Media Feature Pack" ni qo'shimcha o'rnatish kerak.
- Bir vaqtning o'zida bitta manba (bitta monitor yoki bitta oyna) yoziladi; yozuv
  davomida manbani va sozlamalarni o'zgartirib bo'lmaydi.
- Oyna yozib olinayotganda uni yopish yoki kichraytirish kadrlar oqimini to'xtatadi.
- Suzuvchi boshqaruv paneli Windows 10 2004 (build 19041) dan boshlab ishlaydi. Eskiroq
  tizimda u umuman ochilmaydi (aks holda videoning har bir kadrida ko'rinib qolardi) —
  "Pauza" va "To'xtatish" sahifaning o'zida qoladi.

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

---

## ☕ Loyihani qo'llab-quvvatlash

Yordamchi bepul va hech qanday reklama ko'rsatmaydi. Agar u ishingizni yengillashtirgan
bo'lsa, loyihani rivojlantirishga ixtiyoriy hissa qo'shishingiz mumkin:

**9860 3501 4679 1495** — Uzcard · Abduxalil Voxidjonov

Bu majburiy emas va dasturning biror imkoniyatini ochmaydi. Karta raqami dastur ichida ham
turibdi: **"Dastur haqida"** bo'limida, bir bosishda nusxa olish tugmasi bilan.
