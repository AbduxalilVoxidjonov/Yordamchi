using System.Buffers.Binary;

namespace Yordamchi.Remoting.Input;

/// <summary>
/// <see cref="InputEvent"/> ni <see cref="Protocol.PacketType.InputEvent"/> yukiga o'giradi va
/// qaytaradi. Barcha maydonlar bitta tekis tuzilishda saqlanadi (little-endian):
/// <code>
///   kind(1) button(1) pressed(1) reserved(1) x(4 float) y(4 float) wheel(4 int) keyCode(2)
/// </code>
/// Yuk uzunligi qat'iy — buzuq yoki noto'g'ri o'lchamli hodisa jimgina rad etiladi.
/// </summary>
public static class InputEventCodec
{
    private const int PayloadSize = 18;

    public static byte[] Encode(in InputEvent input)
    {
        var buffer = new byte[PayloadSize];

        buffer[0] = (byte)input.Kind;
        buffer[1] = (byte)input.Button;
        buffer[2] = (byte)(input.Pressed ? 1 : 0);
        buffer[3] = 0; // reserved
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(4, 4), input.X);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(8, 4), input.Y);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12, 4), input.WheelDelta);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(16, 2), input.KeyCode);

        return buffer;
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out InputEvent input)
    {
        input = default;

        if (payload.Length != PayloadSize)
            return false;

        var kind = (InputEventKind)payload[0];
        var button = (MouseButton)payload[1];
        var pressed = payload[2] != 0;
        var x = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(4, 4));
        var y = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(8, 4));
        var wheel = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(12, 4));
        var keyCode = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));

        input = kind switch
        {
            InputEventKind.MouseMove => InputEvent.MouseMove(x, y),
            InputEventKind.MouseButton => InputEvent.MouseButtonEvent(button, pressed, x, y),
            InputEventKind.MouseWheel => InputEvent.MouseWheel(wheel),
            InputEventKind.Key => InputEvent.Key(keyCode, pressed),
            _ => default
        };

        return kind is InputEventKind.MouseMove or InputEventKind.MouseButton
            or InputEventKind.MouseWheel or InputEventKind.Key;
    }
}
