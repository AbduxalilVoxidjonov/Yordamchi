using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Yordamchi.Models;
using Yordamchi.Services.Abstractions;
using Yordamchi.Tests.TestSupport;
using Yordamchi.ViewModels;

namespace Yordamchi.Tests.ViewModels;

/// <summary>
/// <see cref="ScreenRecorderViewModel"/> sinovlari, asosan <b>seans hayot sikli</b> haqida:
/// yozuv boshlanganda oyna kichrayadi va suzuvchi boshqaruv paneli ochiladi, tugagach ikkalasi
/// ham qaytariladi.
/// <para>
/// Bu yerdagi eng qimmat xatolik — foydalanuvchini boshqaruvsiz qoldirish: oyna kichraytirilgan,
/// panel esa ochilmagan yoki erta yopilgan holatda yozuvni to'xtatadigan hech narsa qolmaydi.
/// </para>
/// </summary>
public sealed class ScreenRecorderViewModelTests : IDisposable
{
    private readonly TempWorkspace _temp = new();
    private readonly IScreenRecorderService _recorder = Substitute.For<IScreenRecorderService>();
    private readonly FakeDialogService _dialogs = new();

    /// <summary>
    /// Yaratilgan sahifalar. Ular albatta tashlanishi kerak: yozuv boshlangach sahifa
    /// soniyalik taymerni yurgizadi va u sinov tugagach ham tikillashda davom etardi.
    /// </summary>
    private readonly List<ScreenRecorderViewModel> _created = [];

    public ScreenRecorderViewModelTests()
    {
        _recorder.IsSupported.Returns(true);
        _recorder.GetDisplays().Returns([new RecordingSourceInfo(RecordingSourceKind.Display, "DISPLAY1", "Monitor 1")]);
        _recorder.GetWindows().Returns([]);
        _recorder.GetMicrophones().Returns([new AudioDeviceInfo(null, "Tizim tanlagan")]);
        _recorder.GetSpeakers().Returns([new AudioDeviceInfo(null, "Tizim tanlagan")]);
        _recorder.StartRecording(default!).ReturnsForAnyArgs(_ => _temp.At("yozuv.mp4"));
    }

    public void Dispose()
    {
        foreach (var vm in _created)
            vm.Dispose();

        _temp.Dispose();
    }

    // =================================================================================
    //  Suzuvchi panel
    // =================================================================================

    [Fact]
    public void Starting_opens_the_floating_control_panel()
    {
        var vm = Create(floatingControls: true);
        var events = TrackOverlay(vm);

        vm.StartCommand.Execute(null);

        Assert.Equal([true], events);
    }

    [Fact]
    public void The_panel_opens_before_the_window_is_minimised()
    {
        // Tartib muhim: oyna kichrayish animatsiyasi davomida ekranda boshqaruvsiz bo'shliq
        // qolmasligi kerak.
        var vm = Create(floatingControls: true);
        var order = new List<string>();

        vm.OverlayVisibilityChanged += (_, visible) => order.Add(visible ? "panel-ochildi" : "panel-yopildi");
        vm.MinimizeRequested += (_, _) => order.Add("kichraydi");

        vm.StartCommand.Execute(null);

        Assert.Equal(["panel-ochildi", "kichraydi"], order);
    }

    [Fact]
    public void Nothing_floats_on_a_system_that_cannot_hide_it_from_the_recording()
    {
        // Eski Windows da panelni yozuvdan yashirib bo'lmaydi — u har kadrda ko'rinib qolardi.
        // Bunday tizimda panel umuman ochilmaydi, boshqaruv esa sahifada qoladi.
        var vm = Create(floatingControls: false);
        var events = TrackOverlay(vm);

        vm.StartCommand.Execute(null);

        Assert.Empty(events);
        Assert.True(vm.ShowsInlineControls);
    }

    [Fact]
    public void The_panel_closes_when_the_recording_ends()
    {
        var vm = Create(floatingControls: true);
        vm.StartCommand.Execute(null);

        var events = TrackOverlay(vm);
        RaiseState(RecorderState.Idle);

        Assert.Equal([false], events);
    }

    [Fact]
    public void The_panel_closes_when_the_recording_fails_to_start()
    {
        // Servis xatoda ham Idle ga qaytadi — panel o'sha yerda yopilishi kerak, aks holda u
        // ekranda ishlamaydigan tugmalar bilan osilib qolardi.
        var vm = Create(floatingControls: true);
        vm.StartCommand.Execute(null);

        var events = TrackOverlay(vm);
        RaiseState(RecorderState.Idle);
        _recorder.RecordingFailed += Raise.EventWith(new ScreenRecordingFailedEventArgs("Kodek yo'q", null));

        Assert.Equal([false], events);
    }

    [Fact]
    public void The_panel_stays_open_while_the_recording_is_paused()
    {
        var vm = Create(floatingControls: true);
        vm.StartCommand.Execute(null);

        var events = TrackOverlay(vm);
        RaiseState(RecorderState.Recording);
        RaiseState(RecorderState.Paused);

        Assert.Empty(events);
        Assert.True(vm.IsPaused);
    }

    // =================================================================================
    //  Oynani kichraytirish va qaytarish
    // =================================================================================

