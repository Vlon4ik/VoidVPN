using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VoidVPN.Core.Services
{
    /// <summary>
    /// Persists lightweight app settings:
    ///   - IsDarkTheme (so theme survives restart)
    ///   - LastRawKey  (Simple-view key field auto-restore)
    ///   - KillSwitchEnabled (always true — kill-switch cannot be disabled)
    /// </summary>
    public sealed class SettingsService
    {
        static readonly JsonSerializerOptions s_opts = new() { WriteIndented = true };

        readonly string _path;
        readonly ILogger<SettingsService> _log;
        readonly SemaphoreSlim _lk = new(1, 1);

        AppSettings _current = new();

        public SettingsService(ILogger<SettingsService> log)
        {
            _log = log;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoidVPN");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "settings.json");
        }

        public AppSettings Current => _current;

        public async Task LoadAsync(CancellationToken ct = default)
        {
            await _lk.WaitAsync(ct);
            try
            {
                if (!File.Exists(_path)) return;
                var json = await File.ReadAllTextAsync(_path, ct);
                _current = JsonSerializer.Deserialize<AppSettings>(json, s_opts) ?? new();
                // Kill-switch is always forced on regardless of saved value
                _current.KillSwitchEnabled = true;
            }
            catch (Exception ex) { _log.LogWarning(ex, "Settings load failed"); }
            finally { _lk.Release(); }
        }

        public async Task SaveAsync(CancellationToken ct = default)
        {
            await _lk.WaitAsync(ct);
            try
            {
                _current.KillSwitchEnabled = true; // never allow false
                await File.WriteAllTextAsync(_path,
                    JsonSerializer.Serialize(_current, s_opts), ct);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Settings save failed"); }
            finally { _lk.Release(); }
        }

        public async Task SetThemeAsync(bool isDark)
        {
            _current.IsDarkTheme = isDark;
            await SaveAsync();
        }

        public async Task SetLastKeyAsync(string key)
        {
            _current.LastRawKey = key;
            await SaveAsync();
        }
    }

    public sealed class AppSettings
    {
        public bool   IsDarkTheme       { get; set; } = true;
        public string LastRawKey        { get; set; } = string.Empty;
        public bool   KillSwitchEnabled { get; set; } = true; // read-only: always true
    }
}
