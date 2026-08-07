using System.IO;
using System.Text;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Services.Conversion;

namespace Yordamchi.Services;

/// <inheritdoc cref="ITransliterationService"/>
public sealed class TransliterationService : ITransliterationService
{
    /// <summary>Natija fayl nomiga qo'shiladigan qo'shimchalar.</summary>
    private const string LatinSuffix = "-lotin";
    private const string CyrillicSuffix = "-kirill";

    /// <summary>Bir xil nomli natija ustiga yozmaslik uchun urinishlar chegarasi.</summary>
    private const int MaxNameAttempts = 100;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".docx", ".txt"];

    public string OpenFilter =>
        "Matnli hujjatlar (*.docx;*.txt)|*.docx;*.txt|" +
        "Word hujjatlar (*.docx)|*.docx|" +
        "Matn fayllari (*.txt)|*.txt|" +
        "Barcha fayllar (*.*)|*.*";

    public bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public string ConvertText(string? text, TransliterationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return UzbekTransliterator.Convert(text, UzbekTransliterator.Resolve(options, text));
    }

    public TransliterationDirection? DetectDirection(string? text) => UzbekTransliterator.Detect(text);

    public string SuggestOutputPath(string sourcePath, string? outputFolder, TransliterationDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var folder = ResolveFolder(sourcePath, outputFolder);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var suffix = direction == TransliterationDirection.CyrillicToLatin ? LatinSuffix : CyrillicSuffix;

        var candidate = Path.Combine(folder, name + suffix + extension);

        // Manba faylning o'zi ustiga yozib yubormaymiz va oldingi natijani ham o'chirmaymiz.
        for (var attempt = 2; attempt <= MaxNameAttempts && File.Exists(candidate); attempt++)
            candidate = Path.Combine(folder, $"{name}{suffix}-{attempt}{extension}");

        return candidate;
    }

    public async Task<TransliterationFileResult> ConvertFileAsync(
        string sourcePath,
        string? outputFolder,
        TransliterationOptions options,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"'{Path.GetFileName(sourcePath)}' fayli topilmadi.",
                sourcePath);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (extension == ".doc")
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                "Eski '.doc' formati qo'llab-quvvatlanmaydi. Hujjatni Word'da '.docx' ko'rinishida saqlab, qaytadan urinib ko'ring.",
                sourcePath);
        }

        if (!IsSupported(sourcePath))
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                $"'{extension}' fayllarini o'girib bo'lmaydi. Word hujjati (.docx) yoki matn fayli (.txt) tanlang.",
                sourcePath);
        }

        var folder = ResolveFolder(sourcePath, outputFolder);
        EnsureFolder(folder, sourcePath);

        // Fayl amallari diskka bog'liq — UI oqimini band qilmaymiz.
        return await Task.Run(
            () => extension == ".docx"
                ? ConvertDocument(sourcePath, folder, options, progress, cancellationToken)
                : ConvertPlainText(sourcePath, folder, options, progress, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================
    //  Word hujjati
    // =================================================================================

    /// <summary>
    /// Hujjat avval vaqtinchalik faylga o'giriladi va faqat shundan keyin nom oladi: yo'nalish
    /// avtomatik aniqlanganda "-lotin" yoki "-kirill" ekanini oldindan bilib bo'lmaydi.
    /// </summary>
    private TransliterationFileResult ConvertDocument(
        string sourcePath,
        string folder,
        TransliterationOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var workingPath = Path.Combine(folder, $".yordamchi-{Guid.NewGuid():N}.tmp");

        try
        {
            var (direction, characters) = DocxTransliterator.Convert(
                sourcePath, workingPath, options, progress, cancellationToken);

            var targetPath = SuggestOutputPath(sourcePath, folder, direction);

            MoveIntoPlace(workingPath, targetPath);

            return new TransliterationFileResult(sourcePath, targetPath, direction, characters);
        }
        finally
        {
            TryDelete(workingPath);
        }
    }

    // =================================================================================
    //  Oddiy matn fayli
    // =================================================================================

    private TransliterationFileResult ConvertPlainText(
        string sourcePath,
        string folder,
        TransliterationOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new PdfProgress(0, 0, "Fayl o'qilmoqda…"));

        var text = ReadText(sourcePath);

        cancellationToken.ThrowIfCancellationRequested();

        var resolved = UzbekTransliterator.Resolve(options, text);
        var converted = UzbekTransliterator.Convert(text, resolved);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new PdfProgress(0, 0, "Natija yozilmoqda…"));

        var targetPath = SuggestOutputPath(sourcePath, folder, resolved.Direction);
        var workingPath = Path.Combine(folder, $".yordamchi-{Guid.NewGuid():N}.tmp");

        try
        {
            // BOM ataylab yoziladi: usiz Windows'dagi Bloknot kirill matnni noto'g'ri kodlashda ochadi.
            File.WriteAllText(workingPath, converted, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MoveIntoPlace(workingPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(targetPath)}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                targetPath,
                ex);
        }
        finally
        {
            TryDelete(workingPath);
        }

        return new TransliterationFileResult(sourcePath, targetPath, resolved.Direction, text.Length);
    }

    /// <summary>
    /// Tayyor natijani o'z nomiga o'tkazadi. Yarim yozilgan fayl foydalanuvchining papkasida
    /// hech qachon qolmasligi uchun barcha yozuvlar avval vaqtinchalik nomda boradi.
    /// </summary>
    private static void MoveIntoPlace(string workingPath, string targetPath)
    {
        try
        {
            File.Move(workingPath, targetPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(targetPath)}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                targetPath,
                ex);
        }
    }

    /// <summary>
    /// Matn faylini o'qiydi. BOM bo'lsa u ishonchli manba; bo'lmasa fayl UTF-8 deb qat'iy
    /// tekshiriladi. Taxmin qilib o'qish ma'nosi yo'q: Windows-1251 dagi kirill matn "мЄен"
    /// ko'rinishidagi axlatga aylanadi va foydalanuvchi buni faqat natijani ochganda ko'radi.
    /// </summary>
    private static string ReadText(string path)
    {
        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"'{Path.GetFileName(path)}' faylini o'qib bo'lmadi.",
                path,
                ex);
        }

        if (HasBom(bytes, 0xEF, 0xBB, 0xBF))
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (HasBom(bytes, 0xFF, 0xFE))
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (HasBom(bytes, 0xFE, 0xFF))
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                $"'{Path.GetFileName(path)}' UTF-8 kodlashida emas (eski Windows-1251 bo'lishi mumkin). "
                + "Faylni Bloknotda ochib \"UTF-8\" ko'rinishida saqlang yoki matnni to'g'ridan-to'g'ri "
                + "\"Matn\" bo'limiga qo'ying.",
                path,
                ex);
        }
    }

    private static bool HasBom(byte[] bytes, params byte[] bom)
    {
        if (bytes.Length < bom.Length)
            return false;

        for (var i = 0; i < bom.Length; i++)
        {
            if (bytes[i] != bom[i])
                return false;
        }

        return true;
    }

    /// <summary>Natija papkasi: ko'rsatilgani, aks holda manba fayl yonidagi papka.</summary>
    private static string ResolveFolder(string sourcePath, string? outputFolder)
    {
        if (!string.IsNullOrWhiteSpace(outputFolder))
            return outputFolder;

        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath));

            if (!string.IsNullOrEmpty(folder))
                return folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{sourcePath}' — yaroqli fayl yo'li emas.",
                sourcePath,
                ex);
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static void EnsureFolder(string folder, string sourcePath)
    {
        if (Directory.Exists(folder))
            return;

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{folder}' papkasini yaratib bo'lmadi.",
                sourcePath,
                ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Vaqtinchalik fayl qolib ketishi amalni bekor qilishga arzimaydi.
        }
    }
}
