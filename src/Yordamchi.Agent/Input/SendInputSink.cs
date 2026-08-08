using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yordamchi.Agent.Capture;
using Yordamchi.Remoting.Input;
using RemoteMouseButton = Yordamchi.Remoting.Input.MouseButton;

namespace Yordamchi.Agent.Input;

/// <summary>
/// Kirish hodisalarini Windows'ning <c>SendInput</c> funksiyasi bilan bajaradi.
/// <para>
/// <b>Nega <c>SendInput</c>, <c>SetCursorPos</c>/<c>mouse_event</c> emas.</b> <c>SendInput</c>
/// hodisalarni tizimning kirish oqimiga to'g'ridan-to'g'ri qo'yadi: ular to'g'ri tartibda,
/// bo'linmasdan yetib boradi va o'yin/DirectInput ilovalari ham ularni ko'radi. Eski
/// <c>mouse_event</c> esa eskirgan va hodisalarni birlashtirib yubormaydi.
/// </para>
/// <para>
/// <b>Koordinatalar.</b> Master normallashtirilgan (0..1) o'rin yuboradi — u ko'rib turgan kadr
/// ichidagi nisbiy joy. Agent uni avval kadr to'rtburchagidagi haqiqiy pikselga, so'ng
/// <c>SendInput</c> talab qiladigan 0..65535 oralig'idagi virtual ish stoli koordinatasiga
/// o'giradi. Shu tufayli monitor soni va ruxsat (resolution) farq qilsa ham bosish to'g'ri
/// joyga tushadi.
/// </para>
/// <para>
/// <b>Ruxsat.</b> Bu sinf o'zi hech qanday tekshiruv qilmaydi — ruxsat kalitchasi
/// <see cref="GatedInputSink"/> da. Bu ataylab: "bajarish" va "ruxsat berish" mantiqlari
/// aralashib ketmasligi kerak.
/// </para>
/// <para>
/// <b>Cheklov.</b> UIPI (User Interface Privilege Isolation) sababli oddiy huquq bilan
/// ishlayotgan jarayon administrator ilovasining oynasiga kirish yubora olmaydi; xuddi shunday
/// Ctrl+Alt+Del (SAS) ni ham hech qanday dastur yubora olmaydi. Agent xizmat sifatida faol
/// seansda yuqori huquq bilan ishlaganda bu cheklov qolmaydi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SendInputSink : IInputSink
{
    private const int InputMouse = 0;
    private const int InputKeyboard = 1;

    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventAbsolute = 0x8000;
    private const uint MouseEventVirtualDesk = 0x4000;

    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScanCode = 0x0008;

    private const uint MapVkToVsc = 0;

    /// <summary>
    /// <c>SendInput</c> absolut koordinatalarni 0..65535 oralig'ida kutadi (o'lchamdan
    /// qat'iy nazar).
    /// </summary>
    private const int AbsoluteRange = 65535;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    public bool Inject(in InputEvent input, ScreenRegion region)
    {
        return input.Kind switch
        {
            InputEventKind.MouseMove => Send(MouseAt(region, input.X, input.Y, MouseEventMove)),
            InputEventKind.MouseButton => SendButton(input, region),
            InputEventKind.MouseWheel => SendWheel(input.WheelDelta),
            InputEventKind.Key => SendKey(input.KeyCode, input.Pressed),
            _ => false
        };
    }

    private bool SendButton(in InputEvent input, ScreenRegion region)
    {
        var flags = input.Button switch
        {
            RemoteMouseButton.Left => input.Pressed ? MouseEventLeftDown : MouseEventLeftUp,
            RemoteMouseButton.Right => input.Pressed ? MouseEventRightDown : MouseEventRightUp,
            RemoteMouseButton.Middle => input.Pressed ? MouseEventMiddleDown : MouseEventMiddleUp,
            _ => 0u
        };

        if (flags == 0)
            return false;

        // Bosishdan oldin ko'rsatgichni shu nuqtaga qo'yamiz: master bosgan joy bilan agentdagi
        // ko'rsatgich o'rni bir xil bo'lishi shart, aks holda bosish boshqa element ustiga tushadi.
        // Ikki hodisa bitta chaqiruvda yuboriladi — orasiga begona harakat tushmasligi uchun.
        return Send(
            MouseAt(region, input.X, input.Y, MouseEventMove),
            MouseAt(region, input.X, input.Y, flags));
    }

    private static bool SendWheel(int delta)
    {
        if (delta == 0)
            return false;

        var wheel = new INPUT
        {
            Type = InputMouse,
            Data = { Mouse = new MOUSEINPUT { MouseData = unchecked((uint)delta), Flags = MouseEventWheel } }
        };

        return Send(wheel);
    }

    private static bool SendKey(ushort virtualKey, bool pressed)
    {
        if (virtualKey == 0)
            return false;

        var scanCode = (ushort)MapVirtualKey(virtualKey, MapVkToVsc);

        var flags = pressed ? 0u : KeyEventKeyUp;
        if (IsExtendedKey(virtualKey))
            flags |= KeyEventExtendedKey;

        // Skan-kodni ham beramiz: ba'zi ilovalar (ayniqsa o'yinlar) faqat skan-kodga qaraydi.
        // Skan-kod topilmasa (kartada yo'q klavisha) faqat virtual kod bilan yuboriladi.
        if (scanCode != 0)
            flags |= KeyEventScanCode;

        var key = new INPUT
        {
            Type = InputKeyboard,
            Data = { Keyboard = new KEYBDINPUT { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags } }
        };

        return Send(key);
    }

    private static INPUT MouseAt(ScreenRegion region, float normalizedX, float normalizedY, uint flags)
    {
        var virtualScreen = VirtualScreen.Current();

        // Kadr ichidagi nisbiy o'rin -> ish stolidagi piksel.
        var x = region.Left + normalizedX * region.Width;
        var y = region.Top + normalizedY * region.Height;

        // Piksel -> virtual ish stoli bo'ylab 0..65535. Yarim piksel qo'shilishi ("+ 0.5")
        // yaxlitlash xatosini kamaytiradi: aks holda o'ng/quyi chegaradagi nuqtalar bir piksel
        // ichkariga tushardi.
        var absoluteX = (x - virtualScreen.Left + 0.5) * AbsoluteRange / Math.Max(1, virtualScreen.Width);
        var absoluteY = (y - virtualScreen.Top + 0.5) * AbsoluteRange / Math.Max(1, virtualScreen.Height);

        return new INPUT
        {
            Type = InputMouse,
            Data =
            {
                Mouse = new MOUSEINPUT
                {
                    X = Clamp(absoluteX),
                    Y = Clamp(absoluteY),
                    Flags = flags | MouseEventAbsolute | MouseEventVirtualDesk
                }
            }
        };
    }

    private static int Clamp(double value) =>
        value < 0 ? 0 : value > AbsoluteRange ? AbsoluteRange : (int)value;

    private static bool Send(params INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;

    /// <summary>
    /// "Kengaytirilgan" klavishalar (o'ng Ctrl/Alt, strelkalar, Home/End, Insert/Delete va
    /// hokazo) bayroqsiz yuborilsa raqamli blokdagi juftiga aylanib qoladi — masalan strelka
    /// o'rniga raqam kiritiladi.
    /// </summary>
    private static bool IsExtendedKey(ushort virtualKey) => virtualKey switch
    {
        0x0D => false,  // VK_RETURN — asosiy Enter (NumPad Enter alohida kelmaydi)
        0x11 => true,   // VK_CONTROL
        0x12 => true,   // VK_MENU (Alt)
        0x21 => true,   // VK_PRIOR (PageUp)
        0x22 => true,   // VK_NEXT (PageDown)
        0x23 => true,   // VK_END
        0x24 => true,   // VK_HOME
        0x25 => true,   // VK_LEFT
        0x26 => true,   // VK_UP
        0x27 => true,   // VK_RIGHT
        0x28 => true,   // VK_DOWN
        0x2C => true,   // VK_SNAPSHOT (PrintScreen)
        0x2D => true,   // VK_INSERT
        0x2E => true,   // VK_DELETE
        0x5B => true,   // VK_LWIN
        0x5C => true,   // VK_RWIN
        0x5D => true,   // VK_APPS
        0x6F => true,   // VK_DIVIDE (NumPad /)
        0x90 => true,   // VK_NUMLOCK
        0xA3 => true,   // VK_RCONTROL
        0xA5 => true,   // VK_RMENU
        _ => false
    };

    // ---------------------------------------------------------------- Win32 tuzilmalari

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int Type;
        public INPUTUNION Data;
    }

    /// <summary>
    /// Win32 dagi birlashma (union): bitta xotira sichqoncha, klaviatura yoki apparat hodisasi
    /// sifatida o'qiladi. <see cref="FieldOffsetAttribute"/> bilan uchtasi ham bir joydan
    /// boshlanadi — C# da <c>union</c> shunday ifodalanadi.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
