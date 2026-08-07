using System.IO;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Yordamchi.Models;

namespace Yordamchi.Services.Conversion;

// =====================================================================================
//  Microsoft Word (COM avtomatlashtirish) orqali .docx → .pdf eksporti.
//
//  Bu yerda Office'ning "Interop" NuGet paketi ATAYIN ishlatilmagan: paket loyihani
//  ma'lum bir Office versiyasiga bog'lab qo'yadi va foydalanuvchi kompyuterida Office
//  bo'lmasa ham DLL'ni olib yuradi. Buning o'rniga kech bog'lanish (late binding)
//  qo'llanadi: ProgID orqali tur topiladi, chaqiruvlar esa `dynamic` bilan ish vaqtida
//  bog'lanadi. Shu sababli loyihaga hech qanday yangi bog'liqlik qo'shilmaydi.
//
//  Ikki muhim nozik joy bor:
//   1. Word COM obyektlari STA (single threaded apartment) oqimini talab qiladi, chaqiruv
//      esa odatda Task.Run ichidan, ya'ni MTA oqimidan keladi. Shuning uchun butun ish
//      alohida STA oqimida bajariladi.
//   2. COM havolalari bo'shatilmasa, WINWORD.EXE jarayoni xotirada osilib qoladi va
//      keyingi eksportlar sekinlashadi. Shuning uchun tozalash `finally` blokida,
//      har biri alohida try/catch bilan bajariladi.
// =====================================================================================

/// <summary>Microsoft Word ilovasi orqali Word hujjatini PDF ga eksport qiladi.</summary>
public static class OfficeWordInterop
{
    /// <summary>Word ilovasining COM identifikatori.</summary>
    private const string WordProgId = "Word.Application";

    // Word'ning ExportAsFixedFormat metodi uchun sonli konstantalar (WdExportFormat va h.k.).
    private const int WdExportFormatPdf = 17;
    private const int WdExportOptimizeForPrint = 0;
    private const int WdExportAllDocument = 0;
    private const int WdExportDocumentContent = 0;
    private const int WdExportCreateHeadingBookmarks = 1;
    private const int WdExportCreateNoBookmarks = 0;
    private const int WdDoNotSaveChanges = 0;

