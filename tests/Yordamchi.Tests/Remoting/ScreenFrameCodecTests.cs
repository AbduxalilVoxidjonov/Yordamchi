using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Tests.Remoting;

/// <summary>Ekran kadri yukining formati: o'lcham, kodlash turi va rasm baytlari.</summary>
public sealed class ScreenFrameCodecTests
{
    [Fact]
    public void A_frame_survives_encode_then_parse()
    {
        var image = new byte[] { 10, 20, 30, 40, 50 };

        var payload = ScreenFrameCodec.Encode(1920, 1080, ScreenImageFormat.Jpeg, image);
        var ok = ScreenFrameCodec.TryParse(payload, out var width, out var height, out var format, out var parsed);

        Assert.True(ok);
        Assert.Equal(1920, width);
        Assert.Equal(1080, height);
        Assert.Equal(ScreenImageFormat.Jpeg, format);
        Assert.Equal(image, parsed);
    }

    [Fact]
    public void A_too_short_payload_is_rejected()
        => Assert.False(ScreenFrameCodec.TryParse([1, 2, 3], out _, out _, out _, out _));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(100, 999999)]
    public void An_impossible_size_is_refused_on_encode(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => ScreenFrameCodec.Encode(width, height, ScreenImageFormat.Jpeg, [1]));
}
