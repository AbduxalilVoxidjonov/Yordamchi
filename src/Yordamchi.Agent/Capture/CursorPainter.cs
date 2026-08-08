using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Sichqoncha ko'rsatgichini kadrga chizadi.
/// <para>
/// <b>Nega kerak.</b> Na GDI'ning <c>BitBlt</c>, na DXGI Desktop Duplication kadrga
/// ko'rsatgichni qo'shmaydi — Windows uni alohida qatlam sifatida chizadi. Ko'rsatgichsiz kadr
/// masofadan boshqarishni deyarli imkonsiz qiladi: operator qayerni nishonga olganini
/// ko'rmaydi.
/// </para>
/// <para>
/// Chizish muvaffaqiyatsiz bo'lsa (ko'rsatgich yashirilgan, xavfsiz ish stoli, nishon
/// olinmadi) — kadr ko'rsatgichsiz ketadi. Bu istisno emas: bitta kadrdagi kichik kamchilik
/// butun uzatishni to'xtatmasligi kerak.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CursorPainter
{
    private const int CursorShowing = 0x00000001;
    private const int DrawIconNormal = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public POINT ScreenPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CURSORINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(
        IntPtr deviceContext, int x, int y, IntPtr icon,
        int width, int height, int step, IntPtr brush, int flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    /// <summary>
    /// Ko'rsatgichni <paramref name="graphics"/> ga chizadi. <paramref name="region"/> — kadr
    /// qoplagan to'rtburchak: ko'rsatgichning ekran koordinatasi shu to'rtburchakka nisbatan
    /// qayta hisoblanadi.
    /// </summary>
    public static void Draw(Graphics graphics, ScreenRegion region)
    {
        var info = new CURSORINFO { Size = Marshal.SizeOf<CURSORINFO>() };

        if (!GetCursorInfo(ref info) || (info.Flags & CursorShowing) == 0 || info.Cursor == IntPtr.Zero)
            return;

        // Nishon nuqtasi (hotspot) — ko'rsatgich rasmining qaysi pikseli "aynan shu joy"
        // hisoblanadi. Uni hisobga olmasak, o'q kursori bir necha piksel siljib chiziladi.
        var hotspotX = 0;
        var hotspotY = 0;

        if (GetIconInfo(info.Cursor, out var iconInfo))
        {
            hotspotX = iconInfo.HotspotX;
            hotspotY = iconInfo.HotspotY;

            // GetIconInfo bitmaplarning nusxasini qaytaradi — ularni o'chirmaslik GDI
            // resurslarini sekin-asta tugatadi (har kadrda ikki bitmap!).
            if (iconInfo.MaskBitmap != IntPtr.Zero)
                DeleteObject(iconInfo.MaskBitmap);

            if (iconInfo.ColorBitmap != IntPtr.Zero)
                DeleteObject(iconInfo.ColorBitmap);
        }

        var x = info.ScreenPosition.X - region.Left - hotspotX;
        var y = info.ScreenPosition.Y - region.Top - hotspotY;

        var hdc = graphics.GetHdc();
        try
        {
            DrawIconEx(hdc, x, y, info.Cursor, 0, 0, 0, IntPtr.Zero, DrawIconNormal);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }
}
