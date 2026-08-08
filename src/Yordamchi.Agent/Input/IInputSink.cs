using Yordamchi.Agent.Capture;
using Yordamchi.Remoting.Input;

namespace Yordamchi.Agent.Input;

/// <summary>
/// Masterdan kelgan kirish hodisasini bajaruvchi. Interfeys ortida turishi maqsadli: haqiqiy
/// bajaruvchi (<see cref="SendInputSink"/>) Windows API'siga tayanadi, sinovlarda esa hodisalarni
/// shunchaki yozib boruvchi ikkinchi amalga oshirish qo'yiladi — shu tufayli butun qabul qilish
/// yo'li apparatsiz sinaladi.
/// </summary>
public interface IInputSink
{
    /// <summary>
    /// Hodisani bajaradi.
    /// </summary>
    /// <param name="input">Masterdan kelgan hodisa (o'rni 0..1 normallashtirilgan).</param>
    /// <param name="region">
    /// Master ko'rib turgan kadr qoplaydigan to'rtburchak — normallashtirilgan o'rin shu
    /// to'rtburchak ichida hisoblanadi.
    /// </param>
    /// <returns>
    /// Hodisa bajarilgan bo'lsa <c>true</c>. <c>false</c> — bajarilmadi (boshqaruvga ruxsat yo'q
    /// yoki hodisa turi qo'llab-quvvatlanmaydi); bu xato emas, shuning uchun istisno tashlanmaydi.
    /// </returns>
    bool Inject(in InputEvent input, ScreenRegion region);
}

/// <summary>
/// Hech narsa qilmaydigan bajaruvchi: agent boshqaruvga ruxsatsiz ishga tushganda ishlatiladi.
/// <b>Standart holat shu</b> — kirish yuborish alohida yoqilishi kerak, tasodifan yoqilib
/// qolmasligi uchun.
/// </summary>
public sealed class DisabledInputSink : IInputSink
{
    public static readonly DisabledInputSink Instance = new();

    private DisabledInputSink()
    {
    }

    public bool Inject(in InputEvent input, ScreenRegion region) => false;
}

/// <summary>
/// Haqiqiy bajaruvchini <b>ruxsat kalitchasi</b> ortiga oladi: ruxsat o'chirilgan bo'lsa hodisa
/// bajarilmaydi. Kalitcha ish vaqtida o'zgaradi (foydalanuvchi tray menyusidan o'chirib qo'yishi
/// mumkin), shuning uchun u har hodisada qaytadan so'raladi — bir marta o'qib qo'yilmaydi.
/// </summary>
public sealed class GatedInputSink : IInputSink
{
    private readonly IInputSink _inner;
    private readonly Func<bool> _isAllowed;

    public GatedInputSink(IInputSink inner, Func<bool> isAllowed)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _isAllowed = isAllowed ?? throw new ArgumentNullException(nameof(isAllowed));
    }

    public bool Inject(in InputEvent input, ScreenRegion region) =>
        _isAllowed() && _inner.Inject(input, region);
}
