using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoidVPN.Core.Models;
using VoidVPN.Core.Services;

// Alias to avoid ambiguity with Microsoft.Extensions.Logging.LogLevel
using AppLog = VoidVPN.Core.Models.LogLevel;

namespace VoidVPN.UI.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        readonly SingBoxService    _sb;
        readonly ProfileRepository _repo;
        readonly SettingsService   _settings;
        readonly ILogger<MainViewModel> _log;

        public MainViewModel(
            SingBoxService sb,
            ProfileRepository repo,
            SettingsService settings,
            ILogger<MainViewModel> log)
        {
            _sb       = sb;
            _repo     = repo;
            _settings = settings;
            _log      = log;

            _sb.StateChanged += (_, s) => App.Current.Dispatcher.Invoke(() => SyncState(s));
            _sb.LogLine      += (_, l) => App.Current.Dispatcher.Invoke(() => AddLog(AppLog.Info, l));

            SyncState(_sb.State);
            _ = LoadProfilesAsync();
        }

        // ── Connection ────────────────────────────────────────────────────────

        [ObservableProperty] private string _statusText  = "disconnected";
        [ObservableProperty] private string _statusUpper = "DISCONNECTED";
        [ObservableProperty] private string _detailText  = string.Empty;
        [ObservableProperty] private bool   _isConnected = false;
        [ObservableProperty] private bool   _isBusy      = false;
        [ObservableProperty] private bool   _isAnimating = false;

        // ── Kill-switch indicator (always ON, UI-only) ─────────────────────

        // This property is always true — exposed so the UI can show "KS: ON"
        public bool KillSwitchActive => true;

        // ── Profiles ──────────────────────────────────────────────────────────

        public ObservableCollection<VpnProfile> Profiles { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteKeyCommand))]
        private VpnProfile? _selectedProfile;

        // ── Key input ─────────────────────────────────────────────────────────

        [ObservableProperty] private string _rawKey     = string.Empty;
        [ObservableProperty] private string _parseError = string.Empty;
        [ObservableProperty] private bool   _keyValid   = false;

        private VpnProfile? _parsed;

        partial void OnRawKeyChanged(string v)
        {
            ParseError = string.Empty;
            KeyValid   = false;
            _parsed    = null;

            if (string.IsNullOrWhiteSpace(v)) return;

            if (KeyParser.TryParse(v, out var p, out string err))
            {
                _parsed  = p;
                KeyValid = true;
            }
            else
            {
                ParseError = err;
            }

            // Persist last-entered key so it survives app restart
            _ = _settings.SetLastKeyAsync(v);
        }

        // ── Log ───────────────────────────────────────────────────────────────

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        [ObservableProperty] private int    _logCount      = 0;
        [ObservableProperty] private string _localPortText = string.Empty;
        [ObservableProperty] private string _speedText     = string.Empty;

        public void AddLog(AppLog level, string msg)
        {
            const int Max = 600;
            if (LogEntries.Count >= Max)
                LogEntries.RemoveAt(0);

            LogEntries.Add(new LogEntry(level, msg));
            LogCount = LogEntries.Count;
        }

        [RelayCommand]
        private void ClearLog()
        {
            LogEntries.Clear();
            LogCount = 0;
        }

        [RelayCommand]
        private void CopyLog()
        {
            if (LogEntries.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var e in LogEntries) sb.AppendLine(e.Full);
            System.Windows.Clipboard.SetText(sb.ToString());
            AddLog(AppLog.Info, $"Log copied ({LogEntries.Count} lines)");
        }

        // ── Commands ──────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task QuickConnectAsync()
        {
            if (_isConnected)  { await DisconnectAsync(); return; }
            if (_parsed != null) { await DoConnectAsync(_parsed); return; }
            if (SelectedProfile != null) await DoConnectAsync(SelectedProfile);
        }

        [RelayCommand]
        private async Task AddKeyAsync()
        {
            if (_parsed == null)
            {
                ParseError = string.IsNullOrWhiteSpace(RawKey)
                    ? "Paste a vless:// or ss:// key."
                    : ParseError;
                return;
            }

            await _repo.SaveAsync(_parsed);
            var added = _parsed;
            await LoadProfilesAsync();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == added.Id);
            RawKey = string.Empty;
            AddLog(AppLog.Info, $"Profile saved: {added.Name}");
        }

        [RelayCommand(CanExecute = nameof(CanConnect))]
        private async Task ConnectAsync()
        {
            if (SelectedProfile != null)
                await DoConnectAsync(SelectedProfile);
        }

        private bool CanConnect() => SelectedProfile != null && !IsBusy;

        [RelayCommand]
        private async Task DisconnectAsync()
        {
            IsBusy      = true;
            IsAnimating = true;
            try
            {
                AddLog(AppLog.Info, "Disconnecting...");
                await _sb.DisconnectAsync();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Disconnect error");
                AddLog(AppLog.Error, ex.Message);
            }
            finally
            {
                IsBusy      = false;
                IsAnimating = false;
                NotifyAllCommands();
            }
        }

        [RelayCommand(CanExecute = nameof(CanDelete))]
        private async Task DeleteKeyAsync()
        {
            if (SelectedProfile == null) return;
            var name = SelectedProfile.Name;
            await _repo.DeleteAsync(SelectedProfile.Id);
            await LoadProfilesAsync();
            SelectedProfile = Profiles.FirstOrDefault();
            AddLog(AppLog.Info, $"Removed: {name}");
        }

        private bool CanDelete() => SelectedProfile != null && !IsBusy;

        // ── Internals ─────────────────────────────────────────────────────────

        private async Task DoConnectAsync(VpnProfile p)
        {
            IsBusy      = true;
            IsAnimating = true;
            try
            {
                AddLog(AppLog.Info, $"Connecting → {p.DisplayHost}  [{p.DisplayProto}]");
                await _sb.ConnectAsync(p);
                LocalPortText = $"socks  127.0.0.1:{p.LocalSocksPort}";
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Connect error");
                AddLog(AppLog.Error, ex.Message);
            }
            finally
            {
                IsBusy      = false;
                IsAnimating = false;
                NotifyAllCommands();
            }
        }

        private async Task LoadProfilesAsync()
        {
            var list = await _repo.LoadAllAsync();
            App.Current.Dispatcher.Invoke(() =>
            {
                Profiles.Clear();
                foreach (var p in list) Profiles.Add(p);
                if (SelectedProfile == null || !Profiles.Contains(SelectedProfile))
                    SelectedProfile = Profiles.FirstOrDefault();
            });
        }

        private void SyncState(ConnectionState s)
        {
            StatusText  = s.Label;
            StatusUpper = s.LabelUpper;
            DetailText  = s.Detail;
            IsConnected = s.Status == VpnStatus.Connected;
            IsAnimating = s.Status is VpnStatus.Connecting or VpnStatus.Disconnecting;

            if (s.Status is VpnStatus.Connected or VpnStatus.Disconnected or VpnStatus.Error)
                IsBusy = false;

            if (s.ErrorMessage != null)
                AddLog(AppLog.Error, s.ErrorMessage);

            NotifyAllCommands();
        }

        private void NotifyAllCommands()
        {
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            DeleteKeyCommand.NotifyCanExecuteChanged();
            QuickConnectCommand.NotifyCanExecuteChanged();
        }
    }
}
