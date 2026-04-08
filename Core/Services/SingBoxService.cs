using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoidVPN.Core.Exceptions;
using VoidVPN.Core.Models;

namespace VoidVPN.Core.Services
{
    public sealed class SingBoxService : IAsyncDisposable
    {
        const int GraceMs = 5000;
        const int StartMs = 1800;

        readonly ConfigGeneratorService  _cfg;
        readonly NetworkService          _net;
        readonly KillSwitchService       _ks;
        readonly LogEncryptionService    _logEnc;
        readonly ILogger<SingBoxService> _log;
        readonly SemaphoreSlim _lk = new(1, 1);

        // _proc доступен из OnExited (другой поток) и из KillAsync.
        // Всегда захватывай локальную копию ДО любого await — иначе NullRef.
        Process?    _proc;
        VpnProfile? _active;
        string?     _bypassIp;
        CancellationTokenSource _logCts = new();

        public event EventHandler<ConnectionState>? StateChanged;
        public event EventHandler<string>?          LogLine;

        public ConnectionState State     { get; private set; } = ConnectionState.Off;
        public bool            IsRunning => _proc is { HasExited: false };

        public SingBoxService(
            ConfigGeneratorService  cfg,
            NetworkService          net,
            KillSwitchService       ks,
            LogEncryptionService    logEnc,
            ILogger<SingBoxService> log)
        {
            _cfg    = cfg;
            _net    = net;
            _ks     = ks;
            _logEnc = logEnc;
            _log    = log;
            _logEnc.Initialize();
        }

        // ── Connect ───────────────────────────────────────────────────────────

        public async Task ConnectAsync(VpnProfile p, CancellationToken ct = default)
        {
            await _lk.WaitAsync(ct);
            try
            {
                if (IsRunning)
                {
                    _log.LogWarning("ConnectAsync: stale process detected, killing before reconnect");
                    await KillAsync();
                    await CleanupAsync();
                    _active   = null;
                    _bypassIp = null;
                }

                Push(ConnectionState.Connecting(p));

                string exe     = FindExe();
                string cfgPath = CfgPath();
                _log.LogInformation("sing-box exe: {Exe}", exe);

                await _cfg.WriteAsync(p, cfgPath, ct);

                // SECURITY: kill-switch ДО bypass-маршрута — нулевое окно утечки
                await _ks.EnableAsync(p.ServerAddress, ct);

                string? gw = await _net.GetGatewayAsync(ct);
                if (gw != null)
                {
                    _bypassIp = p.ServerAddress;
                    try { await _net.AddBypassAsync(p.ServerAddress, gw, ct); }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Bypass route failed (non-fatal)");
                        _bypassIp = null;
                    }
                }

                _active = p;
                await CleanupTunInterfacesAsync(ct);
                await LaunchAsync(exe, cfgPath, ct);
                await Task.Delay(StartMs, ct);

                if (!IsRunning)
                {
                    await _ks.DisableAsync(ct);
                    await RollbackAsync();
                    throw new SingBoxStartException("sing-box exited immediately after start");
                }

                Push(ConnectionState.Connected(p));
            }
            catch (OperationCanceledException)
            {
                Push(ConnectionState.Off);
                await _ks.DisableAsync();
                await RollbackAsync();
                throw;
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _log.LogError(ex, "Connect failed");
                Push(ConnectionState.Fail(p, ex.Message));
                await _ks.DisableAsync();
                await RollbackAsync();
                throw;
            }
            finally { _lk.Release(); }
        }

