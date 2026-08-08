using System.Text;
using Yordamchi.Agent.Capture;

namespace Yordamchi.Agent.Hosting;

/// <summary>Agent qanday rejimda ishga tushishi.</summary>
public enum AgentRunMode
{
    /// <summary>Oddiy jarayon: konsol/tray bilan foydalanuvchi seansida (standart).</summary>
    Console = 0,

    /// <summary>Windows xizmati sifatida (faqat <c>services.exe</c> chaqirganda ishlaydi).</summary>
    Service = 1,

    /// <summary>Xizmatni ro'yxatga qo'shish va ishga tushirish (administrator huquqi kerak).</summary>
    Install = 2,

    /// <summary>Xizmatni to'xtatish va ro'yxatdan olib tashlash.</summary>
    Uninstall = 3,

    /// <summary>Yordam matnini ko'rsatish.</summary>
    Help = 4
}

/// <summary>
/// Buyruq satri sozlamalari. Alohida, o'zgarmas (immutable) ob'ekt bo'lishi maqsadli: xuddi shu
/// sozlamalar keyin <see cref="ToArgumentString"/> orqali <b>xizmatning buyruq satriga</b> va
/// <b>faol seansda ochiladigan bola jarayonga</b> uzatiladi — ya'ni bir joyda o'qiladi, uch joyda
/// ishlatiladi.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>Boshqaruv (TCP) porti. Discovery porti (5405) dan farqli.</summary>
    public const int DefaultControlPort = 5406;

    /// <summary>TCP boshqaruv porti.</summary>
    public int Port { get; init; } = DefaultControlPort;

    /// <summary>JPEG sifati (1..100).</summary>
    public long JpegQuality { get; init; } = 55;

    /// <summary>Sekundiga kadr soni (1..30).</summary>
    public int FramesPerSecond { get; init; } = 10;

    /// <summary>Ekran olish usuli.</summary>
    public CaptureMode Capture { get; init; } = CaptureMode.Auto;

    /// <summary>Sichqoncha/klaviatura yuborishga ruxsat.</summary>
    public bool AllowInput { get; init; } = true;

    /// <summary>Cheklangan buyruqlarga (xabar, ekran qulfi) ruxsat.</summary>
    public bool AllowCommands { get; init; } = true;

    /// <summary>Tizim majmuasidagi (tray) belgi — "bu kompyuter kuzatilishi mumkin" ko'rsatkichi.</summary>
    public bool ShowTray { get; init; } = true;

    /// <summary>UDP mayoq bilan o'zini e'lon qilish.</summary>
    public bool Announce { get; init; } = true;

    /// <summary>Ishga tushish rejimi.</summary>
    public AgentRunMode Mode { get; init; } = AgentRunMode.Console;

    /// <summary>
    /// Ota jarayon (xizmat) identifikatori. Berilgan bo'lsa, ota jarayon tugashi bilan agent ham
    /// chiqadi — xizmat yiqilib qolsa faol seansda "yetim" agent qolib ketmasligi uchun.
    /// </summary>
    public int? ParentProcessId { get; init; }

    /// <summary>Tahlil qilishda xato bo'lsa — sababi; aks holda <c>null</c>.</summary>
    public string? Error { get; init; }

    /// <summary>Kadrlar orasidagi kutish.</summary>
    public TimeSpan FrameInterval => TimeSpan.FromMilliseconds(1000.0 / FramesPerSecond);

    public static AgentOptions Parse(string[] args)
    {
        var port = DefaultControlPort;
        var quality = 55L;
        var fps = 10;
        var capture = CaptureMode.Auto;
        var allowInput = true;
        var allowCommands = true;
        var showTray = true;
        var announce = true;
        var mode = AgentRunMode.Console;
        int? parentPid = null;

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];

            switch (argument)
            {
                case "--port" or "-p":
                    if (!TryTakeInt(args, ref i, 1, 65535, out port))
                        return Failed($"'{argument}' uchun 1..65535 oralig'idagi port kerak.");
                    break;

                case "--quality" or "-q":
                    if (!TryTakeInt(args, ref i, 1, 100, out var parsedQuality))
                        return Failed($"'{argument}' uchun 1..100 oralig'idagi sifat kerak.");
                    quality = parsedQuality;
                    break;

                case "--fps":
                    if (!TryTakeInt(args, ref i, 1, 30, out fps))
                        return Failed("'--fps' uchun 1..30 oralig'idagi qiymat kerak.");
                    break;

                case "--capture":
                    if (!TryTakeValue(args, ref i, out var captureName)
                        || !Enum.TryParse(captureName, ignoreCase: true, out capture))
                    {
                        return Failed("'--capture' qiymati: auto, dxgi, gdi yoki synthetic.");
                    }

                    break;

                case "--parent-pid":
                    if (!TryTakeInt(args, ref i, 1, int.MaxValue, out var pid))
                        return Failed("'--parent-pid' uchun jarayon raqami kerak.");
                    parentPid = pid;
                    break;

                case "--no-input":
                    allowInput = false;
                    break;

                case "--no-commands":
                    allowCommands = false;
                    break;

                case "--no-tray":
                    showTray = false;
                    break;

                case "--no-discovery":
                    announce = false;
                    break;

                case "--service":
                    mode = AgentRunMode.Service;
                    break;

                case "--install":
                    mode = AgentRunMode.Install;
                    break;

                case "--uninstall":
                    mode = AgentRunMode.Uninstall;
                    break;

                case "--help" or "-h" or "/?":
                    mode = AgentRunMode.Help;
                    break;

                default:
                    return Failed($"Noma'lum parametr: {argument}");
            }
        }

        return new AgentOptions
        {
            Port = port,
            JpegQuality = quality,
            FramesPerSecond = fps,
            Capture = capture,
            AllowInput = allowInput,
            AllowCommands = allowCommands,
            ShowTray = showTray,
            Announce = announce,
            Mode = mode,
            ParentProcessId = parentPid
        };
    }

    /// <summary>
    /// Sozlamalarni qaytadan buyruq satriga o'giradi — xizmatni ro'yxatga qo'shishda va faol
    /// seansda bola jarayon ochishda ishlatiladi. <paramref name="mode"/> bilan rejim
    /// almashtiriladi (masalan <see cref="AgentRunMode.Install"/> o'rniga
    /// <see cref="AgentRunMode.Service"/> yoziladi).
    /// </summary>
    public string ToArgumentString(AgentRunMode mode, int? parentProcessId = null)
    {
        var builder = new StringBuilder();

        if (mode == AgentRunMode.Service)
            builder.Append("--service ");

        builder.Append($"--port {Port} --quality {JpegQuality} --fps {FramesPerSecond}");
        builder.Append($" --capture {Capture.ToString().ToLowerInvariant()}");

        if (!AllowInput)
            builder.Append(" --no-input");

        if (!AllowCommands)
            builder.Append(" --no-commands");

        if (!ShowTray)
            builder.Append(" --no-tray");

        if (!Announce)
            builder.Append(" --no-discovery");

        if (parentProcessId is not null)
            builder.Append($" --parent-pid {parentProcessId.Value}");

        return builder.ToString();
    }

    public static string HelpText =>
        """
        Yordamchi Agent — boshqariladigan kompyuterdagi dastur.

          YordamchiAgent [parametrlar]

        Parametrlar:
          -p, --port <1..65535>     TCP boshqaruv porti (standart: 5406)
          -q, --quality <1..100>    JPEG sifati (standart: 55)
              --fps <1..30>         Sekundiga kadr (standart: 10)
              --capture <usul>      auto | dxgi | gdi | synthetic (standart: auto)
              --no-input            Sichqoncha/klaviatura yuborishni o'chirish
              --no-commands         Masofaviy buyruqlarni (xabar, ekran qulfi) o'chirish
              --no-tray             Tray belgisini ko'rsatmaslik
              --no-discovery        UDP mayoqni o'chirish (IP qo'lda kiritiladi)
              --install             Windows xizmati sifatida o'rnatish (administrator kerak)
              --uninstall           Xizmatni olib tashlash (administrator kerak)
              --service             Xizmat rejimi (xizmat menejeri o'zi chaqiradi)
          -h, --help                Shu yordam

        Ruxsat: bu dastur faqat siz administratsiya qiladigan va foydalanuvchilar xabardor
        bo'lgan kompyuterlarga o'rnatiladi. Agent ishlayotgani tray belgisidan ko'rinadi.
        """;

    private static AgentOptions Failed(string error) => new() { Error = error, Mode = AgentRunMode.Help };

    private static bool TryTakeValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryTakeInt(string[] args, ref int index, int min, int max, out int value)
    {
        value = 0;
        return TryTakeValue(args, ref index, out var text)
               && int.TryParse(text, out value)
               && value >= min
               && value <= max;
    }
}
