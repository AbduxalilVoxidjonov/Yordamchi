using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public sealed partial class ScreenRecorderViewModel : ViewModelBase
{
    private readonly IScreenRecorderService _recorder;

    private CancellationTokenSource? _timerCancellation;
    private DateTimeOffset _startedAt;
    private TimeSpan _pausedTotal;
    private DateTimeOffset? _pausedAt;

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

            if (MinimizeWhileRecording)
                MinimizeRequested?.Invoke(this, EventArgs.Empty);
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
    /// O'tgan vaqtni sekundiga bir marta yangilaydi. <see cref="PeriodicTimer"/> tanlangan,
    /// chunki u UI kutubxonalariga bog'lanmaydi; <c>ConfigureAwait(true)</c> tufayli
    /// davomi baribir UI oqimida bajariladi.
    /// </summary>
    private async void StartTimer()
    {
        StopTimer();
        _timerCancellation = new CancellationTokenSource();
        var token = _timerCancellation.Token;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true))
            {
                if (IsPaused)
                    continue;

                Elapsed = DateTimeOffset.Now - _startedAt - _pausedTotal;
                OnPropertyChanged(nameof(ElapsedText));
            }
        }
        catch (OperationCanceledException)
        {
            // To'xtatildi — kutilgan holat.
        }
    }

    private void StopTimer()
    {
        _timerCancellation?.Cancel();
        _timerCancellation?.Dispose();
        _timerCancellation = null;
    }

    private void OnStateChanged(object? sender, RecorderStateChangedEventArgs e) => State = e.State;

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
