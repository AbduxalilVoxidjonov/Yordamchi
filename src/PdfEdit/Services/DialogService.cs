using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.Services;

/// <inheritdoc cref="IDialogService"/>
public sealed class DialogService : IDialogService
{
    public string[]? OpenFiles(string title, string filter, bool multiSelect = true)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = multiSelect,
            CheckFileExists = true,
            CheckPathExists = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public string? OpenFile(string title, string filter)
        => OpenFiles(title, filter, multiSelect: false)?.FirstOrDefault();

    public string? SaveFile(string title, string filter, string suggestedFileName, string defaultExtension = ".pdf")
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = MakeSafeFileName(suggestedFileName),
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string message)
        => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string title, string message)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string title, string message)
        => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Opening Explorer is a convenience; never let it take the app down.
        }
    }

    /// <summary>The active window, so dialogs are modal to the right owner on multi-monitor setups.</summary>
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    private static MessageBoxResult Show(string message, string title, MessageBoxButton button, MessageBoxImage icon)
    {
        var owner = Owner;
        return owner is not null
            ? MessageBox.Show(owner, message, title, button, icon)
            : MessageBox.Show(message, title, button, icon);
    }

    private static string MakeSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "document.pdf" : cleaned;
    }
}
