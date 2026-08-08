using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yordamchi.Helpers;
using Yordamchi.Remoting.Master;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// Agentdan kelgan kadrni WPF rasmiga o'girish. Buzuq kadr butun ko'rishni to'xtatmasligi
/// kerak, shuning uchun u istisno emas, <c>null</c> qaytaradi.
/// </summary>
public sealed class FrameImageTests
{
    [Fact]
    public void A_raw_bgra_frame_becomes_an_image_of_the_right_size()
    {
        // 2x2 BGRA = 16 bayt.
        var frame = new RemoteFrame(2, 2, ScreenImageFormat.RawBgra, new byte[16]);

        var image = FrameImage.TryCreate(frame) as BitmapSource;

        Assert.NotNull(image);
        Assert.Equal(2, image!.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
        Assert.True(image.IsFrozen); // fon oqimidan UI'ga berish uchun muzlatilgan
    }

    [Fact]
    public void A_jpeg_frame_decodes_back_to_an_image()
    {
        // WPF ning o'zi bilan yaroqli JPEG yasaymiz, so'ng uni kadr sifatida ochamiz.
        var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 4 * 4);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);

        var frame = new RemoteFrame(4, 4, ScreenImageFormat.Jpeg, stream.ToArray());

        var image = FrameImage.TryCreate(frame) as BitmapSource;

        Assert.NotNull(image);
        Assert.Equal(4, image!.PixelWidth);
    }

    [Fact]
    public void A_raw_frame_with_too_few_bytes_returns_null()
        => Assert.Null(FrameImage.TryCreate(new RemoteFrame(100, 100, ScreenImageFormat.RawBgra, new byte[10])));

    [Fact]
    public void Garbage_jpeg_bytes_return_null()
        => Assert.Null(FrameImage.TryCreate(new RemoteFrame(4, 4, ScreenImageFormat.Jpeg, [1, 2, 3, 4, 5])));

    [Fact]
    public void An_unknown_format_returns_null()
        => Assert.Null(FrameImage.TryCreate(new RemoteFrame(4, 4, ScreenImageFormat.Unknown, new byte[64])));
}
