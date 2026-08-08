using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yordamchi.Remoting.Command;

namespace Yordamchi.Agent.Commands;

/// <summary>
/// Ruxsat etilgan buyruqlarni Windows'da bajaradi: foydalanuvchiga xabar ko'rsatish va ish stolini
/// qulflash.
/// <para>
/// <b>Nega faqat shu ikkitasi.</b> Har bir buyruq alohida, tor amal sifatida yozilgan — masterdan
/// kelgan matn hech qachon buyruq satriga, fayl yo'liga yoki jarayon ishga tushirishga
/// tushmaydi. Shuning uchun "masterni egallab olgan odam agent kompyuterida nimani bajara oladi"
/// degan savolning javobi shu faylni o'qish bilan tugaydi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCommandSink : ICommandSink
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconInformation = 0x00000040;

    /// <summary>Xabar oynasi boshqa oynalar ortida qolib ketmasligi uchun.</summary>
    private const uint MessageBoxTopMost = 0x00040000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    private readonly Action<string>? _notify;
    private readonly Action<string>? _log;

    /// <param name="notify">
    /// Xabarni ko'rsatishning yumshoq yo'li (tray bildirishnomasi). Berilmasa yoki tray bo'lmasa
    /// modal xabar oynasi ko'rsatiladi.
    /// </param>
    /// <param name="log">Ixtiyoriy jurnal — nima bajarilgani yozib qo'yiladi (shaffoflik uchun).</param>
    public WindowsCommandSink(Action<string>? notify = null, Action<string>? log = null)
    {
        _notify = notify;
        _log = log;
    }

    public bool Execute(in RemoteCommand command)
    {
        switch (command.Kind)
        {
            case RemoteCommandKind.ShowMessage:
                var text = command.Text;
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                ShowMessage(text);
                _log?.Invoke($"Buyruq: xabar ko'rsatildi — \"{text}\"");
                return true;

            case RemoteCommandKind.LockScreen:
                var locked = LockWorkStation();
                _log?.Invoke(locked
                    ? "Buyruq: ish stoli qulflandi."
                    : $"Buyruq: ish stolini qulflash bajarilmadi (xato {Marshal.GetLastWin32Error()}).");
                return locked;

            default:
                return false;
        }
    }

    private void ShowMessage(string text)
    {
        if (_notify is not null)
        {
            _notify(text);
            return;
        }

        // Modal oyna chaqirgan oqimni to'sib qo'yadi, ulanish halqasi esa to'xtab qolmasligi
        // kerak — shuning uchun xabar alohida, fon oqimida ko'rsatiladi.
        var thread = new Thread(() =>
        {
            try
            {
                MessageBoxW(
                    IntPtr.Zero,
                    text,
                    "Yordamchi — masofaviy xabar",
                    MessageBoxOk | MessageBoxIconInformation | MessageBoxTopMost);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log?.Invoke($"Xabarni ko'rsatib bo'lmadi: {ex.Message}");
            }
        })
        {
            IsBackground = true,
            Name = "Yordamchi agent — masofaviy xabar"
        };

        thread.Start();
    }
}
