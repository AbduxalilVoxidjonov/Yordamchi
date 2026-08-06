using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfEdit.Models;

namespace PdfEdit.ViewModels;

/// <summary>Implemented by whoever owns the page collection, so a card can remove itself.</summary>
public interface IPageItemHost
{
    void RemovePage(PageItemViewModel page);
}

/// <summary>
/// One card in the thumbnail grid: an immutable <see cref="PageModel"/> plus the mutable state
/// the user edits (position, rotation, selection).
/// </summary>
public sealed partial class PageItemViewModel : ObservableObject
{
    private readonly IPageItemHost _host;

    public PageItemViewModel(PageModel model, IPageItemHost host)
    {
        Model = model;
        _host = host;
        _rotation = model.Rotation;
    }

    public PageModel Model { get; }

    public BitmapSource Thumbnail => Model.Thumbnail;

    /// <summary>File the page came from — shown on the card when the grid mixes several documents.</summary>
    public string SourceFileName => Model.SourceFileName;

    /// <summary>Original page number in the source document, before any reordering.</summary>
    public int OriginalPageNumber => Model.DisplayPageNumber;

    /// <summary>1-based position in the current working order; kept in sync by the owning view model.</summary>
    [ObservableProperty]
    private int _pageNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRotated))]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private PageRotation _rotation;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True when the grid shows pages from more than one file.</summary>
    [ObservableProperty]
    private bool _showSourceBadge;

    public bool IsRotated => Rotation != PageRotation.None;

    public string ToolTipText => IsRotated
        ? $"{SourceFileName} — page {OriginalPageNumber} (rotated {(int)Rotation}°)"
        : $"{SourceFileName} — page {OriginalPageNumber}";

    [RelayCommand]
    private void RotateClockwise() => Rotation = Rotation.RotateClockwise();

    [RelayCommand]
    private void RotateCounterClockwise() => Rotation = Rotation.RotateCounterClockwise();

    [RelayCommand]
    private void Delete() => _host.RemovePage(this);

    /// <summary>Projects the card back into the write-side primitive consumed by <c>IPdfService</c>.</summary>
    public PageEdit ToPageEdit() => new(Model.SourceFilePath, Model.SourcePageIndex, Rotation);

    public override string ToString() => $"#{PageNumber} ({Model})";
}
