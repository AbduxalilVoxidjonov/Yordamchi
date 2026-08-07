using System.IO;

namespace Yordamchi.Tests.TestSupport;

/// <summary>
/// Har bir sinov uchun alohida vaqtinchalik papka. Sinov tugagach butunlay o'chiriladi,
/// shuning uchun sinovlar bir-birining fayllarini ko'rmaydi va parallel ishlay oladi.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yordamchi-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Sinovga tegishli barcha fayllar shu papka ichida yaratiladi.</summary>
    public string Root { get; }

    /// <summary>Ildizga nisbatan to'liq yo'l qaytaradi (fayl yaratmaydi).</summary>
    public string At(string relative) => System.IO.Path.Combine(Root, relative);

    /// <summary>Berilgan mazmun bilan matnli fayl yaratadi va uning to'liq yo'lini qaytaradi.</summary>
    public string WriteFile(string relative, string content)
    {
        var full = At(relative);
        var folder = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>Papka yaratadi va uning to'liq yo'lini qaytaradi.</summary>
    public string CreateFolder(string relative)
    {
        var full = At(relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Antivirus faylni ushlab turgan bo'lsa sinov natijasiga ta'sir qilmasin —
            // Windows vaqtinchalik papkani keyinroq o'zi tozalaydi.
        }
    }
}
