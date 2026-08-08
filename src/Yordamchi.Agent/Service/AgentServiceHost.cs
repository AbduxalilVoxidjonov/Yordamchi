using System.Runtime.Versioning;
using System.ServiceProcess;
using Yordamchi.Agent.Hosting;

namespace Yordamchi.Agent.Service;

/// <summary>
/// Agentni Windows xizmati sifatida yuritadi.
/// <para>
/// Xizmatning o'zi <b>hech qanday ekran yoki kirish ishini bajarmaydi</b> — u faqat
/// <see cref="SessionBridge"/> ni yuritadi, ya'ni faol foydalanuvchi seansida agent jarayonini
/// tirik tutadi. Sabab: xizmat 0-seansda ishlaydi va u yerda ish stoli yo'q
/// (<see cref="SessionBridge"/> dagi izohga qarang).
/// </para>
/// <para>
/// <b>Nega xizmat kerak.</b> Xizmat kompyuter yonganda o'zi ishga tushadi va foydalanuvchi
/// almashsa ham qoladi — sinf yoki laboratoriyadagi o'nlab kompyuterda agentni har safar qo'lda
/// ochib chiqishning iloji yo'q.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentServiceHost : ServiceBase
{
    private readonly AgentOptions _options;
    private readonly AgentLog _log;

    private CancellationTokenSource? _stopping;
    private Task? _bridge;

    public AgentServiceHost(AgentOptions options, AgentLog log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));

        ServiceName = ServiceControl.ServiceName;
        CanStop = true;
        CanShutdown = true;

        // Seans o'zgarishlari (kirish, chiqish, qulflash) haqida xabar olamiz — ular jurnalda
        // ko'rinib turishi nosozlikni tushunishni ancha osonlashtiradi.
        CanHandleSessionChangeEvent = true;
    }

    protected override void OnStart(string[] args)
    {
        _log.Write("Xizmat ishga tushdi.");
        _stopping = new CancellationTokenSource();
        _bridge = new SessionBridge(_options, _log).RunAsync(_stopping.Token);
    }

    protected override void OnStop()
    {
        _log.Write("Xizmat to'xtatilmoqda.");
        _stopping?.Cancel();

        try
        {
            // Xizmat menejeri javobni cheksiz kutmaydi; muhlat ichida tugamasa ham chiqamiz —
            // seansdagi jarayon o'zi ham ota jarayon tugashini kuzatib turadi.
            _bridge?.Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            // Bekor qilinganda kutilgan istisnolar.
        }

        _stopping?.Dispose();
        _log.Write("Xizmat to'xtadi.");
    }

    protected override void OnShutdown()
    {
        // Kompyuter o'chirilmoqda — to'xtatish bilan bir xil yo'l.
        OnStop();
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        _log.Write($"Seans o'zgarishi: {changeDescription.Reason} (seans {changeDescription.SessionId}).");
    }
}
