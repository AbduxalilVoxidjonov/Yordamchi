namespace Yordamchi.Services.Abstractions;

/// <summary>
/// Every piece of shell UI a view model may need: file pickers and message boxes.
/// Keeping it behind an interface is what lets the view models stay unit-testable —
/// nothing in <c>Yordamchi.ViewModels</c> ever touches <c>MessageBox</c> directly.
/// </summary>
public interface IDialogService
{
    /// <summary>Fayl oynalari uchun umumiy filtrlar.</summary>
    public static class Filters
    {
        public const string Pdf = "PDF hujjatlar (*.pdf)|*.pdf|Barcha fayllar (*.*)|*.*";

        public const string Images =
            "Rasmlar (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|" +
            "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG (*.png)|*.png|Barcha fayllar (*.*)|*.*";

        public const string Word = "Word hujjatlar (*.docx)|*.docx|Barcha fayllar (*.*)|*.*";

        public const string Excel = "Excel kitoblari (*.xlsx)|*.xlsx|Barcha fayllar (*.*)|*.*";

        public const string PowerPoint = "PowerPoint taqdimotlari (*.pptx)|*.pptx|Barcha fayllar (*.*)|*.*";

        public const string Png = "PNG rasm (*.png)|*.png|Barcha fayllar (*.*)|*.*";

        /// <summary>Berilgan kengaytmaga mos filtrni qaytaradi.</summary>
        public static string ForExtension(string? extension) => extension?.ToLowerInvariant() switch
        {
            ".docx" => Word,
            ".xlsx" => Excel,
            ".pptx" => PowerPoint,
            ".png" => Png,
            ".jpg" or ".jpeg" => Images,
            _ => Pdf
        };
    }

    /// <summary>Shows an open-file dialog. Returns <c>null</c> when the user cancels.</summary>
    string[]? OpenFiles(string title, string filter, bool multiSelect = true);

    /// <summary>Shows a single-selection open-file dialog. Returns <c>null</c> when the user cancels.</summary>
    string? OpenFile(string title, string filter);

    /// <summary>Shows a save-file dialog. Returns <c>null</c> when the user cancels.</summary>
    string? SaveFile(string title, string filter, string suggestedFileName, string defaultExtension = ".pdf");

    /// <summary>
    /// Papka tanlash oynasi. Bekor qilinsa <c>null</c>.
    /// Ekran yozuvi kabi bir necha fayl chiqaradigan amallar uchun kerak: har bir fayl uchun
    /// alohida "saqlash" oynasini ochish o'rniga bir marta papka tanlanadi.
    /// </summary>
    string? SelectFolder(string title, string? initialFolder = null);

    /// <summary>Yes/No question. Returns <c>true</c> for Yes.</summary>
    bool Confirm(string title, string message);

    void ShowError(string title, string message);

    void ShowInformation(string title, string message);

    /// <summary>Opens a file or folder with the shell's default handler; failures are swallowed.</summary>
    void RevealInExplorer(string path);
}
