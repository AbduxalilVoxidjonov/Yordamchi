using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yordamchi.Helpers;
using Yordamchi.Remoting.Discovery;
using Yordamchi.Remoting.Master;
using Yordamchi.Services.Abstractions;

namespace Yordamchi.ViewModels;

/// <summary>Tarmoqda topilgan bitta kompyuter.</summary>
public sealed partial class DiscoveredComputerViewModel : ObservableObject
{
    public DiscoveredComputerViewModel(string name, string host, int port)
    {
        Name = string.IsNullOrWhiteSpace(name) ? host : name;
        Host = host;
        Port = port;
    }

    public string Name { get; }

    public string Host { get; }

    public int Port { get; }

    public string Endpoint => $"{Host}:{Port}";
}

/// <summary>
/// Master paneli: tarmoqdagi agentlarni topadi va tanlangan kompyuterning ekranini ko'rsatadi.
/// <para>
/// Tarmoq ishi (<see cref="MasterSession"/>, <see cref="DiscoveryListener"/>) fon oqimlarida
/// bo'ladi; ularning hodisalari boshlanish paytida olingan UI konteksti orqali xossalarga
/// o'tkaziladi. Discovery <b>faqat foydalanuvchi "Qidirish" ni bosganda</b> yoqiladi — dastur
/// ochilishida UDP portini band qilib, brandmauer so'rovini chiqarmaslik uchun.
/// </para>
/// </summary>
public sealed partial class RemoteViewerViewModel : ViewModelBase
{
    private readonly SynchronizationContext? _ui;

    private DiscoveryListener? _discovery;
    private CancellationTokenSource? _discoveryCts;
    private MasterSession? _session;

    public RemoteViewerViewModel(IDialogService dialogService)
        : base(dialogService)
    {
        _ui = SynchronizationContext.Current;
    }

    public override string Title => "Kompyuter ekranlari";

    public override string Description =>
        "Tarmoqdagi agentli kompyuterlarni toping va ekranini ko'ring.";

    /// <summary>Topilgan kompyuterlar.</summary>
    public ObservableCollection<DiscoveredComputerViewModel> Computers { get; } = [];

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _manualHost = "127.0.0.1";

    [ObservableProperty]
    private int _manualPort = 5406;

    /// <summary>Hozir ko'rinib turgan ekran kadri.</summary>
    [ObservableProperty]
    private ImageSource? _currentFrame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Ulanmagan.";

    // =================================================================================
    //  Qidirish (discovery)
    // =================================================================================

    [RelayCommand]
    private void ToggleSearch()
    {
        if (IsSearching)
            StopSearch();
        else
            StartSearch();
    }

    private void StartSearch()
    {
        Computers.Clear();
        _discoveryCts = new CancellationTokenSource();
        var listener = new DiscoveryListener();
        listener.PeerDiscovered += OnPeerDiscovered;
        _discovery = listener;
        IsSearching = true;
        ConnectionStatus = "Kompyuterlar qidirilmoqda…";

        _ = RunDiscoveryAsync(listener, _discoveryCts.Token);
    }

    private async Task RunDiscoveryAsync(DiscoveryListener listener, CancellationToken token)
    {
        try
        {
            await listener.RunAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Post(() =>
            {
                IsSearching = false;
                ConnectionStatus = $"Qidiruvni boshlab bo'lmadi: {ex.Message}";
            });
        }
    }

    private void StopSearch()
    {
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryCts = null;

        if (_discovery is not null)
            _discovery.PeerDiscovered -= OnPeerDiscovered;
        _discovery = null;

        IsSearching = false;
    }

    private void OnPeerDiscovered(DiscoveredPeer peer)
    {
        // Faqat agentlar — master mayoqlari (agar bo'lsa) e'tiborsiz.
        if (peer.Beacon.Role != PeerRole.Agent)
            return;

        var host = peer.Address.ToString();
        var port = peer.Beacon.TcpPort;

        Post(() =>
        {
            var exists = Computers.Any(c => c.Host == host && c.Port == port);
            if (!exists)
                Computers.Add(new DiscoveredComputerViewModel(peer.Beacon.MachineName, host, port));
        });
    }

    // =================================================================================
    //  Ulanish
    // =================================================================================

    [RelayCommand]
    private Task ConnectManual() => ConnectAsync(ManualHost, ManualPort);

    [RelayCommand]
    private Task ConnectComputer(DiscoveredComputerViewModel? computer) =>
        computer is null ? Task.CompletedTask : ConnectAsync(computer.Host, computer.Port);

    private async Task ConnectAsync(string host, int port)
    {
        await DisconnectAsync().ConfigureAwait(true);

        ConnectionStatus = $"{host}:{port} ga ulanmoqda…";

        try
        {
            var session = await MasterSession.ConnectAsync(host, port).ConfigureAwait(true);
            session.FrameReceived += OnFrameReceived;
            session.Disconnected += OnSessionDisconnected;
            _session = session;

            IsConnected = true;
            ConnectionStatus = $"Ulandi: {host}:{port}. Ekran uzatilmoqda…";

            await session.StartScreenAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ConnectionStatus = $"Ulanib bo'lmadi: {ex.Message}";
            DialogService.ShowError("Ulanish xatosi", ex.Message);
            await DisconnectAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task Disconnect() => await DisconnectAsync().ConfigureAwait(true);

    private async Task DisconnectAsync()
    {
        var session = _session;
        _session = null;

        if (session is not null)
        {
            session.FrameReceived -= OnFrameReceived;
            session.Disconnected -= OnSessionDisconnected;

            try
            {
                await session.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Yopilishdagi xato ahamiyatsiz.
            }
        }

        IsConnected = false;
        CurrentFrame = null;

        if (!IsSearching)
            ConnectionStatus = "Ulanmagan.";
    }

    private void OnFrameReceived(RemoteFrame frame)
    {
        // Muzlatilgan rasmni fon oqimida yasab, UI xossasiga o'tkazamiz.
        var image = FrameImage.TryCreate(frame);
        if (image is not null)
            Post(() => CurrentFrame = image);
    }

    private void OnSessionDisconnected() => Post(() =>
    {
        IsConnected = false;
        ConnectionStatus = "Ulanish uzildi.";
    });

    /// <summary>Fon oqimidagi hodisani UI oqimiga o'tkazadi (kontekst bo'lmasa — o'sha joyda).</summary>
    private void Post(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
