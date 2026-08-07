namespace Yordamchi.Models;

/// <summary>
/// Ishchi oynadan dvigatelga uzatiladigan bitta topshiriq.
/// <para>
/// Universal ishchi oyna (<c>ToolWorkspaceViewModel</c>) qaysi vosita tanlanganidan qat'i nazar
/// shu bitta turdagi so'rovni yuboradi — bu UI ni har bir modul tafsilotidan xalos qiladi.
/// </para>
/// </summary>
public sealed class ToolRequest
{
    public required ToolId Tool { get; init; }

    /// <summary>Manba fayllar — foydalanuvchi tanlagan tartibda.</summary>
    public required IReadOnlyList<string> InputFiles { get; init; }

    /// <summary>Natija fayli (bitta fayl yozadigan vositalar uchun).</summary>
    public string? OutputPath { get; init; }

    /// <summary>Natija papkasi (bo'lish, PDF → rasm uchun).</summary>
    public string? OutputFolder { get; init; }

    /// <summary>Vositaga tegishli sozlamalar obyekti (<c>SplitOptions</c>, <c>ProtectOptions</c> …).</summary>
    public object? Options { get; init; }

    /// <summary>Himoyalangan hujjatni ochish uchun parol.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Sahifa tahriri natijasi: qaysi sahifa qaysi tartibda va qanday burilish bilan yoziladi.
    /// Tartiblash/burish/birlashtirish vositalari shundan foydalanadi.
    /// </summary>
    public IReadOnlyList<PageEdit>? PagePlan { get; init; }
}

/// <summary>Bajarilgan topshiriq natijasi.</summary>
/// <param name="Success">Operatsiya to'liq yakunlandimi.</param>
/// <param name="OutputFiles">Yaratilgan fayllar.</param>
/// <param name="Message">Foydalanuvchiga ko'rsatiladigan qisqa xulosa.</param>
public sealed record ToolRunResult(bool Success, IReadOnlyList<string> OutputFiles, string Message)
{
    public string? PrimaryOutput => OutputFiles.Count > 0 ? OutputFiles[0] : null;

    public static ToolRunResult Ok(string message, params string[] files) => new(true, files, message);

    public static ToolRunResult Ok(string message, IReadOnlyList<string> files) => new(true, files, message);
}
