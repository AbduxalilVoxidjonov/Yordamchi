using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfEdit.Models;
using PdfEdit.Services.Abstractions;

namespace PdfEdit.ViewModels;

/// <summary>
/// The "PDF Builder / Merger" workspace: collect documents, drag them into the order you want,
/// and write them out as one file.
/// </summary>
public sealed partial class PdfBuilderViewModel : ViewModelBase
{
    private const int PreviewWidth = 140;

    private readonly IPdfService _pdfService;

    public PdfBuilderViewModel(IPdfService pdfService, IDialogService dialogService)
        : base(dialogService)
    {
        _pdfService = pdfService;
        Files.CollectionChanged += OnFilesChanged;
    }

    public override string Title => "PDF Builder / Merger";

    public override string Description =>
        "Add PDF files, drag them into the order you want, and merge them into a single document.";

    /// <summary>Merge order. Drag-and-drop reorders this collection in place.</summary>
    public ObservableCollection<PdfSourceViewModel> Files { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private int _totalPages;

    [ObservableProperty]
    private PdfSourceViewModel? _selectedFile;

    public bool HasFiles => Files.Count > 0;

    public long TotalSizeBytes => Files.Sum(f => f.FileSizeBytes);

    public string SummaryText => Files.Count == 0
        ? "No files added"
        : $"{Files.Count} file{(Files.Count == 1 ? string.Empty : "s")} · {TotalPages} page{(TotalPages == 1 ? string.Empty : "s")}";

    // -----------------------------------------------------------------
    // Adding / removing
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task AddFilesAsync()
    {
        var paths = DialogService.OpenFiles("Add PDF files", IDialogService.Filters.Pdf);
        if (paths is not null)
            await AddAsync(paths);
    }

    /// <summary>Bound to the drop zone.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task DropFilesAsync(string[]? paths)
        => paths is null or { Length: 0 } ? Task.CompletedTask : AddAsync(paths);

    private async Task AddAsync(IEnumerable<string> paths)
    {
        var added = new List<PdfSourceViewModel>();

        foreach (var path in paths)
        {
            // Adding the same document twice is legitimate (e.g. a cover sheet), so no de-dup here.
            var item = new PdfSourceViewModel(path, Remove);
            Files.Add(item);
            added.Add(item);
        }

        StatusMessage = $"Added {added.Count} file{(added.Count == 1 ? string.Empty : "s")}";

        // Metadata and previews load one file at a time so the list stays responsive.
        foreach (var item in added)
            await LoadPreviewAsync(item);

        RecalculateTotals();
    }

    private async Task LoadPreviewAsync(PdfSourceViewModel item)
    {
        try
        {
            item.PageCount = await _pdfService.GetPageCountAsync(item.FilePath).ConfigureAwait(true);
            item.Preview = await _pdfService.RenderPageAsync(item.FilePath, 0, PreviewWidth).ConfigureAwait(true);
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
            RecalculateTotals();
        }
    }

    private void Remove(PdfSourceViewModel item)
    {
        Files.Remove(item);
        StatusMessage = $"Removed {item.FileName}";
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void Clear()
    {
        Files.Clear();
        StatusMessage = "Cleared the list";
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        var index = SelectedFile is null ? -1 : Files.IndexOf(SelectedFile);
        if (index > 0)
            Files.Move(index, index - 1);
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        var index = SelectedFile is null ? -1 : Files.IndexOf(SelectedFile);
        if (index >= 0 && index < Files.Count - 1)
            Files.Move(index, index + 1);
    }

    [RelayCommand(CanExecute = nameof(CanModifyList))]
    private void SortByName()
    {
        var ordered = Files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var currentIndex = Files.IndexOf(ordered[i]);
            if (currentIndex != i)
                Files.Move(currentIndex, i);
        }

        StatusMessage = "Sorted by file name";
    }

    // -----------------------------------------------------------------
    // Merging
    // -----------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task MergeAsync()
    {
        var failed = Files.Where(f => f.HasError).ToList();
        if (failed.Count > 0)
        {
            DialogService.ShowError(
                "Cannot merge",
                "Remove the files that could not be read first:\n\n" + string.Join('\n', failed.Select(f => f.FileName)));
            return;
        }

        var suggested = Path.GetFileNameWithoutExtension(Files[0].FileName) + "-merged.pdf";
        var target = DialogService.SaveFile("Save merged PDF", IDialogService.Filters.Pdf, suggested);
        if (target is null)
            return;

        var inputs = Files.Select(f => f.FilePath).ToList();

        var merged = await RunAsync(
            "Merging documents…",
            (progress, token) => _pdfService.MergePdfFilesAsync(inputs, target, progress, token),
            $"Merged {inputs.Count} files into {Path.GetFileName(target)}");

        if (merged && DialogService.Confirm("Merged", $"Created {Path.GetFileName(target)}.\n\nShow it in File Explorer?"))
            DialogService.RevealInExplorer(target);
    }

    // -----------------------------------------------------------------
    // Command availability
    // -----------------------------------------------------------------

    private bool CanModifyList() => IsIdle && Files.Count > 0;

    private bool CanMerge() => IsIdle && Files.Count >= 1;

    private bool CanMoveUp() => IsIdle && SelectedFile is not null && Files.IndexOf(SelectedFile) > 0;

    private bool CanMoveDown() => IsIdle && SelectedFile is not null && Files.IndexOf(SelectedFile) < Files.Count - 1;

    protected override void OnBusyStateChanged() => RefreshCommands();

    partial void OnSelectedFileChanged(PdfSourceViewModel? value)
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCommands()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        DropFilesCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        SortByNameCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Files.Count; i++)
            Files[i].OrderNumber = i + 1;

        RecalculateTotals();
        OnPropertyChanged(nameof(HasFiles));
        RefreshCommands();
    }

    private void RecalculateTotals()
    {
        TotalPages = Files.Sum(f => f.PageCount);
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(SummaryText));
    }
}
