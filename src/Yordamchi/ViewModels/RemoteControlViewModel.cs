using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// "Kompyuterlarni boshqarish" sahifasi: boshqa kompyuterlarni masofadan boshqarish uchun
/// agent (server) faylini yuklab olish va uni maqsadli kompyuterlarga o'rnatish tartibini
/// ko'rsatadi.
/// <para>
/// Bu sahifa — <b>tarqatish markazi</b>, boshqaruv oynasi emas. Agentning o'zi (ekran uzatish,
/// kirish yuborish, tarmoq) alohida katta loyiha bo'lib, GitHub relizidan yuklab olinadi.
/// Dastur faylni faqat <b>yuklab oladi</b> — ishga tushirmaydi, o'rnatishni foydalanuvchi
/// maqsadli kompyuterda administrator huquqida o'zi bajaradi.
/// </para>
/// <para>
/// <b>Ruxsat.</b> Masofaviy boshqaruv faqat <b>o'zingiz administratsiya qiladigan</b>
/// kompyuterlarga (sinf, laboratoriya, ofis) va foydalanuvchilar xabardor holatda o'rnatilishi
/// kerak. Agent maqsadli kompyuterda ko'rinadigan belgi qoldiradi — kuzatuv yashirin emas.
/// </para>
/// </summary>
public sealed partial class RemoteControlViewModel : ViewModelBase
{
    private readonly IRemoteControlService _remote;

    public RemoteControlViewModel(IRemoteControlService remote, IDialogService dialogService)
        : base(dialogService)
    {
        _remote = remote;

        // Standart holatda GitHub relizidagi agent havolasi turadi; foydalanuvchi uni
        // o'zgartirishi mumkin (masalan o'z relizidagi nusxaga).
        _agentDownloadUrl = remote.DefaultDownloadUrl;

        InstallSteps =
        [
            new InstallStep(1, "Agentni yuklab oling",
                "Shu sahifadagi \"Yuklab olish\" tugmasi orqali agent (server) faylini oling. "
                + "U faqat siz boshqaradigan kompyuterlarga mo'ljallangan."),
            new InstallStep(2, "Maqsadli kompyuterga ko'chiring",
                "Faylni boshqariladigan kompyuterga ko'chiring — USB xotira, umumiy tarmoq "
                + "papkasi yoki guruh siyosati (GPO) orqali."),
            new InstallStep(3, "Administrator huquqida o'rnating",
                "Faylni maqsadli kompyuterda administrator sifatida ishga tushiring. Agent Windows "
                + "xizmati sifatida o'rnatiladi, brandmauerda faqat o'zi uchun kiruvchi portni "
                + "ochadi va tizim majmuasida (tray) ko'rinadigan belgi qoldiradi — foydalanuvchi "
                + "kuzatuv borligini biladi va ruxsatni shu belgidan o'chirib qo'yishi mumkin."),
            new InstallStep(4, "Tarmoqni tekshiring",
                "Ikkala kompyuter bir lokal tarmoqda bo'lsin. Portlar: boshqaruv uchun TCP 5406, "
                + "kompyuterlarni topish uchun UDP 5405."),
            new InstallStep(5, "\"Kompyuter ekranlari\" bo'limida ko'rinadi",
                "O'rnatilgach, \"Qidirish\" tugmasi bosilganda kompyuter ro'yxatga tushadi; "
                + "ulangach uning ekrani ko'rinadi.")
        ];

        RefreshAgentStatus();
    }

    public override string Title => "Kompyuterlarni boshqarish";

    public override string Description =>
        "Boshqa kompyuterlarni masofadan kuzatish va boshqarish uchun agentni yuklab oling va o'rnating.";

    // =================================================================================
    //  Agentni yuklab olish
    // =================================================================================

    /// <summary>
    /// Agent faylining yuklab olish manzili. Standart qiymat — GitHub relizidagi agent aktivi;
    /// foydalanuvchi uni boshqa GitHub havolasiga o'zgartirishi mumkin. O'zgartirilgan holat
    /// faqat shu seansda saqlanadi — dasturda sozlamalarni diskka yozadigan joy yo'q.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadAgentCommand))]
    private string _agentDownloadUrl = string.Empty;

    /// <summary>Manzil maydonining namunasi (watermark) va yordam matni uchun.</summary>
    public string ExampleDownloadUrl => _remote.ExampleDownloadUrl;

    /// <summary>Yuklab olingan fayl saqlanadigan papka.</summary>
    public string DownloadFolder => _remote.DownloadFolder;

    /// <summary>Agent fayli allaqachon yuklab olinganmi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AgentStatus))]
    [NotifyCanExecuteChangedFor(nameof(OpenDownloadFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyAgentPathCommand))]
    private bool _isAgentDownloaded;

    /// <summary>Yuklab olish holati haqidagi qisqa matn.</summary>
    public string AgentStatus => IsAgentDownloaded
        ? $"Agent yuklab olingan: {_remote.AgentFilePath}"
        : "Agent hali yuklab olinmagan. \"Yuklab olish\" tugmasini bosing.";

    private bool CanDownload() => IsIdle && _remote.IsDownloadUrlReady(AgentDownloadUrl);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAgentAsync()
    {
        var downloaded = await RunAsync(
            "Agent yuklanmoqda…",
            async (progress, token) =>
            {
                await _remote.DownloadAgentAsync(AgentDownloadUrl.Trim(), progress, token).ConfigureAwait(true);
            },
            "Agent yuklab olindi — endi uni maqsadli kompyuterga ko'chiring.");

        if (downloaded)
            RefreshAgentStatus();
    }

    [RelayCommand(CanExecute = nameof(IsAgentDownloaded))]
    private void OpenDownloadFolder()
    {
        try
        {
            Directory.CreateDirectory(_remote.DownloadFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Papkani yarata olmasak ham, ochishga urinib ko'ramiz.
        }

        DialogService.RevealInExplorer(_remote.DownloadFolder);
    }

    [RelayCommand(CanExecute = nameof(IsAgentDownloaded))]
    private void CopyAgentPath()
    {
        DialogService.SetClipboardText(_remote.AgentFilePath);
        StatusMessage = "Agent fayli yo'li nusxa olindi.";
    }

    [RelayCommand]
    private void RefreshAgent() => RefreshAgentStatus();

    private void RefreshAgentStatus()
    {
        IsAgentDownloaded = _remote.IsAgentDownloaded;
        OnPropertyChanged(nameof(AgentStatus));
    }

    // =================================================================================
    //  O'rnatish tartibi
    // =================================================================================

    /// <summary>Maqsadli kompyuterga agentni o'rnatishning bosqichma-bosqich tartibi.</summary>
    public IReadOnlyList<InstallStep> InstallSteps { get; }

    protected override void OnBusyStateChanged()
    {
        DownloadAgentCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>"Kompyuterlarni boshqarish" sahifasidagi o'rnatish tartibining bitta qadami.</summary>
/// <param name="Number">Qadam tartib raqami.</param>
/// <param name="Title">Qadam sarlavhasi.</param>
/// <param name="Detail">Qadamning to'liq tavsifi.</param>
public sealed record InstallStep(int Number, string Title, string Detail);
