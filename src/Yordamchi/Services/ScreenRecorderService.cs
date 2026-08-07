using System.IO;
using System.Windows;
using System.Windows.Threading;
using ScreenRecorderLib;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using LibRecorder = ScreenRecorderLib.Recorder;
// Kutubxonaning hodisa argumentlari bizning shartnomamizdagilar bilan bir xil nomlanadi,
// shuning uchun ular ataylab taxallus bilan ajratiladi.
using LibCompleteEventArgs = ScreenRecorderLib.RecordingCompleteEventArgs;
using LibFailedEventArgs = ScreenRecorderLib.RecordingFailedEventArgs;
using LibStatusEventArgs = ScreenRecorderLib.RecordingStatusEventArgs;

namespace Yordamchi.Services;

/// <summary>
/// <see cref="IScreenRecorderService"/> ning ScreenRecorderLib (Windows Media Foundation)
/// ustidagi implementatsiyasi.
/// <para>
/// Kutubxona kadrlarni Desktop Duplication / Windows Graphics Capture orqali oladi va
/// darhol H.264 yoki H.265 ga kodlaydi — oraliq kadrlar diskka yozilmaydi. Aynan shu
/// sababli uzoq yozuvlarda ham xotira o'smaydi va kadrlar tushib qolmaydi.
/// </para>
/// <para>
/// Kutubxonaning hamma hodisalari fon oqimida ko'tariladi, shu sababli ular shu yerda
/// dispetcher orqali UI oqimiga o'tkaziladi — shartnoma shuni va'da qiladi.
/// </para>
/// </summary>
public sealed class ScreenRecorderService : IScreenRecorderService
{
    /// <summary>Windows 10 1903 (build 18362) — Windows Graphics Capture shu versiyadan bor.</summary>
    private const int MinimumWindowsBuild = 18362;

    private readonly object _sync = new();

    private LibRecorder? _recorder;
    private string? _currentPath;
    private DateTimeOffset _startedAt;
    private bool _disposed;

    public RecorderState State { get; private set; } = RecorderState.Idle;

    public bool IsSupported =>
        OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= MinimumWindowsBuild;

    public event EventHandler<RecorderStateChangedEventArgs>? StateChanged;

    public event EventHandler<ScreenRecordingCompletedEventArgs>? RecordingCompleted;

    public event EventHandler<ScreenRecordingFailedEventArgs>? RecordingFailed;

    // ---------------------------------------------------------------- Ro'yxatlar

    public IReadOnlyList<RecordingSourceInfo> GetDisplays()
    {
        try
        {
            return LibRecorder.GetDisplays()
                .Select(display => new RecordingSourceInfo(
                    RecordingSourceKind.Display,
                    display.DeviceName,
                    BuildDisplayTitle(display)))
                .ToList();
        }
        catch (Exception)
        {
            // Monitorlar ro'yxatini olmaslik dasturni yiqitmasligi kerak: foydalanuvchi
            // "Yangilash" tugmasi bilan qayta urinib ko'radi.
            return [];
        }
    }

