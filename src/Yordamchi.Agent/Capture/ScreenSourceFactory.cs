using System.Runtime.Versioning;

namespace Yordamchi.Agent.Capture;

/// <summary>Ekran olish usuli — buyruq satridan tanlanadi.</summary>
public enum CaptureMode
{
    /// <summary>Avval DXGI, ishlamasa GDI, u ham ishlamasa sintetik (standart).</summary>
    Auto = 0,

    /// <summary>Faqat DXGI Desktop Duplication.</summary>
    Dxgi = 1,

    /// <summary>Faqat GDI (BitBlt).</summary>
    Gdi = 2,

    /// <summary>Faqat sintetik manba — apparatsiz sinov uchun.</summary>
    Synthetic = 3
}

/// <summary>
/// Ishlaydigan ekran manbasini tanlaydi.
/// <para>
/// <b>Nega zanjir kerak.</b> Eng tez usul (DXGI) hamma joyda mavjud emas, GDI esa faol seans
/// talab qiladi. Agent turli kompyuterlarga o'rnatiladi va "ishlamadi" degan javob emas,
/// <b>ishlaydigan eng yaxshi usul</b> kerak — shuning uchun tanlov ishga tushirish paytida
/// bir marta, haqiqiy urinish orqali qilinadi (imkoniyatlarni "so'rab" bilib bo'lmaydi:
/// DXGI faqat urinib ko'rganda xato beradi).
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class ScreenSourceFactory
{
    /// <summary>
    /// Berilgan usul bo'yicha manba yaratadi. <see cref="CaptureMode.Auto"/> da zanjir bo'ylab
    /// tushadi va tanlangan usulni <paramref name="log"/> ga yozadi.
    /// </summary>
    public static IScreenSource Create(CaptureMode mode, long jpegQuality, Action<string>? log = null)
    {
        switch (mode)
        {
            case CaptureMode.Dxgi:
                return new DxgiScreenSource(jpegQuality);

            case CaptureMode.Gdi:
                return new GdiScreenSource(jpegQuality);

            case CaptureMode.Synthetic:
                return new SyntheticScreenSource();

            default:
                return CreateBest(jpegQuality, log);
        }
    }

    private static IScreenSource CreateBest(long jpegQuality, Action<string>? log)
    {
        try
        {
            var dxgi = new DxgiScreenSource(jpegQuality);
            log?.Invoke("Ekran olish: DXGI Desktop Duplication.");
            return dxgi;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Invoke($"DXGI ishlatilmadi ({ex.Message})");
        }

        try
        {
            var gdi = new GdiScreenSource(jpegQuality);

            // GDI konstruktori ishlashi hech narsani kafolatlamaydi — seans yo'q bo'lsa xato
            // faqat birinchi kadrda chiqadi. Shuning uchun shu yerda bir marta sinab ko'ramiz.
            _ = gdi.Capture();
            log?.Invoke("Ekran olish: GDI (BitBlt).");
            return gdi;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Invoke($"Haqiqiy ekran olinmadi ({ex.Message}) — sintetik manbaga o'tildi.");
        }

        return new SyntheticScreenSource();
    }
}
