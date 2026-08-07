using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Yordamchi.Helpers;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using SkiaSharp;

namespace Yordamchi.Services;

/// <summary>
/// Rasm fonini <c>u2net</c> (yoki yengilroq <c>u2netp</c>) segmentatsiya modeli yordamida
/// olib tashlaydi. Hisob-kitob ONNX Runtime (CPU) ustida bajariladi, rasm bilan ishlash esa
/// SkiaSharp orqali.
///
/// <para><b>MODEL FAYLI QAYERDAN OLINADI</b></para>
/// <para>
/// Model dastur bilan birga tarqatilmaydi (hajmi katta: <c>u2net.onnx</c> ~168 MB,
/// <c>u2netp.onnx</c> ~4,7 MB). Uni bir marta yuklab olib, quyidagi papkalardan biriga
/// joylashtirish kerak.
/// </para>
/// <para>
/// Yuklab olish manbalari:
/// <list type="bullet">
///   <item><description>
///     <c>https://github.com/danielgatis/rembg</c> — loyihaning "Releases" bo'limida
///     <c>u2net.onnx</c> va <c>u2netp.onnx</c> fayllari bor (MIT litsenziyasi).
///   </description></item>
///   <item><description>
///     Bevosita havola:
///     <c>https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx</c>
///     yoki yengil variant uchun
///     <c>https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx</c>.
///   </description></item>
///   <item><description>
///     Muqobil manba — asl loyiha: <c>https://github.com/xuebinqin/U-2-Net</c>.
///   </description></item>
/// </list>
/// </para>
/// <para><b>MODEL FAYLI QAYERGA QO'YILADI</b></para>
/// <para>
/// Fayl quyidagi tartibda qidiriladi, birinchi topilgani ishlatiladi:
/// <list type="number">
///   <item><description><c>&lt;dastur papkasi&gt;\Models\u2net.onnx</c></description></item>
///   <item><description><c>&lt;dastur papkasi&gt;\Models\u2netp.onnx</c></description></item>
///   <item><description><c>%LOCALAPPDATA%\Yordamchi\Models\u2net.onnx</c></description></item>
///   <item><description><c>%LOCALAPPDATA%\Yordamchi\Models\u2netp.onnx</c></description></item>
/// </list>
/// Hech biri topilmasa <see cref="ModelPath"/> uchinchi variantni — ya'ni foydalanuvchi
/// yozish huquqiga ega bo'lgan <c>%LOCALAPPDATA%\Yordamchi\Models\u2net.onnx</c> yo'lini —
/// qaytaradi va xato xabarida aynan shu yo'l ko'rsatiladi.
/// </para>
/// <para>
/// <b>Eslatma:</b> <c>u2netp</c> — <c>u2net</c> ning kichraytirilgan varianti. Tezroq ishlaydi
/// va kam joy egallaydi, lekin sochlar kabi nozik chekkalarda aniqligi pastroq.
/// </para>
/// </summary>
public sealed class OnnxBackgroundRemover : IImageBackgroundRemover, IDisposable
{
    /// <summary>Model fayllarining qidiriladigan nomlari (aniqroq varianti birinchi).</summary>
    private static readonly string[] ModelFileNames = ["u2net.onnx", "u2netp.onnx"];

