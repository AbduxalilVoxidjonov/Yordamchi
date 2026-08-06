using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfEdit.Models;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.ViewModels;

/// <summary>
/// The "Page Reorder &amp; Edit" workspace: load a PDF, drag its pages into a new order,
/// delete and rotate them, then write the result out.
/// <para>
/// Editing is entirely non-destructive — nothing is written until the user saves, and the
/// working state is just an ordered list of <see cref="PageEdit"/> projections.
/// </para>
/// </summary>
public sealed partial class PageEditorViewModel : ViewModelBase, IPageItemHost
{
    /// <summary>Thumbnails are rasterized once at this width and only scaled in the UI afterwards.</summary>
    private const int RenderWidth = 320;

    private const double MinThumbnailSize = 110d;
    private const double MaxThumbnailSize = 300d;

    private readonly IPdfService _pdfService;
    private string _savedSignature = string.Empty;

    public PageEditorViewModel(IPdfService pdfService, IDialogService dialogService)
        : base(dialogService)
    {
        _pdfService = pdfService;
        Pages.CollectionChanged += OnPagesChanged;
    }

    public override string Title => "Page Reorder & Edit";

    public override string Description =>
        "Drag pages to reorder them, rotate or delete what you don't need, then save a new PDF.";

    /// <summary>The working page order. Drag-and-drop reorders this collection in place.</summary>
    public ObservableCollection<PageItemViewModel> Pages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyPropertyChangedFor(nameof(DocumentName))]
    private string? _currentFilePath;

    /// <summary>Where <c>Save</c> writes; set after the first successful "Save as".</summary>
    [ObservableProperty]
    private string? _outputFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private int _selectedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _isDirty;

    /// <summary>Card width in device-independent pixels, driven by the zoom slider.</summary>
    [ObservableProperty]
    private double _thumbnailSize = 170d;

    public bool HasDocument => !string.IsNullOrEmpty(CurrentFilePath);

    public bool HasPages => Pages.Count > 0;

    public string DocumentName => HasDocument ? Path.GetFileName(CurrentFilePath!) : "No document";

    public string SummaryText
    {
        get
        {
            if (Pages.Count == 0)
                return "No pages loaded";

            var text = $"{Pages.Count} page{(Pages.Count == 1 ? string.Empty : "s")}";
            if (SelectedCount > 0)
                text += $" · {SelectedCount} selected";
            if (IsDirty)
                text += " · unsaved changes";
            return text;
        }
    }

    // -----------------------------------------------------------------
    // Loading
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenDocumentAsync()
    {
        var path = DialogService.OpenFile("Open PDF", IDialogService.Filters.Pdf);
        if (path is not null)
            await LoadAsync(path, replaceExisting: true);
    }

    /// <summary>Bound to the drop zone; accepts one or more PDFs dropped from Explorer.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task DropFilesAsync(string[]? paths)
    {
        if (paths is null || paths.Length == 0)
            return;

        var replace = !HasPages;
        foreach (var path in paths)
        {
            await LoadAsync(path, replaceExisting: replace);
            replace = false;
        }
    }

    /// <summary>Appends the pages of another PDF to the current grid, so pages can be mixed.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AppendDocumentAsync()
    {
        var paths = DialogService.OpenFiles("Add pages from PDF", IDialogService.Filters.Pdf);
        if (paths is null)
            return;

        foreach (var path in paths)
            await LoadAsync(path, replaceExisting: false);
    }

    private async Task LoadAsync(string path, bool replaceExisting)
    {
        var loaded = await RunAsync(
            $"Rendering {Path.GetFileName(path)}…",
            async (progress, token) =>
            {
                var pages = await _pdfService
                    .RenderPdfPagesAsync(path, RenderWidth, password: null, progress, token)
                    .ConfigureAwait(true);

                if (replaceExisting)
                    ClearPages();

                foreach (var page in pages)
                    Pages.Add(new PageItemViewModel(page, this));
            });

        if (!loaded)
            return;

        if (replaceExisting)
        {
            CurrentFilePath = path;
            OutputFilePath = null;
        }

        RefreshSourceBadges();
        MarkSaved();
        StatusMessage = $"Loaded {Path.GetFileName(path)}";
    }

    // -----------------------------------------------------------------
    // Editing
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void DeleteSelected()
    {
        foreach (var page in Pages.Where(p => p.IsSelected).ToList())
            Pages.Remove(page);

        StatusMessage = "Deleted selected pages";
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void RotateSelectedClockwise() => RotateSelected(90);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void RotateSelectedCounterClockwise() => RotateSelected(-90);

    private void RotateSelected(int degrees)
    {
        foreach (var page in Pages.Where(p => p.IsSelected))
            page.Rotation = page.Rotation.Add(degrees);

        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SelectAll()
    {
        foreach (var page in Pages)
            page.IsSelected = true;
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void ClearSelection()
    {
        foreach (var page in Pages)
            page.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private void InvertSelection()
    {
        foreach (var page in Pages)
            page.IsSelected = !page.IsSelected;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void CloseDocument()
    {
        if (IsDirty && !DialogService.Confirm("Discard changes?", "The document has unsaved changes. Close it anyway?"))
            return;

        ClearPages();
        CurrentFilePath = null;
        OutputFilePath = null;
        MarkSaved();
        StatusMessage = string.Empty;
    }

    /// <summary>Called by a card's own delete button.</summary>
    public void RemovePage(PageItemViewModel page) => Pages.Remove(page);

    [RelayCommand]
    private void ZoomIn() => ThumbnailSize = Math.Min(ThumbnailSize + 30d, MaxThumbnailSize);

    [RelayCommand]
    private void ZoomOut() => ThumbnailSize = Math.Max(ThumbnailSize - 30d, MinThumbnailSize);

    // -----------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(OutputFilePath))
        {
            await SaveAsAsync();
            return;
        }

        await WriteAsync(OutputFilePath);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsAsync()
    {
        var suggested = HasDocument
            ? $"{Path.GetFileNameWithoutExtension(CurrentFilePath!)}-edited.pdf"
            : "document.pdf";

        var target = DialogService.SaveFile("Save PDF as", IDialogService.Filters.Pdf, suggested);
        if (target is null)
            return;

        if (await WriteAsync(target))
            OutputFilePath = target;
    }

    private async Task<bool> WriteAsync(string target)
    {
        var edits = Pages.Select(p => p.ToPageEdit()).ToList();

        var saved = await RunAsync(
            "Writing PDF…",
            (progress, token) => _pdfService.BuildPdfAsync(edits, target, progress, token),
            $"Saved {Path.GetFileName(target)}");

        if (!saved)
            return false;

        MarkSaved();

        if (DialogService.Confirm("Saved", $"Saved {Path.GetFileName(target)}.\n\nShow it in File Explorer?"))
            DialogService.RevealInExplorer(target);

        return true;
    }

    // -----------------------------------------------------------------
    // Command availability
    // -----------------------------------------------------------------

    private bool CanEdit() => IsIdle && HasPages;

    private bool CanSave() => IsIdle && HasPages;

    private bool CanActOnSelection() => IsIdle && SelectedCount > 0;

    protected override void OnBusyStateChanged() => RefreshCommands();

    private void RefreshCommands()
    {
        OpenDocumentCommand.NotifyCanExecuteChanged();
        DropFilesCommand.NotifyCanExecuteChanged();
        AppendDocumentCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RotateSelectedClockwiseCommand.NotifyCanExecuteChanged();
        RotateSelectedCounterClockwiseCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();
        CloseDocumentCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }

    // -----------------------------------------------------------------
    // Collection bookkeeping
    // -----------------------------------------------------------------

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PageItemViewModel page in e.OldItems)
                page.PropertyChanged -= OnPagePropertyChanged;

        if (e.NewItems is not null)
            foreach (PageItemViewModel page in e.NewItems)
                page.PropertyChanged += OnPagePropertyChanged;

        Renumber();
        RefreshSourceBadges();
        UpdateSelectionCount();
        MarkDirty();

        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(SummaryText));
        RefreshCommands();
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PageItemViewModel.IsSelected):
                UpdateSelectionCount();
                break;
            case nameof(PageItemViewModel.Rotation):
                MarkDirty();
                break;
        }
    }

    /// <summary>Keeps the badge numbers contiguous after any reorder or delete.</summary>
    private void Renumber()
    {
        for (var i = 0; i < Pages.Count; i++)
            Pages[i].PageNumber = i + 1;
    }

    /// <summary>Only show the "which file" badge once the grid actually mixes documents.</summary>
    private void RefreshSourceBadges()
    {
        var mixed = Pages.Select(p => p.Model.SourceFilePath)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(2)
                         .Count() > 1;

        foreach (var page in Pages)
            page.ShowSourceBadge = mixed;
    }

    private void UpdateSelectionCount()
    {
        SelectedCount = Pages.Count(p => p.IsSelected);
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RotateSelectedClockwiseCommand.NotifyCanExecuteChanged();
        RotateSelectedCounterClockwiseCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        InvertSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ClearPages()
    {
        foreach (var page in Pages)
            page.PropertyChanged -= OnPagePropertyChanged;

        Pages.Clear();
    }

    /// <summary>
    /// Dirty state is derived from a cheap signature of the working order rather than a flag,
    /// so undoing a change by hand (rotating back, dragging a page home) clears it again.
    /// </summary>
    private string BuildSignature()
        => string.Join('|', Pages.Select(p => $"{p.Model.SourceFilePath}:{p.Model.SourcePageIndex}:{(int)p.Rotation}"));

    private void MarkDirty() => IsDirty = BuildSignature() != _savedSignature;

    private void MarkSaved()
    {
        _savedSignature = BuildSignature();
        IsDirty = false;
    }
}
