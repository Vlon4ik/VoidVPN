using System;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoidVPN.Core.Services;
using VoidVPN.UI;
using VoidVPN.UI.ViewModels;

// Explicit aliases to resolve ambiguity between WPF and WinForms
using WpfApp      = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMsgButton  = System.Windows.MessageBoxButton;
using WpfMsgImage   = System.Windows.MessageBoxImage;
using StartupArgs   = System.Windows.StartupEventArgs;
using ExitArgs      = System.Windows.ExitEventArgs;

namespace VoidVPN;

public partial class App : WpfApp
{
    ServiceProvider?     _svc;
    SingBoxService?      _sb;
    KillSwitchService?   _ks;
    SettingsService?     _settings;

    protected override async void OnStartup(StartupArgs e)
    {
        base.OnStartup(e);

        if (!IsAdmin())
        {
            WpfMessageBox.Show(
                "VoidVPN requires administrator rights to create a TUN interface.\n\nPlease re-run as Administrator.",
                "Administrator Required",
                WpfMsgButton.OK,
                WpfMsgImage.Warning);
            Shutdown(1);
            return;
        }

        _svc      = Build();
        _sb       = _svc.GetRequiredService<SingBoxService>();
        _ks       = _svc.GetRequiredService<KillSwitchService>();
        _settings = _svc.GetRequiredService<SettingsService>();

        // Clean up any stale kill-switch rules from a previous crash
        await _ks.CleanupStaleRulesAsync();

        // Load persisted settings (theme, last key, etc.)
        await _settings.LoadAsync();

        var vm = _svc.GetRequiredService<MainViewModel>();
        var w  = _svc.GetRequiredService<MainWindow>();
        MainWindow = w;
        w.Show();

        // Apply persisted theme after window loads
        w.ApplyPersistedTheme(_settings.Current.IsDarkTheme);

        // Restore last-used key in Simple view
        if (!string.IsNullOrWhiteSpace(_settings.Current.LastRawKey))
            vm.RawKey = _settings.Current.LastRawKey;
    }

    protected override async void OnExit(ExitArgs e)
    {
        if (_sb?.IsRunning == true)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(8000);
                await _sb.DisconnectAsync(cts.Token);
            }
            catch { /* best-effort */ }
        }

        if (_sb is IAsyncDisposable d)
            await d.DisposeAsync();

        _svc?.Dispose();
        base.OnExit(e);
    }

    static ServiceProvider Build()
    {
        var sc = new ServiceCollection();

        sc.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddConsole();
        });

        sc.AddSingleton<ConfigGeneratorService>();
        sc.AddSingleton<NetworkService>();
        sc.AddSingleton<KillSwitchService>();
        sc.AddSingleton<LogEncryptionService>();
        sc.AddSingleton<SingBoxService>();
        sc.AddSingleton<ProfileRepository>();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();

        return sc.BuildServiceProvider();
    }

    static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
