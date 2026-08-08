using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Yordamchi.Remoting.Protocol;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Yordamchi.Agent.Capture;

/// <summary>
/// Ekranni <b>DXGI Desktop Duplication</b> bilan oladi — Windows'ning ish stolini nusxalash uchun
/// mo'ljallangan rasmiy va eng tez yo'li.
/// <para>
/// <b>Nega GDI'dan tez.</b> GDI'ning <c>BitBlt</c> har kadrda butun ish stolini videoxotiradan
/// tizim xotirasiga ko'chiradi va buni protsessor bajaradi. Desktop Duplication esa
/// kompozitordan tayyor kadrni oladi: nusxalash GPU ichida bo'ladi, protsessor esa faqat
/// natijani o'qiydi. Amalda bu bir necha barobar kam yuklanish, ayniqsa 4K ekranda.
/// </para>
/// <para>
/// <b>Har muhitda mavjud emas.</b> Desktop Duplication'ni yaratish qator hollarda muvaffaqiyatsiz
/// bo'ladi: eski yoki asosiy (Basic Display) drayver, ba'zi virtual mashinalar, RDP seansi, yoki
/// nusxalovchilar soni chegarasi to'lgani. Shuning uchun konstruktor
/// <see cref="NotSupportedException"/> tashlaydi va <see cref="ScreenSourceFactory"/> GDI'ga
/// tushadi — agent baribir ishlaydi.
/// </para>
/// <para>
/// <b>Bitta monitor.</b> Desktop Duplication har bir chiqish (monitor) uchun alohida ishlaydi;
/// bu manba bitta monitorni beradi (standart — asosiy monitor). GDI esa barcha monitorlarni
/// bitta kadrda beradi. Shu sababli kadr qoplagan to'rtburchak <see cref="Bounds"/> orqali
/// e'lon qilinadi — kirish hodisalari to'g'ri joyga tushishi shunga bog'liq.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DxgiScreenSource : IScreenSource
{
    /// <summary>
    /// Yangi kadrni kutish muhlati. Ish stoli o'zgarmasa kadr kelmaydi — bu xato emas, shunchaki
    /// "yangilik yo'q" degani; unda oxirgi kadr qaytariladi.
    /// </summary>
    private const uint AcquireTimeoutMs = 250;

    /// <summary>Birinchi kadrni kutish muhlati — manba ishlashini shu bilan tekshiramiz.</summary>
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(2);

    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
        FeatureLevel.Level_9_1
    ];

    private readonly JpegEncoder _encoder;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutput1 _output;

    private IDXGIOutputDuplication _duplication;
    private ID3D11Texture2D? _staging;
    private int _stagingWidth;
    private int _stagingHeight;

    /// <summary>
    /// Oxirgi tayyor JPEG va uning o'lchami. Ish stoli o'zgarmaganda ham masterga kadr
    /// yuborilishi kerak (aks holda panel bo'sh turadi), shuning uchun natija saqlab qo'yiladi.
    /// <para>
    /// O'lcham ham shu bilan birga saqlanadi — kadrning haqiqiy o'lchami e'lon qilinishi kerak.
    /// Ekran ruxsati o'zgargan paytda monitor tavsifi yangi o'lchamni ko'rsatib turgan bo'lsa
    /// ham, qo'lda turgan kadr hali eskisi bo'ladi.
    /// </para>
    /// </summary>
    private byte[]? _lastImage;

    private int _lastWidth;
    private int _lastHeight;

    /// <param name="jpegQuality">1..100 — kichikroq = kam trafik, past sifat.</param>
    /// <param name="outputIndex">
    /// Qaysi monitor. <c>null</c> — asosiy monitor (ish stoli koordinatasi 0,0 dan boshlanadigan).
    /// </param>
    /// <exception cref="NotSupportedException">
    /// Bu kompyuterda Desktop Duplication ishlatib bo'lmasa.
    /// </exception>
    public DxgiScreenSource(long jpegQuality = 55, int? outputIndex = null)
    {
        _encoder = new JpegEncoder(jpegQuality);

        try
        {
            (_device, _context) = CreateDevice();
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            _encoder.Dispose();
            throw new NotSupportedException($"Direct3D qurilmasi yaratilmadi: {ex.Message}", ex);
        }

        try
        {
            _output = FindOutput(_device, outputIndex);
            _duplication = Duplicate(_output, _device);
        }
        catch
        {
            _context.Dispose();
            _device.Dispose();
            _encoder.Dispose();
            throw;
        }

        // Birinchi kadrni darhol olishga urinamiz: shu yerda muvaffaqiyatsizlik "bu manba bu
        // kompyuterda ishlamaydi" degani va zaxira manbaga o'tish uchun eng qulay joy.
        try
        {
            CaptureFirstFrame();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Nusxalanayotgan monitor ish stolining qaysi qismini egallaydi.</summary>
    public ScreenRegion Bounds
    {
        get
        {
            var rect = _output.Description.DesktopCoordinates;
            return new ScreenRegion(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    public ScreenFrame Capture()
    {
        var image = TryAcquire();

        if (image is not null)
        {
            _lastImage = image;
            _lastWidth = _stagingWidth;
            _lastHeight = _stagingHeight;
        }

        if (_lastImage is null)
            throw new InvalidOperationException("Ekran kadri olinmadi.");

        return new ScreenFrame(_lastWidth, _lastHeight, ScreenImageFormat.Jpeg, _lastImage);
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _duplication.Dispose();
        _output.Dispose();
        _context.Dispose();
        _device.Dispose();
        _encoder.Dispose();
    }

    /// <summary>
    /// Bitta kadr olishga urinadi. Yangi kadr bo'lmasa <c>null</c> qaytaradi — bu odatiy holat
    /// (ish stoli o'zgarmadi).
    /// </summary>
    private byte[]? TryAcquire()
    {
        var result = _duplication.AcquireNextFrame(AcquireTimeoutMs, out _, out var resource);

        if (result.Failure)
        {
            if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                return null;

            // Ekran ruxsati o'zgardi, xavfsiz ish stoli ochildi yoki seans almashdi — nusxalovchi
            // yaroqsiz bo'ladi va uni qaytadan yaratish kerak. Bu kutiladigan holat, xato emas.
            ResetDuplication();
            return null;
        }

        try
        {
            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            var description = texture.Description;

            EnsureStaging((int)description.Width, (int)description.Height);
            _context.CopyResource(_staging!, texture);
        }
        finally
        {
            resource.Dispose();

            // Kadrni imkon qadar tez qo'yib yuborish kerak: nusxalovchi navbatida bir vaqtda
            // faqat bitta kadr turadi va u bo'shatilmaguncha yangisi kelmaydi.
            _duplication.ReleaseFrame();
        }

        return Encode();
    }

    /// <summary>Staging teksturasidagi pikselni JPEG ga o'giradi (ko'rsatgichni ham chizadi).</summary>
    private byte[] Encode()
    {
        // ReadWrite: ko'rsatgichni to'g'ridan-to'g'ri xaritalangan xotiraga chizamiz — bu ortiqcha
        // nusxa olishdan qutqaradi (1920×1080 da har kadrda ~8 MB tejash).
        var map = _context.Map(_staging!, 0, MapMode.ReadWrite, MapFlags.None);

        try
        {
            using var bitmap = new Bitmap(
                _stagingWidth,
                _stagingHeight,
                (int)map.RowPitch,
                PixelFormat.Format32bppRgb,
                map.DataPointer);

            using (var graphics = Graphics.FromImage(bitmap))
            {
                CursorPainter.Draw(graphics, Bounds);
            }

            return _encoder.Encode(bitmap);
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
    }

    private void CaptureFirstFrame()
    {
        var deadline = DateTime.UtcNow + FirstFrameTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var image = TryAcquire();
            if (image is null)
                continue;

            _lastImage = image;
            _lastWidth = _stagingWidth;
            _lastHeight = _stagingHeight;
            return;
        }

        throw new NotSupportedException(
            "Desktop Duplication ishga tushdi, lekin birinchi kadr kelmadi — GDI ishlatiladi.");
    }

    private void ResetDuplication()
    {
        try
        {
            _duplication.Dispose();
            _duplication = Duplicate(_output, _device);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw new NotSupportedException($"Ekran nusxalovchisi qayta yaratilmadi: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// GPU dagi kadrni protsessor o'qiy oladigan "staging" teksturaga ko'chirish kerak —
    /// nusxalovchi bergan tekstura videoxotirada va uni to'g'ridan-to'g'ri o'qib bo'lmaydi.
    /// Tekstura o'lcham o'zgarmaguncha qayta ishlatiladi.
    /// </summary>
    private void EnsureStaging(int width, int height)
    {
        if (_staging is not null && _stagingWidth == width && _stagingHeight == height)
            return;

        _staging?.Dispose();

        var description = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read | CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None
        };

        _staging = _device.CreateTexture2D(in description);

        _stagingWidth = width;
        _stagingHeight = height;
    }

    private static (ID3D11Device Device, ID3D11DeviceContext Context) CreateDevice()
    {
        var result = D3D11.D3D11CreateDevice(
            IntPtr.Zero,                            // adapter: tizim tanlaydi
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,        // BGRA — kadr formati bilan bir xil
            FeatureLevels,
            out var device,
            out var context);

        result.CheckError();
        return (device, context);
    }

    /// <summary>
    /// Kerakli monitorni topadi. <paramref name="outputIndex"/> berilmasa asosiy monitor olinadi —
    /// u ish stoli koordinatalarida (0,0) dan boshlanadi.
    /// </summary>
    private static IDXGIOutput1 FindOutput(ID3D11Device device, int? outputIndex)
    {
        using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
        dxgiDevice.GetAdapter(out var adapter).CheckError();

        using (adapter)
        {
            IDXGIOutput? chosen = null;

            for (uint i = 0; ; i++)
            {
                if (adapter.EnumOutputs(i, out var output).Failure)
                    break;

                var description = output.Description;
                var isRequested = outputIndex is null
                    ? description.AttachedToDesktop
                      && description.DesktopCoordinates.Left == 0
                      && description.DesktopCoordinates.Top == 0
                    : i == (uint)outputIndex.Value;

                // Birinchi mos kelgan monitorni saqlaymiz, lekin qolganlarini ham to'g'ri
                // bo'shatishimiz kerak — COM ob'ektlari hisoblagich bilan yashaydi.
                if (isRequested && chosen is null)
                    chosen = output;
                else
                    output.Dispose();
            }

            if (chosen is null)
                throw new NotSupportedException("Nusxalash uchun monitor topilmadi.");

            using (chosen)
            {
                try
                {
                    return chosen.QueryInterface<IDXGIOutput1>();
                }
                catch (SharpGenException ex)
                {
                    throw new NotSupportedException(
                        "Monitor DXGI 1.2 (Desktop Duplication) ni qo'llab-quvvatlamaydi.", ex);
                }
            }
        }
    }

    private static IDXGIOutputDuplication Duplicate(IDXGIOutput1 output, ID3D11Device device)
    {
        try
        {
            return output.DuplicateOutput(device);
        }
        catch (SharpGenException ex)
        {
            // Eng ko'p uchraydigan sabablar: xavfsiz ish stoli (UAC/qulf ekrani) ochiq,
            // nusxalovchilar chegarasi to'lgan, yoki drayver qo'llab-quvvatlamaydi.
            throw new NotSupportedException(
                $"Ish stolini nusxalash boshlanmadi ({ex.ResultCode}) — GDI ishlatiladi.", ex);
        }
    }
}
