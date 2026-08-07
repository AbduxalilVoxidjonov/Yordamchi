using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Models;

namespace Yordamchi.ViewModels;

/// <summary>
/// "Arxivlash" ro'yxatidagi bitta manba: fayl yoki butun papka.
/// <para>
/// <see cref="WorkspaceFileViewModel"/> dan farqi — u faqat fayl bilan ishlaydi va eskiz,
/// sahifa soni kabi PDF ga xos ma'lumot saqlaydi. Bu yerda esa papka ham to'liq huquqli
/// element: hajmi ichidagi fayllardan yig'iladi.
/// </para>
/// </summary>
public sealed partial class ArchiveSourceViewModel : ObservableObject
{
    private readonly Action<ArchiveSourceViewModel> _remove;

    public ArchiveSourceViewModel(string path, Action<ArchiveSourceViewModel> remove)
    {
        Path = path;
        _remove = remove;
        IsFolder = Directory.Exists(path);

        (SizeBytes, FileCount) = IsFolder ? MeasureFolder(path) : MeasureFile(path);
    }

    public string Path { get; }

    public bool IsFolder { get; }

    public string Name
    {
        get
        {
            var name = System.IO.Path.GetFileName(Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            // Diskning ildizi tanlansa ("D:\") nom bo'sh chiqadi — yo'lning o'zini ko'rsatamiz.
            return string.IsNullOrEmpty(name) ? Path : name;
        }
    }

    public string DirectoryName => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>Umumiy hajm (papka uchun — ichidagi barcha fayllar yig'indisi).</summary>
    public long SizeBytes { get; }

    /// <summary>Arxivga tushadigan fayllar soni (fayl uchun 1).</summary>
    public int FileCount { get; }

    public string Glyph => IsFolder ? "\uE8B7" : "\uE7C3";

    /// <summary>Nom ostidagi izoh: papka uchun fayllar soni, fayl uchun joylashgan papka.</summary>
    public string DetailsText => IsFolder
        ? FileCount switch
        {
            0 => "Bo'sh papka",
            1 => "1 ta fayl",
            _ => $"{FileCount} ta fayl"
        }
        : DirectoryName;

    [RelayCommand]
    private void Remove() => _remove(this);

    private static (long Size, int Count) MeasureFile(string path)
    {
        try
        {
            return (new FileInfo(path).Length, 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (0, 1);
        }
    }

    private static (long Size, int Count) MeasureFolder(string path)
    {
        long size = 0;
        var count = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                count++;

                try
                {
                    size += new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Bitta o'qib bo'lmagan fayl butun hisobni to'xtatmasligi kerak.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Papkaga kirish yopiq — arxivlashda ham o'tkazib yuboriladi.
        }

        return (size, count);
    }

    public override string ToString() => Name;
}

/// <summary>"Arxivni ochish" ro'yxatidagi bitta yozuv.</summary>
public sealed partial class ArchiveEntryViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public ArchiveEntryViewModel(ArchiveEntryInfo entry, Action selectionChanged)
    {
        Entry = entry;
        _selectionChanged = selectionChanged;
    }

    public ArchiveEntryInfo Entry { get; }

    public string Path => Entry.Path;

    public string Name => Entry.Name;

    /// <summary>Arxiv ichidagi papka (bo'sh bo'lsa — ildiz).</summary>
    public string FolderPath
    {
        get
        {
            var slash = Entry.Path.LastIndexOf('/');
            return slash > 0 ? Entry.Path[..slash] : string.Empty;
        }
    }

    public long SizeBytes => Entry.Size;

    public bool IsEncrypted => Entry.IsEncrypted;

    public string ModifiedText => Entry.Modified?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;

    public string SavedText => Entry.SavedPercent is { } percent ? $"−{percent}%" : string.Empty;

    [ObservableProperty]
    private bool _isSelected = true;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();

    public override string ToString() => Path;
}
