using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Haqiqiy ekranni GDI (BitBlt) bilan oladi va JPEG qilib siqadi. Bu v1 uchun yetarli va
/// oddiy; keyingi bosqichda tezroq DXGI Desktop Duplication shu bir <see cref="IScreenSource"/>
/// ortiga qo'yiladi.
/// <para>
/// <b>Cheklov.</b> GDI faqat <b>faol seansda</b> (foydalanuvchi ish stolida) ishlaydi. Agent
/// SYSTEM xizmati sifatida "session 0" da ishlasa, ekranni ko'ra olmaydi — u holda faol
/// seansda yordamchi jarayon ochish kerak bo'ladi (keyingi bosqich). Hozircha agent konsol
/// sifatida foydalanuvchi seansida ishlagani uchun ekran to'g'ri olinadi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiScreenSource : IScreenSource
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private readonly long _jpegQuality;
    private readonly ImageCodecInfo _jpegEncoder;

    /// <param name="jpegQuality">1..100 — kichikroq = kam trafik, past sifat.</param>
    public GdiScreenSource(long jpegQuality = 55)
    {
        _jpegQuality = Math.Clamp(jpegQuality, 1, 100);
        _jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }

    public ScreenFrame Capture()
    {
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // Metrik 0 qaytarsa (masalan seans yo'q) — kichik xavfsiz o'lcham.
        if (width <= 0 || height <= 0)
        {
            width = 1;
            height = 1;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }

        using var stream = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, _jpegQuality);
        bitmap.Save(stream, _jpegEncoder, parameters);

        return new ScreenFrame(width, height, ScreenImageFormat.Jpeg, stream.ToArray());
    }

    public void Dispose()
    {
        // JPEG kodlagichi umumiy resurs — bo'shatiladigan narsa yo'q.
    }
}
