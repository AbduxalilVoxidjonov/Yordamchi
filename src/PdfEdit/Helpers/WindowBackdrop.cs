using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PdfEdit.Helpers;

/// <summary>
/// Windows 11 window composition: Mica backdrop and dark title bar, applied through DWM.
/// <para>
/// Everything here degrades silently — on Windows 10, on an unsupported build, or if DWM
/// refuses, the caller simply keeps its solid background. Nothing throws.
/// </para>
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;   // Windows 10 20H1+
    private const int DwmwaSystemBackdropType = 38;     // Windows 11 22H2+
    private const int DwmwaMicaEffect = 1029;           // Windows 11 21H2 (undocumented)

    private const int BackdropMica = 2;

    private const int Win11Build = 22000;
    private const int Win11_22H2Build = 22621;

    /// <summary>True when the OS is new enough for a Mica backdrop.</summary>
    public static bool IsMicaSupported =>
        Environment.OSVersion.Platform == PlatformID.Win32NT &&
        Environment.OSVersion.Version.Build >= Win11Build;

    /// <summary>
    /// Tries to make <paramref name="window"/> render on a Mica backdrop.
    /// </summary>
    /// <returns>
    /// <c>true</c> when Mica was applied — the caller must then leave its root background
    /// transparent so the backdrop shows through. <c>false</c> means "paint your own background".
    /// </returns>
    public static bool TryApplyMica(Window window, bool isDarkMode)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!IsMicaSupported)
            return false;

        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return false; // Called before the HWND exists; retry from SourceInitialized.

            SetImmersiveDarkMode(handle, isDarkMode);

            var applied = Environment.OSVersion.Version.Build >= Win11_22H2Build
                ? SetAttribute(handle, DwmwaSystemBackdropType, BackdropMica)
                : SetAttribute(handle, DwmwaMicaEffect, 1);

            if (!applied)
                return false;

            // The decisive step: WPF paints an opaque black surface by default, which would
            // cover the backdrop entirely. Clearing the composition target lets DWM through.
            if (HwndSource.FromHwnd(handle) is { CompositionTarget: not null } source)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Switches the title bar between the light and dark non-client themes.</summary>
    public static void SetImmersiveDarkMode(Window window, bool isDarkMode)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                SetImmersiveDarkMode(handle, isDarkMode);
        }
        catch (Exception)
        {
            // Cosmetic only.
        }
    }

    private static void SetImmersiveDarkMode(IntPtr handle, bool isDarkMode)
        => SetAttribute(handle, DwmwaUseImmersiveDarkMode, isDarkMode ? 1 : 0);

    private static bool SetAttribute(IntPtr handle, int attribute, int value)
        => DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) == 0;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
