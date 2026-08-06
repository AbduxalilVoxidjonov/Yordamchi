using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfEdit.Models;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.ViewModels;

/// <summary>
/// Shared plumbing for the three workspace view models: a single busy/progress channel,
/// cancellation, and one place where <see cref="PdfServiceException"/> is turned into a
/// message a person can act on.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    private CancellationTokenSource? _cancellation;

    protected ViewModelBase(IDialogService dialogService)
    {
        DialogService = dialogService;
    }

    protected IDialogService DialogService { get; }

    /// <summary>Short title shown in the workspace header.</summary>
    public abstract string Title { get; }

    /// <summary>One-line explanation shown under <see cref="Title"/>.</summary>
    public abstract string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    /// <summary>Inverse of <see cref="IsBusy"/>; commands use it as their <c>CanExecute</c>.</summary>
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
    /// Runs a long operation with the busy overlay up, progress wired, cancellation available
    /// and every documented failure reported to the user.
    /// </summary>
    /// <returns><c>true</c> when the operation ran to completion.</returns>
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

        // Progress<T> captures the UI SynchronizationContext here, so the callback is marshalled for us.
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
            StatusMessage = "Operation cancelled.";
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
            DialogService.ShowError("Unexpected error", ex.Message);
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
    /// Called whenever <see cref="IsBusy"/> flips. Overrides re-evaluate their commands, because
    /// <c>[NotifyCanExecuteChangedFor]</c> cannot reach commands declared in a derived class.
    /// </summary>
    protected virtual void OnBusyStateChanged()
    {
    }

    partial void OnIsBusyChanged(bool value) => OnBusyStateChanged();

    private static string DescribeError(PdfErrorKind kind) => kind switch
    {
        PdfErrorKind.FileNotFound => "File not found",
        PdfErrorKind.PasswordProtected => "Password protected",
        PdfErrorKind.CorruptedDocument => "Damaged document",
        PdfErrorKind.UnsupportedImage => "Unsupported image",
        PdfErrorKind.OutputNotWritable => "Cannot write file",
        PdfErrorKind.EmptySelection => "Nothing to save",
        PdfErrorKind.PageIndexOutOfRange => "Invalid page",
        _ => "PDF error"
    };

    private static string BuildErrorMessage(PdfServiceException ex)
    {
        var hint = ex.Kind switch
        {
            PdfErrorKind.PasswordProtected => "Remove the password with your PDF reader and try again.",
            PdfErrorKind.OutputNotWritable => "The file may be open in another program. Close it or pick another location.",
            PdfErrorKind.CorruptedDocument => "The file is not a valid PDF, or it is damaged.",
            _ => null
        };

        return hint is null ? ex.Message : $"{ex.Message}\n\n{hint}";
    }
}
