using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Yordamchi.Tests.TestSupport;

/// <summary>
/// Sinovlar uchun haqiqiy PDF fayllar yaratadi va natijani qayta o'qiydi.
/// <para>
/// Har bir sahifa o'zining <i>belgisi</i> (marker) bilan yaratiladi: belgi ham sahifadagi
/// matnga yoziladi, ham sahifa kengligiga kodlanadi. Kenglik — MediaBox ning bir qismi,
/// ya'ni u birlashtirish, bo'lish, burish va parol qo'yishdan keyin ham o'zgarmaydi.
/// Shu tufayli sinovlar "5 ta sahifa chiqdi" bilan cheklanmay, <b>aynan qaysi sahifa qayerga
/// tushganini</b> ham tekshira oladi — sahifalar tartibi buzilishi esa birlashtirish va
/// bo'lishdagi eng qimmat xatolik.
/// </para>
/// </summary>
public static class PdfFactory
{
    /// <summary>Belgisi 0 bo'lgan sahifaning kengligi (punkt).</summary>
    private const double BaseWidthPoints = 200d;

    /// <summary>Ketma-ket belgilar orasidagi kenglik farqi (punkt).</summary>
    private const double MarkerStepPoints = 2d;

    /// <summary>Barcha sahifalar bir xil balandlikda va portret holatda bo'ladi.</summary>
    private const double PageHeightPoints = 700d;

    private static int _fontSetupDone;

    // =================================================================================
    //  Yaratish
    // =================================================================================

    /// <summary>
    /// <paramref name="pageCount"/> ta sahifali PDF yozadi. Sahifalar belgilari
    /// <paramref name="firstMarker"/> dan boshlab ketma-ket oshib boradi — turli fayllarga
    /// turli boshlang'ich qiymat berilsa, birlashtirilgan hujjatda qaysi sahifa qaysi fayldan
    /// kelgani aniq ko'rinadi.
    /// </summary>
    public static string Create(string path, int pageCount, int firstMarker = 1)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        EnsureFontsAvailable();

        using var document = new PdfDocument();
        var font = new XFont("Arial", 14, XFontStyleEx.Regular);

        for (var i = 0; i < pageCount; i++)
        {
            var marker = firstMarker + i;
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(WidthOf(marker));
            page.Height = XUnit.FromPoint(PageHeightPoints);

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString(
                $"Sahifa {marker}",
                font,
                XBrushes.Black,
                new XRect(0, 0, page.Width.Point, page.Height.Point),
                XStringFormats.Center);
        }

        document.Save(path);
        return path;
    }

    // =================================================================================
    //  Qayta o'qish
    // =================================================================================

    /// <summary>Hujjatdagi sahifalar soni.</summary>
    public static int PageCount(string path, string? password = null)
    {
        using var document = Open(path, password);
        return document.PageCount;
    }

    /// <summary>Sahifalarning belgilari hujjatdagi tartibda.</summary>
    public static IReadOnlyList<int> Markers(string path, string? password = null)
    {
        using var document = Open(path, password);

        var markers = new List<int>(document.PageCount);
        for (var i = 0; i < document.PageCount; i++)
            markers.Add(MarkerOf(document.Pages[i].Width.Point));

        return markers;
    }

    /// <summary>Sahifalarning <c>/Rotate</c> qiymatlari hujjatdagi tartibda.</summary>
    public static IReadOnlyList<int> Rotations(string path, string? password = null)
    {
        using var document = Open(path, password);

        var rotations = new List<int>(document.PageCount);
        for (var i = 0; i < document.PageCount; i++)
            rotations.Add(document.Pages[i].Rotate);

        return rotations;
    }

    /// <summary>Hujjat parolsiz ochiladimi — "parol haqiqatan qo'yildimi" ni tekshirish uchun.</summary>
    public static bool OpensWithoutPassword(string path)
    {
        try
        {
            using var document = Open(path);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Sahifadagi matn (PdfPig orqali). Suv belgisi va sahifa raqamlari uchun "fayl yaratildi"
    /// dan ko'ra kuchliroq dalil: matn haqiqatan sahifa mazmuniga tushganini ko'rsatadi.
    /// </summary>
    public static string TextOf(string path, int pageNumber)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(
            File.ReadAllBytes(path),
            new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });

        return document.GetPage(pageNumber).Text;
    }

    // =================================================================================
    //  Ichki yordamchilar
    // =================================================================================

    private static PdfDocument Open(string path, string? password = null)
    {
        // Fayl handle ushlab qolinmasin: sinovlar ko'pincha o'sha faylning ustiga yozadi.
        var stream = new MemoryStream(File.ReadAllBytes(path), writable: false);

        return password is null
            ? PdfReader.Open(stream, PdfDocumentOpenMode.Import)
            : PdfReader.Open(stream, password, PdfDocumentOpenMode.Import);
    }

    private static double WidthOf(int marker) => BaseWidthPoints + (marker * MarkerStepPoints);

    private static int MarkerOf(double widthPoints)
        => (int)Math.Round((widthPoints - BaseWidthPoints) / MarkerStepPoints);

    /// <summary>
    /// PDFsharp 6 da matn chizishdan oldin shrift manbasi ochiq bo'lishi kerak; aks holda
    /// birinchi <see cref="XFont"/> istisno tashlaydi.
    /// </summary>
    private static void EnsureFontsAvailable()
    {
        if (Interlocked.Exchange(ref _fontSetupDone, 1) != 0)
            return;

        try
        {
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Sozlama allaqachon qulflangan — XFont yaratish baribir ishlaydi.
        }
    }
}
