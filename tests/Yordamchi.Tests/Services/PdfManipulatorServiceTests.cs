using System.IO;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="PdfManipulatorService"/> sinovlari. Hammasi <b>haqiqiy PDF fayllar</b> ustida
/// ishlaydi: hujjat <see cref="PdfFactory"/> bilan yaratiladi, amal bajariladi va natija qaytadan
/// ochib tekshiriladi.
/// <para>
/// Bu yerdagi asosiy qiymat — "istisno tashlanmadi" emas, balki <i>natija to'g'rimi</i>: sahifalar
/// soni, ularning tartibi, burilish burchagi, parol bilan ochilishi. Aynan shu narsalar PDFsharp
/// versiyasi almashganda jimgina buzilishi mumkin.
/// </para>
/// </summary>
public sealed class PdfManipulatorServiceTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly PdfManipulatorService _service = new();

    public void Dispose() => _temp.Dispose();

    // =================================================================================
    //  Birlashtirish
    // =================================================================================

    [Fact]
    public async Task Merge_keeps_every_page_of_every_file_in_the_given_order()
    {
        // Belgilar bilan tekshiriladi: sahifalar soni to'g'ri bo'lib, tartibi almashib
        // ketishi — birlashtirishdagi eng bilinmaydigan xatolik.
        var first = PdfFactory.Create(_temp.At("bir.pdf"), pageCount: 2, firstMarker: 1);
        var second = PdfFactory.Create(_temp.At("ikki.pdf"), pageCount: 3, firstMarker: 11);
        var output = _temp.At("natija.pdf");

        await _service.MergePdfsAsync([first, second], output);

        Assert.True(File.Exists(output));
        Assert.Equal(5, PdfFactory.PageCount(output));
        Assert.Equal([1, 2, 11, 12, 13], PdfFactory.Markers(output));
    }

    [Fact]
    public async Task Merge_respects_the_order_of_the_list_not_the_file_names()
    {
        var first = PdfFactory.Create(_temp.At("a.pdf"), pageCount: 1, firstMarker: 1);
        var second = PdfFactory.Create(_temp.At("b.pdf"), pageCount: 1, firstMarker: 21);
        var output = _temp.At("natija.pdf");

        await _service.MergePdfsAsync([second, first], output);

        Assert.Equal([21, 1], PdfFactory.Markers(output));
    }

    [Fact]
    public async Task Merge_accepts_a_single_file()
    {
        var only = PdfFactory.Create(_temp.At("yolg-iz.pdf"), pageCount: 3);
        var output = _temp.At("natija.pdf");

        await _service.MergePdfsAsync([only], output);

        Assert.Equal([1, 2, 3], PdfFactory.Markers(output));
    }

    [Fact]
    public async Task Merge_can_write_over_one_of_its_own_sources()
    {
        // Servis manbalarni oldindan baytlarga o'qiydi. Aks holda natija yozilayotganda
        // manba faylga handle ushlab turilardi va foydalanuvchi "fayl band" xatosini olardi —
        // yoki bundan ham yomoni, yarim yozilgan hujjat qolardi.
        var first = PdfFactory.Create(_temp.At("bir.pdf"), pageCount: 2, firstMarker: 1);
        var second = PdfFactory.Create(_temp.At("ikki.pdf"), pageCount: 2, firstMarker: 31);

        await _service.MergePdfsAsync([first, second], first);

        Assert.Equal([1, 2, 31, 32], PdfFactory.Markers(first));
    }

    [Fact]
    public async Task Merge_rejects_an_empty_selection()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.MergePdfsAsync([], _temp.At("natija.pdf")));

        Assert.Equal(PdfErrorKind.EmptySelection, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Fact]
    public async Task Merge_reports_a_missing_source_as_file_not_found()
    {
        var existing = PdfFactory.Create(_temp.At("bor.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.MergePdfsAsync([existing, _temp.At("yo-q.pdf")], _temp.At("natija.pdf")));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public async Task Merge_reports_a_file_that_is_not_a_pdf_as_a_corrupted_document()
    {
        var fake = _temp.WriteFile("soxta.pdf", "men PDF emasman");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.MergePdfsAsync([fake], _temp.At("natija.pdf")));

        Assert.Equal(PdfErrorKind.CorruptedDocument, error.Kind);
    }

    [Fact]
    public async Task Merge_reports_progress_up_to_a_hundred()
    {
        var first = PdfFactory.Create(_temp.At("bir.pdf"), pageCount: 1);
        var second = PdfFactory.Create(_temp.At("ikki.pdf"), pageCount: 1);
        var progress = new ProgressRecorder();

        await _service.MergePdfsAsync([first, second], _temp.At("natija.pdf"), progress);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(100, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0, 100));
    }

    // =================================================================================
    //  Bo'lish — SplitOptions
    // =================================================================================

    [Fact]
    public async Task Split_by_every_page_writes_one_single_page_file_per_page()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 4);
        var folder = _temp.At("boluvchi");

        var created = await _service.SplitPdfAsync(source, folder, new SplitOptions { Mode = SplitMode.EveryPage });

        Assert.Equal(4, created.Count);
        Assert.All(created, path => Assert.Equal(1, PdfFactory.PageCount(path)));

        // Har bir bo'lakda aynan o'sha tartibdagi sahifa turishi kerak.
        Assert.Equal([1, 2, 3, 4], created.Select(path => PdfFactory.Markers(path)[0]));
        Assert.Equal(
            ["hujjat_1.pdf", "hujjat_2.pdf", "hujjat_3.pdf", "hujjat_4.pdf"],
            created.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_by_ranges_writes_one_file_per_range()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 6);
        var folder = _temp.At("boluvchi");

        var created = await _service.SplitPdfAsync(
            source,
            folder,
            new SplitOptions { Mode = SplitMode.Ranges, RangeExpression = "1-3, 5" });

        Assert.Equal(2, created.Count);
        Assert.Equal(["hujjat_1-3.pdf", "hujjat_5.pdf"], created.Select(Path.GetFileName));
        Assert.Equal([1, 2, 3], PdfFactory.Markers(created[0]));
        Assert.Equal([5], PdfFactory.Markers(created[1]));
    }

    [Theory]
    [InlineData("1;3")]
    [InlineData("1\n3")]
    [InlineData(" 1 , 3 ")]
    [InlineData("1, 3,")]
    public async Task Split_by_ranges_accepts_the_documented_separators(string expression)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 4);

        var created = await _service.SplitPdfAsync(
            source,
            _temp.At("boluvchi"),
            new SplitOptions { Mode = SplitMode.Ranges, RangeExpression = expression });

        Assert.Equal(2, created.Count);
        Assert.Equal([1], PdfFactory.Markers(created[0]));
        Assert.Equal([3], PdfFactory.Markers(created[1]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("0-2")]
    [InlineData("5-2")]
    [InlineData("1-2-3")]
    [InlineData("-3")]
    public async Task Split_rejects_a_range_expression_it_cannot_understand(string expression)
    {
        // Tushunarsiz ifoda "0 ta fayl" bo'lib jimgina o'tib ketmasligi kerak: foydalanuvchi
        // xatoni aynan kiritish paytida ko'rishi shart.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 4);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(
                source,
                _temp.At("boluvchi"),
                new SplitOptions { Mode = SplitMode.Ranges, RangeExpression = expression }));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("1-9")]
    [InlineData("3-4")]
    public async Task Split_rejects_a_range_that_runs_past_the_last_page(string expression)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(
                source,
                _temp.At("boluvchi"),
                new SplitOptions { Mode = SplitMode.Ranges, RangeExpression = expression }));

        Assert.Equal(PdfErrorKind.PageIndexOutOfRange, error.Kind);
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public async Task Split_by_fixed_chunks_leaves_the_last_chunk_short()
    {
        // 7 sahifa / 3 = 2 to'liq bo'lak va 1 sahifali "quyruq". Oxirgi bo'lakni tashlab
        // yuborish yoki hujjat chegarasidan chiqib ketish — eng ehtimolli xatolik.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 7);

        var created = await _service.SplitPdfAsync(
            source,
            _temp.At("boluvchi"),
            new SplitOptions { Mode = SplitMode.FixedChunks, PagesPerFile = 3 });

        Assert.Equal(3, created.Count);
        Assert.Equal([3, 3, 1], created.Select(path => PdfFactory.PageCount(path)));
        Assert.Equal([1, 2, 3], PdfFactory.Markers(created[0]));
        Assert.Equal([4, 5, 6], PdfFactory.Markers(created[1]));
        Assert.Equal([7], PdfFactory.Markers(created[2]));
    }

    [Fact]
    public async Task Split_by_fixed_chunks_keeps_a_document_that_fits_in_one_chunk_whole()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var created = await _service.SplitPdfAsync(
            source,
            _temp.At("boluvchi"),
            new SplitOptions { Mode = SplitMode.FixedChunks, PagesPerFile = 10 });

        Assert.Single(created);
        Assert.Equal([1, 2, 3], PdfFactory.Markers(created[0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Split_by_fixed_chunks_rejects_a_chunk_size_below_one(int pagesPerFile)
    {
        // 0 bo'lsa oraliqlar hosil qiladigan sikl cheksiz aylanardi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(
                source,
                _temp.At("boluvchi"),
                new SplitOptions { Mode = SplitMode.FixedChunks, PagesPerFile = pagesPerFile }));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task Split_uses_the_given_file_name_prefix()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);

        var created = await _service.SplitPdfAsync(
            source,
            _temp.At("boluvchi"),
            new SplitOptions { FileNamePrefix = "Shartnoma" });

        Assert.Equal(["Shartnoma_1.pdf", "Shartnoma_2.pdf"], created.Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_cleans_characters_that_windows_forbids_in_a_file_name()
    {
        // Prefiks foydalanuvchi kiritadigan matn: undagi ':' yoki '/' fayl yozishni
        // to'liq to'xtatib qo'yardi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var created = await _service.SplitPdfAsync(
            source,
            _temp.At("boluvchi"),
            new SplitOptions { FileNamePrefix = "a/b:c*d" });

        var name = Path.GetFileName(created[0]);
        Assert.Equal("a_b_c_d_1.pdf", name);
        Assert.True(File.Exists(created[0]));
    }

    [Fact]
    public async Task Split_creates_the_output_folder_when_it_is_missing()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var folder = _temp.At("hali/yaratilmagan/papka");

        var created = await _service.SplitPdfAsync(source, folder, SplitOptions.Default);

        Assert.True(File.Exists(created[0]));
        Assert.Equal(Path.GetFullPath(folder), Path.GetFullPath(Path.GetDirectoryName(created[0])!));
    }

    [Fact]
    public async Task Split_does_not_overwrite_the_files_of_an_earlier_run()
    {
        // Foydalanuvchi bir papkaga ikki marta bo'lsa, birinchi natija yo'qolib ketmasligi kerak.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var folder = _temp.At("boluvchi");

        var first = await _service.SplitPdfAsync(source, folder, SplitOptions.Default);
        var second = await _service.SplitPdfAsync(source, folder, SplitOptions.Default);

        Assert.Empty(first.Intersect(second));
        Assert.Equal(4, Directory.GetFiles(folder, "*.pdf").Length);
        Assert.All(first, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task Split_reports_a_missing_file_as_file_not_found()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(_temp.At("yo-q.pdf"), _temp.At("boluvchi"), SplitOptions.Default));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    // =================================================================================
    //  Bo'lish — tayyor oraliqlar ro'yxati bilan
    // =================================================================================

    [Fact]
    public async Task Split_with_explicit_ranges_writes_exactly_those_ranges()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 5);

        var created = await _service.SplitPdfAsync(
            source,
            _temp.At("boluvchi"),
            [(1, 2), (4, 4)]);

        Assert.Equal(2, created.Count);
        Assert.Equal([1, 2], PdfFactory.Markers(created[0]));
        Assert.Equal([4], PdfFactory.Markers(created[1]));
    }

    [Fact]
    public async Task Split_with_explicit_ranges_may_repeat_a_page_in_two_parts()
    {
        // Oraliqlar kesishishi mumkin — bu xato emas, faqat sahifa ikki bo'lakka tushadi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var created = await _service.SplitPdfAsync(source, _temp.At("boluvchi"), [(1, 2), (2, 3)]);

        Assert.Equal([1, 2], PdfFactory.Markers(created[0]));
        Assert.Equal([2, 3], PdfFactory.Markers(created[1]));
    }

    [Fact]
    public async Task Split_with_an_empty_range_list_is_rejected()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(source, _temp.At("boluvchi"), []));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task Split_with_an_explicit_range_past_the_last_page_is_rejected()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(source, _temp.At("boluvchi"), [(1, 9)]));

        Assert.Equal(PdfErrorKind.PageIndexOutOfRange, error.Kind);
    }

    [Fact]
    public async Task Split_with_an_explicit_range_that_starts_below_one_is_rejected()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.SplitPdfAsync(source, _temp.At("boluvchi"), [(0, 2)]));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    // =================================================================================
    //  Burish
    // =================================================================================

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task Rotate_writes_the_angle_into_every_page(int degrees)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);
        var output = _temp.At("burilgan.pdf");

        await _service.RotatePagesAsync(source, output, degrees);

        Assert.Equal([degrees, degrees, degrees], PdfFactory.Rotations(output));
        Assert.Equal([1, 2, 3], PdfFactory.Markers(output));
    }

    [Fact]
    public async Task Rotate_touches_only_the_pages_it_was_given()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 4);
        var output = _temp.At("burilgan.pdf");

        await _service.RotatePagesAsync(source, output, 90, pageIndices: [1, 3]);

        Assert.Equal([0, 90, 0, 90], PdfFactory.Rotations(output));
    }

    [Fact]
    public async Task Rotate_adds_to_the_angle_that_is_already_there()
    {
        // Ikki marta 90 daraja bursak 180 chiqishi kerak, 90 emas: burchak almashtirilmaydi,
        // qo'shiladi. Aks holda foydalanuvchi tugmani ikki marta bosganda hech narsa o'zgarmasdi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var once = _temp.At("bir-marta.pdf");
        var twice = _temp.At("ikki-marta.pdf");

        await _service.RotatePagesAsync(source, once, 90);
        await _service.RotatePagesAsync(once, twice, 90);

        Assert.Equal([180], PdfFactory.Rotations(twice));
    }

    [Fact]
    public async Task Rotate_normalises_the_angle_into_the_zero_to_three_sixty_range()
    {
        // PDF spetsifikatsiyasi /Rotate uchun 0..270 kutadi: manfiy yoki 360 dan katta qiymat
        // ba'zi ko'ruvchilarda umuman e'tiborga olinmaydi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var negative = _temp.At("manfiy.pdf");
        var overflow = _temp.At("ortiqcha.pdf");

        await _service.RotatePagesAsync(source, negative, -90);
        await _service.RotatePagesAsync(source, overflow, 450);

        Assert.Equal([270], PdfFactory.Rotations(negative));
        Assert.Equal([90], PdfFactory.Rotations(overflow));
    }

    [Fact]
    public async Task Rotate_by_zero_leaves_the_document_as_it_was()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var output = _temp.At("natija.pdf");

        await _service.RotatePagesAsync(source, output, 0);

        Assert.Equal([0, 0], PdfFactory.Rotations(output));
        Assert.Equal([1, 2], PdfFactory.Markers(output));
    }

    [Theory]
    [InlineData(45)]
    [InlineData(1)]
    [InlineData(-30)]
    public async Task Rotate_rejects_an_angle_that_is_not_a_multiple_of_ninety(int degrees)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.RotatePagesAsync(source, _temp.At("natija.pdf"), degrees));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task Rotate_rejects_a_page_index_outside_the_document(int index)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.RotatePagesAsync(source, _temp.At("natija.pdf"), 90, pageIndices: [index]));

        Assert.Equal(PdfErrorKind.PageIndexOutOfRange, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Fact]
    public async Task Rotate_can_overwrite_the_file_it_read()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);

        await _service.RotatePagesAsync(source, source, 90);

        Assert.Equal([90, 90], PdfFactory.Rotations(source));
    }

    // =================================================================================
    //  Himoyalash va qulfni ochish
    // =================================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Protect_then_unlock_is_a_full_round_trip(bool useAes256)
    {
        // To'liq aylanma: parol qo'yiladi → hujjat parolsiz ochilmaydi → parol bilan ochiladi →
        // qulf ochiladi → yana parolsiz ochiladi. Sahifalar esa hech qayerda yo'qolmaydi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);
        var locked = _temp.At("qulflangan.pdf");
        var unlocked = _temp.At("ochilgan.pdf");

        await _service.ProtectPdfAsync(
            source,
            locked,
            new ProtectOptions { UserPassword = "Parol123", UseAes256 = useAes256 });

        Assert.False(PdfFactory.OpensWithoutPassword(locked));
        Assert.True(await _service.IsPasswordProtectedAsync(locked));
        Assert.Equal([1, 2, 3], PdfFactory.Markers(locked, "Parol123"));

        await _service.UnlockPdfAsync(locked, unlocked, "Parol123");

        Assert.True(PdfFactory.OpensWithoutPassword(unlocked));
        Assert.False(await _service.IsPasswordProtectedAsync(unlocked));
        Assert.Equal([1, 2, 3], PdfFactory.Markers(unlocked));
    }

    [Fact]
    public async Task Protect_with_the_simple_overload_also_asks_for_a_password()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var locked = _temp.At("qulflangan.pdf");

        await _service.ProtectPdfAsync(source, locked, "Maxfiy!42");

        Assert.False(PdfFactory.OpensWithoutPassword(locked));
        Assert.Equal([1, 2], PdfFactory.Markers(locked, "Maxfiy!42"));
    }

    [Fact]
    public async Task Unlock_with_the_wrong_password_says_the_password_is_wrong()
    {
        // "Shikastlangan hujjat" degan xabar foydalanuvchini butunlay noto'g'ri yo'lga solardi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var locked = _temp.At("qulflangan.pdf");
        await _service.ProtectPdfAsync(source, locked, "Parol123");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.UnlockPdfAsync(locked, _temp.At("natija.pdf"), "BoshqaParol"));

        Assert.Equal(PdfErrorKind.InvalidPassword, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Fact]
    public async Task Unlock_without_a_password_is_rejected_before_anything_is_read()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.UnlockPdfAsync(source, _temp.At("natija.pdf"), string.Empty));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task Protect_without_any_password_is_rejected()
    {
        // Parolsiz "himoyalash" hech qanday himoya bermaydi — jimgina o'tib ketmasligi kerak.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ProtectPdfAsync(source, _temp.At("natija.pdf"), new ProtectOptions()));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Fact]
    public async Task An_owner_only_password_still_opens_without_asking_anything()
    {
        // Faqat egalik paroli qo'yilgan hujjat cheklovlarga ega, lekin ochilishi uchun parol
        // so'ramaydi — foydalanuvchidan bekorga parol so'ralmasligi shuni tekshiradi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var locked = _temp.At("cheklangan.pdf");

        await _service.ProtectPdfAsync(source, locked, new ProtectOptions { OwnerPassword = "egasi" });

        Assert.False(await _service.IsPasswordProtectedAsync(locked));
        Assert.Equal([1, 2], PdfFactory.Markers(locked));
    }

    [Fact]
    public async Task A_plain_document_is_not_reported_as_password_protected()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        Assert.False(await _service.IsPasswordProtectedAsync(source));
    }

    [Fact]
    public async Task IsPasswordProtected_reports_a_missing_file_as_file_not_found()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.IsPasswordProtectedAsync(_temp.At("yo-q.pdf")));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public async Task IsPasswordProtected_reports_a_file_that_is_not_a_pdf_as_corrupted()
    {
        var fake = _temp.WriteFile("soxta.pdf", "men PDF emasman");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.IsPasswordProtectedAsync(fake));

        Assert.Equal(PdfErrorKind.CorruptedDocument, error.Kind);
    }

    // =================================================================================
    //  Suv belgisi
    // =================================================================================

    [Theory]
    [InlineData(WatermarkPosition.Center)]
    [InlineData(WatermarkPosition.TopLeft)]
    [InlineData(WatermarkPosition.BottomRight)]
    [InlineData(WatermarkPosition.Tiled)]
    public async Task Watermark_lands_on_every_page_without_changing_the_page_list(WatermarkPosition position)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);
        var output = _temp.At("belgili.pdf");

        await _service.AddWatermarkAsync(
            source,
            output,
            new WatermarkOptions { Text = "MAXFIY", Position = position });

        Assert.Equal([1, 2, 3], PdfFactory.Markers(output));

        // Belgi haqiqatan sahifa mazmuniga tushgani — "fayl yaratildi" dan kuchliroq dalil.
        for (var page = 1; page <= 3; page++)
            Assert.Contains("MAXFIY", PdfFactory.TextOf(output, page));
    }

    [Fact]
    public async Task Watermark_drawn_underneath_the_content_keeps_the_original_text()
    {
        // DrawOnTop = false rejimida suv belgisi mazmun ostiga chiziladi; sahifadagi eski
        // matn yo'qolib qolmasligi kerak.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var output = _temp.At("belgili.pdf");

        await _service.AddWatermarkAsync(
            source,
            output,
            new WatermarkOptions { Text = "NUSXA", DrawOnTop = false });

        var text = PdfFactory.TextOf(output, 1);
        Assert.Contains("NUSXA", text);
        Assert.Contains("Sahifa 1", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Watermark_rejects_empty_text(string text)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.AddWatermarkAsync(source, _temp.At("natija.pdf"), new WatermarkOptions { Text = text }));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Fact]
    public async Task Watermark_falls_back_to_another_font_when_the_requested_one_is_missing()
    {
        // Foydalanuvchi kompyuterida bo'lmagan shrift butun amalni to'xtatib qo'ymasligi kerak.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var output = _temp.At("belgili.pdf");

        await _service.AddWatermarkAsync(
            source,
            output,
            new WatermarkOptions { Text = "MAXFIY", FontFamily = "Bunday Shrift Yo'q 12345" });

        Assert.Contains("MAXFIY", PdfFactory.TextOf(output, 1));
    }

    [Fact]
    public async Task Watermark_reports_a_missing_file_as_file_not_found()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.AddWatermarkAsync(_temp.At("yo-q.pdf"), _temp.At("natija.pdf"), new WatermarkOptions()));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    // =================================================================================
    //  Sahifa raqamlari
    // =================================================================================

    [Fact]
    public async Task PageNumbers_numbers_every_page_from_one()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);
        var output = _temp.At("raqamli.pdf");

        await _service.AddPageNumbersAsync(source, output, new PageNumberOptions());

        Assert.Equal([1, 2, 3], PdfFactory.Markers(output));
        Assert.Contains("1", PdfFactory.TextOf(output, 1));
        Assert.Contains("3", PdfFactory.TextOf(output, 3));
    }

    [Fact]
    public async Task PageNumbers_can_show_the_total_as_well()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 4);
        var output = _temp.At("raqamli.pdf");

        await _service.AddPageNumbersAsync(source, output, new PageNumberOptions { Format = "{0} / {1}" });

        Assert.Contains("2 / 4", PdfFactory.TextOf(output, 2));
    }

    [Fact]
    public async Task PageNumbers_skips_the_cover_and_starts_counting_after_it()
    {
        // Muqova raqamlanmaydi, lekin raqamlash 1 dan boshlanadi — ya'ni ikkinchi sahifada "1"
        // turishi kerak, "2" emas.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);
        var output = _temp.At("raqamli.pdf");

        await _service.AddPageNumbersAsync(
            source,
            output,
            new PageNumberOptions { SkipFirstPages = 1, Format = "-{0}-" });

        Assert.Equal(3, PdfFactory.PageCount(output));
        Assert.DoesNotContain("-1-", PdfFactory.TextOf(output, 1));
        Assert.Contains("-1-", PdfFactory.TextOf(output, 2));
        Assert.Contains("-2-", PdfFactory.TextOf(output, 3));
    }

    [Fact]
    public async Task PageNumbers_can_start_from_a_number_other_than_one()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var output = _temp.At("raqamli.pdf");

        await _service.AddPageNumbersAsync(
            source,
            output,
            new PageNumberOptions { StartNumber = 7, Format = "-{0}-" });

        Assert.Contains("-7-", PdfFactory.TextOf(output, 1));
        Assert.Contains("-8-", PdfFactory.TextOf(output, 2));
    }

    [Fact]
    public async Task PageNumbers_rejects_a_negative_skip_count()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.AddPageNumbersAsync(
                source,
                _temp.At("natija.pdf"),
                new PageNumberOptions { SkipFirstPages = -1 }));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
    }

    [Fact]
    public async Task PageNumbers_rejects_a_template_with_a_placeholder_that_does_not_exist()
    {
        // "{5}" — foydalanuvchi yozishi mumkin bo'lgan xato. U ishlov berilmasa
        // FormatException butun amalni "noma'lum xato" qilib ko'rsatardi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.AddPageNumbersAsync(
                source,
                _temp.At("natija.pdf"),
                new PageNumberOptions { Format = "{5}" }));

        Assert.Equal(PdfErrorKind.InvalidOptions, error.Kind);
        Assert.False(File.Exists(_temp.At("natija.pdf")));
    }

    [Theory]
    [InlineData(PageNumberPosition.BottomCenter)]
    [InlineData(PageNumberPosition.BottomLeft)]
    [InlineData(PageNumberPosition.BottomRight)]
    [InlineData(PageNumberPosition.TopCenter)]
    [InlineData(PageNumberPosition.TopLeft)]
    [InlineData(PageNumberPosition.TopRight)]
    public async Task PageNumbers_work_in_every_position(PageNumberPosition position)
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var output = _temp.At("raqamli.pdf");

        await _service.AddPageNumbersAsync(source, output, new PageNumberOptions { Position = position, Format = "-{0}-" });

        Assert.Equal(1, PdfFactory.PageCount(output));
        Assert.Contains("-1-", PdfFactory.TextOf(output, 1));
    }

    // =================================================================================
    //  Siqish
    // =================================================================================

    [Theory]
    [InlineData(CompressionLevel.Low)]
    [InlineData(CompressionLevel.Medium)]
    [InlineData(CompressionLevel.High)]
    public async Task Compress_writes_a_readable_document_that_is_never_bigger(CompressionLevel level)
    {
        // Siqishning eng muhim va'dasi — natija hech qachon manbadan katta bo'lmasligi va
        // hujjat o'qilishda qolishi. Rasmsiz hujjatda yutuq bo'lmasligi mumkin, lekin
        // "siqilgan" deb kattaroq fayl berish — ochiq xatolik.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 3);
        var output = _temp.At("siqilgan.pdf");

        var result = await _service.CompressPdfAsync(source, output, level);

        Assert.True(File.Exists(output));
        Assert.Equal([1, 2, 3], PdfFactory.Markers(output));

        Assert.Equal(new FileInfo(source).Length, result.OriginalBytes);
        Assert.True(
            result.CompressedBytes <= result.OriginalBytes,
            $"yangi={result.CompressedBytes}, eski={result.OriginalBytes}");
        Assert.Equal(new FileInfo(output).Length, result.CompressedBytes);
        Assert.True(result.SavedPercent >= 0);
    }

    [Fact]
    public async Task Compress_finds_no_images_in_a_text_only_document()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var result = await _service.CompressPdfAsync(source, _temp.At("siqilgan.pdf"), CompressionLevel.High);

        Assert.Equal(0, result.ImagesProcessed);
    }

    [Fact]
    public async Task Compress_can_overwrite_the_file_it_read()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);

        await _service.CompressPdfAsync(source, source, CompressionLevel.Medium);

        Assert.Equal([1, 2], PdfFactory.Markers(source));
    }

    [Fact]
    public async Task Compress_reports_a_missing_file_as_file_not_found()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.CompressPdfAsync(_temp.At("yo-q.pdf"), _temp.At("natija.pdf"), CompressionLevel.Medium));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public async Task Compress_reports_a_file_that_is_not_a_pdf_as_a_corrupted_document()
    {
        var fake = _temp.WriteFile("soxta.pdf", "men PDF emasman");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.CompressPdfAsync(fake, _temp.At("natija.pdf"), CompressionLevel.Medium));

        Assert.Equal(PdfErrorKind.CorruptedDocument, error.Kind);
    }

    // =================================================================================
    //  Bekor qilish
    // =================================================================================

    [Fact]
    public async Task Cancelling_a_merge_leaves_no_output_behind()
    {
        var first = PdfFactory.Create(_temp.At("bir.pdf"), pageCount: 2);
        var second = PdfFactory.Create(_temp.At("ikki.pdf"), pageCount: 2, firstMarker: 11);
        var output = _temp.At("natija.pdf");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.MergePdfsAsync([first, second], output, null, cancellation.Token));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Cancelling_a_split_leaves_no_files_behind()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 5);
        var folder = _temp.CreateFolder("boluvchi");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.SplitPdfAsync(source, folder, SplitOptions.Default, null, cancellation.Token));

        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Cancelling_a_rotation_leaves_no_output_behind()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var output = _temp.At("natija.pdf");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.RotatePagesAsync(source, output, 90, null, null, cancellation.Token));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Cancelling_a_protect_leaves_no_output_behind()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var output = _temp.At("natija.pdf");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ProtectPdfAsync(source, output, "Parol123", null, cancellation.Token));

        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Cancelling_a_compression_leaves_no_output_behind()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var output = _temp.At("natija.pdf");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.CompressPdfAsync(source, output, CompressionLevel.Medium, null, cancellation.Token));

        Assert.False(File.Exists(output));
    }

    // =================================================================================
    //  Natija fayli tekshiruvi
    // =================================================================================

    [Fact]
    public async Task An_operation_without_an_output_path_is_rejected()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.RotatePagesAsync(source, string.Empty, 90));

        Assert.Equal(PdfErrorKind.OutputNotWritable, error.Kind);
    }

    [Fact]
    public async Task An_output_folder_that_does_not_exist_yet_is_created()
    {
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var output = _temp.At("hali/yo-q/natija.pdf");

        await _service.RotatePagesAsync(source, output, 90);

        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task A_result_file_that_another_program_holds_open_is_reported_as_not_writable()
    {
        // Eng ko'p uchraydigan haqiqiy holat: foydalanuvchi natija faylini PDF ko'ruvchida
        // ochiq qoldirgan. Bunda "noma'lum xato" emas, aynan "fayl band" xabari chiqishi kerak.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 1);
        var output = _temp.At("band.pdf");

        using (new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var error = await Assert.ThrowsAsync<PdfServiceException>(
                () => _service.RotatePagesAsync(source, output, 90));

            Assert.Equal(PdfErrorKind.OutputNotWritable, error.Kind);
        }

        // Muvaffaqiyatsiz urinishdan keyin ham vaqtinchalik fayl qolmasligi kerak.
        Assert.Empty(Directory.GetFiles(_temp.Root, "*.tmp-*"));
    }

    [Fact]
    public async Task No_operation_leaves_a_temporary_file_behind()
    {
        // Servis avval yonidagi ".tmp-..." fayliga yozadi. U tozalanmasa, foydalanuvchi
        // papkasi tushunarsiz fayllar bilan to'lib ketardi.
        var source = PdfFactory.Create(_temp.At("hujjat.pdf"), pageCount: 2);
        var folder = _temp.CreateFolder("natijalar");

        await _service.MergePdfsAsync([source], Path.Combine(folder, "birlashgan.pdf"));
        await _service.RotatePagesAsync(source, Path.Combine(folder, "burilgan.pdf"), 90);
        await _service.CompressPdfAsync(source, Path.Combine(folder, "siqilgan.pdf"), CompressionLevel.Medium);
        await _service.AddWatermarkAsync(source, Path.Combine(folder, "belgili.pdf"), new WatermarkOptions());
        await _service.AddPageNumbersAsync(source, Path.Combine(folder, "raqamli.pdf"), new PageNumberOptions());
        await _service.ProtectPdfAsync(source, Path.Combine(folder, "qulflangan.pdf"), "Parol123");

        Assert.All(
            Directory.GetFiles(folder),
            path => Assert.EndsWith(".pdf", path, StringComparison.OrdinalIgnoreCase));
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    /// <summary>
    /// <see cref="IProgress{T}"/> ning sinxron yozib boruvchi ko'rinishi. <c>Progress&lt;T&gt;</c>
    /// xabarlarni navbatga qo'yadi va sinovni kutishga majbur qilardi — bu esa deterministik emas.
    /// </summary>
    private sealed class ProgressRecorder : IProgress<int>
    {
        private readonly List<int> _values = [];

        public IReadOnlyList<int> Values
        {
            get
            {
                lock (_values)
                    return _values.ToList();
            }
        }

        public void Report(int value)
        {
            lock (_values)
                _values.Add(value);
        }
    }
}
