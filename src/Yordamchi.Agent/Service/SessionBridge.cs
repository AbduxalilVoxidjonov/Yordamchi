using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Yordamchi.Agent.Hosting;

namespace Yordamchi.Agent.Service;

/// <summary>
/// Xizmat (SYSTEM, "session 0") va foydalanuvchi seansi orasidagi ko'prik.
/// <para>
/// <b>Muammo: session 0 izolyatsiyasi.</b> Windows Vista'dan boshlab xizmatlar alohida,
/// <b>ish stoli bo'lmagan</b> 0-seansda ishlaydi. U yerdan turib foydalanuvchi ekranini olish ham,
/// unga sichqoncha/klaviatura yuborish ham mumkin emas — hech qanday API bunga yo'l bermaydi
/// (bu ataylab qo'yilgan xavfsizlik chegarasi, chetlab o'tiladigan xato emas).
/// </para>
/// <para>
/// <b>Yechim.</b> Xizmat o'zi ekran bilan ishlamaydi: u faol seansdagi foydalanuvchi nomidan
/// <b>bola jarayon</b> ochadi (<c>CreateProcessAsUser</c>), butun ish esa shu jarayonda bajariladi.
/// Xizmatning vazifasi — bola jarayon doim tirik turishini va seans almashganda (boshqa
/// foydalanuvchi kirdi, seans qulflandi-ochildi) qaytadan ochilishini kuzatish. Xuddi shu naqsh
/// NetSupport, Veyon va boshqa sinf boshqaruvi dasturlarida ishlatiladi.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionBridge
{
    /// <summary>Faol seansni va bola jarayonni shu oraliqda tekshiramiz.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Jarayon ochilmasa shu muddat kutib qayta urinamiz (masalan hali kirish ekrani).</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private const uint NoActiveSession = 0xFFFFFFFF;

    private const uint MaximumAllowed = 0x02000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    /// <summary>Bola jarayon konsol oynasini ko'rsatmasin — foydalanuvchi uchun belgi tray'da.</summary>
    private const uint CreateNoWindow = 0x08000000;

    private const int SecurityIdentification = 1;
    private const int TokenPrimary = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr environment);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUserW(
        IntPtr token,
        string? applicationName,
        string commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private readonly AgentOptions _options;
    private readonly AgentLog _log;

    public SessionBridge(AgentOptions options, AgentLog log)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Bekor qilinguncha faol seansda agent jarayonini tirik tutadi.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var sessionId = GetActiveSession();

            if (sessionId is null)
            {
                // Hech kim kirmagan (faqat kirish ekrani) — kutamiz. Bu xato emas: boshqarish
                // uchun ish stolida foydalanuvchi seansi bo'lishi kerak.
                await DelayAsync(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            Process? child;

            try
            {
                child = Launch(sessionId.Value);
                _log.Write($"Faol seansda ({sessionId.Value}) agent ochildi: PID {child.Id}.");
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                _log.Write($"Faol seansda agent ochilmadi: {ex.Message}");
                await DelayAsync(RetryDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (child)
            {
                await SuperviseAsync(child, sessionId.Value, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Bola jarayonni kuzatadi: u tugasa qaytadan ochiladi, seans almashsa esa to'xtatiladi
    /// (yangi seansda yangi jarayon kerak — eski jarayon boshqa foydalanuvchining ekranini
    /// ko'rmaydi).
    /// </summary>
    private async Task SuperviseAsync(Process child, uint sessionId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (child.HasExited)
            {
                _log.Write($"Seansdagi agent tugadi (kod {child.ExitCode}) — qaytadan ochiladi.");
                return;
            }

            if (GetActiveSession() != sessionId)
            {
                _log.Write("Faol seans almashdi — seansdagi agent to'xtatiladi.");
                Stop(child);
                return;
            }

            await DelayAsync(PollInterval, cancellationToken).ConfigureAwait(false);
        }

        // Xizmat to'xtatilmoqda — bola jarayonni ham yopamiz.
        Stop(child);
    }

    private void Stop(Process child)
    {
        try
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            _log.Write($"Seansdagi agentni to'xtatib bo'lmadi: {ex.Message}");
        }
    }

    /// <summary>Foydalanuvchi kirgan faol konsol seansi; bo'lmasa <c>null</c>.</summary>
    private static uint? GetActiveSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();

        // 0-seans — xizmatlar seansi: unda ish stoli yo'q, shuning uchun u yaroqli emas.
        return sessionId is NoActiveSession or 0 ? null : sessionId;
    }

    /// <summary>
    /// Faol seansdagi foydalanuvchi nomidan agent jarayonini ochadi.
    /// <para>
    /// Nishonlar (token, muhit bloki, jarayon/oqim tutqichlari) qat'iy tartibda bo'shatiladi:
    /// xizmat kunlar davomida ishlaydi va har qayta ochishda oqib turgan tutqich vaqt o'tib
    /// tizimni bo'g'ib qo'yardi.
    /// </para>
    /// </summary>
    private Process Launch(uint sessionId)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Seans foydalanuvchisining nishoni olinmadi.");

        var primaryToken = IntPtr.Zero;
        var environment = IntPtr.Zero;

        try
        {
            if (!DuplicateTokenEx(userToken, MaximumAllowed, IntPtr.Zero, SecurityIdentification, TokenPrimary, out primaryToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Nishon nusxalanmadi.");

            if (!CreateEnvironmentBlock(out environment, primaryToken, false))
            {
                // Muhit bloki bo'lmasa ham jarayonni ocha olamiz — shunchaki foydalanuvchi
                // o'zgaruvchilari (TEMP, APPDATA) merosga o'tmaydi.
                environment = IntPtr.Zero;
                _log.Write("Foydalanuvchi muhit bloki olinmadi — standart muhit bilan ochiladi.");
            }

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),

                // Interaktiv ish stoli: shusiz jarayon ko'rinmas ish stolida ochilib, tray belgisi
                // ham, xabar oynasi ham foydalanuvchiga ko'rinmasdi.
                lpDesktop = @"winsta0\default"
            };

            var commandLine = BuildCommandLine();

            if (!CreateProcessAsUserW(
                    primaryToken,
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    CreateUnicodeEnvironment | CreateNoWindow,
                    environment,
                    null,
                    ref startupInfo,
                    out var processInformation))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Seansda jarayon ochilmadi.");
            }

            try
            {
                return Process.GetProcessById(processInformation.dwProcessId);
            }
            finally
            {
                CloseHandle(processInformation.hProcess);
                CloseHandle(processInformation.hThread);
            }
        }
        finally
        {
            if (environment != IntPtr.Zero)
                DestroyEnvironmentBlock(environment);

            if (primaryToken != IntPtr.Zero)
                CloseHandle(primaryToken);

            CloseHandle(userToken);
        }
    }

    /// <summary>
    /// Bola jarayon uchun buyruq satri: xizmatning o'sha sozlamalari, lekin xizmat rejimisiz va
    /// <c>--parent-pid</c> bilan — xizmat tugasa bola jarayon ham chiqadi.
    /// </summary>
    private string BuildCommandLine()
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Agent fayli yo'li aniqlanmadi.");

        var arguments = _options.ToArgumentString(AgentRunMode.Console, Environment.ProcessId);
        return $"\"{executable}\" {arguments}";
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // To'xtatish — halqa o'zi tekshiradi.
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
