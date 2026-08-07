using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yordamchi.ViewModels;

/// <summary>One image queued for conversion; becomes exactly one page of the output PDF.</summary>
public sealed partial class ImageItemViewModel : ObservableObject
{
    private readonly Action<ImageItemViewModel> _remove;

    public ImageItemViewModel(string filePath, Action<ImageItemViewModel> remove)
    {
        FilePath = filePath;
        _remove = remove;
        FileSizeBytes = TryGetLength(filePath);
    }

    public string FilePath { get; }

    public string FileName => Path.GetFileName(FilePath);

    public long FileSizeBytes { get; }

    /// <summary>1-based page position in the output PDF.</summary>
    [ObservableProperty]
    private int _pageNumber;

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>e.g. <c>4032 × 3024</c>, filled in once the thumbnail is decoded.</summary>
    [ObservableProperty]
    private string? _dimensionsText;

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
