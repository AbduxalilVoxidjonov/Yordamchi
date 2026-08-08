# Kompyuterlarni boshqarish — arxitektura va yo'l xaritasi

Bu hujjat "Kompyuterlarni boshqarish" bo'limi ortidagi masofaviy boshqaruv tizimini
tavsiflaydi: u nima, qanday qismlardan iborat va qaysi bosqichlari tayyor.

> **Ruxsat va shaffoflik — asosiy shart.** Bu tizim NetSupport School / Veyon kabi
> **qonuniy sinf/laboratoriya boshqaruvi** uchun. U faqat **o'zingiz administratsiya
> qiladigan** kompyuterlarga va **foydalanuvchilar xabardor** holatda o'rnatiladi. Agent
> maqsadli kompyuterda **ko'rinadigan belgi** (tray) qoldiradi — yashirin kuzatuv, antivirusdan
> yashirinish yoki ruxsatsiz tarqatish bu loyihaning maqsadi emas va qo'llab-quvvatlanmaydi.

## Qismlar

| Qism | Nima | Holati |
|---|---|---|
| **Master** | Yordamchi ilovasidagi "Kompyuterlarni boshqarish" bo'limi — agentni tarqatish, keyin kompyuterlar ro'yxati va boshqaruv | Tarqatish sahifasi tayyor; boshqaruv oynasi — keyingi bosqich |
| **Agent (server)** | Boshqariladigan kompyuterdagi dastur: ulanish, ekran uzatish, kirish qabul qilish | **Ulanish yadrosi tayyor** (konsol); DXGI, kirish va xizmat — keyingi bosqich |
| **`Yordamchi.Remoting`** | Ikkala tomon uchun umumiy poydevor: protokol, shifrlash, handshake, discovery | **Tayyor va sinovdan o'tgan** |

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

Boshqariladigan kompyuterdagi dastur. Hozircha **konsol ilovasi** — ochiq konsol oynasi
foydalanuvchi uchun "bu kompyuter kuzatilishi mumkin" degan **ko'rinadigan belgi** vazifasini
bajaradi.

Tayyor qismlar:
- **`AgentServer`** — TCP ulanishlarni qabul qiladi (5406-port), har biriga alohida ulanish.
- **`AgentConnection`** — handshake, so'ng shifrlangan paketlar halqasi: `Ping`→`Pong`,
  `ScreenRequest`→ekran kadrlarini uzatish, `Disconnect`→yopish. Yozuvlar bitta qulf bilan
  tartibga solingan (kadr oqimi va javoblar aralashmasin).
- **`IScreenSource`** — ekran manbasi abstraksiyasi. Hozir `SyntheticScreenSource` (apparatsiz,
  sinov uchun) ishlatiladi; haqiqiy DXGI manbasi shu interfeys ustiga qo'yiladi.
- **`DiscoveryAnnouncer`** — UDP mayoqni muntazam yuboradi.

> **Buyruq bajarish ataylab yo'q.** `PacketType.Command` hozircha e'tiborsiz qoldiriladi:
> ixtiyoriy tizim buyrug'ini masofadan bajarish eng xavfli imkoniyat. U keyin faqat
> **ruxsat etilgan, cheklangan** amallar (xabar ko'rsatish, ekranni qulflash) sifatida
> qo'shiladi — ochiq qobiq (shell) sifatida emas.

## Topilish (`Yordamchi.Remoting.Discovery`)

- **`DiscoveryBeacon`** — UDP mayoq (port **5405**). Agent o'zini e'lon qiladi, master eshitib
  ro'yxatga qo'shadi; IP ni qo'lda kiritish shart emas. Xabarda maxfiy narsa yo'q (rol, port,
  mashina nomi), haqiqiy autentifikatsiya TCP handshake'da bo'ladi. Begona/buzuq UDP paketi
  istisno emas, `null` qaytaradi — discovery portiga har xil paketlar tushishi odatiy hol.

## Bosqichlar (yo'l xaritasi)

- [x] **0-bosqich — Tarqatish sahifasi.** "Kompyuterlarni boshqarish" bo'limi: agentni GitHub'dan
  yuklab olish + o'rnatish tartibi. *(2.3.1)*
- [x] **1-bosqich — Umumiy poydevor.** `Yordamchi.Remoting`: protokol, AES-256-GCM, RSA
  handshake, UDP discovery — to'liq sinovdan o'tgan.
- [x] **2a-bosqich — Agent ulanish yadrosi.** `Yordamchi.Agent`: TCP server, handshake,
  shifrlangan halqa, ekran uzatish quvuri (sintetik manba bilan), discovery mayoq — konsol
  ilovasi sifatida ishlaydi, TCP loopback ustida sinovdan o'tgan.
- [ ] **2b-bosqich — Haqiqiy ekran + xizmat.** DXGI Desktop Duplication (Vortice.Windows) bilan
  haqiqiy ekran olish; Windows xizmati va tray belgisi; SYSTEM va faol seans orasidagi ko'prik
  (session 0 izolyatsiyasi). **Apparat va ikkita kompyuterda sinov talab qiladi.**
- [ ] **3-bosqich — Boshqaruv.** Agentda `SendInput` orqali sichqoncha/klaviatura yuborish
  (faqat ruxsat etilgan buyruqlar bilan); masterda to'liq ekran ko'rish/boshqarish oynasi.
- [ ] **4-bosqich — Master paneli.** Yordamchi bo'limida discovery orqali topilgan kompyuterlar
  ro'yxati, ko'p oynali eskizlar (thumbnail grid), fayl tarqatish.
- [ ] **5-bosqich — Reliz.** Agent o'rnatgichi GitHub relizga qo'yiladi, uning URL'i dasturga
  o'zgarmas qilib bog'lanadi; app va agent birga chiqariladi.

> **2b–3-bosqichlar apparatga bog'liq** (GPU/DXGI, kirish yuborish, Windows xizmati) va ikkita
> haqiqiy kompyuterda sinov talab qiladi — ular "foydalanishga tayyor" deb belgilanishidan
> oldin real muhitda ishlatib ko'riladi.
