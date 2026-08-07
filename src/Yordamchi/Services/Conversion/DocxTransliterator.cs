using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Yordamchi.Models;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Yordamchi.Services.Conversion;

/// <summary>
/// Word hujjatidagi matnni kirilldan lotinga (yoki aksincha) o'giradi.
/// <para>
/// Hujjat qaytadan qurilmaydi — mavjud fayl nusxasi ochilib, faqat <c>w:t</c> tugunlaridagi
/// matn almashtiriladi. Shu tufayli shrift, rang, jadval, rasm, sarlavha-koya, ro'yxatlar va
/// sahifa sozlamalari qanday bo'lsa shundayligicha qoladi.
/// </para>
/// <para>
/// Matn tugun bo'yicha emas, <b>abzas bo'yicha</b> o'giriladi. Word bitta so'zni bir necha
/// <c>w:t</c> ga bo'lib tashlashi odatiy hol (imlo tekshiruvi, tahrir izlari); tugunlarni
/// alohida o'girish esa <c>Ўз|бекистон</c> ni ikkita alohida so'zdek ko'rsatib, <c>Oʻz</c> va
/// <c>Bekiston</c> chiqarib yuborardi.
/// </para>
/// </summary>
public static class DocxTransliterator
{
    /// <summary>Yo'nalishni avtomatik aniqlash uchun yetarli namuna hajmi.</summary>
    private const int DetectionSampleLength = 4000;

