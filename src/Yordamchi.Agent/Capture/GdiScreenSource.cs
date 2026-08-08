using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Haqiqiy ekranni GDI (BitBlt) bilan oladi va JPEG qilib siqadi. Bu hamma joyda ishlaydigan
/// zaxira yo'l: <see cref="DxgiScreenSource"/> tezroq, lekin u Desktop Duplication API'siga
/// tayanadi va u har muhitda mavjud emas (eski drayver, ba'zi virtual mashinalar, masofaviy
/// seans) — shunda GDI ishlatiladi.
/// <para>
/// GDI <b>butun virtual ish stolini</b> (barcha monitorlar) bitta kadrda oladi — bu DXGI'dan
/// farqi, u faqat bitta monitorni beradi.
/// </para>
/// <para>
/// <b>Cheklov.</b> GDI faqat <b>faol seansda</b> (foydalanuvchi ish stolida) ishlaydi. Agent
/// SYSTEM xizmati sifatida "session 0" da ishlasa, ekranni ko'ra olmaydi — shu sababli xizmat
/// o'zi ekran olmaydi, faol seansda bola jarayon ochadi (<c>Service/SessionBridge</c>).
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GdiScreenSource : IScreenSource
{
    private readonly JpegEncoder _encoder;

    /// <param name="jpegQuality">1..100 — kichikroq = kam trafik, past sifat.</param>
    public GdiScreenSource(long jpegQuality = 55)
    {
        _encoder = new JpegEncoder(jpegQuality);
    }

    /// <summary>GDI butun virtual ish stolini oladi — barcha monitorlar bitta kadrda.</summary>
    public ScreenRegion Bounds => VirtualScreen.Current();

    public ScreenFrame Capture()
    {
        var region = VirtualScreen.Current();

        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                region.Left, region.Top, 0, 0,
                new Size(region.Width, region.Height),
                CopyPixelOperation.SourceCopy);

            CursorPainter.Draw(graphics, region);
        }

        return new ScreenFrame(region.Width, region.Height, ScreenImageFormat.Jpeg, _encoder.Encode(bitmap));
    }

    public void Dispose() => _encoder.Dispose();
}
