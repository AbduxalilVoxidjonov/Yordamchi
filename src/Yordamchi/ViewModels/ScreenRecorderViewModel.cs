using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Helpers;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>
/// Ekranni videoga yozib olish sahifasi.
/// <para>
/// Bu sahifa boshqa vositalardan tubdan farq qiladi: u "boshla → kut → tugadi" emas,
/// balki holatga asoslangan. Shu sababli <see cref="ViewModelBase.RunAsync"/> band
/// qoplamasi ishlatilmaydi — yozib olish davomida foydalanuvchi dastur bilan ishlashda
/// davom etadi (aksincha, ko'pincha oynani umuman kichraytiradi).
/// </para>
/// <para>
/// Servis hodisalari UI oqimida keladi (shartnomaga qarang), shuning uchun bu yerda
/// qo'shimcha marshalling yo'q.
/// </para>
/// </summary>
public sealed partial class ScreenRecorderViewModel : ViewModelBase, IDisposable
{
    private readonly IScreenRecorderService _recorder;

    private CancellationTokenSource? _timerCancellation;
    private DateTimeOffset _startedAt;
    private TimeSpan _pausedTotal;
    private DateTimeOffset? _pausedAt;

    /// <summary>Oynani aynan biz kichraytirganmizmi — faqat shunda uni qaytaramiz.</summary>
    private bool _minimizedForRecording;

    public ScreenRecorderViewModel(IScreenRecorderService recorder, IDialogService dialogService)
        : base(dialogService)
    {
        _recorder = recorder;

        _recorder.StateChanged += OnStateChanged;
        _recorder.RecordingCompleted += OnRecordingCompleted;
        _recorder.RecordingFailed += OnRecordingFailed;

        OutputFolder = DefaultOutputFolder();

        RefreshSources();
        RefreshAudioDevices();
    }

    public override string Title => "Ekran yozuvi";

    public override string Description => "Ekranni yoki bitta oynani ovoz bilan videoga yozib oling";

    /// <summary>Yozib olish boshlanganda asosiy oynani kichraytirish so'rovi.</summary>
    public event EventHandler? MinimizeRequested;

    /// <summary>
    /// Yozuv tugagach oynani qaytarish so'rovi — faqat uni biz kichraytirgan bo'lsak.
    /// Foydalanuvchi natijani ("Oxirgi yozuv" kartochkasini) topish uchun vazifalar
    /// panelini qidirib o'tirmasligi kerak.
    /// </summary>
    public event EventHandler? RestoreRequested;

    /// <summary>Suzuvchi boshqaruv paneli ochilsin (<c>true</c>) yoki yopilsin (<c>false</c>).</summary>
    public event EventHandler<bool>? OverlayVisibilityChanged;

    /// <summary>
    /// Yozuv davomida boshqaruv alohida suzuvchi panelga chiqariladimi.
    /// <para>
    /// Panelning butun ma'nosi — u ekranda ko'rinib, videoga tushmasligida. Buni
    /// <c>WDA_EXCLUDEFROMCAPTURE</c> ta'minlaydi va u Windows 10 2004 dan mavjud. Eskiroq
    /// tizimda panel har kadrda ko'rinib qolardi, shuning uchun u ochilmaydi va tugmalar
    /// sahifaning o'zida qoladi (<see cref="ShowsInlineControls"/>).
    /// </para>
    /// <para>
    /// Standart qiymat tizimdan olinadi, lekin xossa <c>init</c> qilib qo'yilgan: aks holda
    /// sinov natijasi u ishlayotgan Windows versiyasiga bog'lanib qolardi va ikkala tarmoqni
    /// (panelli va panelsiz) bir vaqtda tekshirib bo'lmasdi.
    /// </para>
    /// </summary>
    public bool UsesFloatingControls { get; init; } = CaptureExclusion.IsSupported;

    /// <summary>Sahifadagi "Pauza" va "To'xtatish" tugmalari — faqat suzuvchi panel yo'q bo'lganda.</summary>
    public bool ShowsInlineControls => !UsesFloatingControls;

    /// <summary>Sahifadagi to'xtatish tugmalari aynan hozir ko'rinishi kerakmi.</summary>
    public bool ShowsInlineStopControls => ShowsInlineControls && !IsIdleState;

