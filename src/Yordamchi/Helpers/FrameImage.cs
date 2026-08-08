using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yordamchi.Remoting.Master;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Helpers;

/// <summary>
/// Agentdan kelgan <see cref="RemoteFrame"/> ni ekranga chiqariladigan <see cref="ImageSource"/> ga
/// o'giradi. Natija <b>muzlatiladi</b> (Freeze), shuning uchun uni fon oqimida yasab, UI oqimidagi
/// xossaga bemalol berish mumkin — WPF muzlatilgan rasmni har qanday oqimda ishlatishga ruxsat beradi.
/// </summary>
public static class FrameImage
{
    /// <summary>Kadrni rasmga o'giradi; baytlar buzuq bo'lsa <c>null</c>.</summary>
    public static ImageSource? TryCreate(RemoteFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        try
        {
            return frame.Format switch
            {
                ScreenImageFormat.Jpeg => FromJpeg(frame.Image),
                ScreenImageFormat.RawBgra => FromRawBgra(frame.Width, frame.Height, frame.Image),
                _ => null
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Bitta buzuq kadr butun ko'rishni to'xtatmasligi kerak.
            return null;
        }
    }

    private static ImageSource FromJpeg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static ImageSource? FromRawBgra(int width, int height, byte[] bytes)
    {
        var stride = width * 4;

        if (bytes.Length < stride * height)
            return null;

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