    public IReadOnlyList<RecordingSourceInfo> GetWindows()
    {
        try
        {
            return LibRecorder.GetWindows()
                .Where(window => window.IsValidWindow() && !window.IsMinmimized())
                .Where(window => !string.IsNullOrWhiteSpace(window.Title))
                .Select(window => new RecordingSourceInfo(
                    RecordingSourceKind.Window,
                    window.Handle.ToInt64().ToString(),
                    window.Title))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetMicrophones() => GetAudioDevices(AudioDeviceSource.InputDevices);

    public IReadOnlyList<AudioDeviceInfo> GetSpeakers() => GetAudioDevices(AudioDeviceSource.OutputDevices);

    // ---------------------------------------------------------------- Boshqaruv

    public string StartRecording(ScreenRecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsSupported)
        {
            throw new PdfServiceException(
                PdfErrorKind.MissingComponent,
                "Bu Windows versiyasida ekranni yozib olish qo'llab-quvvatlanmaydi. Windows 10 (1903) yoki undan yangisi kerak.");
        }

        lock (_sync)
        {
            if (State != RecorderState.Idle)
                throw new PdfServiceException(PdfErrorKind.OperationFailed, "Yozib olish allaqachon davom etmoqda.");

            var path = BuildOutputPath(options.OutputFolder);

            // Oldingi seansdan qolgan nusxani aynan shu yerda tozalaymiz: uni
            // OnRecordingComplete ichida yo'q qilish kutubxonaning o'z oqimida
            // Dispose chaqirishga olib keladi va osilib qolish xavfi bor.
            DisposeRecorder();

            try
            {
                _recorder = LibRecorder.CreateRecorder(BuildRecorderOptions(options));
                _recorder.OnStatusChanged += OnStatusChanged;
                _recorder.OnRecordingComplete += OnRecordingComplete;
                _recorder.OnRecordingFailed += OnRecordingFailed;

                _currentPath = path;
                _startedAt = DateTimeOffset.Now;
                SetState(RecorderState.Starting);

                _recorder.Record(path);
                return path;
            }
            catch (PdfServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DisposeRecorder();
                SetState(RecorderState.Idle);

                throw new PdfServiceException(
                    PdfErrorKind.OperationFailed,
                    $"Yozib olishni boshlab bo'lmadi: {ex.Message}",
                    path,
                    ex);
            }
        }
    }

    public void StopRecording()
    {
        lock (_sync)
        {
            if (_recorder is null || State is RecorderState.Idle or RecorderState.Finishing)
                return;

            _recorder.Stop();
        }
    }

    public void PauseRecording()
    {
        lock (_sync)
        {
            if (_recorder is not null && State == RecorderState.Recording)
                _recorder.Pause();
        }
    }

    public void ResumeRecording()
    {
        lock (_sync)
        {
            if (_recorder is not null && State == RecorderState.Paused)
                _recorder.Resume();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        lock (_sync)
        {
            // Dastur yopilayotganda ham fayl to'g'ri yakunlanishi kerak, aks holda
            // .mp4 ochilmaydigan bo'lib qoladi (moov atomi yozilmay qoladi).
            try
            {
                if (State is RecorderState.Recording or RecorderState.Paused)
                    _recorder?.Stop();
            }
            catch (Exception)
            {
                // Yopilish yo'lida xato ko'rsatadigan joy yo'q.
            }

            DisposeRecorder();
        }
    }

    // ---------------------------------------------------------------- Sozlamalarni yig'ish

    private static RecorderOptions BuildRecorderOptions(ScreenRecordingOptions options)
    {
        var framerate = Math.Clamp(options.Framerate, 15, 60);

        return new RecorderOptions
        {
            SourceOptions = new SourceOptions
            {
                RecordingSources = [BuildSource(options)]
            },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                // Uniform — manba nisbatini saqlaydi; oyna yozilayotganda uning
                // o'lchami o'zgarsa ham video cho'zilib ketmaydi.
                Stretch = StretchMode.Uniform
            },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = framerate,
                Bitrate = BitrateFor(options.Quality, framerate),
                IsHardwareEncodingEnabled = options.UseHardwareEncoding,
                // Fayl yakunlanmay qolsa ham (masalan tok o'chsa) boshidan o'qiladigan
                // bo'lishi uchun; ko'chirib berishda ham foydali.
                IsMp4FastStartEnabled = true,
                // O'zgaruvchan kadr chastotasi (ataylab): ekran qimirlamay turganda kadr
                // takrorlanmaydi, ya'ni statik ekranli darslik videosi bir necha barobar
                // kichik chiqadi. Sifatga ta'sir qilmaydi — Media Foundation kadrlarga
                // vaqt tamg'asini yozadi, shuning uchun davomiylik to'g'ri qoladi.
                IsFixedFramerate = false,
                Encoder = BuildEncoder(options.Encoder)
            },
            AudioOptions = BuildAudioOptions(options),
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = options.ShowCursor,
                IsMouseClicksDetected = options.HighlightClicks
            },
            LogOptions = new LogOptions { IsLogEnabled = false }
        };
    }

    private static RecordingSourceBase BuildSource(ScreenRecordingOptions options)
    {
        if (options.Source is { Kind: RecordingSourceKind.Window } window
            && long.TryParse(window.Id, out var handle)
            && handle != 0)
        {
            return new WindowRecordingSource(new IntPtr(handle))
            {
                IsCursorCaptureEnabled = options.ShowCursor
            };
        }

        if (options.Source is { Kind: RecordingSourceKind.Display } display
            && !string.IsNullOrWhiteSpace(display.Id))
        {
            return new DisplayRecordingSource(display.Id)
            {
                IsCursorCaptureEnabled = options.ShowCursor
            };
        }

        // Hech narsa tanlanmagan bo'lsa — asosiy monitor.
        var main = DisplayRecordingSource.MainMonitor;
        main.IsCursorCaptureEnabled = options.ShowCursor;
        return main;
    }

    private static IVideoEncoder BuildEncoder(VideoEncoderKind kind) => kind switch
    {
        // H.265 da faqat CBR va Quality rejimlari bor.
        VideoEncoderKind.H265 => new H265VideoEncoder
        {
            BitrateMode = H265BitrateControlMode.CBR,
            EncoderProfile = H265Profile.Main
        },
        // Cheklanmagan VBR — ekran yozuvi uchun eng mos: statik kadrlarda bitrate
        // o'z-o'zidan tushadi, harakat paytida ko'tariladi.
        _ => new H264VideoEncoder
        {
            BitrateMode = H264BitrateControlMode.UnconstrainedVBR,
            EncoderProfile = H264Profile.High
        }
    };

    private static AudioOptions BuildAudioOptions(ScreenRecordingOptions options)
    {
        var anyAudio = options.RecordSystemAudio || options.RecordMicrophone;

        return new AudioOptions
        {
            IsAudioEnabled = anyAudio,
            IsOutputDeviceEnabled = options.RecordSystemAudio,
            IsInputDeviceEnabled = options.RecordMicrophone,
            // null — tizimning standart qurilmasi.
            AudioOutputDevice = options.RecordSystemAudio ? options.SystemAudioDeviceId : null,
            AudioInputDevice = options.RecordMicrophone ? options.MicrophoneDeviceId : null,
            // Ikkalasi birga yozilsa ovozlar 100% dan aralashtiriladi va yig'indi
            // "qirqilib" ketadi. Shu sababli birga ishlaganda ikkalasi ham pasaytiriladi.
            OutputVolume = options.RecordSystemAudio && options.RecordMicrophone ? 0.6f : 1.0f,
            InputVolume = options.RecordSystemAudio && options.RecordMicrophone ? 0.8f : 1.0f,
            Bitrate = AudioBitrate.bitrate_128kbps,
            Channels = AudioChannels.Stereo
        };
    }

    /// <summary>
    /// Sifat + kadr chastotasidan bitrate. 1080p uchun mo'ljallangan; kadr chastotasi
    /// 30 dan yuqori bo'lsa mutanosib ko'tariladi, aks holda 60 fps da video "sochiladi".
    /// </summary>
    private static int BitrateFor(RecordingQuality quality, int framerate)
    {
        var baseBitrate = quality switch
        {
            RecordingQuality.Low => 3_000_000,
            RecordingQuality.High => 16_000_000,
            _ => 8_000_000
        };

        return (int)(baseBitrate * Math.Max(1d, framerate / 30d));
    }

    private static string BuildOutputPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new PdfServiceException(PdfErrorKind.InvalidOptions, "Videolar saqlanadigan papka tanlanmagan.");

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            throw new PdfServiceException(
                PdfErrorKind.OutputNotWritable,
                $"Papkani yaratib bo'lmadi: {folder}",
                folder,
                ex);
        }

        var name = $"yozuv-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.mp4";
        return Path.Combine(folder, name);
    }

    private static string BuildDisplayTitle(RecordableDisplay display)
    {
        var friendly = string.IsNullOrWhiteSpace(display.FriendlyName) ? "Monitor" : display.FriendlyName;
        return $"{friendly} ({display.DeviceName})";
    }

    private static IReadOnlyList<AudioDeviceInfo> GetAudioDevices(AudioDeviceSource source)
    {
        // Birinchi element — "standart qurilma": foydalanuvchi ro'yxatdan hech nima
        // tanlamasa ham ovoz yozilib turishi kerak.
        var devices = new List<AudioDeviceInfo> { new(null, "Tizim tanlagan qurilma") };

        try
        {
            devices.AddRange(LibRecorder.GetSystemAudioDevices(source)
                .Where(device => !string.IsNullOrWhiteSpace(device.DeviceName))
                .Select(device => new AudioDeviceInfo(
                    device.DeviceName,
                    string.IsNullOrWhiteSpace(device.FriendlyName) ? device.DeviceName : device.FriendlyName)));
        }
        catch (Exception)
        {
            // Ovoz qurilmalari ro'yxati olinmasa ham video yozilaveradi.
        }

        return devices;
    }

    // ---------------------------------------------------------------- Hodisalar

    private void OnStatusChanged(object? sender, LibStatusEventArgs e) => SetState(e.Status switch
    {
        RecorderStatus.Recording => RecorderState.Recording,
        RecorderStatus.Paused => RecorderState.Paused,
        RecorderStatus.Finishing => RecorderState.Finishing,
        _ => RecorderState.Idle
    });

    private void OnRecordingComplete(object? sender, LibCompleteEventArgs e)
    {
        var duration = DateTimeOffset.Now - _startedAt;
        var path = string.IsNullOrWhiteSpace(e.FilePath) ? _currentPath ?? string.Empty : e.FilePath;

        _currentPath = null;
        SetState(RecorderState.Idle);
        Raise(() => RecordingCompleted?.Invoke(this, new ScreenRecordingCompletedEventArgs(path, duration)));
    }

    private void OnRecordingFailed(object? sender, LibFailedEventArgs e)
    {
        var partial = string.IsNullOrWhiteSpace(e.FilePath) ? _currentPath : e.FilePath;
        var message = string.IsNullOrWhiteSpace(e.Error) ? "Yozib olish kutilmaganda uzildi." : e.Error;

        _currentPath = null;
        SetState(RecorderState.Idle);
        Raise(() => RecordingFailed?.Invoke(this, new ScreenRecordingFailedEventArgs(message, partial)));
    }

    private void SetState(RecorderState state)
    {
        if (State == state)
            return;

        State = state;
        Raise(() => StateChanged?.Invoke(this, new RecorderStateChangedEventArgs(state)));
    }

    /// <summary>Kutubxona hodisalari fon oqimida keladi; obunachilar esa UI ni yangilaydi.</summary>
    private static void Raise(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    /// <summary>
    /// Nusxani hodisalardan uzadi va uni fon oqimida yo'q qiladi.
    /// <para>
    /// <c>Recorder.Dispose()</c> ni TO'G'RIDAN-TO'G'RI chaqirib bo'lmaydi: u o'zining native
    /// ishchi oqimini kutadi va sinovda cheksiz osilib qoldi. Agar bu UI oqimida yoki
    /// dastur yopilayotganda chaqirilsa, dastur muzlab qoladi. Nusxa bu paytda hodisalardan
    /// allaqachon uzilgan, shuning uchun uning qachon yo'q bo'lishi bizga muhim emas —
    /// muhimi, hech kim uni kutib turmasligi.
    /// </para>
    /// </summary>
    private void DisposeRecorder()
    {
        if (_recorder is null)
            return;

        var recorder = _recorder;
        _recorder = null;

        recorder.OnStatusChanged -= OnStatusChanged;
        recorder.OnRecordingComplete -= OnRecordingComplete;
        recorder.OnRecordingFailed -= OnRecordingFailed;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                recorder.Dispose();
            }
            catch (Exception)
            {
                // Kutubxona ichidagi tozalash xatosi bizga hech narsa bermaydi.
            }
        });
    }
}
