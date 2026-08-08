namespace Yordamchi.Agent.Hosting;

/// <summary>
/// Agent nimaga ruxsat berayotganini saqlaydi va uni <b>ish vaqtida</b> o'zgartirishga imkon
/// beradi (tray menyusi orqali).
/// <para>
/// <b>Nega alohida ob'ekt.</b> Ruxsatni tekshiruvchi kod (ulanish halqasi, kirish bajaruvchisi)
/// va uni o'zgartiruvchi kod (tray) turli oqimlarda ishlaydi. Qiymatlar
/// <see cref="Volatile"/> bilan o'qilib-yozilgani uchun o'zgarish darhol ko'rinadi — aks holda
/// foydalanuvchi "o'chirdim" degan bo'lsa ham, ulanish oqimi eski qiymatni ko'rib turishi mumkin
/// edi.
/// </para>
/// </summary>
public sealed class AgentPermissions
{
    private int _allowInput;
    private int _allowCommands;

    public AgentPermissions(bool allowInput, bool allowCommands)
    {
        AllowInput = allowInput;
        AllowCommands = allowCommands;
    }

    /// <summary>Ruxsatlardan biri o'zgarganda (tray belgisini yangilash uchun).</summary>
    public event Action? Changed;

    /// <summary>Sichqoncha/klaviatura yuborishga ruxsat.</summary>
    public bool AllowInput
    {
        get => Volatile.Read(ref _allowInput) != 0;
        set
        {
            if (Interlocked.Exchange(ref _allowInput, value ? 1 : 0) != (value ? 1 : 0))
                Changed?.Invoke();
        }
    }

    /// <summary>Cheklangan buyruqlarga ruxsat.</summary>
    public bool AllowCommands
    {
        get => Volatile.Read(ref _allowCommands) != 0;
        set
        {
            if (Interlocked.Exchange(ref _allowCommands, value ? 1 : 0) != (value ? 1 : 0))
                Changed?.Invoke();
        }
    }
}
