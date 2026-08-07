using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="ArchiveService"/> sinovlari. Ular haqiqiy fayllar bilan ishlaydi (mock emas):
/// bu yerdagi qiymat aynan kutubxonalar bilan bo'lgan kelishuvda — soxta ZIP ustidagi sinov
/// SharpZipLib va SharpCompress orasidagi moslikni umuman tekshirmagan bo'lardi.
/// </summary>
public sealed class ArchiveServiceTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly ArchiveService _service = new();

    public void Dispose() => _temp.Dispose();

    // =================================================================================
    //  Yaratish → o'qish → chiqarish
    // =================================================================================

    [Fact]
    public async Task CreateZip_then_extract_restores_the_original_content()
    {
        var source = _temp.WriteFile("hujjat.txt", "Salom dunyo");
        var archive = _temp.At("natija.zip");

        var written = await _service.CreateZipAsync([source], archive);

        Assert.Equal(1, written);
        Assert.True(File.Exists(archive));

        var target = _temp.At("chiqdi");
        var extracted = await _service.ExtractAsync(archive, target);

        Assert.Equal(1, extracted);
        Assert.Equal("Salom dunyo", File.ReadAllText(Path.Combine(target, "hujjat.txt")));
    }

    [Fact]
    public async Task CreateZip_keeps_the_folder_structure_by_default()
    {
        _temp.WriteFile("manba/tepada.txt", "a");
        _temp.WriteFile("manba/ichki/pastda.txt", "b");
        var folder = _temp.At("manba");
        var archive = _temp.At("papka.zip");

        await _service.CreateZipAsync([folder], archive);
        var info = await _service.ReadAsync(archive);

        Assert.Contains(info.Entries, entry => entry.Path == "manba/tepada.txt");
        Assert.Contains(info.Entries, entry => entry.Path == "manba/ichki/pastda.txt");
    }

    [Fact]
    public async Task CreateZip_flattens_when_the_structure_is_turned_off()
    {
        _temp.WriteFile("manba/ichki/pastda.txt", "b");
        var archive = _temp.At("tekis.zip");

        await _service.CreateZipAsync(
            [_temp.At("manba")],
            archive,
            new CreateArchiveOptions { KeepFolderStructure = false });

        var info = await _service.ReadAsync(archive);

        Assert.Equal(["pastda.txt"], info.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public async Task CreateZip_gives_colliding_names_a_unique_entry()
    {
        // Ikkita boshqa-boshqa papkadagi bir xil nomli fayl bitta arxivga tushganda,
        // ikkinchisi birinchisini yozib yuborishi mumkin edi.
        var first = _temp.WriteFile("a/hisobot.txt", "birinchi");
        var second = _temp.WriteFile("b/hisobot.txt", "ikkinchi");
        var archive = _temp.At("takror.zip");

        await _service.CreateZipAsync([first, second], archive);
        var info = await _service.ReadAsync(archive);

        Assert.Equal(2, info.Entries.Count);
        Assert.Equal(2, info.Entries.Select(entry => entry.Path).Distinct().Count());

        var target = _temp.At("chiqdi");
        await _service.ExtractAsync(archive, target);

        var contents = Directory.GetFiles(target).Select(File.ReadAllText).OrderBy(text => text).ToArray();
        Assert.Equal(["birinchi", "ikkinchi"], contents);
    }

    [Fact]
    public async Task CreateZip_does_not_pack_the_archive_into_itself()
    {
        // Arxiv manba papkasining ichiga saqlansa, u o'zini o'ziga qo'shib, cheksiz
        // o'sib ketishi mumkin edi.
        _temp.WriteFile("manba/fayl.txt", "matn");
        var folder = _temp.At("manba");
        var archive = Path.Combine(folder, "ichkarida.zip");

        var written = await _service.CreateZipAsync([folder], archive);
        var info = await _service.ReadAsync(archive);

        Assert.Equal(1, written);
        Assert.DoesNotContain(info.Entries, entry => entry.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Extract_writes_only_the_requested_entries()
    {
        _temp.WriteFile("manba/bir.txt", "1");
        _temp.WriteFile("manba/ikki.txt", "2");
        var archive = _temp.At("tanlov.zip");
        await _service.CreateZipAsync([_temp.At("manba")], archive);

        var target = _temp.At("chiqdi");
        var extracted = await _service.ExtractAsync(archive, target, entryPaths: ["manba/bir.txt"]);

        Assert.Equal(1, extracted);
        Assert.True(File.Exists(Path.Combine(target, "manba", "bir.txt")));
        Assert.False(File.Exists(Path.Combine(target, "manba", "ikki.txt")));
    }

    [Fact]
    public async Task Extract_creates_the_target_folder_when_it_is_missing()
    {
        var archive = _temp.At("a.zip");
        await _service.CreateZipAsync([_temp.WriteFile("a.txt", "x")], archive);

        var target = _temp.At("hali/yaratilmagan/papka");
        await _service.ExtractAsync(archive, target);

        Assert.True(File.Exists(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async Task Read_reports_the_detected_format_and_totals()
    {
        _temp.WriteFile("manba/bir.txt", new string('x', 100));
        _temp.WriteFile("manba/ikki.txt", new string('y', 400));
        var archive = _temp.At("jamlama.zip");
        await _service.CreateZipAsync([_temp.At("manba")], archive);

        var info = await _service.ReadAsync(archive);

        Assert.Equal(ArchiveFormat.Zip, info.Format);
        Assert.Equal(2, info.FileCount);
        Assert.Equal(500, info.TotalSize);
        Assert.False(info.IsEncrypted);
    }

    // =================================================================================
    //  Parol
    // =================================================================================

    [Theory]
    [InlineData(ZipEncryption.Aes256)]
    [InlineData(ZipEncryption.ZipCrypto)]
    public async Task Password_protected_archive_opens_with_the_right_password(ZipEncryption encryption)
    {
        var source = _temp.WriteFile("maxfiy.txt", "maxfiy matn");
        var archive = _temp.At("qulflangan.zip");

        await _service.CreateZipAsync([source], archive,
            new CreateArchiveOptions { Password = "Parol123", Encryption = encryption });

        var target = _temp.At("chiqdi");
        var extracted = await _service.ExtractAsync(archive, target, "Parol123");

        Assert.Equal(1, extracted);
        Assert.Equal("maxfiy matn", File.ReadAllText(Path.Combine(target, "maxfiy.txt")));
    }

    [Theory]
    [InlineData(ZipEncryption.Aes256)]
    [InlineData(ZipEncryption.ZipCrypto)]
    public async Task Wrong_password_is_reported_as_a_password_problem_not_a_broken_file(ZipEncryption encryption)
    {
        // Bu sinov aynan chalg'ituvchi xato tufayli yozilgan: noto'g'ri parolda SharpCompress
        // "oqim turini aniqlab bo'lmadi" deydi va uni "arxiv shikastlangan" deb ko'rsatish
        // foydalanuvchini butunlay noto'g'ri yo'lga solardi.
        var archive = _temp.At("qulflangan.zip");
        await _service.CreateZipAsync([_temp.WriteFile("a.txt", "x")], archive,
            new CreateArchiveOptions { Password = "Parol123", Encryption = encryption });

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ExtractAsync(archive, _temp.At("chiqdi"), "BoshqaParol"));

        Assert.Equal(PdfErrorKind.InvalidPassword, error.Kind);
    }

    [Fact]
    public async Task Encrypted_archive_without_a_password_asks_for_one()
    {
        var archive = _temp.At("qulflangan.zip");
        await _service.CreateZipAsync([_temp.WriteFile("a.txt", "x")], archive,
            new CreateArchiveOptions { Password = "Parol123" });

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ExtractAsync(archive, _temp.At("chiqdi")));

        Assert.Equal(PdfErrorKind.PasswordProtected, error.Kind);
    }

    [Fact]
    public async Task Read_flags_an_encrypted_archive_so_the_ui_can_show_the_password_box()
    {
        var archive = _temp.At("qulflangan.zip");
        await _service.CreateZipAsync([_temp.WriteFile("a.txt", "x")], archive,
            new CreateArchiveOptions { Password = "Parol123" });

        var info = await _service.ReadAsync(archive, "Parol123");

        Assert.True(info.IsEncrypted);
        Assert.All(info.Entries, entry => Assert.True(entry.IsEncrypted));
    }

    [Fact]
    public async Task An_empty_password_means_no_encryption()
    {
        var archive = _temp.At("ochiq.zip");
        await _service.CreateZipAsync([_temp.WriteFile("a.txt", "x")], archive,
            new CreateArchiveOptions { Password = string.Empty });

        var info = await _service.ReadAsync(archive);

        Assert.False(info.IsEncrypted);
    }

    // =================================================================================
    //  Xavfsizlik: "Zip Slip"
    // =================================================================================

    [Theory]
    [InlineData("../../qochib-ketdi.txt")]
    [InlineData("..\\..\\qochib-ketdi.txt")]
    [InlineData("manba/../../qochib-ketdi.txt")]
    public async Task Extract_refuses_an_entry_that_escapes_the_target_folder(string maliciousName)
    {
        var archive = WriteRawZip("yovuz.zip", maliciousName, "men tashqaridaman");
        var target = _temp.CreateFolder("chiqdi/ichkarida");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ExtractAsync(archive, target));

        Assert.Equal(PdfErrorKind.CorruptedDocument, error.Kind);
        Assert.False(File.Exists(_temp.At("qochib-ketdi.txt")));
        Assert.False(File.Exists(_temp.At("chiqdi/qochib-ketdi.txt")));
    }

    [Fact]
    public async Task Extract_strips_an_absolute_path_instead_of_writing_outside()
    {
        // Mutlaq yo'lli yozuv ildizga "tushirilishi" kerak, dasturni yiqitmasligi ham.
        var archive = WriteRawZip("mutlaq.zip", "C:/Windows/Temp/yordamchi-sinov.txt", "matn");
        var target = _temp.CreateFolder("chiqdi");

        var extracted = await _service.ExtractAsync(archive, target);

        Assert.Equal(1, extracted);
        Assert.True(File.Exists(Path.Combine(target, "Windows", "Temp", "yordamchi-sinov.txt")));
    }

    [Fact]
    public async Task A_target_folder_whose_name_merely_starts_the_same_is_not_treated_as_inside()
    {
        // "C:\chiq" va "C:\chiqdi" — StartsWith bo'yicha bir-biriga o'xshaydi, lekin
        // ikkinchisi birinchisining ichida emas. Chegara ajratuvchi bilan tekshirilishi shart.
        var archive = WriteRawZip("qo-shni.zip", "../chiqdi-boshqa/fayl.txt", "matn");
        _temp.CreateFolder("chiqdi-boshqa");
        var target = _temp.CreateFolder("chiqdi");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ExtractAsync(archive, target));

        Assert.Equal(PdfErrorKind.CorruptedDocument, error.Kind);
        Assert.False(File.Exists(_temp.At("chiqdi-boshqa/fayl.txt")));
    }

    // =================================================================================
    //  Xato holatlari
    // =================================================================================

    [Fact]
    public async Task Reading_a_file_that_is_not_an_archive_reports_an_unsupported_format()
    {
        var notAnArchive = _temp.WriteFile("oddiy.txt", "men arxiv emasman");

        var error = await Assert.ThrowsAsync<PdfServiceException>(() => _service.ReadAsync(notAnArchive));

        Assert.Equal(PdfErrorKind.UnsupportedFormat, error.Kind);
    }

    [Fact]
    public async Task Reading_a_missing_file_reports_file_not_found()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ReadAsync(_temp.At("yo-q.zip")));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public async Task Creating_an_archive_from_nothing_is_rejected()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.CreateZipAsync([], _temp.At("bo-sh.zip")));

        Assert.Equal(PdfErrorKind.EmptySelection, error.Kind);
    }

    [Fact]
    public async Task Creating_an_archive_from_an_empty_folder_is_rejected()
    {
        var empty = _temp.CreateFolder("bo-sh-papka");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.CreateZipAsync([empty], _temp.At("natija.zip")));

        Assert.Equal(PdfErrorKind.EmptySelection, error.Kind);
    }

    [Fact]
    public async Task Creating_an_archive_from_a_missing_source_reports_file_not_found()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.CreateZipAsync([_temp.At("yo-q.txt")], _temp.At("natija.zip")));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public async Task Extracting_a_selection_that_matches_nothing_is_rejected()
    {
        var archive = _temp.At("a.zip");
        await _service.CreateZipAsync([_temp.WriteFile("a.txt", "x")], archive);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ExtractAsync(archive, _temp.At("chiqdi"), entryPaths: ["yo-q.txt"]));

        Assert.Equal(PdfErrorKind.EmptySelection, error.Kind);
    }

    // =================================================================================
    //  Bekor qilish va sozlamalar
    // =================================================================================

    [Fact]
    public async Task Cancelling_leaves_no_half_written_archive_behind()
    {
        // Yarim yozilgan .zip "tayyor arxiv" bo'lib qolsa, foydalanuvchi buni faqat uni
        // ochmoqchi bo'lganda — ehtimol manba fayllarni o'chirib yuborgandan keyin — bilardi.
        for (var i = 0; i < 40; i++)
            _temp.WriteFile($"manba/fayl-{i}.txt", new string('x', 20_000));

        var archive = _temp.At("bekor.zip");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.CreateZipAsync([_temp.At("manba")], archive, null, null, cancellation.Token));

        Assert.False(File.Exists(archive));
        Assert.False(File.Exists(archive + ".tmp"));
    }

    [Fact]
    public async Task Maximum_compression_produces_a_smaller_file_than_no_compression()
    {
        var source = _temp.WriteFile("takrorlanuvchi.txt", new string('x', 50_000));

        var stored = _temp.At("store.zip");
        var packed = _temp.At("max.zip");

        await _service.CreateZipAsync([source], stored,
            new CreateArchiveOptions { Level = ArchiveCompressionLevel.Store });
        await _service.CreateZipAsync([source], packed,
            new CreateArchiveOptions { Level = ArchiveCompressionLevel.Maximum });

        Assert.True(
            new FileInfo(packed).Length < new FileInfo(stored).Length,
            $"maksimal={new FileInfo(packed).Length}, siqishsiz={new FileInfo(stored).Length}");
    }

    [Fact]
    public async Task Progress_is_reported_for_every_file_and_ends_at_the_total()
    {
        for (var i = 0; i < 3; i++)
            _temp.WriteFile($"manba/fayl-{i}.txt", "matn");

        var reports = new List<PdfProgress>();
        var progress = new Progress<PdfProgress>(reports.Add);

        await _service.CreateZipAsync([_temp.At("manba")], _temp.At("a.zip"), null, progress);

        // Progress<T> xabarlarni navbatga qo'yadi — hammasi yetib kelishini kutamiz.
        await WaitUntil(() => reports.Count > 0 && reports[^1].Completed == 3);

        Assert.All(reports, report => Assert.Equal(3, report.Total));
        Assert.Equal(3, reports[^1].Completed);
    }

    // =================================================================================
    //  Kengaytmalarni tanish
    // =================================================================================

    [Theory]
    [InlineData("a.zip")]
    [InlineData("a.RAR")]
    [InlineData("a.7z")]
    [InlineData("papka/ichida/a.tar")]
    public void LooksLikeArchive_accepts_supported_extensions(string path) =>
        Assert.True(_service.LooksLikeArchive(path));

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.pdf")]
    [InlineData("kengaytmasiz")]
    [InlineData("")]
    public void LooksLikeArchive_rejects_everything_else(string path) =>
        Assert.False(_service.LooksLikeArchive(path));

    [Fact]
    public void OpenFilter_lists_every_supported_extension()
    {
        foreach (var extension in _service.SupportedReadExtensions)
            Assert.Contains("*" + extension, _service.OpenFilter, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>
    /// Servisning o'zidan o'tmasdan, to'g'ridan-to'g'ri ZIP yozadi — shu tarzda hech qanday
    /// oddiy dastur yaratmaydigan "yovuz" yozuv nomlarini qo'yish mumkin.
    /// </summary>
    private string WriteRawZip(string archiveName, string entryName, string content)
    {
        var path = _temp.At(archiveName);

        using var file = File.Create(path);
        using var zip = new ZipOutputStream(file);

        zip.SetLevel(0);
        zip.PutNextEntry(new ZipEntry(entryName));

        var bytes = Encoding.UTF8.GetBytes(content);
        zip.Write(bytes, 0, bytes.Length);

        zip.CloseEntry();
        zip.Finish();

        return path;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);
    }
}