    [Fact]
    public void The_window_comes_back_after_the_recording_ends()
    {
        var vm = Create(floatingControls: true);
        var restored = 0;
        vm.RestoreRequested += (_, _) => restored++;

        vm.StartCommand.Execute(null);
        RaiseState(RecorderState.Idle);

        Assert.Equal(1, restored);
    }

    [Fact]
    public void The_window_is_not_touched_when_the_user_asked_to_keep_it_open()
    {
        // "Kichraytirilsin" belgisi o'chirilgan bo'lsa, oynani biz kichraytirmaganmiz —
        // demak uni qaytarishga ham haqimiz yo'q (foydalanuvchi o'zi kichraytirgan bo'lishi mumkin).
        var vm = Create(floatingControls: true);
        vm.MinimizeWhileRecording = false;

        var minimised = 0;
        var restored = 0;
        vm.MinimizeRequested += (_, _) => minimised++;
        vm.RestoreRequested += (_, _) => restored++;

        vm.StartCommand.Execute(null);
        RaiseState(RecorderState.Idle);

        Assert.Equal(0, minimised);
        Assert.Equal(0, restored);
    }

    [Fact]
    public void The_window_is_restored_only_once_per_session()
    {
        // Servis Idle ni bir necha marta yuborishi mumkin; oyna har safar oldinga otilib
        // chiqmasligi kerak.
        var vm = Create(floatingControls: true);
        var restored = 0;
        vm.RestoreRequested += (_, _) => restored++;

        vm.StartCommand.Execute(null);
        RaiseState(RecorderState.Idle);
        RaiseState(RecorderState.Idle);

        Assert.Equal(1, restored);
    }

    [Fact]
    public void A_failed_start_leaves_the_window_and_the_panel_alone()
    {
        // Yozuv umuman boshlanmagan bo'lsa oynani kichraytirish ham, panel ochish ham mantiqsiz.
        _recorder.StartRecording(default!)
            .ThrowsForAnyArgs(new PdfServiceException(PdfErrorKind.OperationFailed, "Manba topilmadi"));

        var vm = Create(floatingControls: true);
        var overlay = TrackOverlay(vm);
        var minimised = 0;
        vm.MinimizeRequested += (_, _) => minimised++;

        vm.StartCommand.Execute(null);

        Assert.Empty(overlay);
        Assert.Equal(0, minimised);
        Assert.Single(_dialogs.ShownErrors);
    }

    // =================================================================================
    //  Sahifadagi boshqaruv ko'rinishi
    // =================================================================================

    [Fact]
    public void The_page_hides_its_stop_buttons_and_points_at_the_panel_instead()
    {
        var vm = Create(floatingControls: true);

        Assert.False(vm.ShowsInlineStopControls);
        Assert.False(vm.ShowsOverlayHint);

        RaiseState(RecorderState.Recording);

        Assert.False(vm.ShowsInlineStopControls);
        Assert.True(vm.ShowsOverlayHint);
    }

    [Fact]
    public void The_page_keeps_its_stop_buttons_when_there_is_no_panel()
    {
        var vm = Create(floatingControls: false);

        // Bo'sh holatda tugmalar baribir yashirin — ular "Yozishni boshlash" bilan bir joyda turadi.
        Assert.False(vm.ShowsInlineStopControls);

        RaiseState(RecorderState.Recording);

        Assert.True(vm.ShowsInlineStopControls);
        Assert.False(vm.ShowsOverlayHint);
    }

    // =================================================================================
    //  Komandalar
    // =================================================================================

    [Fact]
    public void Stop_is_only_available_once_something_is_being_recorded()
    {
        var vm = Create(floatingControls: true);

        Assert.False(vm.StopCommand.CanExecute(null));

        RaiseState(RecorderState.Recording);
        Assert.True(vm.StopCommand.CanExecute(null));

        RaiseState(RecorderState.Paused);
        Assert.True(vm.StopCommand.CanExecute(null));

        RaiseState(RecorderState.Finishing);
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact]
    public void Pause_and_resume_reach_the_recorder()
    {
        var vm = Create(floatingControls: true);
        vm.StartCommand.Execute(null);
        RaiseState(RecorderState.Recording);

        vm.TogglePauseCommand.Execute(null);
        _recorder.Received(1).PauseRecording();

        RaiseState(RecorderState.Paused);
        vm.TogglePauseCommand.Execute(null);
        _recorder.Received(1).ResumeRecording();
    }

    [Fact]
    public void Starting_is_blocked_without_an_output_folder()
    {
        var vm = Create(floatingControls: true);

        vm.OutputFolder = "   ";

        Assert.False(vm.StartCommand.CanExecute(null));
    }

    // =================================================================================
    //  Yordamchilar
    // =================================================================================

    private ScreenRecorderViewModel Create(bool floatingControls)
    {
        var vm = new ScreenRecorderViewModel(_recorder, _dialogs)
        {
            UsesFloatingControls = floatingControls,
            OutputFolder = _temp.CreateFolder("videolar")
        };

        _created.Add(vm);
        return vm;
    }

    private static List<bool> TrackOverlay(ScreenRecorderViewModel vm)
    {
        var events = new List<bool>();
        vm.OverlayVisibilityChanged += (_, visible) => events.Add(visible);
        return events;
    }

    private void RaiseState(RecorderState state) =>
        _recorder.StateChanged += Raise.EventWith(new RecorderStateChangedEventArgs(state));
}
