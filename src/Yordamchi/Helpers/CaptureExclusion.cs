using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Yordamchi.Helpers;

/// <summary>
/// Oynani ekran yozuvi va skrinshotlardan yashiradi: u monitorda ko'rinib turadi, lekin
/// suratga tushmaydi.
/// <para>
/// Bu yozuv paytidagi suzuvchi boshqaruv paneli uchun kerak. Aks holda "To'xtatish" tugmasi
/// videoning har bir kadrida turib qolardi — ya'ni panelni ekranga chiqarishning ma'nosi
/// yo'qolardi.
/// </para>
/// <para>
/// <c>WDA_EXCLUDEFROMCAPTURE</c> Windows 10 <b>2004</b> (build 19041) da paydo bo'lgan. Undan
/// eski tizimda oynani yozuvdan yashirib bo'lmaydi, shuning uchun u yerda panel umuman
/// ochilmaydi va boshqaruv tugmalari sahifaning o'zida qoladi — <see cref="IsSupported"/>.
/// </para>
/// <para>
/// Bu yerdagi hamma narsa jimgina degradatsiya qiladi: hech qachon istisno tashlamaydi,
/// faqat <c>false</c> qaytaradi.
/// </para>
/// </summary>
public static class CaptureExclusion
{
    /// <summary>Oyna faqat monitorda ko'rinadi; kadr olishga urinishlar uni umuman ko'rmaydi.</summary>
    private const uint WdaExcludeFromCapture = 0x00000011;

    private const int ExcludeFromCaptureBuild = 19041;

    /// <summary>Bu tizimda oynani yozuvdan yashirish mumkinmi.</summary>
    public static bool IsSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT
        && Environment.OSVersion.Version.Build >= ExcludeFromCaptureBuild;

    /// <summary>
    /// Oynani yozuvdan chiqarib tashlashga urinadi. Oyna HWND ga ega bo'lishi kerak, ya'ni
    /// bu <c>SourceInitialized</c> dan oldin chaqirilmasin.
    /// </summary>
    /// <returns><c>true</c> — oyna endi videoga tushmaydi.</returns>
    public static bool TryExclude(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!IsSupported)
            return false;

        try
        {
            var handle = new WindowInteropHelper(window).Handle;

            return handle != IntPtr.Zero
                   && SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
