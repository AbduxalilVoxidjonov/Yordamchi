using Yordamchi.Remoting.Input;

namespace Yordamchi.Tests.Remoting;

/// <summary>
/// Kirish hodisasining kodlanishi. Bu qatlam <b>tarmoqdan kelgan ishonchsiz baytlar</b> bilan
/// ishlaydi, shuning uchun to'g'ri hodisani aynan qaytarish bilan bir qatorda buzuq yukni
/// istisno tashlamasdan rad etishi ham tekshiriladi.
/// </summary>
public sealed class InputEventCodecTests
{
    [Fact]
    public void A_mouse_move_survives_the_round_trip()
    {
        var payload = InputEventCodec.Encode(InputEvent.MouseMove(0.25f, 0.75f));

        Assert.True(InputEventCodec.TryParse(payload, out var parsed));
        Assert.Equal(InputEventKind.MouseMove, parsed.Kind);
        Assert.Equal(0.25f, parsed.X);
        Assert.Equal(0.75f, parsed.Y);
    }

    [Theory]
    [InlineData(MouseButton.Left, true)]
    [InlineData(MouseButton.Right, false)]
    [InlineData(MouseButton.Middle, true)]
    public void A_button_event_keeps_its_button_and_state(MouseButton button, bool pressed)
    {
        var payload = InputEventCodec.Encode(InputEvent.MouseButtonEvent(button, pressed, 0.5f, 0.5f));

        Assert.True(InputEventCodec.TryParse(payload, out var parsed));
        Assert.Equal(InputEventKind.MouseButton, parsed.Kind);
        Assert.Equal(button, parsed.Button);
        Assert.Equal(pressed, parsed.Pressed);
    }

    [Fact]
    public void A_wheel_event_keeps_a_negative_delta()
    {
        // Manfiy qiymat alohida muhim: u ishorasiz butun sonda kodlanadi va noto'g'ri o'girilsa
        // "pastga aylantirish" o'rniga juda katta musbat qiymat chiqadi.
        var payload = InputEventCodec.Encode(InputEvent.MouseWheel(-240));

        Assert.True(InputEventCodec.TryParse(payload, out var parsed));
        Assert.Equal(InputEventKind.MouseWheel, parsed.Kind);
        Assert.Equal(-240, parsed.WheelDelta);
    }

    [Fact]
    public void A_key_event_keeps_its_virtual_key_code()
    {
        var payload = InputEventCodec.Encode(InputEvent.Key(0x1B, pressed: true)); // VK_ESCAPE

        Assert.True(InputEventCodec.TryParse(payload, out var parsed));
        Assert.Equal(InputEventKind.Key, parsed.Kind);
        Assert.Equal(0x1B, parsed.KeyCode);
        Assert.True(parsed.Pressed);
    }

    [Fact]
    public void Positions_outside_the_screen_are_clamped()
    {
        var parsedFar = InputEvent.MouseMove(5f, -3f);

        Assert.Equal(1f, parsedFar.X);
        Assert.Equal(0f, parsedFar.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(19)]
    public void A_payload_of_the_wrong_size_is_refused(int size)
    {
        Assert.False(InputEventCodec.TryParse(new byte[size], out _));
    }

    [Fact]
    public void An_unknown_event_kind_is_refused()
    {
        var payload = InputEventCodec.Encode(InputEvent.MouseMove(0.5f, 0.5f));
        payload[0] = 200; // ro'yxatda yo'q tur

        Assert.False(InputEventCodec.TryParse(payload, out _));
    }
}
