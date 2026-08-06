namespace PdfEdit.Services.Abstractions;

/// <summary>
/// Every piece of shell UI a view model may need: file pickers and message boxes.
/// Keeping it behind an interface is what lets the view models stay unit-testable —
/// nothing in <c>PdfEdit.ViewModels</c> ever touches <c>MessageBox</c> directly.
/// </summary>
public interface IDialogService
{
    /// <summary>Common file dialog filters.</summary>
    public static class Filters
    {
        public const string Pdf = "PDF documents (*.pdf)|*.pdf|All files (*.*)|*.*";

        public const string Images =
            "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|" +
            "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG (*.png)|*.png|All files (*.*)|*.*";
    }

    /// <summary>Shows an open-file dialog. Returns <c>null</c> when the user cancels.</summary>
    string[]? OpenFiles(string title, string filter, bool multiSelect = true);

    /// <summary>Shows a single-selection open-file dialog. Returns <c>null</c> when the user cancels.</summary>
    string? OpenFile(string title, string filter);

    /// <summary>Shows a save-file dialog. Returns <c>null</c> when the user cancels.</summary>
    string? SaveFile(string title, string filter, string suggestedFileName, string defaultExtension = ".pdf");

    /// <summary>Yes/No question. Returns <c>true</c> for Yes.</summary>
    bool Confirm(string title, string message);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);

    /// <summary>Opens a file or folder with the shell's default handler; failures are swallowed.</summary>
    void RevealInExplorer(string path);
}
