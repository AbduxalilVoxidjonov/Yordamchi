using Yordamchi.Services.Abstractions;

namespace Yordamchi.Tests.TestSupport;

/// <summary>
/// Hech qanday oyna ochmaydigan <see cref="IDialogService"/>. Qaytariladigan qiymatlar
/// oldindan beriladi, chaqiruvlar esa yozib boriladi — shu tufayli "foydalanuvchi bekor
/// qildi" yoki "xato ko'rsatildimi" kabi holatlarni tekshirish mumkin.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    /// <summary><c>OpenFiles</c> navbat bilan qaytaradigan javoblar; tugasa <c>null</c> (bekor qilindi).</summary>
    public Queue<string[]?> OpenFilesResults { get; } = new();

    public Queue<string?> OpenFileResults { get; } = new();

    public Queue<string?> SaveFileResults { get; } = new();

    public Queue<string?> SelectFolderResults { get; } = new();

    /// <summary>Tasdiqlash oynasi qaytaradigan javob (standart — "Ha").</summary>
    public bool ConfirmResult { get; set; } = true;

    public List<string> ShownErrors { get; } = [];

    public List<string> ShownInformation { get; } = [];

    public List<string> Confirmations { get; } = [];

    public List<string> RevealedPaths { get; } = [];

    /// <summary>Clipboard'ga ko'chirilgan matnlar (oxirgisi — eng so'nggi nusxa).</summary>
    public List<string?> ClipboardTexts { get; } = [];

    public string[]? OpenFiles(string title, string filter, bool multiSelect = true) =>
        OpenFilesResults.Count > 0 ? OpenFilesResults.Dequeue() : null;

    public string? OpenFile(string title, string filter) =>
        OpenFileResults.Count > 0 ? OpenFileResults.Dequeue() : null;

    public string? SaveFile(string title, string filter, string suggestedFileName, string defaultExtension = ".pdf") =>
        SaveFileResults.Count > 0 ? SaveFileResults.Dequeue() : null;

    public string? SelectFolder(string title, string? initialFolder = null) =>
        SelectFolderResults.Count > 0 ? SelectFolderResults.Dequeue() : null;

    public bool Confirm(string title, string message)
    {
        Confirmations.Add(title);
        return ConfirmResult;
    }

    public void ShowError(string title, string message) => ShownErrors.Add($"{title}: {message}");

    public void ShowInformation(string title, string message) => ShownInformation.Add($"{title}: {message}");

    public void RevealInExplorer(string path) => RevealedPaths.Add(path);

    public void SetClipboardText(string? text) => ClipboardTexts.Add(text);
}