    /// <summary>Model topilmaganda ko'rsatiladigan "kutilayotgan" yo'l uchun papka.</summary>
    private static readonly string UserModelsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yordamchi",
        "Models");

    /// <summary>Dastur bilan birga keladigan model papkasi.</summary>
    private static readonly string AppModelsDirectory =
        Path.Combine(AppContext.BaseDirectory, "Models");

    /// <summary>
    /// Dastur ichidan yuklab olinadigan model. Ataylab to'liq <c>u2net</c> tanlangan:
    /// yengil <c>u2netp</c> chekkalarni (ayniqsa soch va mo'yna) sezilarli yomonroq ajratadi.
    /// Sekin ulanish uchun <c>u2netp.onnx</c> ni qo'lda joylashtirish imkoni saqlanib qoladi.
    /// </summary>
    private const string ModelDownloadUrl =
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx";

    /// <summary>Model ~168 MB, shuning uchun OCR til fayllariga qaraganda ancha uzoq muhlat.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Butun jarayon uchun bitta <see cref="HttpClient"/>: har safar yangisini yaratish
    /// soketlarni tugatib qo'yadi (socket exhaustion).
    /// </summary>
    private static readonly Lazy<HttpClient> SharedHttpClient =
        new(CreateHttpClient, LazyThreadSafetyMode.ExecutionAndPublication);

    // -------------------------------------------------------------------------------------
    //  u2net normalizatsiyasi. Model ImageNet o'rtacha qiymatlari bilan o'rgatilgan:
    //      value = (piksel / 255 - mean) / std
    //  Ba'zi u2net eksportlarida (masalan rembg ning eski skriptlarida) soddaroq
    //  "piksel / max(piksel)" normalizatsiyasi uchraydi. Amalda ikkala variant ham ishlaydi,
    //  chunki maska baribir min/max bo'yicha qayta normallashtiriladi; biz standart
    //  (ImageNet) variantini qo'llaymiz — u rasmiy U-2-Net kodiga mos keladi.
    // -------------------------------------------------------------------------------------
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] Std = [0.229f, 0.224f, 0.225f];

    /// <summary>Kichraytirish uchun yuqori sifatli namuna olish.</summary>
    private static readonly SKSamplingOptions DownscaleSampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Maskani kattalashtirish uchun Mitchell kubik filtri — chekka pog'onali bo'lmaydi.</summary>
    private static readonly SKSamplingOptions MaskUpscaleSampling =
        new(SKCubicResampler.Mitchell);

    /// <summary>
    /// <see cref="InferenceSession"/> thread-safe emas, shuningdek uni yaratish sekin
    /// (u2net uchun bir necha soniya). Shu sababli sessiya birinchi chaqiruvda yaratiladi,
    /// keyin qayta ishlatiladi va bu semafor bilan himoyalanadi.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private InferenceSession? _session;

    /// <summary>Sessiya qaysi model fayli uchun ochilganini eslab qolamiz.</summary>
    private string? _sessionModelPath;

    private bool _disposed;

    /// <summary>Parametrsiz konstruktor — DI konteyneri shu tarzda yaratadi.</summary>
    public OnnxBackgroundRemover()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// Har chaqiruvda qaytadan qidiriladi: foydalanuvchi modelni dastur ishlab turgan paytda
    /// yuklab olsa ham, dasturni qayta ishga tushirmasdan ishlaydi.
    /// </remarks>
    public string ModelPath => ResolveModelPath();

    /// <inheritdoc />
    public bool IsModelAvailable => File.Exists(ModelPath);

    /// <inheritdoc />
    public string DownloadableModelName => ModelFileNames[0];

    /// <inheritdoc />
    /// <remarks>
    /// Windows fayl hajmini MiB da ko'rsatadi, shuning uchun bu yerda ham o'sha o'lchov:
    /// 175 997 641 bayt — Explorer'da "168 MB". Yuklash paytidagi foiz matni ham shu birlikda.
    /// </remarks>
    public string DownloadableModelSizeText => "~168 MB";

    // =====================================================================================
    //  Ommaviy API
    // =====================================================================================

    /// <inheritdoc />
    public async Task<BitmapSource> RemoveBackgroundAsync(
        string inputImagePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var bitmap = await RemoveBackgroundToBitmapAsync(
            inputImagePath,
            BackgroundRemovalOptions.Default,
            progress,
            cancellationToken).ConfigureAwait(false);

        try
        {
            // Natija UI oqimida ishlatiladi, shuning uchun muzlatilgan (frozen) bo'lishi shart.
            return ToFrozenAlphaBitmap(bitmap);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <inheritdoc />
    public Task<SKBitmap> RemoveBackgroundToBitmapAsync(
        string inputImagePath,
        BackgroundRemovalOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(inputImagePath))
        {
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                "Manba rasm fayli ko'rsatilmagan.");
        }

        var effective = options ?? BackgroundRemovalOptions.Default;

        // Butun hisob-kitob (dekodlash, model, maska) fon oqimida bajariladi.
        return Task.Run(
            () => RemoveBackgroundCore(inputImagePath, effective, progress, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsPngAsync(
        SKBitmap image,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new PdfServiceException(
                PdfErrorKind.InvalidOptions,
                "Saqlash uchun fayl yo'li ko'rsatilmagan.");
        }

        return Task.Run(() => SavePngCore(image, outputPath, cancellationToken), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> DownloadModelAsync(
        IProgress<PdfProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Model dastur papkasida yoki foydalanuvchi papkasida allaqachon bo'lishi mumkin —
        // 168 MB ni qaytadan tortib olishning ma'nosi yo'q.
        var existing = ResolveModelPath();
        if (SafeFileExists(existing))
            return existing;

        try
        {
            Directory.CreateDirectory(UserModelsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Model uchun papka yaratilmadi: {UserModelsDirectory}. Papkaga yozish huquqini tekshiring.",
                UserModelsDirectory,
                ex);
        }

        var target = Path.Combine(UserModelsDirectory, ModelFileNames[0]);
        await DownloadModelCoreAsync(target, progress, cancellationToken).ConfigureAwait(false);

        return target;
    }

    /// <summary>ONNX sessiyasini va semaforni bo'shatadi.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _session?.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Sessiyani yopishdagi xato foydalanuvchiga hech narsa bermaydi — e'tiborsiz qoldiramiz.
        }

        _session = null;
        _sessionModelPath = null;
        _gate.Dispose();
    }

    // =====================================================================================
    //  Model faylini topish
    // =====================================================================================

    /// <summary>
    /// Model faylini belgilangan tartibda qidiradi. Hech biri topilmasa — foydalanuvchi
    /// papkasidagi <c>u2net.onnx</c> yo'lini (kutilayotgan joyni) qaytaradi.
    /// </summary>
    private static string ResolveModelPath()
    {
        foreach (var directory in new[] { AppModelsDirectory, UserModelsDirectory })
        {
            foreach (var fileName in ModelFileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (SafeFileExists(candidate))
                    return candidate;
            }
        }

        // Hech narsa topilmadi — foydalanuvchi yozish huquqiga ega bo'lgan joyni ko'rsatamiz.
        return Path.Combine(UserModelsDirectory, ModelFileNames[0]);
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Model topilmaganda tashlanadigan, aniq yo'riqnomali xato.</summary>
    private static PdfServiceException ModelMissing(string modelPath) => new(
        PdfErrorKind.MissingComponent,
        "AI modeli topilmadi. Uni sahifadagi yoki \"Dastur haqida\" bo'limidagi " +
        "\"Yuklab olish\" tugmasi orqali oling." + Environment.NewLine + Environment.NewLine +
        "Yoki 'u2net.onnx' faylini quyidagi papkaga o'zingiz joylashtiring:" +
        Environment.NewLine + modelPath + Environment.NewLine + Environment.NewLine +
        "Manba: https://github.com/danielgatis/rembg — yengilroq variant sifatida " +
        "'u2netp.onnx' ham qabul qilinadi.",
        modelPath);

    // =====================================================================================
    //  Modelni yuklab olish
    // =====================================================================================

    /// <summary>
    /// Modelni oqim orqali yuklaydi va foizni xabar qiladi. Fayl katta bo'lgani uchun
    /// <c>CopyToAsync</c> emas, qo'lda o'qish halqasi ishlatiladi — aks holda progress-bar
    /// yuklash tugagunicha qimirlamasdi.
    /// </summary>
    private static async Task DownloadModelCoreAsync(
        string targetPath,
        IProgress<PdfProgress>? progress,
        CancellationToken cancellationToken)
    {
        var tempPath = targetPath + ".tmp";

        try
        {
            progress?.Report(new PdfProgress(0, 100, "AI modeli yuklanmoqda…"));

            using var response = await SharedHttpClient.Value
                .GetAsync(ModelDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new PdfServiceException(
                    PdfErrorKind.MissingComponent,
                    $"Model serverdan olinmadi (HTTP {(int)response.StatusCode}). Keyinroq qaytadan urinib ko'ring " +
                    "yoki faylni qo'lda joylashtiring.",
                    targetPath);
            }

            // Server hajmni bermasligi mumkin (chunked) — u holda foiz o'rniga aylanma ko'rsatkich qoladi.
            var totalBytes = response.Content.Headers.ContentLength;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                var lastReportedPercent = -1;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;

                    if (progress is null || totalBytes is not > 0)
                        continue;

                    // Har baytda emas, faqat foiz o'zgarganda xabar beramiz: aks holda UI oqimi
                    // minglab yangilanish bilan ko'miladi.
                    var percent = (int)(received * 100 / totalBytes.Value);
                    if (percent == lastReportedPercent)
                        continue;

                    lastReportedPercent = percent;
                    progress.Report(new PdfProgress(
                        percent, 100, $"AI modeli yuklanmoqda… {FormatMegabytes(received)} / {FormatMegabytes(totalBytes.Value)}"));
                }
            }

            File.Move(tempPath, targetPath, overwrite: true);
            progress?.Report(new PdfProgress(100, 100, "AI modeli tayyor"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (PdfServiceException)
        {
            SafeDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tempPath);
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                "AI modelini yuklab bo'lmadi — internetga ulanishni tekshiring va qaytadan urinib ko'ring.",
                targetPath,
                ex);
        }
    }

    private static string FormatMegabytes(long bytes) =>
        $"{bytes / (1024d * 1024d):0.#} MB";

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Yarim yuklangan .tmp qolib ketsa ham ish davom etadi: u model sifatida qidirilmaydi.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = DownloadTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Yordamchi/2.0");
        return client;
    }

    // =====================================================================================
    //  Sessiya
    // =====================================================================================

    /// <summary>
    /// Sessiyani (kerak bo'lsa) yaratadi. Chaqiruvchi <see cref="_gate"/> ni ushlab turgan
    /// bo'lishi shart.
    /// </summary>
    private InferenceSession GetOrCreateSession(string modelPath)
    {
        if (_session is not null &&
            string.Equals(_sessionModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
        {
            return _session;
        }

        // Model fayli almashtirilgan bo'lsa — eskisini yopamiz.
        _session?.Dispose();
        _session = null;
        _sessionModelPath = null;

        try
        {
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Min(4, Environment.ProcessorCount)
            };

            _session = new InferenceSession(modelPath, sessionOptions);
            _sessionModelPath = modelPath;
            return _session;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                "AI modelini yuklab bo'lmadi. Fayl buzilgan yoki to'liq yuklab olinmagan bo'lishi " +
                "mumkin. Uni o'chirib, qaytadan yuklab oling:" + Environment.NewLine + modelPath,
                modelPath,
                ex);
        }
    }

    // =====================================================================================
    //  Asosiy algoritm
    // =====================================================================================

    private SKBitmap RemoveBackgroundCore(
        string inputImagePath,
        BackgroundRemovalOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!SafeFileExists(inputImagePath))
        {
            throw new PdfServiceException(
                PdfErrorKind.FileNotFound,
                $"Rasm fayli topilmadi: {inputImagePath}",
                inputImagePath);
        }

        var modelPath = ResolveModelPath();
        if (!SafeFileExists(modelPath))
            throw ModelMissing(modelPath);

        // 1-bosqich — rasmni o'qish (EXIF burilishi hisobga olinadi).
        progress?.Report(5);

        SKBitmap? source = null;
        SKBitmap? sourceRgba = null;

        try
        {
            try
            {
                source = SkiaImageHelper.DecodeOriented(inputImagePath);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                throw new PdfServiceException(
                    PdfErrorKind.UnsupportedImage,
                    $"'{Path.GetFileName(inputImagePath)}' faylini rasm sifatida o'qib bo'lmadi. " +
                    "JPG, PNG, BMP, WEBP yoki TIFF formatidagi rasmni tanlang.",
                    inputImagePath,
                    ex);
            }

            if (source.Width <= 0 || source.Height <= 0)
            {
                throw new PdfServiceException(
                    PdfErrorKind.UnsupportedImage,
                    $"'{Path.GetFileName(inputImagePath)}' rasmining o'lchami noto'g'ri.",
                    inputImagePath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var originalWidth = source.Width;
            var originalHeight = source.Height;

            // 2-bosqich — model uchun kirish tenzorini tayyorlash.
            progress?.Report(20);

            var inputSize = NormalizeInputSize(options.ModelInputSize);
            var inputTensor = BuildInputTensor(source, inputSize, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // 3-bosqich — modelni ishga tushirish (sessiya semafor bilan himoyalangan).
            progress?.Report(60);

            var (rawMask, maskWidth, maskHeight) = RunModel(modelPath, inputTensor, inputSize, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // 4-bosqich — maskani normallashtirish, kattalashtirish va yumshatish.
            progress?.Report(85);

            var mask8 = NormalizeMask(rawMask, maskWidth * maskHeight);
            var mask = ResizeMask(mask8, maskWidth, maskHeight, originalWidth, originalHeight);

            if (options.FeatherRadius > 0f)
                mask = Feather(mask, originalWidth, originalHeight, options.FeatherRadius);

            if (options.AlphaThreshold > 0)
            {
                var threshold = options.AlphaThreshold;
                for (var i = 0; i < mask.Length; i++)
                {
                    if (mask[i] < threshold)
                        mask[i] = 0;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Kesish chegarasi (kerak bo'lsa) maska bo'yicha aniqlanadi.
            var crop = options.TrimTransparentBorder
                ? FindOpaqueBounds(mask, originalWidth, originalHeight)
                : new SKRectI(0, 0, originalWidth, originalHeight);

            // 5-bosqich — RGB originaldan, ALPHA maskadan.
            sourceRgba = ToRgbaUnpremul(source);
            var result = Compose(sourceRgba, mask, originalWidth, crop, cancellationToken);

            progress?.Report(100);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "Fonni olib tashlashda kutilmagan xato yuz berdi: " + ex.Message,
                inputImagePath,
                ex);
        }
        finally
        {
            sourceRgba?.Dispose();
            source?.Dispose();
        }
    }

    /// <summary>Kirish o'lchamini xavfsiz oraliqqa keltiradi (u2net uchun odatda 320).</summary>
    private static int NormalizeInputSize(int requested)
        => requested < 32 ? 320 : Math.Min(requested, 2048);

    /// <summary>
    /// Rasmni <paramref name="size"/>×<paramref name="size"/> ga <b>cho'zib</b> (aspect nisbatini
    /// saqlamasdan) kichraytiradi va NCHW tartibidagi normallashtirilgan tenzor yasaydi.
    /// Aspect nisbati saqlanmaydi, chunki u2net aynan shunday o'rgatilgan.
    /// </summary>
    private static DenseTensor<float> BuildInputTensor(
        SKBitmap source,
        int size,
        CancellationToken cancellationToken)
    {
        using var small = ResizeExact(source, size, size, DownscaleSampling);
        using var rgba = ToRgbaUnpremul(small);

        cancellationToken.ThrowIfCancellationRequested();

        var tensor = new DenseTensor<float>(new[] { 1, 3, size, size });
        var buffer = tensor.Buffer.Span;
        var plane = size * size;

        var pixels = rgba.GetPixelSpan();
        var rowBytes = rgba.RowBytes;

        for (var y = 0; y < size; y++)
        {
            var rowStart = y * rowBytes;
            var planeRow = y * size;

            for (var x = 0; x < size; x++)
            {
                var offset = rowStart + (x * 4);

                // Rgba8888: bayt tartibi R, G, B, A.
                var r = pixels[offset] / 255f;
                var g = pixels[offset + 1] / 255f;
                var b = pixels[offset + 2] / 255f;

                var index = planeRow + x;
                buffer[index] = (r - Mean[0]) / Std[0];
                buffer[plane + index] = (g - Mean[1]) / Std[1];
                buffer[(2 * plane) + index] = (b - Mean[2]) / Std[2];
            }
        }

        return tensor;
    }

    /// <summary>
    /// Modelni ishga tushiradi va birinchi chiqish tenzorini (1×1×H×W) qaytaradi.
    /// Kirish/chiqish nomlari modelning metama'lumotlaridan olinadi — qattiq yozilmagan.
    /// </summary>
    private (float[] Values, int Width, int Height) RunModel(
        string modelPath,
        DenseTensor<float> inputTensor,
        int fallbackSize,
        CancellationToken cancellationToken)
    {
        _gate.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var session = GetOrCreateSession(modelPath);

            var inputName = session.InputMetadata.Keys.FirstOrDefault()
                ?? throw new PdfServiceException(
                    PdfErrorKind.MissingComponent,
                    "AI modelida kirish tenzori topilmadi — model fayli mos emas.",
                    modelPath);

            var outputName = session.OutputMetadata.Keys.FirstOrDefault()
                ?? throw new PdfServiceException(
                    PdfErrorKind.MissingComponent,
                    "AI modelida chiqish tenzori topilmadi — model fayli mos emas.",
                    modelPath);

            var inputs = new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

            using var results = session.Run(inputs, new[] { outputName });

            var first = results.FirstOrDefault()
                ?? throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    "AI modeli natija qaytarmadi.",
                    modelPath);

            var tensor = first.AsTensor<float>()
                ?? throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    "AI modelining natijasini o'qib bo'lmadi.",
                    modelPath);

            var dimensions = tensor.Dimensions;
            var height = dimensions.Length >= 2 ? dimensions[dimensions.Length - 2] : fallbackSize;
            var width = dimensions.Length >= 1 ? dimensions[dimensions.Length - 1] : fallbackSize;

            if (width <= 0 || height <= 0)
            {
                width = fallbackSize;
                height = fallbackSize;
            }

            // Faqat birinchi kanal (d0 chiqishi) kerak.
            var needed = width * height;
            var all = tensor.ToArray();
            if (all.Length < needed)
            {
                throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    "AI modelining natija tenzori kutilganidan kichik.",
                    modelPath);
            }

            var values = new float[needed];
            Array.Copy(all, 0, values, 0, needed);
            return (values, width, height);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "AI modelini ishga tushirishda xato: " + ex.Message,
                modelPath,
                ex);
        }
        finally
        {
            if (!_disposed)
                _gate.Release();
        }
    }

    // =====================================================================================
    //  Maska bilan ishlash
    // =====================================================================================

    /// <summary>
    /// u2net chiqishi [0..1] oralig'ida bo'lishi shart emas, shuning uchun min/max bo'yicha
    /// <c>(v - min) / (max - min)</c> formulasi bilan qayta normallashtiriladi.
    /// </summary>
    private static byte[] NormalizeMask(float[] values, int count)
    {
        var min = float.MaxValue;
        var max = float.MinValue;

        for (var i = 0; i < count; i++)
        {
            var v = values[i];
            if (float.IsNaN(v))
                continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var mask = new byte[count];
        var range = max - min;

        if (range <= 1e-6f || float.IsInfinity(range))
        {
            // Model bir xil qiymat qaytardi — xom qiymatni [0..1] ga qisib ishlatamiz.
            for (var i = 0; i < count; i++)
            {
                var v = float.IsNaN(values[i]) ? 0f : Math.Clamp(values[i], 0f, 1f);
                mask[i] = (byte)Math.Round(v * 255f);
            }

            return mask;
        }

        for (var i = 0; i < count; i++)
        {
            var v = float.IsNaN(values[i]) ? min : values[i];
            var normalized = Math.Clamp((v - min) / range, 0f, 1f);
            mask[i] = (byte)Math.Round(normalized * 255f);
        }

        return mask;
    }

    /// <summary>
    /// Maskani original rasm o'lchamiga keltiradi. Buning uchun maskadan 8-bitli kulrang
    /// (<see cref="SKColorType.Gray8"/>) bitmap yasaladi va Mitchell kubik filtri bilan
    /// kattalashtiriladi — natijada chekka pog'onali bo'lmaydi. Skia bu formatni
    /// qo'llab-quvvatlamasa, qo'lda bilinear interpolyatsiya ishlatiladi.
    /// </summary>
    private static byte[] ResizeMask(byte[] mask, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth == targetWidth && sourceHeight == targetHeight)
            return mask;

        try
        {
            var sourceInfo = new SKImageInfo(sourceWidth, sourceHeight, SKColorType.Gray8, SKAlphaType.Opaque);
            using var small = new SKBitmap(sourceInfo);
            WriteRows(small, mask, sourceWidth, sourceHeight);

            var targetInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Gray8, SKAlphaType.Opaque);
            using var large = small.Resize(targetInfo, MaskUpscaleSampling);
            if (large is not null)
                return ReadRows(large, targetWidth, targetHeight);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Gray8 ni qo'llab-quvvatlamaydigan platformada quyidagi zaxira yo'lga o'tamiz.
        }

        return ResizeMaskBilinear(mask, sourceWidth, sourceHeight, targetWidth, targetHeight);
    }

    /// <summary>Zaxira variant: oddiy bilinear interpolyatsiya.</summary>
    private static byte[] ResizeMaskBilinear(byte[] mask, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var result = new byte[targetWidth * targetHeight];
        var scaleX = sourceWidth / (float)targetWidth;
        var scaleY = sourceHeight / (float)targetHeight;

        for (var y = 0; y < targetHeight; y++)
        {
            var sy = ((y + 0.5f) * scaleY) - 0.5f;
            var y0 = (int)MathF.Floor(sy);
            var fy = sy - y0;
            var y0c = Math.Clamp(y0, 0, sourceHeight - 1);
            var y1c = Math.Clamp(y0 + 1, 0, sourceHeight - 1);

            for (var x = 0; x < targetWidth; x++)
            {
                var sx = ((x + 0.5f) * scaleX) - 0.5f;
                var x0 = (int)MathF.Floor(sx);
                var fx = sx - x0;
                var x0c = Math.Clamp(x0, 0, sourceWidth - 1);
                var x1c = Math.Clamp(x0 + 1, 0, sourceWidth - 1);

                var p00 = mask[(y0c * sourceWidth) + x0c];
                var p01 = mask[(y0c * sourceWidth) + x1c];
                var p10 = mask[(y1c * sourceWidth) + x0c];
                var p11 = mask[(y1c * sourceWidth) + x1c];

                var top = p00 + ((p01 - p00) * fx);
                var bottom = p10 + ((p11 - p10) * fx);
                var value = top + ((bottom - top) * fy);

                result[(y * targetWidth) + x] = (byte)Math.Clamp(MathF.Round(value), 0f, 255f);
            }
        }

        return result;
    }

    /// <summary>
    /// Maska chekkasini yumshatadi — ajratilgan obyekt chegarasi keskin "kesilgan" ko'rinmaydi.
    /// Ajraladigan (separable) Gauss yadrosi ishlatiladi: avval gorizontal, keyin vertikal.
    /// </summary>
    private static byte[] Feather(byte[] mask, int width, int height, float radius)
    {
        var r = (int)MathF.Ceiling(radius);
        if (r < 1)
            return mask;

        r = Math.Min(r, 32);
        var sigma = MathF.Max(0.35f, radius);
        var kernel = new float[(2 * r) + 1];
        var sum = 0f;

        for (var i = 0; i < kernel.Length; i++)
        {
            var d = i - r;
            kernel[i] = MathF.Exp(-(d * d) / (2f * sigma * sigma));
            sum += kernel[i];
        }

        for (var i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;

        var horizontal = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var acc = 0f;
                for (var k = -r; k <= r; k++)
                {
                    var sx = Math.Clamp(x + k, 0, width - 1);
                    acc += mask[row + sx] * kernel[k + r];
                }

                horizontal[row + x] = acc;
            }
        }

        var result = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var acc = 0f;
                for (var k = -r; k <= r; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    acc += horizontal[(sy * width) + x] * kernel[k + r];
                }

                result[row + x] = (byte)Math.Clamp(MathF.Round(acc), 0f, 255f);
            }
        }

        return result;
    }

    /// <summary>Alfasi 8 dan katta piksellar chegarasi; hech narsa topilmasa — butun rasm.</summary>
    private static SKRectI FindOpaqueBounds(byte[] mask, int width, int height)
    {
        const byte Visible = 8;

        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (mask[row + x] <= Visible)
                    continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        if (right < 0 || bottom < 0)
            return new SKRectI(0, 0, width, height); // Butunlay shaffof — kesmaymiz.

        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    /// <summary>
    /// Yakuniy tasvir: RGB originaldan, ALPHA maskadan olinadi. Natija
    /// <see cref="SKColorType.Rgba8888"/> + <see cref="SKAlphaType.Unpremul"/> formatida —
    /// shu holda PNG ga ham, WPF ga ham yo'qotishsiz uzatiladi.
    /// </summary>
    private static SKBitmap Compose(
        SKBitmap sourceRgba,
        byte[] mask,
        int maskWidth,
        SKRectI crop,
        CancellationToken cancellationToken)
    {
        var width = crop.Width;
        var height = crop.Height;

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = new SKBitmap(info);

        try
        {
            var targetRowBytes = result.RowBytes;
            var buffer = new byte[targetRowBytes * height];

            var sourcePixels = sourceRgba.GetPixelSpan();
            var sourceRowBytes = sourceRgba.RowBytes;
            var sourcePremultiplied = sourceRgba.AlphaType == SKAlphaType.Premul;

            for (var y = 0; y < height; y++)
            {
                if ((y & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                var sourceRow = (y + crop.Top) * sourceRowBytes;
                var maskRow = (y + crop.Top) * maskWidth;
                var targetRow = y * targetRowBytes;

                for (var x = 0; x < width; x++)
                {
                    var sourceOffset = sourceRow + ((x + crop.Left) * 4);
                    var targetOffset = targetRow + (x * 4);

                    var r = sourcePixels[sourceOffset];
                    var g = sourcePixels[sourceOffset + 1];
                    var b = sourcePixels[sourceOffset + 2];

                    // Manba premultiplied bo'lsa, rangni asl holiga qaytaramiz.
                    if (sourcePremultiplied)
                    {
                        var a = sourcePixels[sourceOffset + 3];
                        if (a == 0)
                        {
                            r = g = b = 0;
                        }
                        else if (a < 255)
                        {
                            r = (byte)Math.Min(255, (r * 255) / a);
                            g = (byte)Math.Min(255, (g * 255) / a);
                            b = (byte)Math.Min(255, (b * 255) / a);
                        }
                    }

                    buffer[targetOffset] = r;
                    buffer[targetOffset + 1] = g;
                    buffer[targetOffset + 2] = b;
                    buffer[targetOffset + 3] = mask[maskRow + x + crop.Left];
                }
            }

            Marshal.Copy(buffer, 0, result.GetPixels(), buffer.Length);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    // =====================================================================================
    //  WPF ga uzatish va saqlash
    // =====================================================================================

    /// <summary>
    /// <see cref="SKBitmap"/> ni WPF uchun muzlatilgan, alfa kanalli tasvirga aylantiradi.
    /// <para>
    /// <see cref="SkiaImageHelper.ToFrozenBitmapImage"/> rasmni PNG ga kodlagani uchun alfani
    /// yo'qotmaydi, lekin katta rasmlarda kodlash/dekodlash ortiqcha vaqt oladi. Shu sababli
    /// piksellar to'g'ridan-to'g'ri <see cref="PixelFormats.Bgra32"/> (premultiplied EMAS)
    /// buferga o'qiladi. Biror sabab bilan bu yo'l ishlamasa — PNG orqali zaxira yo'l ishlatiladi.
    /// </para>
    /// </summary>
    private static BitmapSource ToFrozenAlphaBitmap(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var stride = width * 4;
        var buffer = new byte[stride * height];

        var copied = false;
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            // Bgra32 (WPF) = Bgra8888 + Unpremul (Skia).
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixmap = bitmap.PeekPixels();
            if (pixmap is not null)
                copied = pixmap.ReadPixels(info, handle.AddrOfPinnedObject(), stride);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            copied = false;
        }
        finally
        {
            handle.Free();
        }

        if (!copied)
        {
            // Zaxira yo'l: PNG orqali (alfa saqlanadi).
            return SkiaImageHelper.ToFrozenBitmapImage(bitmap);
        }

        var source = BitmapSource.Create(
            width,
            height,
            96d,
            96d,
            PixelFormats.Bgra32,
            null,
            buffer,
            stride);

        source.Freeze();
        return source;
    }

    private static void SavePngCore(SKBitmap image, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        string? tempPath = null;

        try
        {
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // PNG alfa kanalni to'liq saqlaydi.
            using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    "Tasvirni PNG formatiga kodlab bo'lmadi.",
                    outputPath);

            cancellationToken.ThrowIfCancellationRequested();

            // Avval vaqtinchalik faylga yozamiz — shunda yozish yarim yo'lda uzilsa,
            // mavjud fayl buzilmay qoladi.
            tempPath = Path.Combine(
                string.IsNullOrEmpty(directory) ? Path.GetTempPath() : directory,
                Path.GetFileName(outputPath) + ".tmp");

            using (var stream = File.Create(tempPath))
            {
                data.SaveTo(stream);
            }

            File.Move(tempPath, outputPath, overwrite: true);
            tempPath = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfServiceException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"'{outputPath}' fayliga yozib bo'lmadi. Fayl boshqa dasturda ochiq yoki papkaga " +
                "yozish huquqi yo'q bo'lishi mumkin.",
                outputPath,
                ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                "PNG faylni saqlashda xato: " + ex.Message,
                outputPath,
                ex);
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Vaqtinchalik faylni o'chira olmadik — muhim emas.
                }
            }
        }
    }

    // =====================================================================================
    //  Skia yordamchilari
    // =====================================================================================

    /// <summary>Berilgan aniq o'lchamga keltiradi (aspect nisbati saqlanmaydi).</summary>
    private static SKBitmap ResizeExact(SKBitmap source, int width, int height, SKSamplingOptions sampling)
    {
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        return source.Resize(info, sampling)
            ?? throw new PdfServiceException(
                PdfErrorKind.OperationFailed,
                $"Rasmni {width}x{height} o'lchamiga keltirib bo'lmadi.");
    }

    /// <summary>
    /// Har qanday formatdagi bitmapni <c>Rgba8888 / Unpremul</c> ko'rinishiga o'tkazadi.
    /// Qaytgan bitmapni chaqiruvchi <c>Dispose</c> qiladi.
    /// </summary>
    private static SKBitmap ToRgbaUnpremul(SKBitmap source)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = new SKBitmap(info);

        try
        {
            var converted = false;
            using (var pixmap = source.PeekPixels())
            {
                if (pixmap is not null)
                    converted = pixmap.ReadPixels(info, result.GetPixels(), result.RowBytes);
            }

            if (!converted)
            {
                // Zaxira yo'l: nusxa olish (alfa turi manbadagidek qoladi, Compose buni hisobga oladi).
                var copy = source.Copy(SKColorType.Rgba8888)
                    ?? throw new PdfServiceException(
                        PdfErrorKind.UnsupportedImage,
                        "Rasm piksellarini RGBA formatiga o'tkazib bo'lmadi.");

                result.Dispose();
                return copy;
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    /// <summary>8-bitli kulrang bitmapga qatorma-qator bayt yozadi (RowBytes width ga teng bo'lmasligi mumkin).</summary>
    private static void WriteRows(SKBitmap bitmap, byte[] source, int width, int height)
    {
        var rowBytes = bitmap.RowBytes;
        var basePointer = bitmap.GetPixels();

        for (var y = 0; y < height; y++)
            Marshal.Copy(source, y * width, basePointer + (y * rowBytes), width);
    }

    /// <summary>8-bitli kulrang bitmapdan qatorma-qator bayt o'qiydi.</summary>
    private static byte[] ReadRows(SKBitmap bitmap, int width, int height)
    {
        var result = new byte[width * height];
        var rowBytes = bitmap.RowBytes;
        var basePointer = bitmap.GetPixels();

        for (var y = 0; y < height; y++)
            Marshal.Copy(basePointer + (y * rowBytes), result, y * width, width);

        return result;
    }
}
