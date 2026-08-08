using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Yordamchi.Agent.Hosting;

namespace Yordamchi.Agent.Ui;

/// <summary>
/// Tizim majmuasidagi (tray) belgi — agent ishlayotganining <b>ko'rinadigan belgisi</b>.
/// <para>
/// <b>Nega bu shart.</b> Loyihaning asosiy qoidasi: yashirin kuzatuv yo'q. Kompyuterda o'tirgan
/// odam agent ishlayotganini, kim ulanganini va boshqaruv yoqilganmi-yo'qmi ko'rib turishi, hamda
/// ruxsatni <b>bir bosishda</b> olib qo'ya olishi kerak. Belgi shu vazifani bajaradi.
/// </para>
/// <para>
/// <b>Nega alohida oqim.</b> Tray belgisi Windows xabar halqasini (message loop) talab qiladi, u
/// esa bloklanmasligi kerak; agentning tarmoq halqasi esa asosiy oqimda ishlaydi. Shu sababli
/// belgi o'zining STA oqimida yashaydi.
/// </para>
/// <para>
/// <b>Nega o'zgarishlar taymer bilan yangilanadi.</b> Holat (ulanishlar soni, ruxsatlar) boshqa
/// oqimlardan o'zgaradi. UI elementiga boshqa oqimdan tegish taqiqlangani uchun bu yerda teskari
/// yo'l tanlangan: UI oqimidagi taymer holatni <b>o'zi so'rab</b> oladi. Bu <c>Invoke</c>
/// zanjirlarisiz, xatoga kam moyil yechim.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIndicator : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly string _title;
    private readonly AgentPermissions _permissions;
    private readonly Func<string> _statusText;
    private readonly Action _requestExit;
    private readonly string? _logPath;

    /// <summary>Boshqa oqimlardan kelgan bildirishnomalar navbati — UI oqimi uni bo'shatadi.</summary>
    private readonly ConcurrentQueue<(string Title, string Text)> _notifications = new();

    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private NotifyIcon? _icon;
    private ApplicationContext? _context;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _inputItem;
    private ToolStripMenuItem? _commandsItem;

    /// <param name="title">Sarlavha, masalan "Yordamchi agent — PC-12:5406".</param>
    /// <param name="permissions">Ruxsatlar — menyudagi belgilar shu qiymatlarni ko'rsatadi va o'zgartiradi.</param>
    /// <param name="statusText">Hozirgi holat matni (masalan "Ulanishlar: 1").</param>
    /// <param name="requestExit">"Chiqish" bosilganda — agentni tartibli to'xtatish.</param>
    /// <param name="logPath">Jurnal fayli (menyudan ochish uchun); bo'lmasa punkt ko'rsatilmaydi.</param>
    public TrayIndicator(
        string title,
        AgentPermissions permissions,
        Func<string> statusText,
        Action requestExit,
        string? logPath)
    {
        _title = title;
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _statusText = statusText ?? throw new ArgumentNullException(nameof(statusText));
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
        _logPath = logPath;
    }

    /// <summary>Belgini ko'rsatadi. Xabar halqasi tayyor bo'lguncha kutadi.</summary>
    public void Show()
    {
        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "Yordamchi agent — tray"
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // Belgi ko'rinmasidan turib "ulanish bo'ldi" degan bildirishnoma kelib qolmasligi uchun
        // qisqa kutish. Muhlat bor: tray ochilmasa ham agent ishlashi kerak.
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Foydalanuvchiga qalqib chiquvchi bildirishnoma ko'rsatadi (masalan yangi ulanish).</summary>
    public void Notify(string title, string text) => _notifications.Enqueue((title, text));

    public void Dispose()
    {
        // Xabar halqasini o'z oqimida to'xtatamiz — ApplicationContext.ExitThread buni xavfsiz
        // bajaradi (u ichida Post qiladi).
        _context?.ExitThread();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _ready.Dispose();
    }

    private void RunMessageLoop()
    {
        try
        {
            Application.EnableVisualStyles();

            var menu = BuildMenu();

            _icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = Shorten(_title),
                Visible = true,
                ContextMenuStrip = menu
            };

            // Belgi ustiga ikki marta bosilsa — hozirgi holatni ko'rsatamiz.
            _icon.DoubleClick += (_, _) => ShowBalloon(_title, _statusText());

            using var timer = new System.Windows.Forms.Timer { Interval = (int)RefreshInterval.TotalMilliseconds };
            timer.Tick += (_, _) => Refresh();
            timer.Start();

            _context = new ApplicationContext();
            _ready.Set();

            ShowBalloon(
                "Yordamchi agent ishga tushdi",
                "Bu kompyuter masofadan kuzatilishi mumkin. Ruxsatni shu belgidan boshqarishingiz mumkin.");

            Application.Run(_context);

            _icon.Visible = false;
            _icon.Dispose();
            menu.Dispose();
        }
        catch (Exception)
        {
            // Tray ochilmadi (masalan seansda ish stoli yo'q) — bu agentni to'xtatish uchun sabab
            // emas, shuning uchun istisno shu oqimda qoladi.
            _ready.Set();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem(_title) { Enabled = false });

        _statusItem = new ToolStripMenuItem(_statusText()) { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());

        _inputItem = new ToolStripMenuItem("Boshqaruvga ruxsat (sichqoncha, klaviatura)")
        {
            CheckOnClick = true,
            Checked = _permissions.AllowInput
        };
        _inputItem.CheckedChanged += (_, _) => _permissions.AllowInput = _inputItem!.Checked;
        menu.Items.Add(_inputItem);

        _commandsItem = new ToolStripMenuItem("Buyruqlarga ruxsat (xabar, ekran qulfi)")
        {
            CheckOnClick = true,
            Checked = _permissions.AllowCommands
        };
        _commandsItem.CheckedChanged += (_, _) => _permissions.AllowCommands = _commandsItem!.Checked;
        menu.Items.Add(_commandsItem);

        if (_logPath is not null)
        {
            menu.Items.Add(new ToolStripSeparator());
            var logItem = new ToolStripMenuItem("Jurnalni ochish");
            logItem.Click += (_, _) => OpenLog();
            menu.Items.Add(logItem);
        }

        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("Chiqish");
        exitItem.Click += (_, _) => _requestExit();
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>Holatni UI oqimida yangilaydi: matn, belgilar va kutib turgan bildirishnomalar.</summary>
    private void Refresh()
    {
        var status = _statusText();

        if (_statusItem is not null && _statusItem.Text != status)
            _statusItem.Text = status;

        if (_icon is not null)
            _icon.Text = Shorten($"{_title}\n{status}");

        // Menyudagi belgi va haqiqiy ruxsat farq qilishi mumkin (masalan ruxsat boshqa joydan
        // o'zgargan) — ko'rinish har doim haqiqatga moslanadi.
        if (_inputItem is not null && _inputItem.Checked != _permissions.AllowInput)
            _inputItem.Checked = _permissions.AllowInput;

        if (_commandsItem is not null && _commandsItem.Checked != _permissions.AllowCommands)
            _commandsItem.Checked = _permissions.AllowCommands;

        while (_notifications.TryDequeue(out var notification))
            ShowBalloon(notification.Title, notification.Text);
    }

    private void ShowBalloon(string title, string text)
    {
        if (_icon is null)
            return;

        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = ToolTipIcon.Info;
            _icon.ShowBalloonTip(5000);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Bildirishnoma ko'rsatilmadi — ahamiyatsiz.
        }
    }

    private void OpenLog()
    {
        try
        {
            // Faylni tizimning standart dasturi bilan ochamiz; UseShellExecute shart, aks holda
            // .log kengaytmasi bajariladigan fayl deb qarab xato beradi.
            Process.Start(new ProcessStartInfo(_logPath!) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowBalloon("Jurnal ochilmadi", ex.Message);
        }
    }

    /// <summary>
    /// Agent bilan bir xil nishonni ishlatamiz (dastur faylining o'zidan olinadi). Olinmasa —
    /// tizimning standart nishoni: belgi baribir ko'rinishi kerak.
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;

            if (path is not null)
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                    return icon;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Pastdagi standart nishonga tushamiz.
        }

        return SystemIcons.Application;
    }

    /// <summary>Tray maslahat matni 127 belgidan uzun bo'lsa Windows uni rad etadi.</summary>
    private static string Shorten(string text) => text.Length <= 127 ? text : text[..127];
}
