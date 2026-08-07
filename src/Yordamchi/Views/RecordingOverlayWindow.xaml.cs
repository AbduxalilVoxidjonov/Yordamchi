using System.Windows;
using System.Windows.Input;
using Yordamchi.Helpers;

namespace Yordamchi.Views;

/// <summary>
/// Yozuv davomida ekranda turadigan kichik boshqaruv paneli: taymer, pauza va to'xtatish.
/// <para>
/// Nima uchun alohida oyna. Yozuv boshlanganda dastur oynasi kichraytiriladi, aks holda u
/// videoning boshida ko'rinib qolardi — lekin shunda foydalanuvchida yozuvni to'xtatadigan
/// tugma ham qolmaydi. Oynani qaytarib ochish esa uni videoga tushirib yuboradi. Yagona
/// yechim — boshqaruvni <b>yozuvdan yashirilgan</b> alohida panelga chiqarish
/// (<see cref="CaptureExclusion"/>).
/// </para>
/// <para>
/// Panel <see cref="ViewModels.ScreenRecorderViewModel"/> ga bog'lanadi — sahifaning o'zi
/// bilan bir xil holatga, shuning uchun taymer va tugmalar hamma joyda mos turadi.
/// </para>
/// </summary>
public partial class RecordingOverlayWindow : Window
{
    /// <summary>Ekranning pastki chetidan qoldiriladigan masofa.</summary>
    private const double BottomMargin = 48d;

    /// <summary>
    /// Foydalanuvchi panelni surgan joy. <c>static</c>, chunki panel har yozuvda qaytadan
    /// yaratiladi va tanlangan joy seans davomida esda qolishi kerak.
    /// </summary>
    private static Point? _lastPosition;

    public RecordingOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>Panel yozuvdan haqiqatan yashirildimi (diagnostika uchun).</summary>
    public bool IsHiddenFromCapture { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Tartib muhim: avval yashiramiz, keyin ko'rsatamiz. Aks holda panel bir necha kadr
        // davomida yozuvga tushib ulgurardi.
        IsHiddenFromCapture = CaptureExclusion.TryExclude(this);

        WindowBackdrop.TryRoundCorners(this);
        MoveIntoPlace();
    }

    /// <summary>
    /// Panelni oxirgi surilgan joyga, birinchi ochilishda esa ekranning pastki o'rtasiga qo'yadi.
    /// </summary>
    private void MoveIntoPlace()
    {
        var work = SystemParameters.WorkArea;

        var position = _lastPosition ?? new Point(
            work.Left + (work.Width - ActualWidth) / 2,
            work.Bottom - ActualHeight - BottomMargin);

        // Monitorlar tarkibi o'zgargan bo'lishi mumkin (noutbuk dokdan uzilgan) — eski joy
        // endi mavjud bo'lmagan ekranda qolib ketmasligi kerak.
        Left = Math.Clamp(position.X, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        Top = Math.Clamp(position.Y, work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
    }

    /// <summary>Panelning bo'sh joyidan ushlab surish — sarlavha paneli yo'q.</summary>
    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Sichqoncha tugmasi sudrash boshlangunicha qo'yib yuborilgan — zararsiz.
        }

        _lastPosition = new Point(Left, Top);
    }
}
