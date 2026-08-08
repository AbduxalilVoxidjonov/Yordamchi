# Kompyuterlarni boshqarish — arxitektura va yo'l xaritasi

Bu hujjat "Kompyuterlarni boshqarish" bo'limi ortidagi masofaviy boshqaruv tizimini
tavsiflaydi: u nima, qanday qismlardan iborat va qaysi bosqichlari tayyor.

> **Ruxsat va shaffoflik — asosiy shart.** Bu tizim NetSupport School / Veyon kabi
> **qonuniy sinf/laboratoriya boshqaruvi** uchun. U faqat **o'zingiz administratsiya
> qiladigan** kompyuterlarga va **foydalanuvchilar xabardor** holatda o'rnatiladi. Agent
> maqsadli kompyuterda **ko'rinadigan belgi** (tray) qoldiradi va boshqaruv ruxsatini shu
> belgidan bir bosishda olib qo'yish mumkin — yashirin kuzatuv, antivirusdan yashirinish yoki
> ruxsatsiz tarqatish bu loyihaning maqsadi emas va qo'llab-quvvatlanmaydi.

## Qismlar

| Qism | Nima | Holati |
|---|---|---|
| **Master** | Yordamchi: "Kompyuterlarni boshqarish" (tarqatish) va "Kompyuter ekranlari" (topish + ekran ko'rish) bo'limlari | Tarqatish + ekran ko'rish paneli tayyor; **ekranni boshqarish rejimi (UI) — keyingi bosqich** |
| **Agent (server)** | Boshqariladigan kompyuterdagi dastur: ulanish, ekran uzatish, kirish qabul qilish, xizmat, tray | **Tayyor**: DXGI/GDI ekran, `SendInput`, cheklangan buyruqlar, tray belgisi, Windows xizmati + seans ko'prigi, o'rnatgich |
| **`Yordamchi.Remoting`** | Ikkala tomon uchun umumiy poydevor: protokol, shifrlash, handshake, discovery, kirish/buyruq kodlash | **Tayyor va sinovdan o'tgan** |

`Yordamchi.Remoting` ataylab oynasiz (`net8.0`) va platformaga bog'lanmagan — sof mantiq
bo'lgani uchun to'liq unit-test bilan qamrab olingan (haqiqiy apparatsiz).

## Protokol (`Yordamchi.Remoting.Protocol`)

Master va agent orasidagi binar ramka (frame), hammasi little-endian:

```
ofset 0..1   magic       "YA" (0x59 0x41)     begona ulanishni ajratadi
ofset 2      version     protokol versiyasi
ofset 3      type        PacketType
ofset 4      flags       PacketFlags (bit0 = Encrypted)
ofset 5      reserved    0
ofset 6..9   length      yuk uzunligi (uint32)
ofset 10..13 crc32       yukning CRC-32 si
ofset 14..   payload     yuk baytlari
```

- **Uzunlik chegarasi** (`MaxPayloadLength`, 64 MB): TCP oqimidan kelgan uzunlikka ko'r-ko'rona
  ishonib bo'lmaydi — yaroqsiz katta uzunlik xotirani tugatib qo'yardi.
- **CRC-32** tasodifiy buzilishni ilg'aydi (xavfsizlik emas — u AES-GCM teg ishi).
- `PacketType`: `Handshake`, `Ping/Pong`, `ScreenRequest/ScreenFrame`, `InputEvent`,
  `Command`, `FileChunk`, `Disconnect`, `Error`. Qiymatlar aniq raqamlangan va faqat oxiriga
  qo'shiladi — protokol ikki tomonda alohida yig'ilgani uchun tartib buzilmasligi shart.

### Yuk formatlari

| Paket | Yuk |
|---|---|
| `ScreenRequest` | 1 bayt: `1` — boshlash, `0` — to'xtatish |
| `ScreenFrame` | `ScreenFrameCodec`: kenglik, balandlik, format (JPEG / xom BGRA), rasm baytlari |
| `InputEvent` | `InputEventCodec`, qat'iy 18 bayt: `kind(1) button(1) pressed(1) reserved(1) x(4) y(4) wheel(4) keyCode(2)` |
| `Command` | `RemoteCommandCodec`: `kind(1) reserved(1) textLength(2) text(UTF-8)` |
| `Error` | UTF-8 matn — foydalanuvchiga ko'rsatish uchun sabab (masalan "boshqarish o'chirilgan") |

Kirish va buyruq yuklari **ishonchsiz ma'lumot** sifatida qaraladi: noto'g'ri o'lcham, noma'lum
tur yoki buzuq matn istisno tashlamaydi — `TryParse` `false` qaytaradi va agent bunday paketni
jimgina rad etadi. Buzuq paket ulanishni ham, agentni ham yiqitmasligi kerak.

## Xavfsizlik (`Yordamchi.Remoting.Security`)

- **`SessionCipher`** — AES-256-GCM. Har xabar `nonce(12) || tag(16) || shifrmatn` ko'rinishida;
  bilib turib qilingan bitta bayt o'zgartirish ham ochishda aniqlanadi (istisno tashlanadi).
  Nonce har xabarda yangi va tasodifiy.
- **`KeyExchange`** — RSA-2048 + OAEP-SHA256. Ulanishni boshlovchi vaqtinchalik RSA juftini
  yaratadi va ochiq kalitni yuboradi; ikkinchi tomon AES sessiya kalitini shu ochiq kalit
  bilan o'raydi. Maxfiy kalit tarmoqqa chiqmaydi, sessiya kaliti faqat o'ralgan holda uzatiladi.

Handshake oqimi: `Handshake` (ochiq kalit) → `HandshakeAck` (o'ralgan sessiya kaliti) →
keyingi barcha yuklar `PacketFlags.Encrypted` bilan. Handshake va shifrlangan kanal
(`RemoteHandshake`, `SecureChannel`) `Yordamchi.Remoting` da — ikkala tomon bir kodni
ishlatadi va u haqiqiy TCP loopback ustida sinovdan o'tgan.

## Agent (`Yordamchi.Agent`)

Boshqariladigan kompyuterdagi dastur. Uch xil ishga tushadi:

| Rejim | Qachon | Ko'rinadigan belgi |
|---|---|---|
| Oddiy jarayon | Qo'lda ishga tushirilganda, sinash uchun | Konsol oynasi + tray belgisi |
| Windows xizmati (`--service`) | O'rnatgich orqali; kompyuter yonganda o'zi | Faol seansdagi bola jarayonning tray belgisi |
| Seansdagi bola jarayon | Xizmat ochadi (`--parent-pid` bilan) | Tray belgisi (oyna yo'q) |

### Tarmoq va ulanish

- **`AgentServer`** — TCP ulanishlarni qabul qiladi (standart port **5406**), har biriga alohida
  ulanish. Ulanish/uzilish hodisalari tray bildirishnomasiga chiqadi — foydalanuvchi kim
  ulanganini darhol ko'radi.
- **`AgentConnection`** — handshake, so'ng shifrlangan paketlar halqasi: `Ping`→`Pong`,
  `ScreenRequest`→kadr oqimi, `InputEvent`→kirish, `Command`→cheklangan buyruq,
  `Disconnect`→yopish. Yozuvlar bitta qulf bilan tartibga solingan (kadr oqimi va javoblar
  aralashmasin).
- **`DiscoveryAnnouncer`** — UDP mayoqni muntazam yuboradi (port **5405**).
- Rad etilgan so'rov haqida master **bir marta** `Error` paketi bilan xabardor qilinadi:
  sichqoncha sekundda o'nlab hodisa yuboradi va har biriga javob qaytarish kanalni ham,
  jurnalni ham ko'mib tashlardi.

### Ekran olish (`Yordamchi.Agent.Capture`)

`IScreenSource` ortida uchta manba bor va `ScreenSourceFactory` ishga tushishda ishlaydiganini
**haqiqiy urinish orqali** tanlaydi (imkoniyatni "so'rab" bilib bo'lmaydi):

| Manba | Nima | Izoh |
|---|---|---|
| **`DxgiScreenSource`** | DXGI Desktop Duplication (GPU) | Asosiy yo'l. Bitta monitor (standart — asosiy). Ish stoli o'zgarmasa kadr kelmaydi — oxirgi kadr qayta yuboriladi |
| **`GdiScreenSource`** | GDI `BitBlt` | Zaxira yo'l. **Barcha monitorlarni** bitta kadrda beradi |
| **`SyntheticScreenSource`** | 32×32 sun'iy kadr | Apparatsiz sinov uchun |

O'lchov (shu repozitoriyda, 1920×1080): DXGI ~8 ms/kadr, GDI ~43 ms/kadr — DXGI kadrni
kompozitordan tayyor holda oladi, GDI esa har kadrda protsessor bilan ko'chiradi.

Ikki manba ham kadrni JPEG qiladi (`JpegEncoder`, standart sifat 55): xom BGRA kadr 1920×1080
da ~8 MB, JPEG esa ~100–300 KB — sekundda 10 kadrni tarmoq faqat shunda ko'taradi.

**Sichqoncha ko'rsatgichi kadrga alohida chiziladi** (`CursorPainter`): na `BitBlt`, na Desktop
Duplication uni kadrga qo'shmaydi, ko'rsatgichsiz kadrda esa operator qayerni nishonga olganini
ko'rmaydi.

**`ScreenRegion` — kadr qoplagan to'rtburchak.** Har manba o'zi qoplagan to'rtburchakni
(`Bounds`) e'lon qiladi. Bu kirishni to'g'ri joyga yuborish uchun shart: master
normallashtirilgan (0..1) o'rin yuboradi, DXGI bitta monitorni, GDI esa butun virtual ish
stolini beradi — 0.5 qiymati ikki holatda ekranning har xil nuqtasiga tushadi.

### Kirish yuborish (`Yordamchi.Agent.Input`)

- **`SendInputSink`** — hodisalarni `SendInput` bilan bajaradi. `SetCursorPos`/`mouse_event`
  emas: `SendInput` hodisalarni tizim kirish oqimiga to'g'ri tartibda, bo'linmasdan qo'yadi va
  DirectInput ilovalari ham ularni ko'radi.
- Koordinata yo'li: normallashtirilgan o'rin → kadr to'rtburchagidagi piksel → virtual ish
  stoli bo'ylab 0..65535 (`MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`). Shu tufayli monitor
  soni va ekran ruxsati farq qilsa ham bosish to'g'ri joyga tushadi.
- Bosish har doim **avval ko'rsatgichni shu nuqtaga qo'yadi**, keyin tugmani bosadi — ikkisi
  bitta `SendInput` chaqiruvida, orasiga begona harakat tushmasligi uchun.
- Klavishalar virtual kod **va** skan-kod bilan yuboriladi; kengaytirilgan klavishalar
  (strelkalar, o'ng Ctrl/Alt, Home/End va hokazo) `KEYEVENTF_EXTENDEDKEY` bilan — aks holda
  strelka o'rniga raqamli blokdagi juftini bosgan bo'lardi.
- **`GatedInputSink`** — ruxsat kalitchasi. Har hodisada qaytadan so'raladi: foydalanuvchi
  tray menyusidan ruxsatni olib qo'ysa, o'zgarish darhol amal qilishi kerak.
- **`DisabledInputSink`** — kutubxonaning standart holati. Kirish yuborish faqat host ataylab
  yoqqanda ishlaydi.

Cheklovlar: UIPI sababli oddiy huquqdagi jarayon administrator ilovasining oynasiga kirish
yubora olmaydi; Ctrl+Alt+Del (SAS) ni esa hech qanday dastur yubora olmaydi.

### Cheklangan buyruqlar (`Yordamchi.Agent.Commands`)

Buyruqlar ro'yxati **yopiq** (`RemoteCommandKind`): `ShowMessage` (foydalanuvchiga qisqa xabar)
va `LockScreen` (ish stolini qulflash). Ochiq qobiq (shell), ixtiyoriy dastur ishga tushirish
yoki fayl yo'li bilan ishlash yo'q va qo'shilmaydi — masterdan kelgan matn hech qachon buyruq
satriga tushmaydi. Noma'lum tur bajarilmaydi va `Error` bilan rad etiladi.

Xabar tray bildirishnomasi sifatida ko'rsatiladi; tray bo'lmasa modal oyna alohida oqimda
ochiladi — ulanish halqasi to'xtab qolmasligi kerak.

### Xizmat va seans ko'prigi (`Yordamchi.Agent.Service`)

**Muammo: session 0 izolyatsiyasi.** Windows xizmatlari ish stoli bo'lmagan 0-seansda ishlaydi.
U yerdan turib foydalanuvchi ekranini olish ham, unga kirish yuborish ham mumkin emas — bu
ataylab qo'yilgan xavfsizlik chegarasi.

**Yechim.** Xizmat o'zi ekran bilan ishlamaydi:

1. `AgentServiceHost` (xizmat) `SessionBridge` ni yuritadi.
2. `SessionBridge` faol konsol seansini topadi (`WTSGetActiveConsoleSessionId`), foydalanuvchi
   nishonini oladi (`WTSQueryUserToken` + `DuplicateTokenEx`) va agentni **shu foydalanuvchi
   nomidan** ochadi (`CreateProcessAsUser`, `winsta0\default` ish stolida, `CREATE_NO_WINDOW`).
3. Bola jarayon tugasa qaytadan ochiladi; faol seans almashsa (boshqa foydalanuvchi kirdi) eski
   jarayon to'xtatilib, yangi seansda yangisi ochiladi.
4. Bola jarayon `--parent-pid` bilan ochiladi va **xizmat tugashi bilan o'zi ham chiqadi** —
   xizmat yiqilsa faol seansda hech kim boshqarmaydigan "yetim" tinglovchi qolib ketmasligi kerak.

`ServiceControl` xizmatni ro'yxatga qo'shadi/olib tashlaydi (`sc.exe` orqali: yaratish, tavsif,
nosozlikdan keyin qayta ishga tushish, ishga tushirish) va `FirewallRules` brandmauerda
**faqat shu dastur fayli uchun** kiruvchi TCP qoidasini ochadi. Qoida bo'lmasa agent ishlaydi,
lekin master unga ulana olmaydi.

### Tray belgisi (`Yordamchi.Agent.Ui`)

`TrayIndicator` — agent ishlayotganining ko'rinadigan belgisi va boshqaruv paneli: sarlavha
(mashina:port), ulanishlar soni, **"Boshqaruvga ruxsat"** va **"Buyruqlarga ruxsat"** belgilari,
"Jurnalni ochish", "Chiqish". Yangi ulanish va masofaviy xabar qalqib chiquvchi bildirishnoma
sifatida ko'rsatiladi.

Belgi o'zining STA oqimida yashaydi (xabar halqasi bloklanmasligi kerak), holatni esa UI
oqimidagi taymer **o'zi so'rab** oladi — boshqa oqimdan UI elementiga tegish `Invoke`
zanjirlarini talab qilardi, bu esa xatoga moyil.

### Jurnal

`AgentLog` konsolga (bo'lsa) va faylga yozadi: `%ProgramData%\Yordamchi\Agent\agent.log`, yozib
bo'lmasa `%LocalAppData%` ga tushadi; 1 MB dan oshsa `.old` ga ko'chiriladi. Jurnalga yozib
bo'lmagani agentni to'xtatish uchun sabab emas.

Jurnal shaffoflik vositasi ham: kim ulandi, qachon boshqaruv ishlatildi yoki rad etildi — hammasi
kompyuter egasi ochib ko'ra oladigan joyda.

### Buyruq satri

```
YordamchiAgent [parametrlar]

  -p, --port <1..65535>     TCP boshqaruv porti (standart: 5406)
  -q, --quality <1..100>    JPEG sifati (standart: 55)
      --fps <1..30>         Sekundiga kadr (standart: 10)
      --capture <usul>      auto | dxgi | gdi | synthetic (standart: auto)
      --no-input            Sichqoncha/klaviatura yuborishni o'chirish
      --no-commands         Masofaviy buyruqlarni o'chirish
      --no-tray             Tray belgisini ko'rsatmaslik
      --no-discovery        UDP mayoqni o'chirish (IP qo'lda kiritiladi)
      --install             Windows xizmati sifatida o'rnatish (administrator kerak)
      --uninstall           Xizmatni olib tashlash (administrator kerak)
      --service             Xizmat rejimi (xizmat menejeri o'zi chaqiradi)
  -h, --help                Yordam
```

`AgentOptions` sozlamalarni buyruq satriga **qaytarib yozishni** ham biladi: o'rnatishda
tanlangan port va ruxsatlar xizmatning buyruq satriga, u orqali esa seansdagi bola jarayonga
o'zgarmagan holda yetib boradi.

## Master paneli (`Yordamchi.Remoting.Master` + Yordamchi)

- **`MasterSession`** — masterning agentga ulanishi: handshake, fon halqasida kadrlarni o'qib
  `FrameReceived` orqali chiqaradi; `SendInputAsync` va `SendCommandAsync` bilan boshqaruv
  yuboradi; agent rad etsa `ErrorReported` hodisasi chiqadi. UI'ni bilmaydi — loopback ustida
  sinaladi.
- **`DiscoveryListener`** — UDP mayoqlarni tinglaydi, topilgan kompyuterlarni chiqaradi.
- Yordamchida **"Kompyuter ekranlari"** bo'limi: "Qidirish" (discovery), qo'lda IP kiritish,
  ulanish va ekranni ko'rsatish (`FrameImage` kadrlarni WPF rasmiga o'giradi). Discovery faqat
  foydalanuvchi bosganda yoqiladi — dastur ochilishida brandmauer so'rovini chiqarmaslik uchun.
- **Qoldi:** ekranni boshqarish rejimi (sichqoncha/klaviatura hodisalarini panelda ushlab
  `SendInputAsync` ga uzatish), ko'p oynali eskizlar (thumbnail grid), fayl tarqatish.

## Topilish (`Yordamchi.Remoting.Discovery`)

- **`DiscoveryBeacon`** — UDP mayoq (port **5405**). Agent o'zini e'lon qiladi, master eshitib
  ro'yxatga qo'shadi; IP ni qo'lda kiritish shart emas. Xabarda maxfiy narsa yo'q (rol, port,
  mashina nomi), haqiqiy autentifikatsiya TCP handshake'da bo'ladi. Begona/buzuq UDP paketi
  istisno emas, `null` qaytaradi — discovery portiga har xil paketlar tushishi odatiy hol.

## O'rnatgich

`build-agent-installer.ps1` → `artifacts\YordamchiAgentSetup.exe`:

1. Agentni o'zi-yetarli (self-contained, ReadyToRun) win-x64 qilib chiqaradi — nishon
   kompyuterda .NET o'rnatilgan bo'lishi shart emas.
2. `installer\Agent.wxs` bilan MSI yasaydi. MSI fayllarni "Program Files\Yordamchi Agent" ga
   qo'yadi va **agentning o'z `--install` rejimini** chaqiradi (deferred custom action): xizmat
   nomi, buyruq satri, qayta ishga tushish siyosati va brandmauer qoidasi — hammasi bitta
   joyda, sinovdan o'tgan C# kodida. WiX tilida takrorlash ikkita haqiqat manbasi bo'lardi.
3. `installer\AgentBundle.wxs` bilan MSI ni `YordamchiAgentSetup.exe` ichiga o'raydi.

Fayl nomi ataylab versiyasiz — dasturdagi yuklab olish havolasi shu nomga bog'langan
(`RemoteControlService.AgentFileName`). Olib tashlash "Dasturlar va komponentlar" dan: MSI
`--uninstall` ni chaqiradi (xizmat + brandmauer qoidasi olib tashlanadi).

## Sinovlar

| Sinov | Nimani tekshiradi |
|---|---|
| `PacketCodecTests`, `SecurityTests`, `HandshakeTests` | Ramka, AES-GCM, RSA handshake (loopback ustida) |
| `ScreenFrameCodecTests`, `DiscoveryBeaconTests` | Kadr va mayoq kodlash, buzuq paketni rad etish |
| `InputEventCodecTests`, `RemoteCommandCodecTests` | Kirish/buyruq kodlash, chegaradan chiqqan qiymatlar, buzuq yuk |
| `AgentConnectionTests` | Handshake → Ping/Pong → kadr oqimi; begona protokol rad etiladi |
| `AgentControlTests` | Kirish va buyruq **bajaruvchigacha** yetib boradi; ruxsat o'chirilganda bajarilmaydi va master bir marta ogohlantiriladi; buzuq yuk ulanishni buzmaydi |
| `AgentOptionsTests` | Buyruq satrini o'qish va qaytarib yozish (xizmat/bola jarayon uchun) |
| `ScreenSourceTests` | Kadr o'lchami e'lon qilingan to'rtburchakka mos; DXGI yo ishlaydi, yo `NotSupportedException` beradi; zanjir har muhitda ishlaydigan manba qaytaradi |
| `MasterSessionTests` | Masterning ulanishi va kadr qabul qilishi |

Haqiqiy `SendInput` sinovlarda ishlatilmaydi — o'rniga hodisalarni yozib boruvchi bajaruvchi
qo'yiladi, shu tufayli butun zanjir sichqonchani qimirlatmasdan sinaladi.

## Bosqichlar (yo'l xaritasi)

- [x] **0-bosqich — Tarqatish sahifasi.** "Kompyuterlarni boshqarish" bo'limi: agentni GitHub'dan
  yuklab olish + o'rnatish tartibi. *(2.3.1)*
- [x] **1-bosqich — Umumiy poydevor.** `Yordamchi.Remoting`: protokol, AES-256-GCM, RSA
  handshake, UDP discovery — to'liq sinovdan o'tgan.
- [x] **2a-bosqich — Agent ulanish yadrosi.** `Yordamchi.Agent`: TCP server, handshake,
  shifrlangan halqa, ekran uzatish quvuri, discovery mayoq.
- [x] **2b-bosqich — Haqiqiy ekran.** `DxgiScreenSource` (Desktop Duplication) + `GdiScreenSource`
  zaxira sifatida, ko'rsatgichni chizish, `ScreenSourceFactory` zanjiri.
- [x] **3-bosqich (agent tomoni) — Boshqaruv.** `SendInput` bilan sichqoncha/klaviatura,
  cheklangan buyruqlar (xabar, ekran qulfi), ruxsat kalitchalari va tray boshqaruvi.
  **Qoldi:** masterda ekranni boshqarish rejimi (UI).
- [x] **4-bosqich — Master paneli.** `MasterSession` + `DiscoveryListener`; Yordamchida
  "Kompyuter ekranlari" bo'limi — topish, ulanish, ekranni ko'rish. **Qoldi:** ko'p oynali
  eskizlar (thumbnail grid), fayl tarqatish.
- [x] **5-bosqich — Reliz.** Windows xizmati, seans ko'prigi, tray belgisi, brandmauer qoidasi,
  `YordamchiAgentSetup.exe`. Agent GitHub relizida (`agent-v1` tegi) va uning havolasi dasturga
  bog'langan (`RemoteControlService.ConfiguredDownloadUrl`). Agentning yangi nusxasi chiqqanda
  shu tegdagi aktiv almashtiriladi — dasturni qayta yig'ish shart emas.

> **Ikkita haqiqiy kompyuterda sinov shart.** Ekran olish, kirish yuborish va xizmat + seans
> ko'prigi shu repozitoriyda bitta kompyuterda (loopback, haqiqiy DXGI kadrlar) sinab
> ko'rilgan. Xizmatni o'rnatish, foydalanuvchi almashishi va tarmoq orqali boshqaruv esa
> ikkita haqiqiy kompyuterda tekshirilishi kerak — u yerdagi natija "foydalanishga tayyor"
> deb belgilashning shartidir.
