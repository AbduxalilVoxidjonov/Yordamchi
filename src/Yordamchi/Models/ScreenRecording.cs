namespace Yordamchi.Models;

/// <summary>Yozib olinadigan manba turi.</summary>
public enum RecordingSourceKind
{
    /// <summary>Butun monitor.</summary>
    Display,

    /// <summary>Tanlangan bitta oyna.</summary>
    Window
}

/// <summary>
/// Yozib olish uchun tanlanishi mumkin bo'lgan manba.
/// <para>
/// <see cref="Id"/> — bu ScreenRecorderLib tushunadigan qurilma yo'li yoki oyna tutqichi
/// (HWND) ning matn ko'rinishi. ViewModel uni hech qachon talqin qilmaydi: shunchaki
/// servisga qaytarib beradi. Aynan shu tufayli UI qatlami yozib olish kutubxonasining
/// turlariga bog'lanib qolmaydi.
/// </para>
/// </summary>
/// <param name="Kind">Monitormi yoki oynami.</param>
/// <param name="Id">Servis uchun manba identifikatori.</param>
/// <param name="Title">Ro'yxatda ko'rinadigan nom, masalan "\\.\DISPLAY1 — 1920×1080".</param>
public sealed record RecordingSourceInfo(RecordingSourceKind Kind, string Id, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Ovoz qurilmasi (mikrofon yoki tizim chiqishi).</summary>
/// <param name="Id">Servis uchun qurilma identifikatori; <c>null</c> — tizimning standart qurilmasi.</param>
/// <param name="Name">Ro'yxatda ko'rinadigan nom.</param>
public sealed record AudioDeviceInfo(string? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Video kodek tanlovi.</summary>
public enum VideoEncoderKind
{
    /// <summary>H.264/AVC — hamma joyda ochiladi, standart tanlov.</summary>
    H264,

    /// <summary>H.265/HEVC — bir xil sifatda ~30% kichik fayl, lekin eskiroq pleyerlarda ochilmasligi mumkin.</summary>
    H265
}

/// <summary>Yozib olish sifati; bitrate ni to'g'ridan-to'g'ri so'ramaslik uchun.</summary>
public enum RecordingQuality
{
    /// <summary>Kichik fayl — uzun darsliklar va ekran ko'rsatmalari uchun.</summary>
    Low,

    /// <summary>Muvozanatli standart tanlov.</summary>
    Medium,

    /// <summary>Mayda matn ham o'qiladigan yuqori sifat; fayl ancha kattaroq.</summary>
    High
}

/// <summary>Yozib olish jarayonining holati.</summary>
public enum RecorderState
{
    /// <summary>Yozilmayapti.</summary>
    Idle,

    /// <summary>Boshlash buyrug'i berildi, kutubxona hali tayyorlanmoqda.</summary>
    Starting,

    /// <summary>Yozilyapti.</summary>
    Recording,

    /// <summary>Vaqtincha to'xtatilgan.</summary>
    Paused,

    /// <summary>To'xtatish buyrug'i berildi, fayl yakunlanmoqda.</summary>
    Finishing
}

/// <summary>
/// Foydalanuvchi tanlagan yozib olish sozlamalari. Sof ma'lumot — hech qanday
/// kutubxonaga bog'liq emas.
/// </summary>
public sealed class ScreenRecordingOptions
{
    /// <summary>Qaysi monitor yoki oyna yoziladi. <c>null</c> — asosiy monitor.</summary>
    public RecordingSourceInfo? Source { get; set; }

    /// <summary>Sekundiga kadrlar. 15–60 oralig'ida cheklanadi.</summary>
    public int Framerate { get; set; } = 30;

    public RecordingQuality Quality { get; set; } = RecordingQuality.Medium;

    public VideoEncoderKind Encoder { get; set; } = VideoEncoderKind.H264;

    /// <summary>Apparat (GPU) kodlash. O'chirilsa protsessor ishlatiladi — sekinroq, lekin mosroq.</summary>
    public bool UseHardwareEncoding { get; set; } = true;

    /// <summary>Tizim ovozi (dinamikdan chiqayotgan hamma narsa) yozilsinmi.</summary>
    public bool RecordSystemAudio { get; set; } = true;

    /// <summary>Mikrofon yozilsinmi.</summary>
    public bool RecordMicrophone { get; set; }

    /// <summary><c>null</c> bo'lsa tizimning standart chiqish qurilmasi olinadi.</summary>
    public string? SystemAudioDeviceId { get; set; }

    /// <summary><c>null</c> bo'lsa tizimning standart kirish qurilmasi olinadi.</summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Sichqoncha ko'rsatkichi videoda ko'rinsinmi.</summary>
    public bool ShowCursor { get; set; } = true;

    /// <summary>Sichqoncha bosilganda halqa chizilsinmi — darslik videolari uchun foydali.</summary>
    public bool HighlightClicks { get; set; }

    /// <summary>Tayyor .mp4 fayl tushadigan papka.</summary>
    public string OutputFolder { get; set; } = string.Empty;
}
