using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yordamchi.ViewModels;

/// <summary>
/// Universal ishchi oynadagi bitta manba fayl: PDF, rasm yoki Word hujjati.
/// <para>
/// <c>PdfSourceViewModel</c> va <c>ImageItemViewModel</c> ning umumlashtirilgan ko'rinishi —
/// ishchi oyna qaysi vosita ochilganidan qat'i nazar shu bitta turdan foydalanadi.
/// </para>
/// </summary>
public sealed partial class WorkspaceFileViewModel : ObservableObject
{
    private readonly Action<WorkspaceFileViewModel> _remove;

    public WorkspaceFileViewModel(string filePath, Action<WorkspaceFileViewModel> remove)
    {
        FilePath = filePath;
        _remove = remove;
        FileSizeBytes = TryGetLength(filePath);
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public string DirectoryName => Path.GetDirectoryName(FilePath) ?? string.Empty;

    /// <summary>Kichik harfli kengaytma, nuqtasi bilan: <c>".pdf"</c>.</summary>
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();

    public long FileSizeBytes { get; }

    /// <summary>Fayl turiga mos Segoe Fluent Icons belgisi (eskiz hali yuklanmaganda ko'rsatiladi).</summary>
    public string Glyph => Extension switch
    {
        ".pdf" => "\uE8A5",
        ".doc" or ".docx" => "\uE8A5",
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp" or ".tif" or ".tiff" => "\uEB9F",
        _ => "\uE7C3"
    };

    /// <summary>Ro'yxatdagi 1 dan boshlanuvchi tartib raqami; egasi (workspace) yangilab turadi.</summary>
    [ObservableProperty]
    private int _orderNumber;

    /// <summary>PDF fayllar uchun sahifalar soni; noma'lum bo'lsa 0.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private int _pageCount;

    /// <summary>Birinchi sahifa yoki rasmning eskizi — fayl qo'shilgach fonda yuklanadi.</summary>
    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Rasm o'lchami, masalan <c>4032 × 3024</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private string? _dimensionsText;

    /// <summary>Fayl o'qilmasa to'ldiriladi; qator xato ko'rinishida chiziladi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(DetailsText))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Fayl nomi ostida ko'rsatiladigan qisqa ma'lumot.</summary>
    public string DetailsText
    {
        get
        {
            if (HasError)
                return ErrorMessage!;

            if (!string.IsNullOrEmpty(DimensionsText))
                return DimensionsText!;

            return PageCount switch
            {
                0 => string.Empty,
                1 => "1 sahifa",
                _ => $"{PageCount} sahifa"
            };
        }
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    private static long TryGetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            // Fayl o'chirilgan yoki tarmoq diski uzilgan bo'lishi mumkin — hajmsiz ko'rsatamiz.
            return 0;
        }
    }

    public override string ToString() => FileName;
}
