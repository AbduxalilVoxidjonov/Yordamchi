using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PdfEdit.ViewModels;

/// <summary>One source document in the merge list.</summary>
public sealed partial class PdfSourceViewModel : ObservableObject
{
    private readonly Action<PdfSourceViewModel> _remove;

    public PdfSourceViewModel(string filePath, Action<PdfSourceViewModel> remove)
    {
        FilePath = filePath;
        _remove = remove;
        FileSizeBytes = TryGetLength(filePath);
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public string DirectoryName => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public long FileSizeBytes { get; }

    /// <summary>1-based position in the merge order; maintained by the owning view model.</summary>
    [ObservableProperty]
    private int _orderNumber;

    [ObservableProperty]
    private int _pageCount;

    /// <summary>First page of the document, rendered lazily after the file is added.</summary>
    [ObservableProperty]
    private BitmapSource? _preview;

    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>Set when the file could not be read; the row is then shown as an error.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string PageCountText => PageCount == 1 ? "1 page" : $"{PageCount} pages";

    partial void OnPageCountChanged(int value) => OnPropertyChanged(nameof(PageCountText));

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
            return 0;
        }
    }

    public override string ToString() => FileName;
}
