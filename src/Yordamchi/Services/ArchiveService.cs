using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.Services;

/// <summary>
/// Arxivlar bilan ishlash. Ataylab <b>ikkita</b> kutubxonaga tayanadi — xuddi PDF tomonida
/// PDFsharp va PdfPig ajratilgani kabi:
/// <list type="bullet">
///   <item><description>
///     <b>SharpCompress</b> — <i>o'qish</i>: ZIP, RAR (RAR5 ham), 7z, TAR, GZip. Bitta
///     kutubxonada shuncha formatni o'qiy oladigan boshqa boshqariladigan (managed) variant yo'q.
///     Lekin u ZIP ni <i>shifrlab yoza olmaydi</i>.
///   </description></item>
///   <item><description>
///     <b>SharpZipLib</b> — <i>yozish</i>: parolli ZIP, jumladan WinZip AES-256. Aynan shu
///     yetishmagan bo'lagi uchun qo'shilgan.
///   </description></item>
/// </list>
/// </summary>
public sealed class ArchiveService : IArchiveService
{
    /// <summary>Katta fayllarni ko'chirishda ishlatiladigan bufer.</summary>
    private const int CopyBufferSize = 81920;

    private static readonly string[] ReadExtensions =
        [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".cbz", ".cbr"];

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedReadExtensions => ReadExtensions;

    /// <inheritdoc />
    public string OpenFilter =>
        "Arxivlar (" + string.Join(";", ReadExtensions.Select(e => "*" + e)) + ")|"
        + string.Join(";", ReadExtensions.Select(e => "*" + e)) + "|"
        + "ZIP (*.zip)|*.zip|RAR (*.rar)|*.rar|7-Zip (*.7z)|*.7z|"
        + "Barcha fayllar (*.*)|*.*";

    /// <inheritdoc />
    public bool LooksLikeArchive(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && ReadExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // =====================================================================================
    //  O'qish
    // =====================================================================================

    /// <inheritdoc />
    public Task<ArchiveInfo> ReadAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        EnsureFileExists(archivePath);
        return Task.Run(() => ReadCore(archivePath, password, cancellationToken), cancellationToken);
    }

