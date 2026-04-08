using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoidVPN.Core.Models;
using VoidVPN.Core.Services;
using VoidVPN.UI.ViewModels;

// ── Aliases ───────────────────────────────────────────────────────────────────
using WpfColor           = System.Windows.Media.Color;
using WinFormsNotifyIcon  = System.Windows.Forms.NotifyIcon;
using WinFormsContextMenu = System.Windows.Forms.ContextMenuStrip;
using WinFormsToolTipIcon = System.Windows.Forms.ToolTipIcon;
using DrawingBitmap       = System.Drawing.Bitmap;
using DrawingGraphics     = System.Drawing.Graphics;
using DrawingBrushes      = System.Drawing.Brushes;
using DrawingColor        = System.Drawing.Color;
using DrawingFont         = System.Drawing.Font;
using DrawingFontStyle    = System.Drawing.FontStyle;
using DrawingIcon         = System.Drawing.Icon;

namespace VoidVPN.UI;

public partial class MainWindow : Window
{
    readonly MainViewModel   _vm;
    readonly SettingsService _settings;

    bool _beta          = false;
    bool _dark          = true;
    bool _themeChanging = false;

    // Guard: Resources не готовы до Loaded
    bool _loaded = false;

    WinFormsNotifyIcon? _trayIcon;
    Storyboard?         _dotsAnim;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow(MainViewModel vm, SettingsService settings)
    {
        InitializeComponent();
        _vm       = vm;
        _settings = settings;
        DataContext = vm;
        _dotsAnim = (Storyboard)Resources["DotsAnim"];

        InitTrayIcon();

        Loaded += (_, _) =>
        {
            _loaded = true;
            // Тема применяется здесь — Resources уже инициализированы
            ApplyTheme(dark: true, animate: false);
        };

        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsAnimating): Dispatcher.Invoke(SyncAnim);        break;
                case nameof(MainViewModel.ParseError):  Dispatcher.Invoke(SyncParseErr);    break;
                case nameof(MainViewModel.RawKey):      Dispatcher.Invoke(SyncPlaceholder); break;
                case nameof(MainViewModel.IsConnected): Dispatcher.Invoke(SyncDot);         break;
            }
        };

        vm.LogEntries.CollectionChanged += (_, _) =>
            Dispatcher.Invoke(() => LogScroll?.ScrollToEnd());

        vm.AddLog(LogLevel.Info, "VoidVPN ready  —  sing-box 1.13.4  —  tun/wintun");
        vm.AddLog(LogLevel.Info, "Kill-switch: ALWAYS ON  |  Logs: AES-256-GCM");
    }

    // ── Called by App after settings loaded ───────────────────────────────────

    public void ApplyPersistedTheme(bool isDark)
    {
        if (!_loaded) return;
        if (_dark == isDark) return;

        _themeChanging = true;
        ApplyTheme(dark: isDark, animate: false);
        if (isDark)
        {
            if (ThemeDarkS  != null) ThemeDarkS.IsChecked  = true;
            if (ThemeDarkB  != null) ThemeDarkB.IsChecked  = true;
        }
        else
        {
            if (ThemeLightS != null) ThemeLightS.IsChecked = true;
            if (ThemeLightB != null) ThemeLightB.IsChecked = true;
        }
        _themeChanging = false;
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    void InitTrayIcon()
    {
        using var bmp = new DrawingBitmap(16, 16);
        using var g   = DrawingGraphics.FromImage(bmp);
        g.Clear(DrawingColor.Transparent);
        g.FillRectangle(DrawingBrushes.Black, 0, 0, 16, 16);
        using var font = new DrawingFont("Segoe UI", 8f, DrawingFontStyle.Bold);
        g.DrawString("V", font, DrawingBrushes.White, 1f, 1f);

        var iconHandle = bmp.GetHicon();
        var icon       = DrawingIcon.FromHandle(iconHandle);

        _trayIcon = new WinFormsNotifyIcon { Icon = icon, Text = "VoidVPN", Visible = false };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new WinFormsContextMenu();
        menu.Items.Add("Show", null, (_, _) => ShowFromTray());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        });
        _trayIcon.ContextMenuStrip = menu;
    }

    void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            ShowInTaskbar = true;
            WindowState   = WindowState.Normal;
            Activate();
            if (_trayIcon != null) _trayIcon.Visible = false;
        });
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void Window_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Window_StateChanged(object? s, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }

    private void Window_Closing(object? s, System.ComponentModel.CancelEventArgs e)
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    private void BtnTray_Click(object s, RoutedEventArgs e)
    {
        Hide();
        ShowInTaskbar = false;
        if (_trayIcon != null)
        {
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(1500, "VoidVPN", "Running in background",
                                     WinFormsToolTipIcon.None);
        }
    }

    private void BtnClose_Click(object s, RoutedEventArgs e)
    {
        if (_trayIcon != null) _trayIcon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    // ── Mode toggle ───────────────────────────────────────────────────────────

    private void BtnToggleMode_Click(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _beta = !_beta;

        var fade = (Storyboard)Resources["FadeIn"];

        if (_beta)
        {
            SimpleView.Visibility = Visibility.Collapsed;
            BetaView.Visibility   = Visibility.Visible;
            Storyboard.SetTarget(fade, BetaView);

            _themeChanging = true;
            if (_dark) { if (ThemeDarkB  != null) ThemeDarkB.IsChecked  = true; }
            else        { if (ThemeLightB != null) ThemeLightB.IsChecked = true; }
            _themeChanging = false;
        }
        else
        {
            BetaView.Visibility   = Visibility.Collapsed;
            SimpleView.Visibility = Visibility.Visible;
            Storyboard.SetTarget(fade, SimpleView);
        }

        fade.Begin();
    }

    // ── Simple connect ────────────────────────────────────────────────────────

    private void BtnMainConnect_Click(object s, RoutedEventArgs e)
        => _vm.QuickConnectCommand.Execute(null);

    // ── Placeholder ───────────────────────────────────────────────────────────

    private void TxtKey_GotFocus(object s, RoutedEventArgs e)
    {
        PhSimple.Visibility = Visibility.Collapsed;
        PhBeta.Visibility   = Visibility.Collapsed;
    }

    private void TxtKey_LostFocus(object s, RoutedEventArgs e) => SyncPlaceholder();

    void SyncPlaceholder()
    {
        var v = string.IsNullOrEmpty(_vm.RawKey) ? Visibility.Visible : Visibility.Collapsed;
        PhSimple.Visibility = v;
        PhBeta.Visibility   = v;
    }

    // ── Parse error ───────────────────────────────────────────────────────────

    void SyncParseErr()
    {
        bool has = !string.IsNullOrEmpty(_vm.ParseError);
        ParseErrSimple.Visibility = has && !_beta ? Visibility.Visible : Visibility.Collapsed;
        ParseErrBeta.Visibility   = has && _beta  ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Dot animation ─────────────────────────────────────────────────────────

    void SyncAnim()
    {
        if (!_loaded) return;
        if (_vm.IsAnimating)
        {
            AnimBorder.Visibility = Visibility.Visible;
            _dotsAnim?.Begin(this, true);
        }
        else
        {
            _dotsAnim?.Stop(this);
            AnimBorder.Visibility = Visibility.Collapsed;
        }
    }

    // ── Status dot ────────────────────────────────────────────────────────────

    void SyncDot()
    {
        if (!_loaded) return;

        WpfColor target = _vm.IsConnected
            ? (_dark ? Colors.White : Colors.Black)
            : (_dark ? WpfColor.FromRgb(0x33, 0x33, 0x33)
                     : WpfColor.FromRgb(0xBB, 0xBB, 0xBB));

        AnimRes("Br.StatusDot", target, 200);

        if (BtnMainConnect != null)
            BtnMainConnect.Content = _vm.IsConnected ? "STOP" : "CONNECT";
    }

    // ── Theme handlers ────────────────────────────────────────────────────────

    private void ThemeDark_Checked(object sender, RoutedEventArgs e)
    {
        if (_themeChanging || !_loaded) return;
        bool changed = _dark != true;
        ApplyTheme(dark: true, animate: changed);
        _themeChanging = true;
        if (!ReferenceEquals(sender, ThemeDarkS)  && ThemeDarkS  != null) ThemeDarkS.IsChecked  = true;
        if (!ReferenceEquals(sender, ThemeDarkB)  && ThemeDarkB  != null) ThemeDarkB.IsChecked  = true;
        _themeChanging = false;
        if (changed) _ = _settings.SetThemeAsync(true);
    }

    private void ThemeLight_Checked(object sender, RoutedEventArgs e)
    {
        if (_themeChanging || !_loaded) return;
        bool changed = _dark != false;
        ApplyTheme(dark: false, animate: changed);
        _themeChanging = true;
        if (!ReferenceEquals(sender, ThemeLightS) && ThemeLightS != null) ThemeLightS.IsChecked = true;
        if (!ReferenceEquals(sender, ThemeLightB) && ThemeLightB != null) ThemeLightB.IsChecked = true;
        _themeChanging = false;
        if (changed) _ = _settings.SetThemeAsync(false);
    }

    // ── Core theme engine ─────────────────────────────────────────────────────

    internal void ApplyTheme(bool dark, bool animate = true)
    {
        if (!_loaded && animate) return;
        _dark = dark;

        WpfColor bg     = dark ? WpfColor.FromRgb(0x0C, 0x0C, 0x0C) : Colors.White;
        WpfColor fg     = dark ? Colors.White : WpfColor.FromRgb(0x0C, 0x0C, 0x0C);
        WpfColor muted  = dark ? WpfColor.FromRgb(0x55, 0x55, 0x55) : WpfColor.FromRgb(0x88, 0x88, 0x88);
        WpfColor border = dark ? WpfColor.FromRgb(0x2A, 0x2A, 0x2A) : WpfColor.FromRgb(0xCC, 0xCC, 0xCC);

        if (animate)
        {
            AnimRes("Br.Bg",     bg,     300);
            AnimRes("Br.Fg",     fg,     300);
            AnimRes("Br.Muted",  muted,  300);
            AnimRes("Br.Border", border, 300);
        }
        else
        {
            SetRes("Br.Bg",     bg);
            SetRes("Br.Fg",     fg);
            SetRes("Br.Muted",  muted);
            SetRes("Br.Border", border);
        }

        SyncDot();
    }

    // ── Resource helpers ──────────────────────────────────────────────────────

    void SetRes(string key, WpfColor color)
        => Resources[key] = new SolidColorBrush(color);

    // FIX: InvalidOperationException "объект только для чтения"
    // Причина: brush.Color = ... не работает если brush frozen или shared.
    // Решение: на каждом тике ЗАМЕНЯЕМ весь ресурс новым SolidColorBrush(interpolatedColor).
    // DynamicResource подхватывает замену автоматически — визуально идентично.
    // Дополнительно: проверяем _loaded перед любым доступом к Resources.
    void AnimRes(string key, WpfColor target, int ms)
    {
        if (!_loaded) return;

        // Читаем текущий цвет для интерполяции
        WpfColor from = Resources[key] is SolidColorBrush existing
            ? existing.Color
            : target;

        // Уникальный токен — позволяет прервать предыдущую анимацию того же ключа
        var token = new object();
        Resources[$"{key}.__token"] = token;

        var start = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) =>
        {
            // Если запущена новая анимация того же ключа — останавливаем старую
            if (!ReferenceEquals(Resources[$"{key}.__token"], token))
            {
                timer.Stop();
                return;
            }

            double t = Math.Min(1.0, (DateTime.UtcNow - start).TotalMilliseconds / ms);
            // Cubic ease-in-out
            t = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

            // FIX: создаём НОВЫЙ brush вместо мутации существующего
            Resources[key] = new SolidColorBrush(WpfColor.FromScRgb(
                Lerp(from.ScA, target.ScA, (float)t),
                Lerp(from.ScR, target.ScR, (float)t),
                Lerp(from.ScG, target.ScG, (float)t),
                Lerp(from.ScB, target.ScB, (float)t)));

            if (t >= 1.0) timer.Stop();
        };

        timer.Start();
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