    /// <summary>
    /// Tekshiruv natijasi keshlanadi: registr o'qish arzon bo'lsa ham, bu xossa
    /// interfeysdan (dvigatel tanlashda) tez-tez so'raladi.
    /// </summary>
    private static readonly Lazy<bool> AvailabilityProbe =
        new(ProbeWordInstallation, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Ushbu kompyuterda Microsoft Word o'rnatilganmi.</summary>
    public static bool IsAvailable => AvailabilityProbe.Value;

    /// <summary>
    /// Word hujjatini Microsoft Word yordamida PDF ga eksport qiladi.
    /// </summary>
    /// <param name="docxPath">Manba Word hujjati (.docx yoki .doc).</param>
    /// <param name="pdfPath">Yaratiladigan PDF fayl yo'li.</param>
    /// <param name="createBookmarks">Sarlavhalardan PDF xatcho'plari yasalsinmi.</param>
    /// <param name="cancellationToken">Bekor qilish belgisi.</param>
    public static void ExportToPdf(string docxPath, string pdfPath, bool createBookmarks, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(docxPath))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Manba Word hujjatining yo'li ko'rsatilmagan.");

        if (string.IsNullOrWhiteSpace(pdfPath))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Natijaviy PDF fayl yo'li ko'rsatilmagan.", docxPath);

        var fullSource = Path.GetFullPath(docxPath);
        var fullTarget = Path.GetFullPath(pdfPath);

        if (!File.Exists(fullSource))
            throw new PdfServiceException(PdfErrorKind.FileNotFound, $"'{Path.GetFileName(fullSource)}' fayli topilmadi.", fullSource);

        if (!IsAvailable)
        {
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                "Bu kompyuterda Microsoft Word o'rnatilmagan, shuning uchun Word dvigateli ishlatib bo'lmaydi. "
                + "Sozlamalarda ichki (Office talab qilmaydigan) dvigatelni tanlang.",
                fullSource);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var targetDirectory = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        RunInSingleThreadedApartment(() => ExportCore(fullSource, fullTarget, createBookmarks), fullSource);

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(fullTarget))
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Microsoft Word eksportni yakunlamadi: natijaviy PDF fayl yaratilmadi.",
                fullSource);
        }
    }

    /// <summary>ProgID ro'yxatdan o'tganmi — ya'ni Word o'rnatilganmi.</summary>
    private static bool ProbeWordInstallation()
    {
        try
        {
            return OperatingSystem.IsWindows() && Type.GetTypeFromProgID(WordProgId, throwOnError: false) is not null;
        }
        catch
        {
            // Registrga kirish taqiqlangan bo'lsa Word yo'q deb hisoblaymiz — bu xato emas.
            return false;
        }
    }

    /// <summary>
    /// Berilgan amalni STA oqimida bajaradi va u yerda yuzaga kelgan xatoni
    /// (stek ma'lumotini yo'qotmagan holda) chaqiruvchi oqimga uzatadi.
    /// </summary>
    private static void RunInSingleThreadedApartment(Action action, string sourcePath)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Yordamchi.WordInterop"
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Word bilan ishlash uchun STA oqimini yaratib bo'lmadi.",
                sourcePath,
                ex);
        }

        thread.Start();

        // Word chaqiruvi boshlanganidan keyin uni to'xtatib bo'lmaydi: yarim yo'lda tashlab
        // ketilsa WINWORD.EXE osilib qoladi. Shuning uchun oqim tozalashni tugatguncha kutamiz.
        thread.Join();

        failure?.Throw();
    }

    /// <summary>Word ilovasini ochib, hujjatni PDF ga eksport qiladigan asosiy qism.</summary>
    private static void ExportCore(string docxPath, string pdfPath, bool createBookmarks)
    {
        var wordType = Type.GetTypeFromProgID(WordProgId, throwOnError: false)
            ?? throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                "Microsoft Word COM komponenti topilmadi. Office o'rnatilganini tekshiring.",
                docxPath);

        dynamic? application = null;
        dynamic? documents = null;
        dynamic? document = null;

        try
        {
            try
            {
                application = Activator.CreateInstance(wordType)
                    ?? throw new PdfServiceException(
                        PdfErrorKind.MissingComponent,
                        "Microsoft Word ilovasini ishga tushirib bo'lmadi.",
                        docxPath);
            }
            catch (PdfServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PdfServiceException(
                    PdfErrorKind.MissingComponent,
                    $"Microsoft Word ilovasini ishga tushirib bo'lmadi: {ex.Message}",
                    docxPath,
                    ex);
            }

            // Foydalanuvchiga hech narsa ko'rinmasin va hech qanday dialog chiqmasin.
            TrySet(() => application.Visible = false);
            TrySet(() => application.DisplayAlerts = 0);
            TrySet(() => application.ScreenUpdating = false);
            TrySet(() => application.Options.UpdateLinksAtOpen = false);

            documents = application.Documents;

            document = documents.Open(
                FileName: docxPath,
                ConfirmConversions: false,
                ReadOnly: true,
                AddToRecentFiles: false,
                Revert: false,
                Visible: false,
                OpenAndRepair: false);

            if (document is null)
            {
                throw new PdfServiceException(
                    PdfErrorKind.CorruptedDocument,
                    $"Microsoft Word '{Path.GetFileName(docxPath)}' hujjatini ocha olmadi.",
                    docxPath);
            }

            document.ExportAsFixedFormat(
                pdfPath,
                WdExportFormatPdf,
                false,                       // eksportdan keyin ochilmasin
                WdExportOptimizeForPrint,
                WdExportAllDocument,
                0,                           // From
                0,                           // To
                WdExportDocumentContent,
                true,                        // hujjat xossalari qo'shilsin
                true,                        // IRM saqlansin
                createBookmarks ? WdExportCreateHeadingBookmarks : WdExportCreateNoBookmarks,
                true,                        // hujjat tuzilmasi teglari
                true,                        // yo'q shriftlar rasm sifatida
                false);                      // PDF/A emas
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"Microsoft Word orqali eksport qilishda xato yuz berdi: {ex.Message}",
                docxPath,
                ex);
        }
        finally
        {
            // Tozalash tartibi muhim: avval hujjat yopiladi, keyin ilova, so'ng havolalar bo'shatiladi.
            TrySet(() => document?.Close(WdDoNotSaveChanges));
            TrySet(() => application?.Quit(WdDoNotSaveChanges));

            ReleaseComObject(document);
            ReleaseComObject(documents);
            ReleaseComObject(application);

            // RCW'lar yig'ilmasa Word jarayoni tirik qoladi.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Word'ning ba'zi xossalari versiyaga qarab mavjud bo'lmasligi mumkin; bunday
    /// chaqiruvlar butun eksportni to'xtatmasligi kerak.
    /// </summary>
    private static void TrySet(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Ataylab jim: bu ikkinchi darajali sozlamalar va tozalash chaqiruvlari.
        }
    }

    /// <summary>COM havolasini oxirigacha bo'shatadi.</summary>
    private static void ReleaseComObject(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
                Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // Havola allaqachon bo'shatilgan bo'lishi mumkin — bu xato emas.
        }
    }
}
