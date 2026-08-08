using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Virtual ish stolining (barcha monitorlarni qamrab olgan to'rtburchak) o'lchamini beradi.
/// <para>
/// Bitta joyda turishi maqsadli: ekranni olish (GDI) va kirishni yuborish (SendInput'ning
/// absolut koordinatalari) <b>ayni bir</b> to'rtburchakdan foydalanishi shart — aks holda
/// sichqoncha ko'rsatilgan joydan siljib bosiladi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class VirtualScreen
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// Hozirgi virtual ekran to'rtburchagi. Metrikalar 0 qaytarsa (masalan interaktiv seans yo'q)
    /// 1×1 qaytadi — nol o'lchamli bitmap yaratish istisno tashlardi.
    /// </summary>
    public static ScreenRegion Current()
    {
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
            return new ScreenRegion(0, 0, 1, 1);

        return new ScreenRegion(left, top, width, height);
    }
}
