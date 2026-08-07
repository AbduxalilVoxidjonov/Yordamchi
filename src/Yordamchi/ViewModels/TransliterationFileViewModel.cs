using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yordamchi.ViewModels;

/// <summary>
/// "Kirill ↔ Lotin" bo'limidagi ro'yxatning bitta qatori: o'giriladigan fayl va uning holati.
/// <para>
/// Ishchi oynadagi <see cref="WorkspaceFileViewModel"/> dan ataylab ajratilgan: u yerda eskiz,
/// sahifalar soni va tartib raqami bor, bu yerda esa bittagina savol muhim — fayl o'girildimi
/// yoki xato berdimi.
/// </para>
/// </summary>
public sealed partial class TransliterationFileViewModel : ObservableObject
{
    private readonly Action<TransliterationFileViewModel> _remove;

    public TransliterationFileViewModel(string path, Action<TransliterationFileViewModel> remove)
    {
        Path = path;
        _remove = remove;
        SizeBytes = TryGetLength(path);
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();

    public long SizeBytes { get; }

    /// <summary>Fayl turiga mos Segoe Fluent Icons belgisi.</summary>
    public string Glyph => Extension switch
    {
        ".docx" or ".doc" => "\uE8A5",
        _ => "\uE7C3"
    };

    /// <summary>Fayl nomi ostidagi bir qatorli holat: natija nomi yoki xato sababi.</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>Amal shu faylda muvaffaqiyatsiz tugadi — qator ogohlantirish rangida chiziladi.</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>Fayl o'girildi va natija diskka yozildi.</summary>
    [ObservableProperty]
    private bool _isDone;

    /// <summary>Yangi amal boshlanishidan oldin oldingi natija izlarini tozalaydi.</summary>
    public void ResetStatus()
    {
        StatusText = string.Empty;
        HasError = false;
        IsDone = false;
    }

    public void MarkDone(string outputName)
    {
        StatusText = "→ " + outputName;
        HasError = false;
        IsDone = true;
    }

    public void MarkFailed(string message)
    {
        StatusText = message;
        HasError = true;
        IsDone = false;
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    private static long TryGetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fayl o'chirilgan yoki tarmoq diski uzilgan bo'lishi mumkin — hajmsiz ko'rsatamiz.
            return 0;
        }
    }

    public override string ToString() => Name;
}
