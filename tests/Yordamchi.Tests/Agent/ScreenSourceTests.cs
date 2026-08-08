using Yordamchi.Agent.Capture;
using Yordamchi.Remoting.Protocol;

namespace Yordamchi.Tests.Agent;

/// <summary>
/// Ekran manbalari. Bu qism apparatga bog'liq: DXGI har kompyuterda mavjud emas, GDI esa faol
/// seans talab qiladi. Shuning uchun sinovlar <b>o'zgarmas shartlarni</b> tekshiradi — kadr
/// o'lchami e'lon qilingan to'rtburchakka mos kelishi va zanjirning har qanday muhitda ishlaydigan
/// manba qaytarishi.
/// </summary>
public sealed class ScreenSourceTests
{
    [Fact]
    public void The_synthetic_source_reports_a_region_that_matches_its_frame()
    {
        using var source = new SyntheticScreenSource();

        var frame = source.Capture();

        Assert.Equal(ScreenImageFormat.RawBgra, frame.Format);
        Assert.Equal(source.Bounds.Width, frame.Width);
        Assert.Equal(source.Bounds.Height, frame.Height);
        Assert.NotEmpty(frame.Image);
    }

    [Fact]
    public void The_gdi_source_produces_a_jpeg_that_matches_its_region()
    {
        using var source = new GdiScreenSource();

        var region = source.Bounds;
        var frame = source.Capture();

        Assert.Equal(ScreenImageFormat.Jpeg, frame.Format);
        Assert.Equal(region.Width, frame.Width);
        Assert.Equal(region.Height, frame.Height);
        Assert.NotEmpty(frame.Image);

        // JPEG sarlavhasi: buzuq baytlar emas, haqiqiy rasm qaytganini shu bilan tekshiramiz.
        Assert.Equal(0xFF, frame.Image[0]);
        Assert.Equal(0xD8, frame.Image[1]);
    }

    /// <summary>
    /// DXGI ishlatib bo'lmasa, u <see cref="NotSupportedException"/> tashlashi <b>shart</b> —
    /// <see cref="ScreenSourceFactory"/> ning zaxira zanjiri aynan shunga tayanadi. Ishlasa esa
    /// kadr e'lon qilingan to'rtburchakka mos bo'lishi kerak.
    /// </summary>
    [Fact]
    public void The_dxgi_source_either_captures_a_frame_or_says_it_is_unsupported()
    {
        DxgiScreenSource source;

        try
        {
            source = new DxgiScreenSource();
        }
        catch (NotSupportedException ex)
        {
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            return;
        }

        using (source)
        {
            var region = source.Bounds;
            var frame = source.Capture();

            Assert.Equal(ScreenImageFormat.Jpeg, frame.Format);
            Assert.Equal(region.Width, frame.Width);
            Assert.Equal(region.Height, frame.Height);
            Assert.NotEmpty(frame.Image);
        }
    }

    [Fact]
    public void The_factory_always_returns_a_working_source()
    {
        var messages = new List<string>();

        using var source = ScreenSourceFactory.Create(CaptureMode.Auto, 55, messages.Add);

        var frame = source.Capture();

        Assert.NotEmpty(frame.Image);
        Assert.True(frame.Width > 0);
        Assert.True(frame.Height > 0);

        // Tanlangan usul jurnalga yozilishi kerak: nosozlik tekshirilganda "qaysi usul ishlagan"
        // degan savol birinchi bo'lib chiqadi.
        Assert.NotEmpty(messages);
    }

    [Fact]
    public void The_factory_honours_an_explicit_choice()
    {
        using var source = ScreenSourceFactory.Create(CaptureMode.Synthetic, 55);

        Assert.IsType<SyntheticScreenSource>(source);
    }
}