    /// <summary>
    /// Yozuv ketayotgani va boshqaruv suzuvchi panelda ekani haqidagi eslatma. Oyna
    /// kichraytirilmagan holatda foydalanuvchi tugmalarni sahifada qidirmasligi uchun.
    /// </summary>
    public bool ShowsOverlayHint => UsesFloatingControls && !IsIdleState;

    // ------------------------------------------------------------------ Manba

    public ObservableCollection<RecordingSourceInfo> Displays { get; } = [];

    public ObservableCollection<RecordingSourceInfo> Windows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowMode))]
    private RecordingSourceKind _sourceKind = RecordingSourceKind.Display;

    public bool IsWindowMode => SourceKind == RecordingSourceKind.Window;

    [ObservableProperty]
    private RecordingSourceInfo? _selectedDisplay;

    [ObservableProperty]
    private RecordingSourceInfo? _selectedWindow;

    // ------------------------------------------------------------------ Video

    public IReadOnlyList<int> FramerateChoices { get; } = [15, 24, 30, 60];

    [ObservableProperty]
    private int _framerate = 30;

    [ObservableProperty]
    private RecordingQuality _quality = RecordingQuality.Medium;

    [ObservableProperty]
    private VideoEncoderKind _encoder = VideoEncoderKind.H264;

    [ObservableProperty]
    private bool _useHardwareEncoding = true;

    [ObservableProperty]
    private bool _showCursor = true;

    [ObservableProperty]
    private bool _highlightClicks;

    // ------------------------------------------------------------------ Ovoz

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = [];

    public ObservableCollection<AudioDeviceInfo> Speakers { get; } = [];

    [ObservableProperty]
    private bool _recordSystemAudio = true;

    [ObservableProperty]
    private bool _recordMicrophone;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedMicrophone;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedSpeaker;

    // ------------------------------------------------------------------ Chiqish

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _outputFolder = string.Empty;

    /// <summary>Yozish boshlanganda dastur oynasi kichraytirilsinmi.</summary>
    [ObservableProperty]
    private bool _minimizeWhileRecording = true;

    [ObservableProperty]
    private string? _lastRecordingPath;

    public bool HasLastRecording => !string.IsNullOrEmpty(LastRecordingPath);

    // ------------------------------------------------------------------ Holat

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsPaused), nameof(IsIdleState), nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(ShowsInlineStopControls), nameof(ShowsOverlayHint))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(TogglePauseCommand), nameof(RefreshSourcesCommand))]
    private RecorderState _state = RecorderState.Idle;

    public bool IsRecording => State is RecorderState.Recording;

    public bool IsPaused => State is RecorderState.Paused;

    /// <summary>Sozlamalarni o'zgartirish faqat shu holatda mumkin.</summary>
    public bool IsIdleState => State is RecorderState.Idle;

    public string StateText => State switch
    {
        RecorderState.Starting => "Tayyorlanmoqda…",
        RecorderState.Recording => "Yozilyapti",
        RecorderState.Paused => "Vaqtincha to'xtatildi",
        RecorderState.Finishing => "Fayl yakunlanmoqda…",
        _ => "Tayyor"
    };

    [ObservableProperty]
    private TimeSpan _elapsed;

    public string ElapsedText => $"{(int)Elapsed.TotalHours:00}:{Elapsed.Minutes:00}:{Elapsed.Seconds:00}";

    public bool IsSupported => _recorder.IsSupported;

    // ------------------------------------------------------------------ Komandalar

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        try
        {
            var path = _recorder.StartRecording(BuildOptions());

            LastRecordingPath = null;
            StatusMessage = $"Yozilmoqda: {Path.GetFileName(path)}";
            _startedAt = DateTimeOffset.Now;
            _pausedTotal = TimeSpan.Zero;
            _pausedAt = null;
            Elapsed = TimeSpan.Zero;
            StartTimer();

            // Panel oynadan OLDIN ochiladi: kichraytirish animatsiyasi tugagunicha ekranda
            // boshqaruvsiz bo'shliq qolmasligi kerak.
            if (UsesFloatingControls)
                OverlayVisibilityChanged?.Invoke(this, true);

            if (MinimizeWhileRecording)
            {
                _minimizedForRecording = true;
                MinimizeRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (PdfServiceException ex)
        {
            StatusMessage = ex.Message;
            DialogService.ShowError("Yozib olishni boshlab bo'lmadi", ex.Message);
        }
    }

    private bool CanStart() => IsIdleState && !string.IsNullOrWhiteSpace(OutputFolder) && IsSupported;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _recorder.StopRecording();

    private bool CanStop() => State is RecorderState.Recording or RecorderState.Paused;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void TogglePause()
    {
        if (IsPaused)
        {
            // To'xtab turgan vaqt yozuvga kirmaydi, shuning uchun uni umumiy hisobdan chiqaramiz.
            if (_pausedAt is { } since)
                _pausedTotal += DateTimeOffset.Now - since;

            _pausedAt = null;
            _recorder.ResumeRecording();
        }
        else
        {
            _pausedAt = DateTimeOffset.Now;
            _recorder.PauseRecording();
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdleState))]
    private void RefreshSources()
    {
        var previousDisplay = SelectedDisplay?.Id;
        var previousWindow = SelectedWindow?.Id;

        Displays.Clear();
        foreach (var display in _recorder.GetDisplays())
            Displays.Add(display);

        Windows.Clear();
        foreach (var window in _recorder.GetWindows())
            Windows.Add(window);

        SelectedDisplay = Displays.FirstOrDefault(d => d.Id == previousDisplay) ?? Displays.FirstOrDefault();
        SelectedWindow = Windows.FirstOrDefault(w => w.Id == previousWindow) ?? Windows.FirstOrDefault();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var chosen = DialogService.SelectFolder("Videolar saqlanadigan papkani tanlang", OutputFolder);
        if (!string.IsNullOrWhiteSpace(chosen))
            OutputFolder = chosen;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var target = LastRecordingPath is not null && File.Exists(LastRecordingPath)
            ? LastRecordingPath
            : OutputFolder;

        DialogService.RevealInExplorer(target);
    }

    // ------------------------------------------------------------------ Ichki

    private ScreenRecordingOptions BuildOptions() => new()
    {
        Source = SourceKind == RecordingSourceKind.Window ? SelectedWindow : SelectedDisplay,
        Framerate = Framerate,
        Quality = Quality,
        Encoder = Encoder,
        UseHardwareEncoding = UseHardwareEncoding,
        RecordSystemAudio = RecordSystemAudio,
        RecordMicrophone = RecordMicrophone,
        SystemAudioDeviceId = SelectedSpeaker?.Id,
        MicrophoneDeviceId = SelectedMicrophone?.Id,
        ShowCursor = ShowCursor,
        HighlightClicks = HighlightClicks,
        OutputFolder = OutputFolder
    };

    private void RefreshAudioDevices()
    {
        Microphones.Clear();
        foreach (var device in _recorder.GetMicrophones())
            Microphones.Add(device);

        Speakers.Clear();
        foreach (var device in _recorder.GetSpeakers())
            Speakers.Add(device);

        SelectedMicrophone = Microphones.FirstOrDefault();
        SelectedSpeaker = Speakers.FirstOrDefault();
    }

    /// <summary>
    /// O'tgan vaqtni sekundiga bir marta yangilaydi.
    /// <para>
    /// <c>ConfigureAwait(<b>false</b>)</c> ataylab: sikl hech qanday sinxronizatsiya
    /// kontekstini ushlab qolmaydi. Ushlab qolgan taqdirda u kontekst tugatilgandan keyin
    /// ham unga ish yuborishda davom etardi — bu holat sinov muhitida jarayonni butunlay
    /// qulatgan (davomni yetkazish `try` blokidan tashqarida bo'lgani uchun uni ushlab ham
    /// bo'lmaydi).
    /// </para>
    /// <para>
    /// UI uchun bu xavfsiz: WPF oddiy (kolleksiya bo'lmagan) xossaning
    /// <c>PropertyChanged</c> xabarini fon oqimidan kelganda o'zi dispetcherga o'tkazadi.
    /// </para>
    /// </summary>
    private void StartTimer()
    {
        StopTimer();
        _timerCancellation = new CancellationTokenSource();

        // `async void` ATAYLAB ishlatilmaydi. U sinxronizatsiya kontekstida "tugallanmagan
        // amal" sifatida ro'yxatga olinadi va kontekst uni kutishga majbur bo'ladi — cheksiz
        // sikl uchun bu kontekstning umuman yakunlanmasligini anglatadi. `async Task` da
        // bunday ro'yxatga olish yo'q.
        _ = RunTimerAsync(_timerCancellation.Token);
    }

    private async Task RunTimerAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (IsPaused)
                    continue;

                Elapsed = DateTimeOffset.Now - _startedAt - _pausedTotal;
                OnPropertyChanged(nameof(ElapsedText));
            }
        }
        catch (Exception)
        {
            // Bu metod `async void`: bu yerdan chiqib ketgan har qanday istisno butun dasturni
            // yiqitadi. Taymer esa faqat ekrandagi soatni yuritadi — yozuvning o'ziga
            // aloqasi yo'q, shuning uchun uning nosozligi hech qachon dastur qulashiga
            // sabab bo'lmasligi kerak. Odatdagi holat — bekor qilish (to'xtatildi).
        }
    }

    /// <summary>
    /// Taymerni to'xtatadi. Maydon tashlashdan OLDIN bo'shatiladi: aks holda ayni damda
    /// ishlayotgan sikl allaqachon tashlangan manbaga murojaat qilib qolishi mumkin.
    /// </summary>
    private void StopTimer()
    {
        var cancellation = _timerCancellation;
        _timerCancellation = null;

        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Allaqachon tashlangan — qiladigan ish yo'q.
        }

        cancellation.Dispose();
    }

    private void OnStateChanged(object? sender, RecorderStateChangedEventArgs e)
    {
        State = e.State;

        // Idle — seansning yagona tugash nuqtasi: servis uni ham muvaffaqiyatli yakunda,
        // ham xatoda, ham umuman boshlanmaganda o'rnatadi. Shuning uchun tozalash shu yerda.
        if (State is not RecorderState.Idle)
            return;

        // Taymer odatda RecordingCompleted/Failed da to'xtaydi, lekin seans ular kelmasdan
        // ham tugashi mumkin (masalan yozuv umuman boshlanmagan) — shunda u abadiy tikillab
        // qolardi.
        StopTimer();

        OverlayVisibilityChanged?.Invoke(this, false);

        if (!_minimizedForRecording)
            return;

        _minimizedForRecording = false;
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecordingCompleted(object? sender, ScreenRecordingCompletedEventArgs e)
    {
        StopTimer();
        Elapsed = e.Duration;
        OnPropertyChanged(nameof(ElapsedText));

        LastRecordingPath = e.FilePath;
        OnPropertyChanged(nameof(HasLastRecording));
        StatusMessage = $"Saqlandi: {Path.GetFileName(e.FilePath)}";
    }

    private void OnRecordingFailed(object? sender, ScreenRecordingFailedEventArgs e)
    {
        StopTimer();

        LastRecordingPath = e.PartialFilePath;
        OnPropertyChanged(nameof(HasLastRecording));
        StatusMessage = e.Message;

        var detail = e.PartialFilePath is null
            ? e.Message
            : $"{e.Message}\n\nChala fayl saqlanib qoldi:\n{e.PartialFilePath}";

        DialogService.ShowError("Yozib olish uzildi", detail);
    }

    partial void OnLastRecordingPathChanged(string? value) => OnPropertyChanged(nameof(HasLastRecording));

    partial void OnElapsedChanged(TimeSpan value) => OnPropertyChanged(nameof(ElapsedText));

    /// <summary>
    /// Taymerni to'xtatadi va servis hodisalaridan uziladi.
    /// <para>
    /// Sahifa dastur bilan birga yashaydi, shuning uchun bu odatda faqat yopilishda ishlaydi.
    /// Lekin ishlab turgan <see cref="PeriodicTimer"/> — egasi tashlab yuborilgandan keyin ham
    /// tikillashda davom etadigan resurs, va uni to'xtatadigan joy bo'lishi kerak.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        StopTimer();

        _recorder.StateChanged -= OnStateChanged;
        _recorder.RecordingCompleted -= OnRecordingCompleted;
        _recorder.RecordingFailed -= OnRecordingFailed;
    }

    /// <summary>Standart joy: "Videolar\Yordamchi". Yo'q bo'lsa yaratiladi.</summary>
    private static string DefaultOutputFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videos))
            videos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var folder = Path.Combine(videos, "Yordamchi");

        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception)
        {
            // Papka yaratib bo'lmasa foydalanuvchi o'zi tanlaydi — bu yerda yiqilish mantiqsiz.
            return videos;
        }
    }
}