    /// <summary>
    /// <paramref name="sourcePath"/> dagi hujjatning nusxasini <paramref name="workingPath"/> ga
    /// olib, o'sha nusxadagi matnni o'giradi. Manba faylga umuman tegilmaydi.
    /// <para>
    /// Natijaga yakuniy nom berish chaqiruvchining ishi: yo'nalish avtomatik aniqlanganda u
    /// faqat shu yerda, hujjat ochilgandan keyin ma'lum bo'ladi — ya'ni nomni oldindan tanlab
    /// bo'lmaydi ("-lotin" o'rniga "-kirill" bo'lib chiqishi mumkin).
    /// </para>
    /// </summary>
    /// <returns>Amalda qo'llangan yo'nalish va o'girilgan belgilar soni.</returns>
    /// <exception cref="PdfServiceException"/>
    public static (TransliterationDirection Direction, int Characters) Convert(
        string sourcePath,
        string workingPath,
        TransliterationOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            File.Copy(sourcePath, workingPath, overwrite: true);

            return ConvertInPlace(workingPath, options, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"'{Path.GetFileName(sourcePath)}' fayli topilmadi.",
                sourcePath,
                ex);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{Path.GetFileName(sourcePath)}' hujjatini o'girib yozib bo'lmadi. "
                + "Fayl boshqa dasturda ochiq yoki papkaga yozish taqiqlangan bo'lishi mumkin.",
                sourcePath,
                ex);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException)
        {
            throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                $"'{Path.GetFileName(sourcePath)}' haqiqiy Word hujjati emas yoki shikastlangan.",
                sourcePath,
                ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"Word hujjatini o'girishda xatolik yuz berdi: {ex.Message}",
                sourcePath,
                ex);
        }
    }

    private static (TransliterationDirection Direction, int Characters) ConvertInPlace(
        string path,
        TransliterationOptions options,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: true);

        var main = document.MainDocumentPart
            ?? throw new PdfServiceException(
                PdfErrorKind.CorruptedDocument,
                "Hujjatning asosiy qismi topilmadi — fayl shikastlangan bo'lishi mumkin.",
                path);

        var roots = CollectRoots(main);
        var groups = roots.SelectMany(CollectParagraphGroups).ToList();

        var resolved = options.AutoDetectDirection
            ? UzbekTransliterator.Resolve(options, BuildSample(groups))
            : options;

        var characters = 0;
        var done = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            characters += ConvertGroup(group, resolved);
            done++;

            // Har bir abzasda xabar berish UI ni behuda bezovta qiladi.
            if (done % 25 == 0 || done == groups.Count)
                progress?.Report(new PdfProgress(done, groups.Count, "Matn o'girilmoqda…"));
        }

        foreach (var root in roots)
        {
            if (root is OpenXmlPartRootElement saveable)
                saveable.Save();
        }

        return (resolved.Direction, characters);
    }

    // =================================================================================
    //  Hujjatning matn saqlaydigan barcha qismlari
    // =================================================================================

    /// <summary>
    /// Asosiy tana, kolontitullar, izohlar va sharhlar. Maydon kodlari (<c>w:instrText</c>)
    /// ataylab tashqarida: ular ko'rinadigan matn emas, Word uchun buyruq — o'girilsa
    /// avtomatik mundarija va sana maydonlari ishlamay qoladi.
    /// </summary>
    private static List<OpenXmlElement> CollectRoots(MainDocumentPart main)
    {
        var roots = new List<OpenXmlElement>();

        if (main.Document is not null)
            roots.Add(main.Document);

        foreach (var header in main.HeaderParts)
        {
            if (header.Header is not null)
                roots.Add(header.Header);
        }

        foreach (var footer in main.FooterParts)
        {
            if (footer.Footer is not null)
                roots.Add(footer.Footer);
        }

        if (main.FootnotesPart?.Footnotes is not null)
            roots.Add(main.FootnotesPart.Footnotes);

        if (main.EndnotesPart?.Endnotes is not null)
            roots.Add(main.EndnotesPart.Endnotes);

        if (main.WordprocessingCommentsPart?.Comments is not null)
            roots.Add(main.WordprocessingCommentsPart.Comments);

        return roots;
    }

    /// <summary>
    /// Matn tugunlarini abzaslar bo'yicha guruhlaydi. Shakl (<c>SmartArt</c>, matn ramkasi)
    /// ichidagi <c>a:t</c> tugunlari ham qamrab olinadi — ular ham foydalanuvchi ko'radigan matn.
    /// </summary>
    private static List<List<OpenXmlLeafTextElement>> CollectParagraphGroups(OpenXmlElement root)
    {
        var buckets = new Dictionary<OpenXmlElement, List<OpenXmlLeafTextElement>>();
        var order = new List<List<OpenXmlLeafTextElement>>();

        void Add(OpenXmlLeafTextElement node)
        {
            var key = FindParagraph(node) ?? root;

            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets[key] = bucket;
                order.Add(bucket);
            }

            bucket.Add(node);
        }

        foreach (var node in root.Descendants<W.Text>())
            Add(node);

        foreach (var node in root.Descendants<A.Text>())
            Add(node);

        return order;
    }

    private static OpenXmlElement? FindParagraph(OpenXmlElement node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is W.Paragraph or A.Paragraph)
                return current;
        }

        return null;
    }

    private static string BuildSample(List<List<OpenXmlLeafTextElement>> groups)
    {
        var builder = new StringBuilder(DetectionSampleLength);

        foreach (var group in groups)
        {
            foreach (var node in group)
            {
                builder.Append(node.Text);

                if (builder.Length >= DetectionSampleLength)
                    return builder.ToString();
            }

            builder.Append(' ');
        }

        return builder.ToString();
    }

    // =================================================================================
    //  Bitta abzas
    // =================================================================================

    /// <summary>
    /// Abzas matnini yaxlit o'giradi va natijani manba tugunlari bo'yicha qaytadan tarqatadi.
    /// Har bir bo'lak <b>o'zi boshlangan</b> tugunga tushadi, shuning uchun ikki tugun chegarasida
    /// turgan <c>o'</c> yoki <c>ye</c> ikkiga bo'linib ketmaydi.
    /// </summary>
    /// <returns>O'girilgan belgilar soni.</returns>
    private static int ConvertGroup(List<OpenXmlLeafTextElement> nodes, TransliterationOptions options)
    {
        if (nodes.Count == 0)
            return 0;

        var offsets = new int[nodes.Count];
        var combined = new StringBuilder();

        for (var i = 0; i < nodes.Count; i++)
        {
            offsets[i] = combined.Length;
            combined.Append(nodes[i].Text);
        }

        if (combined.Length == 0)
            return 0;

        var source = combined.ToString();
        var buffers = new StringBuilder[nodes.Count];

        for (var i = 0; i < buffers.Length; i++)
            buffers[i] = new StringBuilder();

        // Bo'laklar manba tartibida keladi, shuning uchun tugunni qidirish o'rniga
        // oddiy kursor yetarli.
        var cursor = 0;

        UzbekTransliterator.Convert(source, options, (start, _, output) =>
        {
            while (cursor + 1 < offsets.Length && start >= offsets[cursor + 1])
                cursor++;

            buffers[cursor].Append(output);
        });

        for (var i = 0; i < nodes.Count; i++)
            SetText(nodes[i], buffers[i].ToString());

        return source.Length;
    }

    private static void SetText(OpenXmlLeafTextElement node, string text)
    {
        node.Text = text;

        // xml:space="preserve" bo'lmasa Word chetdagi bo'sh joylarni yo'q qiladi va so'zlar
        // bir-biriga yopishib ketadi.
        if (node is W.Text wordText && text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
            wordText.Space = SpaceProcessingModeValues.Preserve;
    }
}