        // ── Disconnect ────────────────────────────────────────────────────────

        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            await _lk.WaitAsync(ct);
            try
            {
                if (!IsRunning && _active == null) return;
                if (_active != null) Push(ConnectionState.Stopping(_active));

                await KillAsync();
                await CleanupAsync();
                _active   = null;
                _bypassIp = null;

                // kill-switch выключается ПОСЛЕ остановки sing-box
                await _ks.DisableAsync(ct);
                await EncryptSessionLogAsync(ct);

                Push(ConnectionState.Off);
            }
            finally { _lk.Release(); }
        }

        // ── Log encryption ────────────────────────────────────────────────────

        async Task EncryptSessionLogAsync(CancellationToken ct)
        {
            try
            {
                string exeDir   = Path.GetDirectoryName(FindExe())!;
                string plainLog = Path.Combine(exeDir, "sing-box.log");
                string encLog   = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoidVPN", $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log.enc");
                await _logEnc.EncryptLogAsync(plainLog, encLog);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Session log encryption failed (non-fatal)"); }
        }

        // ── TUN cleanup ───────────────────────────────────────────────────────

        async Task CleanupTunInterfacesAsync(CancellationToken ct)
        {
            string[] vpnProcesses =
            [
                "openvpn", "wireguard", "wg-quick",
                "tailscaled", "tailscale",
                "nordvpn", "nordvpnd",
                "expressvpn", "expressvpnd",
                "surfshark", "surfshark-service",
                "mullvad-daemon", "mullvad",
                "protonvpn", "protonvpn-service",
                "windscribe", "windscribeworker",
                "pia_nw", "pia_manager",
                "vpnkit", "v2ray", "xray",
                "clash", "clash-meta", "sing-box"
            ];

            foreach (var name in vpnProcesses)
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            _log.LogInformation("Killing VPN process: {Name} (PID={Pid})", name, proc.Id);
                            proc.Kill(entireProcessTree: true);
                            await proc.WaitForExitAsync(ct);
                        }
                        catch { }
                        finally { proc.Dispose(); }
                    }
                }
                catch { }
            }

            await Task.Delay(400, ct);

            // PowerShell: убрать оставшиеся TUN-адаптеры
            const string ps = """
                Get-NetAdapter | Where-Object {
                    $_.InterfaceDescription -match 'Wintun|WireGuard|TAP-Windows|OpenVPN|Tailscale|Mullvad|Nord|Surfshark|Proton|Windscribe|Cisco|Juniper|Fortinet' `
                    -or $_.Name -match '^(void-tun|wg\d+|tun\d+|tap\d+|utun\d+)'
                } | ForEach-Object {
                    Disable-NetAdapter -Name $_.Name -Confirm:$false -ErrorAction SilentlyContinue
                    Remove-NetAdapter  -Name $_.Name -Confirm:$false -ErrorAction SilentlyContinue
                }
                """;

            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName  = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -Command \"{ps.Replace("\"", "\\\"")}\"",
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true
                    }
                };
                proc.Start();
                await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "TUN cleanup failed (non-fatal)"); }

            await Task.Delay(600, ct);
        }

        // ── Process launch ────────────────────────────────────────────────────

        async Task LaunchAsync(string exe, string cfg, CancellationToken ct)
        {
            await _logCts.CancelAsync();
            _logCts.Dispose();
            _logCts = new CancellationTokenSource();

            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName         = exe,
                    Arguments        = $"run -c \"{cfg}\"",
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                },
                EnableRaisingEvents = true
            };
            _proc.Exited += OnExited;
            _proc.Start();
            _log.LogInformation("sing-box PID={Pid}", _proc.Id);

            var tok = _logCts.Token;
            _ = PumpAsync(_proc.StandardOutput, tok);
            _ = PumpAsync(_proc.StandardError,  tok);
        }

        // ── FIX: NullReferenceException при await _proc.WaitForExitAsync() ──
        // Проблема: OnExited (другой поток) обнуляет _proc=null между проверкой
        // HasExited и вызовом WaitForExitAsync — получаем NullRef.
        // Решение: захватить локальную копию proc ДО любого await и работать только с ней.
        async Task KillAsync()
        {
            // Захватываем и сразу обнуляем поле — дальше работаем только с локальной копией
            var proc = _proc;
            _proc = null;

            if (proc == null) return;

            // Отменяем pump-задачи
            await _logCts.CancelAsync();

            // Отписываемся от события ПРЕЖДЕ любого await — иначе OnExited
            // может снова обнулить proc пока мы его ждём
            proc.Exited -= OnExited;

            if (!proc.HasExited)
            {
                try
                {
                    proc.CloseMainWindow();
                    // WaitForExit с таймаутом (синхронный — нет NullRef риска)
                    if (!proc.WaitForExit(GraceMs))
                        proc.Kill(entireProcessTree: true);
                    // Финальный await — proc уже локальная, поле _proc = null
                    await proc.WaitForExitAsync();
                }
                catch (Exception ex) { _log.LogWarning(ex, "Kill error"); }
            }

            try { proc.Dispose(); } catch { }
        }

        void OnExited(object? s, EventArgs e)
        {
            // Захватываем локальные копии полей ДО async-перехода
            var proc    = _proc;
            var profile = _active;
            int code    = -1;

            try { code = proc?.ExitCode ?? -1; } catch { }
            _log.LogWarning("sing-box exited code={Code}", code);

            Task.Run(async () =>
            {
                // Отписываемся и освобождаем — KillAsync мог ещё не успеть
                if (proc != null)
                {
                    proc.Exited -= OnExited;
                    // Если KillAsync уже обнулил _proc — proc != _proc, просто чистим
                    if (ReferenceEquals(_proc, proc)) _proc = null;
                    try { proc.Dispose(); } catch { }
                }

                await CleanupAsync();

                // SECURITY: при неожиданном крэше kill-switch ОСТАЁТСЯ активным.
                // Трафик не утечёт — пользователь должен явно нажать Stop.
                _active   = null;
                _bypassIp = null;

                if (code != 0)
                {
                    // Sing-box упал неожиданно — активируем blackhole чтобы трафик не утёк
                    try { await _ks.ActivateEmergencyBlackholeAsync(); } catch { }
                }

                Push(code == 0
                    ? ConnectionState.Off
                    : ConnectionState.Fail(profile,
                        $"sing-box exited (code {code}) — kill-switch ACTIVE, press Stop to release"));
            });
        }

        async Task CleanupAsync()
        {
            if (_bypassIp != null)
            {
                await _net.RemoveBypassAsync(_bypassIp);
                _bypassIp = null;
            }
        }

        async Task RollbackAsync()
        {
            await KillAsync();
            await CleanupAsync();
        }

        async Task PumpAsync(StreamReader r, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var l = await r.ReadLineAsync(ct);
                    if (l == null) break;
                    LogLine?.Invoke(this, l);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.LogWarning(ex, "Pump error"); }
        }

        void Push(ConnectionState s) { State = s; StateChanged?.Invoke(this, s); }

        // ── Path resolution ───────────────────────────────────────────────────

        static string FindExe()
        {
            string a = Path.Combine(AppContext.BaseDirectory, "Assets", "sing-box.exe");
            if (File.Exists(a)) return a;

            string b = Path.Combine(AppContext.BaseDirectory, "sing-box.exe");
            if (File.Exists(b)) return b;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string c = Path.Combine(dir.FullName, "Assets", "sing-box.exe");
                if (File.Exists(c)) return c;
                dir = dir.Parent;
            }

            throw new SingBoxNotFoundException(a);
        }

        static string CfgPath()
        {
            string d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoidVPN");
            Directory.CreateDirectory(d);
            return Path.Combine(d, "active.json");
        }

        public async ValueTask DisposeAsync()
        {
            if (IsRunning) await DisconnectAsync();
            _logCts.Dispose();
            _lk.Dispose();
        }
    }
}
