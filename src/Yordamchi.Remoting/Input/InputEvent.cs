namespace Yordamchi.Remoting.Input;

/// <summary>Kirish hodisasining turi.</summary>
public enum InputEventKind : byte
{
    None = 0,
    MouseMove = 1,
    MouseButton = 2,
    MouseWheel = 3,
    Key = 4
}

/// <summary>Sichqoncha tugmasi.</summary>
public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2
}

/// <summary>
/// Masterdan agentga yuboriladigan bitta kirish hodisasi (sichqoncha yoki klaviatura).
/// <para>
/// Sichqoncha o'rni <b>normallashtirilgan</b> (0..1) saqlanadi — master va agent ekranlari
/// har xil o'lchamda bo'lishi mumkin, shuning uchun aniq piksel emas, nisbiy joy uzatiladi va
/// agent uni o'z ekraniga moslaydi.
/// </para>
/// </summary>
public readonly struct InputEvent
{
    private InputEvent(InputEventKind kind, float x, float y, MouseButton button, bool pressed, int wheelDelta, ushort keyCode)
    {
        Kind = kind;
        X = x;
        Y = y;
        Button = button;
        Pressed = pressed;
        WheelDelta = wheelDelta;
        KeyCode = keyCode;
    }

    public InputEventKind Kind { get; }

    /// <summary>Normallashtirilgan gorizontal o'rin (0..1).</summary>
    public float X { get; }

    /// <summary>Normallashtirilgan vertikal o'rin (0..1).</summary>
    public float Y { get; }

    public MouseButton Button { get; }

    /// <summary>Tugma/klavisha bosilganmi (aks holda qo'yib yuborilgan).</summary>
    public bool Pressed { get; }

    public int WheelDelta { get; }

    /// <summary>Windows virtual-key kodi (klaviatura uchun).</summary>
    public ushort KeyCode { get; }

    public static InputEvent MouseMove(float x, float y) =>
        new(InputEventKind.MouseMove, Clamp(x), Clamp(y), MouseButton.Left, false, 0, 0);

    public static InputEvent MouseButtonEvent(MouseButton button, bool pressed, float x, float y) =>
        new(InputEventKind.MouseButton, Clamp(x), Clamp(y), button, pressed, 0, 0);

    public static InputEvent MouseWheel(int delta) =>
        new(InputEventKind.MouseWheel, 0, 0, MouseButton.Left, false, delta, 0);

    public static InputEvent Key(ushort keyCode, bool pressed) =>
        new(InputEventKind.Key, 0, 0, MouseButton.Left, pressed, 0, keyCode);

    private static float Clamp(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
}
