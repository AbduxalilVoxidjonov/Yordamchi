using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Apparatga bog'liq bo'lmagan sinov manbasi: har chaqirilganda o'zgarib turadigan kichik
/// kadr yasaydi. Uning vazifasi — ulanish va uzatish quvurini <b>haqiqiy GPU'siz</b> ishlatib
/// ko'rish. Haqiqiy DXGI manbasi keyingi bosqichda shu interfeys ustiga qo'yiladi.
/// </summary>
public sealed class SyntheticScreenSource : IScreenSource
{
    private const int Width = 32;
    private const int Height = 32;
    private const int BytesPerPixel = 4; // BGRA

    private int _frameCounter;

    public ScreenFrame Capture()
    {
        // Kadr raqamiga qarab rang siljiydi, ya'ni ketma-ket kadrlar bir xil bo'lmaydi —
        // bu keyinchalik "faqat o'zgargan qismini yuborish" mantig'ini sinashda foydali.
        var tick = _frameCounter++;
        var image = new byte[Width * Height * BytesPerPixel];

        for (var i = 0; i < image.Length; i += BytesPerPixel)
        {
            var pixel = i / BytesPerPixel;
            image[i + 0] = (byte)(pixel + tick);         // B
            image[i + 1] = (byte)(pixel * 2 + tick);     // G
            image[i + 2] = (byte)(tick);                 // R
            image[i + 3] = 0xFF;                         // A
        }

        return new ScreenFrame(Width, Height, ScreenImageFormat.RawBgra, image);
    }

    public void Dispose()
    {
        // Sintetik manbada bo'shatadigan resurs yo'q.
    }
}
