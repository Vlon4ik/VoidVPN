using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VoidVPN.Core.Services
{
    /// <summary>
    /// Kill-switch через Windows routing table + sing-box StrictRoute.
    ///
    /// ПОЧЕМУ НЕ netsh advfirewall:
    ///   advfirewall блокирует трафик до того как void-tun адаптер создан sing-box-ом,
    ///   что полностью ломает подключение — трафик через TUN тоже блокируется.
    ///
    /// КАК РАБОТАЕТ:
    ///   1. sing-box TunInbound.StrictRoute = true — встроенный kill-switch:
    ///      весь трафик обязан идти через void-tun, иначе drop на уровне routing.
    ///   2. При неожиданном крэше sing-box — добавляем blackhole-маршрут 0.0.0.0/0
    ///      через несуществующий gateway (224.0.0.1) чтобы заблокировать трафик.
    ///   3. При нормальном Disconnect — удаляем blackhole-маршрут.
    ///
    /// Такой подход не требует прав сверх admin (уже есть) и не мешает TUN.
    /// </summary>
    public sealed class KillSwitchService
    {
        // Фиктивный gateway для blackhole — этот адрес не существует в LAN
        const string BlackholeGw = "224.0.0.1";

        readonly ILogger<KillSwitchService> _log;

        bool _blackholeActive = false;

        public KillSwitchService(ILogger<KillSwitchService> log) => _log = log;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Вызывается ДО запуска sing-box.
        /// При нормальной работе sing-box StrictRoute=true уже является kill-switch.
        /// Здесь мы только убеждаемся что предыдущий blackhole-маршрут удалён.
        /// </summary>
        public async Task EnableAsync(string vpnServerIp, CancellationToken ct = default)
        {
            _log.LogInformation("Kill-switch: enabling (StrictRoute mode, server={Ip})", vpnServerIp);

            // Убираем старый blackhole если остался от прошлого краша
            await RemoveBlackholeAsync(ct);

            _log.LogInformation("Kill-switch: active via sing-box StrictRoute=true");
        }

        /// <summary>
        /// Вызывается ПОСЛЕ остановки sing-box при нормальном disconnect.
        /// Убирает blackhole-маршрут если он был добавлен.
        /// </summary>
        public async Task DisableAsync(CancellationToken ct = default)
        {
            _log.LogInformation("Kill-switch: disabling");
            await RemoveBlackholeAsync(ct);
        }

        /// <summary>
        /// Вызывается при неожиданном крэше sing-box (OnExited с ненулевым кодом).
        /// Добавляет blackhole-маршрут чтобы заблокировать трафик до явного Disconnect.
        /// </summary>
        public async Task ActivateEmergencyBlackholeAsync(CancellationToken ct = default)
        {
            if (_blackholeActive) return;
            _log.LogWarning("Kill-switch: EMERGENCY blackhole activated (sing-box crashed)");

            // Добавляем маршрут 0.0.0.0/0 через несуществующий gateway
            // Windows отбросит такой трафик с "Network unreachable"
            await RunRouteAsync($"ADD 0.0.0.0 MASK 128.0.0.0 {BlackholeGw} METRIC 1", ct);
            await RunRouteAsync($"ADD 128.0.0.0 MASK 128.0.0.0 {BlackholeGw} METRIC 1", ct);
            _blackholeActive = true;
        }

        /// <summary>
        /// Очищает stale правила от предыдущего краша при старте приложения.
        /// </summary>
        public async Task CleanupStaleRulesAsync(CancellationToken ct = default)
        {
            _log.LogInformation("Kill-switch: cleaning up stale rules");
            await RemoveBlackholeAsync(ct);
        }

        // ── Private ───────────────────────────────────────────────────────────

        async Task RemoveBlackholeAsync(CancellationToken ct)
        {
            // Удаляем оба half-subnet маршрута (ignoreError=true — возможно их нет)
            await RunRouteAsync($"DELETE 0.0.0.0 MASK 128.0.0.0 {BlackholeGw}", ct, ignoreError: true);
            await RunRouteAsync($"DELETE 128.0.0.0 MASK 128.0.0.0 {BlackholeGw}", ct, ignoreError: true);
            _blackholeActive = false;
        }

        async Task RunRouteAsync(string args, CancellationToken ct, bool ignoreError = false)
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName               = "route.exe",
                        Arguments              = args,
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true
                    }
                };
                proc.Start();
                string stderr = await proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode != 0 && !ignoreError)
                    _log.LogWarning("route {Args} failed (code={Code}): {Err}", args, proc.ExitCode, stderr.Trim());
                else
                    _log.LogDebug("route {Args} → ok", args);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (!ignoreError) _log.LogError(ex, "route failed: {Args}", args);
            }
        }
    }
}