    private static ArchiveInfo ReadCore(string archivePath, string? password, CancellationToken cancellationToken)
    {
        using var archive = OpenArchive(archivePath, password);

        var entries = new List<ArchiveEntryInfo>();
        long totalSize = 0;
        var encrypted = false;

        try
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = entry.Key;
                if (string.IsNullOrEmpty(key))
                    continue;

                if (entry.IsEncrypted)
                    encrypted = true;

                if (!entry.IsDirectory)
                    totalSize += entry.Size;

                entries.Add(new ArchiveEntryInfo(
                    key.Replace('\\', '/'),
                    entry.Size,
                    entry.CompressedSize,
                    entry.LastModifiedTime,
                    entry.IsDirectory,
                    entry.IsEncrypted));
            }
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            // Ba'zi formatlarda (7z, shifrlangan sarlavhali ZIP) ro'yxatning o'zi ham parolsiz
            // o'qilmaydi — bu yerda "arxiv buzuq" emas, "parol kerak" deyish to'g'ri.
            throw PasswordError(archivePath, password);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Corrupted(archivePath, ex);
        }

        // Arxiv darajasidagi bayroq ham hisobga olinadi: 7z da sarlavhaning o'zi shifrlangan
        // bo'lsa, alohida yozuvlarda IsEncrypted ko'rinmasligi mumkin.
        return new ArchiveInfo(MapFormat(archive.Type), entries, totalSize, encrypted || archive.IsEncrypted);
    }

    // =====================================================================================
    //  Chiqarish
    // =====================================================================================

    /// <inheritdoc />
    public Task<int> ExtractAsync(
        string archivePath,
        string targetFolder,
        string? password = null,
        IReadOnlyCollection<string>? entryPaths = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureFileExists(archivePath);

        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                "Fayllar chiqariladigan papka tanlanmadi.");
        }

        return Task.Run(
            () => ExtractCore(archivePath, targetFolder, password, entryPaths, progress, cancellationToken),
            cancellationToken);
    }

    private static int ExtractCore(
        string archivePath,
        string targetFolder,
        string? password,
        IReadOnlyCollection<string>? entryPaths,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = Path.GetFullPath(targetFolder);
            Directory.CreateDirectory(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Papkaga yozib bo'lmadi: {targetFolder}. Huquqlarni tekshiring yoki boshqa joy tanlang.",
                targetFolder,
                ex);
        }

        var wanted = entryPaths is { Count: > 0 }
            ? new HashSet<string>(entryPaths.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase)
            : null;

        using var archive = OpenArchive(archivePath, password);

        List<IArchiveEntry> selected;
        try
        {
            selected = archive.Entries
                .Where(entry => !entry.IsDirectory && !string.IsNullOrEmpty(entry.Key))
                .Where(entry => wanted is null || wanted.Contains(NormalizeKey(entry.Key!)))
                .ToList();
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            throw PasswordError(archivePath, password);
        }

        if (selected.Count == 0)
        {
            throw new PdfServiceException(
                PdfErrorKind.EmptySelection,
                "Chiqarish uchun hech narsa tanlanmadi — arxiv bo'sh yoki tanlov bilan mos yozuv yo'q.",
                archivePath);
        }

        var done = 0;

        foreach (var entry in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PdfProgress(done, selected.Count, entry.Key));

            var destination = ResolveSafeDestination(root, entry.Key!, archivePath);

            try
            {
                var folder = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);

                using var source = entry.OpenEntryStream();
                using var target = new FileStream(
                    destination, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize);

                source.CopyTo(target, CopyBufferSize);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PdfServiceException)
            {
                throw;
            }
            catch (Exception ex) when (IsPasswordFailure(ex))
            {
                throw PasswordError(archivePath, password);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PdfServiceException(
                    PdfErrorKind.OutputNotWritable,
                    $"'{entry.Key}' faylini yozib bo'lmadi. U boshqa dasturda ochiq bo'lishi mumkin.",
                    destination,
                    ex);
            }
            catch (Exception) when (entry.IsEncrypted)
            {
                // Noto'g'ri parol bilan ochilgan oqim shunchaki axlat bo'ladi, shuning uchun
                // kutubxona "oqim turini aniqlab bo'lmadi" kabi butunlay boshqa xato beradi.
                // Yozuv shifrlangani ma'lum bo'lgani uchun sababni parolda deb aytamiz —
                // haqiqatan shikastlangan shifrlangan faylga qaraganda bu ancha ehtimolli.
                throw PasswordError(archivePath, password);
            }
            catch (Exception ex)
            {
                throw Corrupted(archivePath, ex);
            }

            done++;
            progress?.Report(new PdfProgress(done, selected.Count, entry.Key));
        }

        return done;
    }

    /// <summary>
    /// Arxiv ichidagi yo'lni maqsad papkasi <b>ichida</b> qoladigan qilib hisoblaydi.
    /// <para>
    /// Bu shunchaki ehtiyotkorlik emas: arxivga <c>..\..\Windows\System32\...</c> kabi yozuv
    /// qo'yish mumkin ("Zip Slip"). Kutubxonaga ishonib qo'yib yubormaymiz — natija yo'lini
    /// o'zimiz to'liq yechib, ildizdan chiqib ketmasligini tekshiramiz.
    /// </para>
    /// </summary>
    private static string ResolveSafeDestination(string root, string entryKey, string archivePath)
    {
        var relative = entryKey.Replace('/', Path.DirectorySeparatorChar)
                               .Replace('\\', Path.DirectorySeparatorChar)
                               .TrimStart(Path.DirectorySeparatorChar);

        // Diskli ("C:\...") yoki UNC ("\\server\...") yo'llar ham ildizdan chiqarib yuboradi.
        if (Path.IsPathRooted(relative))
            relative = relative.TrimStart(Path.DirectorySeparatorChar, '/');

        if (relative.Length > 1 && relative[1] == ':')
            relative = relative[2..].TrimStart(Path.DirectorySeparatorChar);

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                $"Arxivdagi '{entryKey}' yozuvining nomi Windows uchun yaroqsiz — bu fayl chiqarilmadi.",
                archivePath,
                ex);
        }

        var fence = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(fence, StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                $"Arxiv xavfli yozuv saqlaydi: '{entryKey}' tanlangan papkadan tashqariga yozmoqchi. "
                + "Bunday arxiv ishonchsiz, chiqarish to'xtatildi.",
                archivePath);
        }

        return full;
    }

    // =====================================================================================
    //  Yaratish
    // =====================================================================================

    /// <inheritdoc />
    public Task<int> CreateZipAsync(
        IReadOnlyList<string> sourcePaths,
        string archivePath,
        CreateArchiveOptions? options = null,
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);

        if (sourcePaths.Count == 0)
        {
            throw new PdfServiceException(
                PdfErrorKind.EmptySelection,
                "Arxivga qo'shish uchun kamida bitta fayl yoki papka tanlang.");
        }

        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                "Arxiv saqlanadigan joy tanlanmadi.");
        }

        var effective = options ?? CreateArchiveOptions.Default;

        return Task.Run(
            () => CreateZipCore(sourcePaths, archivePath, effective, progress, cancellationToken),
            cancellationToken);
    }

    private static int CreateZipCore(
        IReadOnlyList<string> sourcePaths,
        string archivePath,
        CreateArchiveOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = BuildPlan(sourcePaths, options, archivePath);

        if (plan.Count == 0)
        {
            throw new PdfServiceException(
                PdfErrorKind.EmptySelection,
                "Tanlangan papkalar bo'sh — arxivga yoziladigan fayl topilmadi.");
        }

        // Yarim yozilgan .zip "tayyor arxiv" bo'lib qolmasligi uchun avval vaqtinchalik faylga.
        var tempPath = archivePath + ".tmp";
        var written = 0;

        try
        {
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize))
            using (var zip = new ZipOutputStream(file))
            {
                zip.SetLevel(options.DeflateLevel);

                var hasPassword = !string.IsNullOrEmpty(options.Password);
                if (hasPassword)
                    zip.Password = options.Password;

                foreach (var (sourceFile, entryName) in plan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new PdfProgress(written, plan.Count, entryName));

                    var info = new FileInfo(sourceFile);

                    var entry = new ZipEntry(entryName)
                    {
                        DateTime = info.LastWriteTime,
                        Size = info.Length,

                        // Kirill/o'zbek harfli nomlar boshqa dasturlarda ham to'g'ri ko'rinsin.
                        IsUnicodeText = true
                    };

                    // AES faqat entry darajasida yoqiladi; yoqilmasa SharpZipLib eski
                    // ZipCrypto ni ishlatadi — bu ataylab beriladigan tanlov (ZipEncryption).
                    if (hasPassword && options.Encryption == ZipEncryption.Aes256)
                        entry.AESKeySize = 256;

                    zip.PutNextEntry(entry);

                    using (var source = new FileStream(
                               sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize))
                    {
                        source.CopyTo(zip, CopyBufferSize);
                    }

                    zip.CloseEntry();

                    written++;
                    progress?.Report(new PdfProgress(written, plan.Count, entryName));
                }

                zip.Finish();
            }

            File.Move(tempPath, archivePath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (PdfServiceException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SafeDelete(tempPath);
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Arxivni yozib bo'lmadi: {archivePath}. Fayl ochiq bo'lishi yoki papkaga yozish "
                + "huquqi bo'lmasligi mumkin.",
                archivePath,
                ex);
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"Arxiv yaratilmadi: {ex.Message}",
                archivePath,
                ex);
        }

        return written;
    }

    /// <summary>
    /// Tanlangan yo'llarni "manba fayl → arxivdagi nom" juftliklariga yoyadi. Papkalar
    /// rekursiv o'tiladi, nomlar takrorlansa oxiriga raqam qo'shiladi.
    /// </summary>
    private static List<(string SourceFile, string EntryName)> BuildPlan(
        IReadOnlyList<string> sourcePaths,
        CreateArchiveOptions options,
        string archivePath)
    {
        var plan = new List<(string, string)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Arxivning o'zi manba papkasi ichida bo'lsa, u o'zini o'ziga yozib olmasligi kerak.
        var archiveFull = SafeFullPath(archivePath);

        foreach (var path in sourcePaths)
        {
            if (Directory.Exists(path))
            {
                var baseFolder = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                var folderName = Path.GetFileName(baseFolder);

                foreach (var file in Directory.EnumerateFiles(baseFolder, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(SafeFullPath(file), archiveFull, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var name = options.KeepFolderStructure
                        ? Path.Combine(folderName, Path.GetRelativePath(baseFolder, file))
                        : Path.GetFileName(file);

                    plan.Add((file, Unique(ToEntryName(name), used)));
                }

                continue;
            }

            if (!File.Exists(path))
            {
                throw new PdfServiceException(
                    PdfErrorKind.FileNotFound,
                    $"Fayl topilmadi: {path}",
                    path);
            }

            if (string.Equals(SafeFullPath(path), archiveFull, StringComparison.OrdinalIgnoreCase))
                continue;

            plan.Add((path, Unique(ToEntryName(Path.GetFileName(path)), used)));
        }

        return plan;
    }

    private static string ToEntryName(string name) =>
        name.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/').TrimStart('/');

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];

        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){extension}";
            if (used.Add(candidate))
                return candidate;
        }
    }

    // =====================================================================================
    //  Yordamchilar
    // =====================================================================================

    private static IArchive OpenArchive(string archivePath, string? password)
    {
        // Kutubxonaning o'z tayyor sozlamalari: fayldan o'qish uchun ForFilePath, parol
        // kerak bo'lganda esa uning shifrlangan arxiv uchun varianti.
        var options = string.IsNullOrEmpty(password)
            ? ReaderOptions.ForFilePath
            : ReaderOptions.ForEncryptedArchive(password);

        try
        {
            return ArchiveFactory.OpenArchive(archivePath, options);
        }
        catch (Exception ex) when (IsPasswordFailure(ex))
        {
            throw PasswordError(archivePath, password);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"Arxivni ochib bo'lmadi: {archivePath}. U boshqa dasturda ochiq bo'lishi mumkin.",
                archivePath,
                ex);
        }
        catch (Exception ex) when (IsRecognizedArchive(archivePath))
        {
            // Imzo bo'yicha bu haqiqiy arxiv, lekin ochilmadi. Sabab deyarli har doim parol:
            // SharpCompress formatni aniqlashda ham deshifrlaydi, shuning uchun noto'g'ri parol
            // "oqim turini aniqlab bo'lmadi" degan mutlaqo boshqa xato ko'rinishida chiqadi.
            throw LockedOrDamaged(archivePath, password, ex);
        }
        catch (Exception ex) when (ex is InvalidFormatException
                                      or InvalidOperationException
                                      or ArchiveOperationException)
        {
            throw new PdfServiceException(
                PdfErrorKind.UnsupportedFormat,
                $"'{Path.GetFileName(archivePath)}' tanilgan arxiv formatlaridan biriga o'xshamaydi. "
                + $"Qo'llab-quvvatlanadi: {string.Join(", ", ReadExtensions)}.",
                archivePath,
                ex);
        }
        catch (Exception ex)
        {
            throw Corrupted(archivePath, ex);
        }
    }

    /// <summary>
    /// Fayl imzosi bo'yicha tanilgan arxivmi — mazmunini ochmasdan va parolsiz tekshiradi.
    /// Shu tufayli "parol noto'g'ri" bilan "fayl umuman arxiv emas" ni ajratish mumkin.
    /// </summary>
    private static bool IsRecognizedArchive(string path)
    {
        try
        {
            return ArchiveFactory.IsArchive(path, out _);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Xato aynan parol sababli chiqqanmi. Har bir format o'z turini tashlaydi
    /// (7z — <see cref="CryptographicException"/>, ZIP — SharpCompress ning o'z xatosi),
    /// shuning uchun tur bo'yicha ham, xabar bo'yicha ham tekshiramiz.
    /// </summary>
    private static bool IsPasswordFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            // Ikkala nom ham "CryptographicException": biri SharpCompress ning o'ziniki,
            // ikkinchisi .NET niki — shuning uchun to'liq nom bilan yozilgan.
            if (current is SharpCompress.Common.CryptographicException
                or System.Security.Cryptography.CryptographicException)
            {
                return true;
            }

            var message = current.Message;
            if (string.IsNullOrEmpty(message))
                continue;

            if (message.Contains("password", StringComparison.OrdinalIgnoreCase)
                || message.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static PdfServiceException PasswordError(string archivePath, string? password) =>
        string.IsNullOrEmpty(password)
            ? new PdfServiceException(
                PdfErrorKind.PasswordProtected,
                "Bu arxiv parol bilan himoyalangan. Parolni kiriting va qaytadan urinib ko'ring.",
                archivePath)
            : new PdfServiceException(
                PdfErrorKind.InvalidPassword,
                "Parol to'g'ri kelmadi. Katta-kichik harflarga e'tibor bering.",
                archivePath);

    /// <summary>
    /// Imzosi to'g'ri, lekin ochilmagan arxiv uchun. Parol kiritilgan bo'lsa aybdor deyarli
    /// har doim parol; kiritilmagan bo'lsa esa ikkala ehtimolni ham aytamiz, chunki shifrlangan
    /// sarlavhali arxiv ham, shikastlangan fayl ham aynan shu nuqtada yiqiladi.
    /// </summary>
    private static PdfServiceException LockedOrDamaged(string archivePath, string? password, Exception ex) =>
        string.IsNullOrEmpty(password)
            ? new PdfServiceException(
                PdfErrorKind.PasswordProtected,
                "Arxivni ochib bo'lmadi. U parol bilan himoyalangan bo'lsa — parolni kiriting; "
                + "aks holda fayl shikastlangan bo'lishi mumkin.",
                archivePath,
                ex)
            : new PdfServiceException(
                PdfErrorKind.InvalidPassword,
                "Parol to'g'ri kelmadi. Katta-kichik harflarga e'tibor bering. "
                + "(Parol aniq to'g'ri bo'lsa — arxivning o'zi shikastlangan bo'lishi mumkin.)",
                archivePath,
                ex);

    private static PdfServiceException Corrupted(string archivePath, Exception ex) => new(
        PdfErrorKind.CorruptedDocument,
        $"Arxivni o'qib bo'lmadi — fayl shikastlangan yoki to'liq yuklanmagan bo'lishi mumkin. "
        + $"({ex.Message})",
        archivePath,
        ex);

    private static ArchiveFormat MapFormat(ArchiveType type) => type switch
    {
        ArchiveType.Zip => ArchiveFormat.Zip,
        ArchiveType.Rar => ArchiveFormat.Rar,
        ArchiveType.SevenZip => ArchiveFormat.SevenZip,
        ArchiveType.Tar => ArchiveFormat.Tar,
        ArchiveType.GZip => ArchiveFormat.GZip,
        _ => ArchiveFormat.Unknown
    };

    private static string NormalizeKey(string key) => key.Replace('\\', '/').TrimStart('/');

    private static void EnsureFileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, "Arxiv fayli ko'rsatilmagan.");

        if (!File.Exists(path))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"Fayl topilmadi: {path}", path);
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Vaqtinchalik fayl qolib ketsa ham asosiy natijaga ta'sir qilmaydi.
        }
    }
}
