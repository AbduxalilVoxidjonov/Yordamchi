using Yordamchi.Remoting.Command;

namespace Yordamchi.Agent.Commands;

/// <summary>
/// Masterdan kelgan <b>cheklangan</b> buyruqni bajaruvchi. Ro'yxat
/// <see cref="RemoteCommandKind"/> da yopiq: ixtiyoriy dastur ishga tushirish yoki qobiq
/// buyrug'i yo'q.
/// </summary>
public interface ICommandSink
{
    /// <summary>Buyruqni bajaradi; bajarilmasa (ruxsat yo'q, qo'llab-quvvatlanmaydi) <c>false</c>.</summary>
    bool Execute(in RemoteCommand command);
}

/// <summary>
/// Hech narsa qilmaydigan bajaruvchi — buyruqlar o'chirilgan holat. <b>Standart shu</b>:
/// masofadan biror amal bajarilishi alohida yoqilishi kerak.
/// </summary>
public sealed class DisabledCommandSink : ICommandSink
{
    public static readonly DisabledCommandSink Instance = new();

    private DisabledCommandSink()
    {
    }

    public bool Execute(in RemoteCommand command) => false;
}

/// <summary>
/// Haqiqiy bajaruvchini ruxsat kalitchasi ortiga oladi (qarang:
/// <see cref="Input.GatedInputSink"/>) — foydalanuvchi ruxsatni ish vaqtida olib qo'yishi mumkin,
/// shuning uchun kalitcha har buyruqda qaytadan so'raladi.
/// </summary>
public sealed class GatedCommandSink : ICommandSink
{
    private readonly ICommandSink _inner;
    private readonly Func<bool> _isAllowed;

    public GatedCommandSink(ICommandSink inner, Func<bool> isAllowed)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _isAllowed = isAllowed ?? throw new ArgumentNullException(nameof(isAllowed));
    }

    public bool Execute(in RemoteCommand command) => _isAllowed() && _inner.Execute(command);
}
