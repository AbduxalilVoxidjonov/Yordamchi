using Yordamchi.Models;

namespace Yordamchi.Services.Abstractions;

/// <summary>Yozib olish holati o'zgarganda uzatiladi.</summary>
public sealed class RecorderStateChangedEventArgs(RecorderState state) : EventArgs
{
    public RecorderState State { get; } = state;
}

/// <summary>Yozib olish muvaffaqiyatli yakunlanganda uzatiladi.</summary>
public sealed class ScreenRecordingCompletedEventArgs(string filePath, TimeSpan duration) : EventArgs
{
    /// <summary>Saqlangan .mp4 faylning to'liq yo'li.</summary>
    public string FilePath { get; } = filePath;

    public TimeSpan Duration { get; } = duration;
}

/// <summary>Yozib olish xato bilan tugaganda uzatiladi.</summary>
public sealed class ScreenRecordingFailedEventArgs(string message, string? partialFilePath) : EventArgs
{
    public string Message { get; } = message;

    /// <summary>Kutubxona chala faylni saqlab qolgan bo'lsa — uning yo'li.</summary>
    public string? PartialFilePath { get; } = partialFilePath;
}

/// <summary>
/// Ekranni videoga yozib olish shartnomasi.
/// <para>
/// Barcha hodisalar UI oqimida ko'tariladi — implementatsiya kerak bo'lsa o'zi
/// dispetcherga o'tkazadi, shu tufayli ViewModel'da qo'shimcha marshalling kerak emas.
/// </para>
/// <para>
/// Amal ketma-ketligi: <see cref="GetDisplays"/>/<see cref="GetWindows"/> bilan manba
/// tanlanadi → <see cref="StartRecording"/> → <see cref="StopRecording"/> →
/// <see cref="RecordingCompleted"/> hodisasi kelganda fayl tayyor. To'xtatish
/// darhol qaytadi: fayl yakunlanishi (moov atomini yozish) bir necha yuz millisekund
/// vaqt oladi va shu sababli natija hodisa orqali xabar qilinadi.
/// </para>
/// </summary>
public interface IScreenRecorderService : IDisposable
{
    /// <summary>Joriy holat.</summary>
    RecorderState State { get; }

    /// <summary>
    /// Bu kompyuterda yozib olish umuman mumkinmi. Faqat Windows versiyasi tekshiriladi
    /// (Windows Graphics Capture 10.0.18362 dan bor); kodek va Media Foundation
    /// mavjudligini oldindan bilib bo'lmaydi — ular yo'q bo'lsa xato
    /// <see cref="RecordingFailed"/> orqali keladi.
    /// </summary>
    bool IsSupported { get; }

    event EventHandler<RecorderStateChangedEventArgs>? StateChanged;

    event EventHandler<ScreenRecordingCompletedEventArgs>? RecordingCompleted;

    event EventHandler<ScreenRecordingFailedEventArgs>? RecordingFailed;

    /// <summary>Ulangan monitorlar ro'yxati.</summary>
    IReadOnlyList<RecordingSourceInfo> GetDisplays();

    /// <summary>Yozib olish mumkin bo'lgan ochiq oynalar ro'yxati (sarlavhasi bor va ko'rinadiganlari).</summary>
    IReadOnlyList<RecordingSourceInfo> GetWindows();

    /// <summary>Mikrofonlar. Birinchi element — tizimning standart qurilmasi.</summary>
    IReadOnlyList<AudioDeviceInfo> GetMicrophones();

    /// <summary>Tizim ovozi manbalari (dinamik/naushnik). Birinchi element — standart qurilma.</summary>
    IReadOnlyList<AudioDeviceInfo> GetSpeakers();

    /// <summary>
    /// Yozib olishni boshlaydi va yaratilayotgan faylning to'liq yo'lini qaytaradi.
    /// </summary>
    /// <exception cref="PdfServiceException">
    /// Papkaga yozib bo'lmasa, manba topilmasa yoki kutubxona ishga tushmasa.
    /// </exception>
    string StartRecording(ScreenRecordingOptions options);

    /// <summary>Yozib olishni to'xtatadi. Fayl yakunlangach <see cref="RecordingCompleted"/> keladi.</summary>
    void StopRecording();

    /// <summary>Vaqtincha to'xtatadi (fayl yopilmaydi).</summary>
    void PauseRecording();

    /// <summary>Vaqtincha to'xtatilgan yozuvni davom ettiradi.</summary>
    void ResumeRecording();
}
