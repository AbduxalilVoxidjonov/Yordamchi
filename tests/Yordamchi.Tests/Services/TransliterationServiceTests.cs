using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Yordamchi.Models;
using Yordamchi.Services;
using Yordamchi.Tests.TestSupport;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Yordamchi.Tests.Services;

/// <summary>
/// <see cref="TransliterationService"/> sinovlari. Word bilan ishlash haqiqiy <c>.docx</c>
/// fayllar ustida sinaladi: bu yerdagi qiymat aynan OpenXML bilan bo'lgan kelishuvda —
/// soxta hujjat ustidagi sinov "matn bir necha <c>w:t</c> ga bo'linib ketgan" degan eng
/// muhim holatni umuman tekshirmagan bo'lardi.
/// </summary>
public sealed class TransliterationServiceTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly TransliterationService _service = new();

    public void Dispose() => _temp.Dispose();

    private static readonly TransliterationOptions ToLatin = new()
    {
        Direction = TransliterationDirection.CyrillicToLatin
    };

    // =================================================================================
    //  Matn
    // =================================================================================

    [Fact]
    public void ConvertText_uses_the_direction_it_was_given()
        => Assert.Equal("O'zbekiston", _service.ConvertText("Ўзбекистон", ToLatin));

    [Fact]
    public void ConvertText_can_work_out_the_direction_on_its_own()
    {
        var auto = new TransliterationOptions { AutoDetectDirection = true };

        // Boshlang'ich yo'nalish kirill→lotin, lekin matn lotinda — teskarisi tanlanishi kerak.
        Assert.Equal("Ўзбекистон", _service.ConvertText("O'zbekiston", auto));
    }

    // =================================================================================
    //  Nomlash
    // =================================================================================

    [Fact]
    public void The_result_gets_a_name_that_says_where_it_went()
    {
        var source = _temp.WriteFile("hujjat.docx", "x");

        Assert.Equal(
            _temp.At("hujjat-lotin.docx"),
            _service.SuggestOutputPath(source, null, TransliterationDirection.CyrillicToLatin));

        Assert.Equal(
            _temp.At("hujjat-kirill.docx"),
            _service.SuggestOutputPath(source, null, TransliterationDirection.LatinToCyrillic));
    }

    [Fact]
    public void An_earlier_result_is_never_overwritten_silently()
    {
        var source = _temp.WriteFile("hujjat.docx", "x");
        _temp.WriteFile("hujjat-lotin.docx", "oldingi natija");

        var suggested = _service.SuggestOutputPath(source, null, TransliterationDirection.CyrillicToLatin);

        Assert.Equal(_temp.At("hujjat-lotin-2.docx"), suggested);
        Assert.Equal("oldingi natija", File.ReadAllText(_temp.At("hujjat-lotin.docx")));
    }

    // =================================================================================
    //  Matn fayli
    // =================================================================================

    [Fact]
    public async Task A_text_file_is_converted_into_a_new_file_next_to_it()
    {
        var source = _temp.WriteFile("xat.txt", "Ассалому алайкум, дунё!");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        Assert.Equal(_temp.At("xat-lotin.txt"), result.OutputPath);
        Assert.Equal("Assalomu alaykum, dunyo!", File.ReadAllText(result.OutputPath));

        // Manba faylga tegilmasligi shart.
        Assert.Equal("Ассалому алайкум, дунё!", File.ReadAllText(source));
    }

    [Fact]
    public async Task A_text_file_is_written_with_a_utf8_signature()
    {
        var source = _temp.WriteFile("xat.txt", "Салом");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);
        var bytes = File.ReadAllBytes(result.OutputPath);

        // BOM'siz Windows'dagi Bloknot kirill matnni noto'g'ri kodlashda ochadi.
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
    }

    [Fact]
    public async Task The_result_can_be_sent_to_a_folder_of_its_own()
    {
        var source = _temp.WriteFile("xat.txt", "Салом");
        var folder = _temp.At("natijalar");

        var result = await _service.ConvertFileAsync(source, folder, ToLatin);

        Assert.Equal(Path.Combine(folder, "xat-lotin.txt"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task A_text_file_that_is_not_utf8_is_reported_clearly()
    {
        // Windows-1251 dagi "Салом" — .NET Core bunday kodlashni o'zi bilmaydi.
        var source = _temp.At("eski.txt");
        File.WriteAllBytes(source, [0xD1, 0xE0, 0xEB, 0xEE, 0xEC]);

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ConvertFileAsync(source, null, ToLatin));

        Assert.Equal(PdfErrorKind.UnsupportedFormat, error.Kind);
        Assert.Contains("UTF-8", error.Message);
    }

    // =================================================================================
    //  Word hujjati
    // =================================================================================

    [Fact]
    public async Task A_word_document_keeps_its_formatting()
    {
        var source = CreateDocument("hujjat.docx");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        using var document = WordprocessingDocument.Open(result.OutputPath, false);
        var body = document.MainDocumentPart!.Document!.Body!;

        var paragraphs = body.Descendants<W.Paragraph>().Select(p => p.InnerText).ToList();

        Assert.Contains("O'zbekiston Respublikasi", paragraphs);
        Assert.Contains("to'g'ri bo'ldi", paragraphs);

        // Jadval ham, qalin shrift ham joyida qolishi kerak: hujjat qaytadan qurilmaydi.
        Assert.NotEmpty(body.Descendants<W.Table>());
        Assert.NotNull(body.Descendants<W.Run>().First().RunProperties?.Bold);
    }

    [Fact]
    public async Task A_word_paragraph_is_converted_as_a_whole()
    {
        // "Ўз" va "бекистон" alohida run'larda: har birini alohida o'girish "O'z" dan keyin
        // "Bekiston" ni yasab, so'z boshini ikki marta hisoblab yuborardi.
        var source = CreateDocument("hujjat.docx");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        using var document = WordprocessingDocument.Open(result.OutputPath, false);
        var runs = document.MainDocumentPart!.Document!.Body!
            .Descendants<W.Paragraph>().First()
            .Descendants<W.Run>().Select(run => run.InnerText).ToList();

        Assert.Equal(new[] { "O'z", "bekiston Respublikasi" }, runs);
    }

    [Fact]
    public async Task Word_field_codes_are_left_alone()
    {
        // Maydon kodi ko'rinadigan matn emas, Word uchun buyruq — o'girilsa avtomatik
        // mundarija va sana maydonlari ishlamay qoladi.
        var source = CreateDocument("hujjat.docx");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        using var document = WordprocessingDocument.Open(result.OutputPath, false);
        var field = document.MainDocumentPart!.Document!.Body!.Descendants<W.FieldCode>().Single();

        Assert.Equal(" PAGE \\* MERGEFORMAT ", field.Text);
    }

    [Fact]
    public async Task Headers_are_converted_together_with_the_body()
    {
        var source = CreateDocument("hujjat.docx");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        using var document = WordprocessingDocument.Open(result.OutputPath, false);
        var header = document.MainDocumentPart!.HeaderParts.Single().Header!;

        Assert.Equal("Sarlavha", header.InnerText);
    }

    [Fact]
    public async Task The_direction_of_a_document_can_be_worked_out_from_its_own_text()
    {
        var source = CreateDocument("hujjat.docx");

        var result = await _service.ConvertFileAsync(
            source,
            null,
            new TransliterationOptions { AutoDetectDirection = true });

        // Hujjat kirillda — natija lotinda bo'lishi va nomi ham shuni aytishi kerak.
        Assert.Equal(TransliterationDirection.CyrillicToLatin, result.Direction);
        Assert.Equal(_temp.At("hujjat-lotin.docx"), result.OutputPath);
    }

    [Fact]
    public async Task No_leftovers_are_kept_in_the_output_folder()
    {
        var source = CreateDocument("hujjat.docx");

        await _service.ConvertFileAsync(source, null, ToLatin);

        Assert.Empty(Directory.EnumerateFiles(_temp.Root, "*.tmp", SearchOption.AllDirectories));
    }

    // =================================================================================
    //  Rad etiladigan holatlar
    // =================================================================================

    [Fact]
    public async Task A_missing_file_is_reported_as_such()
    {
        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ConvertFileAsync(_temp.At("yo-q.docx"), null, ToLatin));

        Assert.Equal(PdfErrorKind.FileNotFound, error.Kind);
    }

    [Fact]
    public async Task The_old_doc_format_gets_an_answer_the_user_can_act_on()
    {
        var source = _temp.WriteFile("eski.doc", "x");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ConvertFileAsync(source, null, ToLatin));

        Assert.Equal(PdfErrorKind.UnsupportedFormat, error.Kind);
        Assert.Contains(".docx", error.Message);
    }

    [Fact]
    public async Task An_unrelated_file_type_is_refused()
    {
        var source = _temp.WriteFile("rasm.png", "x");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ConvertFileAsync(source, null, ToLatin));

        Assert.Equal(PdfErrorKind.UnsupportedFormat, error.Kind);
    }

    [Fact]
    public async Task A_file_that_only_pretends_to_be_a_document_is_reported_as_damaged()
    {
        var source = _temp.WriteFile("soxta.docx", "bu umuman docx emas");

        var error = await Assert.ThrowsAsync<PdfServiceException>(
            () => _service.ConvertFileAsync(source, null, ToLatin));

        Assert.Equal(PdfErrorKind.CorruptedDocument, error.Kind);

        // Shikastlangan fayldan keyin ham papka toza qolishi kerak.
        Assert.Empty(Directory.EnumerateFiles(_temp.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("hujjat.docx", true)]
    [InlineData("xat.TXT", true)]
    [InlineData("kitob.pdf", false)]
    [InlineData(null, false)]
    public void Only_word_and_text_files_are_accepted(string? path, bool supported)
        => Assert.Equal(supported, _service.IsSupported(path));

    [Fact]
    public async Task Spaces_between_runs_are_not_swallowed()
    {
        var source = CreateDocument("hujjat.docx");

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        using var document = WordprocessingDocument.Open(result.OutputPath, false);
        var paragraphs = document.MainDocumentPart!.Document!.Body!
            .Descendants<W.Paragraph>().Select(p => p.InnerText).ToList();

        Assert.Contains("salom dunyo", paragraphs);
    }

    [Fact]
    public async Task A_file_written_with_a_bom_is_read_correctly()
    {
        var source = _temp.At("bom.txt");
        File.WriteAllText(source, "Салом", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await _service.ConvertFileAsync(source, null, ToLatin);

        Assert.Equal("Salom", File.ReadAllText(result.OutputPath));
    }

    // =================================================================================
    //  Sinov uchun hujjat
    // =================================================================================

    /// <summary>
    /// Haqiqiy Word hujjatiga o'xshash namuna: bo'lingan run'lar, jadval, maydon kodi va
    /// kolontitul — o'girishda muammo tug'diradigan hamma narsa bir joyda.
    /// </summary>
    private string CreateDocument(string name)
    {
        var path = _temp.At(name);

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new W.Document();
        var body = main.Document.AppendChild(new W.Body());

        // Bitta so'z ikkita run'ga bo'lingan — Word'ning odatiy holati.
        var split = body.AppendChild(new W.Paragraph());
        split.AppendChild(new W.Run(new W.Text("Ўз")) { RunProperties = new W.RunProperties(new W.Bold()) });
        split.AppendChild(new W.Run(new W.Text("бекистон Республикаси")));

        // "ў" va "ғ" ikkita tugun chegarasida.
        var boundary = body.AppendChild(new W.Paragraph());
        boundary.AppendChild(new W.Run(new W.Text("тў")));
        boundary.AppendChild(new W.Run(new W.Text("ғри бўлди")));

        // Chetdagi bo'sh joy yo'qolmasligi kerak.
        var spaced = body.AppendChild(new W.Paragraph());
        spaced.AppendChild(new W.Run(new W.Text("салом ") { Space = SpaceProcessingModeValues.Preserve }));
        spaced.AppendChild(new W.Run(new W.Text("дунё")));

        var table = body.AppendChild(new W.Table());
        var row = table.AppendChild(new W.TableRow());
        var cell = row.AppendChild(new W.TableCell());
        cell.AppendChild(new W.Paragraph(new W.Run(new W.Text("Шаҳар"))));

        var field = body.AppendChild(new W.Paragraph());
        field.AppendChild(new W.Run(new W.FieldCode(" PAGE \\* MERGEFORMAT ")));
        field.AppendChild(new W.Run(new W.Text("бет")));

        var headerPart = main.AddNewPart<HeaderPart>();
        headerPart.Header = new W.Header(new W.Paragraph(new W.Run(new W.Text("Сарлавҳа"))));
        headerPart.Header.Save();

        main.Document.Save();

        return path;
    }

}
