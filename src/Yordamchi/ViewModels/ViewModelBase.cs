using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// Ishchi sahifalar uchun umumiy asos: bitta band/progress kanali, bekor qilish va
/// <see cref="PdfServiceException"/> ni odam tushunadigan xabarga aylantiradigan yagona joy.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private CancellationTokenSource? _cancellation;

    protected ViewModelBase(IDialogService dialogService)
    {
        DialogService = dialogService;
    }

    protected IDialogService DialogService { get; }

    /// <summary>Sahifa sarlavhasida ko'rinadigan qisqa nom.</summary>
    public abstract string Title { get; }

    /// <summary><see cref="Title"/> ostida ko'rinadigan bir qatorli izoh.</summary>
    public abstract string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary><see cref="IsBusy"/> ning teskarisi; komandalar buni <c>CanExecute</c> sifatida ishlatadi.</summary>
    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cancellation?.Cancel();

    /// <summary>
    /// Uzoq davom etadigan amalni band qoplamasi bilan bajaradi: progress ulanadi, bekor qilish
    /// mumkin bo'ladi va har qanday kutilgan nosozlik foydalanuvchiga ko'rsatiladi.
    /// </summary>
    /// <returns>Amal to'liq yakunlansa <c>true</c>.</returns>
    protected async Task<bool> RunAsync(
        string busyMessage,
        Func<IProgress<PdfProgress>, CancellationToken, Task> operation,
        string? successMessage = null)
    {
        if (IsBusy)
            return false;

        _cancellation = new CancellationTokenSource();
        IsBusy = true;
        BusyMessage = busyMessage;
        ProgressValue = 0;
        IsProgressIndeterminate = true;
        StatusMessage = string.Empty;

        // Progress<T> shu yerda UI SynchronizationContext ni oladi, ya'ni chaqiruv o'zi UI oqimiga o'tadi.
        var progress = new Progress<PdfProgress>(report =>
        {
            IsProgressIndeterminate = report.IsIndeterminate;
            ProgressValue = report.Percentage;
            if (!string.IsNullOrWhiteSpace(report.Message))
                BusyMessage = report.Message!;
        });

        try
        {
            await operation(progress, _cancellation.Token).ConfigureAwait(true);
            StatusMessage = successMessage ?? string.Empty;
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Amal bekor qilindi.";
            return false;
        }
        catch (PdfServiceException ex)
        {
            StatusMessage = ex.Message;
            DialogService.ShowError(DescribeError(ex.Kind), BuildErrorMessage(ex));
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            DialogService.ShowError("Kutilmagan xato", ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            ProgressValue = 0;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>
    /// <see cref="IsBusy"/> o'zgarganda chaqiriladi. Voris sinflar shu yerda o'z komandalarini
    /// qayta baholaydi, chunki <c>[NotifyCanExecuteChangedFor]</c> voris sinfdagi komandalarga yetmaydi.
    /// </summary>
    protected virtual void OnBusyStateChanged()
    {
    }

    partial void OnIsBusyChanged(bool value) => OnBusyStateChanged();

    private static string DescribeError(PdfErrorKind kind) => kind switch
    {
        PdfErrorKind.FileNotFound => "Fayl topilmadi",
        PdfErrorKind.PasswordProtected => "Parol bilan himoyalangan",
        PdfErrorKind.CorruptedDocument => "Shikastlangan hujjat",
        PdfErrorKind.UnsupportedImage => "Qo'llab-quvvatlanmaydigan rasm",
        PdfErrorKind.OutputNotWritable => "Faylni yozib bo'lmadi",
        PdfErrorKind.EmptySelection => "Saqlash uchun hech narsa yo'q",
        PdfErrorKind.PageIndexOutOfRange => "Noto'g'ri sahifa",
        PdfErrorKind.InvalidPassword => "Parol noto'g'ri",
        PdfErrorKind.MissingComponent => "Komponent topilmadi",
        PdfErrorKind.UnsupportedFormat => "Format mos emas",
        PdfErrorKind.InvalidOptions => "Sozlamalar noto'g'ri",
        PdfErrorKind.OperationFailed => "Amal bajarilmadi",
        _ => "PDF xatosi"
    };

    private static string BuildErrorMessage(PdfServiceException ex)
    {
        var hint = ex.Kind switch
        {
            PdfErrorKind.PasswordProtected => "Hujjat paroli bilan himoyalangan. \"Qulfni ochish\" vositasidan foydalaning yoki parolni kiriting.",
            PdfErrorKind.OutputNotWritable => "Fayl boshqa dasturda ochiq bo'lishi mumkin. Uni yoping yoki boshqa joy tanlang.",
            PdfErrorKind.CorruptedDocument => "Fayl haqiqiy PDF emas yoki shikastlangan.",
            PdfErrorKind.InvalidPassword => "Kiritilgan parol to'g'ri kelmadi. Katta-kichik harflarga e'tibor bering.",
            PdfErrorKind.MissingComponent => "Kerakli komponent o'rnatilmagan. \"Dastur haqida\" sahifasida uni yuklab olishingiz mumkin.",
            PdfErrorKind.UnsupportedFormat => "Bu fayl turi tanlangan vosita uchun mos emas.",
            _ => null
        };

        return hint is null ? ex.Message : $"{ex.Message}\n\n{hint}";
    }
}
