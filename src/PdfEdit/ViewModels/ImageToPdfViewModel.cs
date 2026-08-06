using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfEdit.Models;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.ViewModels;

/// <summary>
/// The "Image to PDF" workspace: drop in JPG/PNG files, drag them into order, choose a page
/// size, and write one PDF where each image is a page.
/// </summary>
public sealed partial class ImageToPdfViewModel : ViewModelBase
{
    private const int ThumbnailRenderWidth = 320;

    private const double MinThumbnailSize = 110d;
    private const double MaxThumbnailSize = 300d;

    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

    private readonly IPdfService _pdfService;

    public ImageToPdfViewModel(IPdfService pdfService, IDialogService dialogService)
        : base(dialogService)
    {
        _pdfService = pdfService;
        Images.CollectionChanged += OnImagesChanged;
        _selectedSizeLimit = SizeLimitOptions.First(o => o.MaxEdgePixels == MaxImageEdgePixels);
    }

    public override string Title => "Image to PDF";

    public override string Description =>
        "Turn JPG and PNG files into a PDF — one image per page, in the order you arrange them.";

    /// <summary>Output page order. Drag-and-drop reorders this collection in place.</summary>
    public ObservableCollection<ImageItemViewModel> Images { get; } = [];

    [ObservableProperty]
    private double _thumbnailSize = 170d;

    // ---- Conversion options -----------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixedPageSize))]
    private PdfPageSizeMode _pageSizeMode = PdfPageSizeMode.FitToImage;

    [ObservableProperty]
    private double _marginPoints = 28d;

    [ObservableProperty]
    private bool _autoOrientation = true;

    [ObservableProperty]
    private int _maxImageEdgePixels = 3508;

    /// <summary>Margins and orientation only apply to the fixed page sizes.</summary>
    public bool IsFixedPageSize => PageSizeMode != PdfPageSizeMode.FitToImage;

    /// <summary>A downscale ceiling the user can pick, so a folder of 40 MP photos stays usable.</summary>
    /// <param name="Label">Text shown in the drop-down.</param>
    /// <param name="MaxEdgePixels">Longest allowed edge in pixels; <c>0</c> keeps the original.</param>
    public sealed record ImageSizeLimitOption(string Label, int MaxEdgePixels);

    public IReadOnlyList<ImageSizeLimitOption> SizeLimitOptions { get; } =
    [
        new("Original size", 0),
        new("Screen — 1600 px", 1600),
        new("Print — 3508 px (A4 @ 300 dpi)", 3508),
        new("High — 5000 px", 5000)
    ];

    [ObservableProperty]
    private ImageSizeLimitOption? _selectedSizeLimit;

    partial void OnSelectedSizeLimitChanged(ImageSizeLimitOption? value)
    {
        if (value is not null)
            MaxImageEdgePixels = value.MaxEdgePixels;
    }

    public bool HasImages => Images.Count > 0;

    public string SummaryText => Images.Count == 0
        ? "No images added"
        : $"{Images.Count} image{(Images.Count == 1 ? string.Empty : "s")} → {Images.Count} page{(Images.Count == 1 ? string.Empty : "s")}";

    // -----------------------------------------------------------------
    // Adding / removing
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task AddImagesAsync()
    {
        var paths = DialogService.OpenFiles("Add images", IDialogService.Filters.Images);
        if (paths is not null)
            await AddAsync(paths);
    }

    /// <summary>Bound to the drop zone.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task DropFilesAsync(string[]? paths)
        => paths is null or { Length: 0 } ? Task.CompletedTask : AddAsync(paths);

    private async Task AddAsync(IEnumerable<string> paths)
    {
        var accepted = paths
            .Where(p => SupportedExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (accepted.Count == 0)
        {
            StatusMessage = "No supported image files in that selection";
            return;
        }

        var added = new List<ImageItemViewModel>();
        foreach (var path in accepted)
        {
            var item = new ImageItemViewModel(path, Remove);
            Images.Add(item);
            added.Add(item);
        }

        StatusMessage = $"Added {added.Count} image{(added.Count == 1 ? string.Empty : "s")}";

        foreach (var item in added)
            await LoadThumbnailAsync(item);
    }

    private async Task LoadThumbnailAsync(ImageItemViewModel item)
    {
        try
        {
            var thumbnail = await _pdfService
                .RenderImageThumbnailAsync(item.FilePath, ThumbnailRenderWidth)
                .ConfigureAwait(true);

            item.Thumbnail = thumbnail;
            item.DimensionsText = $"{thumbnail.PixelWidth} × {thumbnail.PixelHeight}";
        }
        catch (PdfServiceException ex)
        {
            item.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    private void Remove(ImageItemViewModel item) => Images.Remove(item);

    [RelayCommand]
    private void ZoomIn() => ThumbnailSize = Math.Min(ThumbnailSize + 30d, MaxThumbnailSize);

    [RelayCommand]
    private void ZoomOut() => ThumbnailSize = Math.Max(ThumbnailSize - 30d, MinThumbnailSize);

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void Clear()
    {
        Images.Clear();
        StatusMessage = "Cleared the list";
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        foreach (var image in Images.Where(i => i.IsSelected).ToList())
            Images.Remove(image);
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void SortByName()
    {
        var ordered = Images.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var currentIndex = Images.IndexOf(ordered[i]);
            if (currentIndex != i)
                Images.Move(currentIndex, i);
        }

        StatusMessage = "Sorted by file name";
    }

    // -----------------------------------------------------------------
    // Conversion
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreatePdfAsync()
    {
        var suggested = Path.GetFileNameWithoutExtension(Images[0].FileName) + ".pdf";
        var target = DialogService.SaveFile("Save PDF", IDialogService.Filters.Pdf, suggested);
        if (target is null)
            return;

        var inputs = Images.Select(i => i.FilePath).ToList();
        var options = new ImageToPdfOptions
        {
            PageSizeMode = PageSizeMode,
            MarginPoints = MarginPoints,
            AutoOrientation = AutoOrientation,
            MaxImageEdgePixels = MaxImageEdgePixels
        };

        var created = await RunAsync(
            "Building PDF…",
            (progress, token) => _pdfService.ConvertImagesToPdfAsync(inputs, target, options, progress, token),
            $"Created {Path.GetFileName(target)} with {inputs.Count} pages");

        if (created && DialogService.Confirm("Created", $"Created {Path.GetFileName(target)}.\n\nShow it in File Explorer?"))
            DialogService.RevealInExplorer(target);
    }

    // -----------------------------------------------------------------
    // Command availability
    // -----------------------------------------------------------------

    private bool CanModifyList() => IsIdle && Images.Count > 0;

    private bool CanCreate() => IsIdle && Images.Count > 0 && Images.All(i => !i.HasError);

    private bool CanRemoveSelected() => IsIdle && Images.Any(i => i.IsSelected);

    protected override void OnBusyStateChanged() => RefreshCommands();

    private void RefreshCommands()
    {
        AddImagesCommand.NotifyCanExecuteChanged();
        DropFilesCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        SortByNameCommand.NotifyCanExecuteChanged();
        CreatePdfCommand.NotifyCanExecuteChanged();
    }

    private void OnImagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Images.Count; i++)
            Images[i].PageNumber = i + 1;

        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(SummaryText));
        RefreshCommands();
    }
}
