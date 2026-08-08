using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Kadrni JPEG ga siqadi. Ikki ekran manbasi (GDI va DXGI) uchun bitta joyda: siqish sifati
/// va kodlagichni izlash mantig'i takrorlanmasligi kerak.
/// <para>
/// <b>Nega JPEG.</b> Xom BGRA kadr 1920×1080 da ~8 MB — sekundda 10 marta yuborilsa tarmoq
/// ko'tarmaydi. JPEG shu kadrni odatda 100–300 KB ga tushiradi va ekran tasviri uchun sifat
/// yo'qolishi ko'zga tashlanmaydi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class JpegEncoder : IDisposable
{
    private readonly ImageCodecInfo _encoder;
    private readonly EncoderParameters _parameters;

    /// <param name="quality">1..100 — kichikroq = kam trafik, past sifat.</param>
    public JpegEncoder(long quality)
    {
        _encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        // Parametrlar bir marta yaratiladi: har kadrda yangisini ochish ortiqcha ish va
        // bo'shatilishi kerak bo'lgan resurs.
        _parameters = new EncoderParameters(1);
        _parameters.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality,
            Math.Clamp(quality, 1, 100));
    }

    public byte[] Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, _encoder, _parameters);
        return stream.ToArray();
    }

    public void Dispose() => _parameters.Dispose();
}
