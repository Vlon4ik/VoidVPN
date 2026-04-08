# VoidVPN — Техническая документация
https://ru.pinterest.com/pin/704531935494964163/
> WPF-клиент для Windows на базе **sing-box 1.13.4**.  
> Протоколы VLESS/Reality и Shadowsocks, TUN-режим, постоянный kill switch, AES-256-GCM шифрование логов.

---

## Содержание

1. [Обзор проекта](#1-обзор-проекта)
2. [Структура репозитория](#2-структура-репозитория)
3. [Технологический стек](#3-технологический-стек)
4. [Граф зависимостей (DI)](#4-граф-зависимостей-di)
5. [Слой моделей (Core/Models)](#5-слой-моделей-coremodels)
6. [Слой сервисов (Core/Services)](#6-слой-сервисов-coreservices)
7. [Конечный автомат состояния](#7-конечный-автомат-состояния-подключения)
8. [Жизненный цикл подключения](#8-жизненный-цикл-подключения)
9. [Генерация конфига sing-box 1.13.4](#9-генерация-конфига-sing-box-1134)
10. [Kill Switch — детальный разбор](#10-kill-switch--детальный-разбор)
11. [Шифрование логов](#11-шифрование-логов)
12. [Парсинг URI-ключей](#12-парсинг-uri-ключей)
13. [UI-архитектура (MVVM + WPF)](#13-ui-архитектура-mvvm--wpf)
14. [Тема и анимации](#14-тема-и-анимации)
15. [Сборка и деплой](#15-сборка-и-деплой)
16. [Манифест и привилегии](#16-манифест-и-привилегии)
17. [Персистентность данных](#17-персистентность-данных)
18. [Критические миграции sing-box 1.12 → 1.13](#18-критические-миграции-sing-box-112--113)

---

## 1. Обзор проекта

VoidVPN — десктопный VPN-клиент только для Windows, который выступает GUI-оболочкой над **sing-box** — высокопроизводительным прокси-ядром на Go. Приложение само генерирует JSON-конфиг sing-box, запускает его как дочерний процесс, управляет маршрутизацией и kill switch на уровне ОС, перехватывает его лог в реальном времени и шифрует сессионные логи после отключения.

Ключевые характеристики:

- **Нет внешних API** — никакой телеметрии, регистрации, облачных сервисов
- **Kill switch всегда включён** — нет режима работы без защиты от утечки трафика
- **Только локальное хранилище** — профили и настройки в `%LocalAppData%\VoidVPN\`
- **Single-file exe** — sing-box.exe и wintun.dll встроены как EmbeddedResource

---

## 2. Структура репозитория

```
VoidVPN/
│
├── App.xaml                         # Application-level WPF ресурсы (стиль ScrollBar, ListBox)
├── App.xaml.cs                      # Точка входа: DI-контейнер, startup/exit логика
├── app.manifest                     # UAC (requireAdministrator), DPI-awareness, Win10/11 support
├── VoidVPN.csproj                   # Конфиг сборки: net8.0-windows, single-file, EmbeddedResource
│
├── Assets/
│   ├── icon.ico                     # Иконка приложения
│   ├── sing-box.exe                 # Прокси-ядро (EmbeddedResource)
│   └── wintun.dll                   # TUN-драйвер для Windows (EmbeddedResource)
│
├── Core/
│   ├── Exceptions/
│   │   └── VpnExceptions.cs         # Иерархия исключений домена
│   │
│   ├── Models/
│   │   ├── ConnectionState.cs       # Иммутабельное состояние + конечный автомат
│   │   ├── VpnProfile.cs            # Профиль подключения (все параметры протокола)
│   │   ├── SingBoxConfig.cs         # Полная объектная модель JSON-конфига sing-box 1.13.4
│   │   └── LogEntry.cs              # Запись лога с уровнем, временем и тегом
│   │
│   └── Services/
│       ├── SingBoxService.cs        # Оркестратор: запуск/остановка sing-box, pump логов
│       ├── ConfigGeneratorService.cs# Генерация валидного JSON-конфига из VpnProfile
│       ├── KillSwitchService.cs     # StrictRoute + blackhole-маршруты при краше
│       ├── NetworkService.cs        # Определение gateway, bypass-маршруты
│       ├── LogEncryptionService.cs  # AES-256-GCM шифрование, DPAPI-привязка ключа
│       ├── KeyParser.cs             # Парсер vless:// и ss:// URI
│       ├── ProfileRepository.cs     # Персистентность профилей (JSON, async, mutex)
│       └── SettingsService.cs       # Тема, последний ключ, настройки приложения
│
└── UI/
    ├── MainWindow.xaml              # Разметка: Simple View + Beta View, стили, анимации
    ├── MainWindow.xaml.cs           # Code-behind: тема, трей, анимации, состояние кнопок
    └── ViewModels/
        └── MainViewModel.cs         # MVVM ViewModel: команды, свойства, синхронизация
```

---

## 3. Технологический стек

| Компонент | Версия | Назначение |
|---|---|---|
| .NET | 8.0 (net8.0-windows) | Runtime, BCL |
| WPF | встроен в .NET 8 | UI-фреймворк |
| Windows Forms | встроен в .NET 8 | `NotifyIcon` для трея |
| CommunityToolkit.Mvvm | 8.3.2 | Source-gen: `[ObservableProperty]`, `[RelayCommand]` |
| Microsoft.Extensions.DI | 8.0.1 | IoC-контейнер |
| Microsoft.Extensions.Logging | 8.0.1 | Структурное логирование |
| System.Text.Json | 8.0.5 | Сериализация профилей и конфига |
| System.Security.Cryptography.ProtectedData | 8.0.0 | Windows DPAPI |
| sing-box | 1.13.4 | Прокси-ядро (дочерний процесс) |
| Wintun | — | TUN-драйвер для Windows |

**Сборка:** `win-x64`, `SelfContained=true`, `PublishSingleFile=true` с компрессией. sing-box.exe и wintun.dll вшиты как `EmbeddedResource`.

---

## 4. Граф зависимостей (DI)

Все сервисы регистрируются как `Singleton` в `App.xaml.cs`:

```
ServiceCollection
│
├── ConfigGeneratorService          (нет зависимостей — только ILogger)
├── NetworkService                  (нет зависимостей — только ILogger)
├── KillSwitchService               (нет зависимостей — только ILogger)
├── LogEncryptionService            (нет зависимостей — только ILogger)
│
├── SingBoxService ──────────────── ConfigGeneratorService
│                                   NetworkService
│                                   KillSwitchService
│                                   LogEncryptionService
│
├── ProfileRepository               (нет зависимостей — только ILogger)
├── SettingsService                 (нет зависимостей — только ILogger)
│
├── MainViewModel ───────────────── SingBoxService
│                                   ProfileRepository
│                                   SettingsService
│
└── MainWindow ──────────────────── MainViewModel
                                    SettingsService
```

`MainWindow` получает `MainViewModel` через DI-конструктор, DataContext устанавливается вручную: `DataContext = vm`. Это сознательное решение — не привязывать к автоматическому разрешению из XAML, чтобы сохранить контроль над порядком инициализации.

---

## 5. Слой моделей (Core/Models)

### VpnProfile

Полное описание одного подключения. Хранится в JSON-списке, передаётся в `ConfigGeneratorService` для генерации конфига и в `SingBoxService` для запуска.

```csharp
public enum ProxyProtocol    { Vless, Shadowsocks }
public enum VlessTransport   { Tcp, Grpc, WebSocket }

public sealed class VpnProfile
{
    public Guid   Id          // GUID — ключ для CRUD в ProfileRepository
    public string Name        // Отображаемое имя (из fragment URI или "host:port")

    // Общие
    public ProxyProtocol Protocol      // Vless | Shadowsocks
    public string ServerAddress        // hostname или IP
    public int    ServerPort           // 1–65535

    // VLESS-специфичные
    public string         Uuid         // UUID пользователя
    public string         Flow         // "xtls-rprx-vision" (только TCP)
    public VlessTransport Transport    // Tcp | Grpc | WebSocket
    public string         GrpcService  // serviceName для gRPC

    // Reality TLS
    public string RealitySni           // SNI для маскировки
    public string RealityPublicKey     // x25519 публичный ключ сервера
    public string RealityShortId       // короткий ID
    public string Fingerprint          // uTLS fingerprint ("chrome", "firefox" и т.д.)

    // Shadowsocks
    public string SsMethod             // "aes-256-gcm", "chacha20-ietf-poly1305" и др.
    public string SsPassword

    // Локальные порты
    public int LocalSocksPort  = 2080  // SOCKS5/HTTP mixed inbound
    public int LocalHttpPort   = 2081  // (резерв)

    // TUN
    public string TunAddress   = "172.19.0.1/30"
    public string TunAddressV6 = "fdfe:dcba:9876::1/126"
    public string DnsServer    = "8.8.8.8"           // upstream для DoH

    // Bypass
    public List<string> BypassProcesses = ["sing-box.exe"]
    public List<string> BypassCidrs     = ["10.0.0.0/8", "172.16.0.0/12",
                                           "192.168.0.0/16", "127.0.0.0/8"]

    // Вычисляемые свойства для UI
    public string DisplayHost   // "host:port"
    public string DisplayProto  // "vless/reality", "vless/grpc", "vless/ws", "ss"
}
```

### ConnectionState

Иммутабельный value-object, описывающий текущее состояние туннеля. Распространяется через событие `SingBoxService.StateChanged`.

```csharp
public enum VpnStatus { Disconnected, Connecting, Connected, Disconnecting, Error }

public sealed class ConnectionState
{
    public VpnStatus   Status
    public VpnProfile? ActiveProfile
    public string?     ErrorMessage

    // Фабричные методы (единственный способ создания):
    public static readonly ConnectionState Off            // Disconnected, нет профиля
    public static ConnectionState Connecting(VpnProfile)
    public static ConnectionState Connected(VpnProfile)
    public static ConnectionState Stopping(VpnProfile)
    public static ConnectionState Fail(VpnProfile?, string error)

    // Вычисляемые свойства для биндинга:
    public bool   IsActive    // Connected | Connecting | Disconnecting
    public string Label       // "connected", "connecting...", "error" и т.д.
    public string LabelUpper  // Label.ToUpper() — для заголовка статуса
    public string Detail      // "host:port  ·  :2080" или ""
}
```

### SingBoxConfig — объектная модель

`SingBoxConfig.cs` содержит все C#-классы, которые `System.Text.Json` сериализует в итоговый JSON. Структура отражает схему sing-box 1.13.4:

```
SingBoxConfig
├── LogConfig          { level, output, timestamp }
├── NtpConfig          { enabled, server="time.apple.com", interval="30m" }
├── DnsConfig
│   ├── Servers[]      { DnsServerHttps | DnsServerLocal }
│   ├── Rules[]        { DnsRule }
│   ├── Final          = "remote-dns"
│   ├── Strategy       = "prefer_ipv4"
│   └── IndependentCache = true
├── Inbounds[]         { TunInbound | MixedInbound }
├── Outbounds[]        { VlessOutbound | ShadowsocksOutbound | DirectOutbound | BlockOutbound }
├── RouteConfig
│   ├── Rules[]        { RouteRule }
│   ├── Final          = "proxy"
│   ├── AutoDetectInterface = true
│   └── DefaultDomainResolver = "local-dns"
└── ExperimentalConfig { CacheFileConfig }
```

Все поля с `null`-значением помечены `[JsonIgnore(Condition = WhenWritingNull)]` — в JSON попадают только заполненные поля.

### LogEntry

```csharp
public enum LogLevel { Debug, Info, Warn, Error }

public sealed class LogEntry
{
    public string   Time     // "HH:mm:ss"
    public LogLevel Level
    public string   Message
    public string   Tag      // "INF" | "WRN" | "ERR" | "DBG"
    public string   Full     // "{Time}  {Tag}  {Message}"
}
```

---

## 6. Слой сервисов (Core/Services)

### 6.1 SingBoxService

Центральный оркестратор. Управляет всем жизненным циклом туннеля.

**Поля состояния:**

```csharp
Process?    _proc        // дочерний процесс sing-box
VpnProfile? _active      // профиль текущей сессии
string?     _bypassIp    // IP сервера для bypass-маршрута (удаляется при disconnect)
CancellationTokenSource _logCts  // отмена pump-задач при остановке
SemaphoreSlim _lk = new(1,1)    // мьютекс Connect/Disconnect
```

**Публичные события:**

```csharp
event EventHandler<ConnectionState> StateChanged   // при каждом переходе состояния
event EventHandler<string>          LogLine        // каждая строка stdout/stderr sing-box
```

**ConnectAsync — последовательность (под мьютексом):**

```
1. Если _proc жив → KillAsync() + CleanupAsync() (защита от двойного вызова)
2. Push(Connecting)
3. FindExe() → путь к sing-box.exe
4. ConfigGeneratorService.WriteAsync(profile) → %LocalAppData%\VoidVPN\active.json
5. KillSwitchService.EnableAsync(serverIp)   ← SECURITY: kill switch ДО bypass
6. NetworkService.GetGatewayAsync()          ← определяем текущий default gateway
7. NetworkService.AddBypassAsync(serverIp, gw) ← маршрут к серверу через реальный gateway
8. CleanupTunInterfacesAsync()               ← убиваем конфликтующие VPN-процессы и TUN
9. LaunchAsync(exe, cfgPath)                 ← запуск sing-box + pump stdout/stderr
10. Task.Delay(1800ms)                       ← ожидание инициализации
11. Проверка IsRunning:
    - false → KillSwitchService.DisableAsync() + RollbackAsync() + throw
    - true  → Push(Connected)
```

**DisconnectAsync — последовательность (под мьютексом):**

```
1. Push(Stopping)
2. KillAsync()                   ← остановка процесса с grace period 5000ms
3. CleanupAsync()                ← удаление bypass-маршрута
4. KillSwitchService.DisableAsync() ← kill switch выключается ПОСЛЕ sing-box
5. EncryptSessionLogAsync()      ← шифруем sing-box.log → session-YYYYMMDD-HHmmss.log.enc
6. Push(Off)
```

**KillAsync — защита от NullReferenceException:**

Критическая деталь: `OnExited` вызывается из потока CLR threadpool и может обнулить `_proc = null` в любой момент. `KillAsync` захватывает локальную копию **до** любого `await`:

```csharp
async Task KillAsync()
{
    var proc = _proc;      // захват до await — защита от гонки с OnExited
    _proc = null;          // сразу обнуляем поле

    if (proc == null) return;
    await _logCts.CancelAsync();
    proc.Exited -= OnExited;  // отписка ДО await — иначе OnExited повторно обнулит proc

    if (!proc.HasExited)
    {
        proc.CloseMainWindow();
        if (!proc.WaitForExit(5000))  // синхронный WaitForExit — нет риска NullRef
            proc.Kill(entireProcessTree: true);
        await proc.WaitForExitAsync(); // финальный await — proc локальная, _proc=null
    }
    proc.Dispose();
}
```

**OnExited — обработка краша:**

```csharp
void OnExited(object? s, EventArgs e)
{
    // Захватываем копии ДО async Task.Run — поля могут измениться
    var proc    = _proc;
    var profile = _active;
    int code    = proc?.ExitCode ?? -1;

    Task.Run(async () =>
    {
        if (proc != null) { proc.Exited -= OnExited; proc.Dispose(); }
        await CleanupAsync();  // удаляем bypass-маршрут
        _active = null;

        if (code != 0)
            // SECURITY: трафик блокируется немедленно при неожиданном краше
            await _ks.ActivateEmergencyBlackholeAsync();

        Push(code == 0 ? ConnectionState.Off
                       : ConnectionState.Fail(profile,
                           $"sing-box exited (code {code}) — kill-switch ACTIVE, press Stop to release"));
    });
}
```

**CleanupTunInterfacesAsync:**

Перед запуском убивает список из 20+ известных VPN-процессов (`openvpn`, `wireguard`, `nordvpnd`, `tailscaled`, `clash`, `v2ray` и др.) и через PowerShell удаляет TUN-адаптеры с именами/описаниями, содержащими `Wintun`, `WireGuard`, `TAP-Windows`, `void-tun` и т.д. Два задержки: 400ms после kill процессов, 600ms после PowerShell — дать OS время освободить адаптеры.

**PumpAsync — стриминг логов:**

```csharp
async Task PumpAsync(StreamReader r, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var line = await r.ReadLineAsync(ct);
        if (line == null) break;          // EOF — процесс завершился
        LogLine?.Invoke(this, line);       // → MainViewModel.AddLog
    }
}
```

Два pump-экземпляра: один для `stdout`, один для `stderr`. Отменяются через `_logCts` при `KillAsync`.

---

### 6.2 ConfigGeneratorService

Принимает `VpnProfile`, возвращает валидный `SingBoxConfig` для sing-box 1.13.4.

**Валидация перед генерацией:**

```
Общие:       ServerAddress не пустой, Port в диапазоне 1–65535
VLESS:       UUID не пустой, RealityPublicKey не пустой, RealitySni не пустой
Shadowsocks: SsPassword не пустой
```

При ошибке бросает `ConfigException` со списком всех нарушений.

**DNS конфигурация:**

```csharp
DnsConfig
{
    Servers = [
        DnsServerHttps {
            Tag    = "remote-dns",
            Server = profile.DnsServer,   // 8.8.8.8 по умолчанию
            Port   = 443,
            Path   = "/dns-query",
            Detour = "proxy",             // DNS-запросы туннелируются через VPN
            DomainStrategy = "prefer_ipv4"
        },
        DnsServerLocal {
            Tag = "local-dns"             // bootstrap для разрешения hostname сервера
        }
    ],
    Rules  = [],    // outbound DNS rule УДАЛЁН в sing-box 1.12 (deprecated) → 1.13 (fatal)
    Final  = "remote-dns",
    Strategy = "prefer_ipv4",
    IndependentCache = true
}
```

Поле `DefaultDomainResolver = "local-dns"` на уровне `RouteConfig` заменяет устаревший DNS rule `{ outbound: "any" }`.

**Inbounds:**

```
TunInbound:
  - InterfaceName = "void-tun"
  - Address       = [profile.TunAddress, profile.TunAddressV6]   // IPv4 + IPv6
  - MTU           = 9000
  - AutoRoute     = true       // sing-box сам пишет маршруты
  - StrictRoute   = true       // Kill switch на уровне ядра
  - Stack         = "mixed"    // gVisor для UDP + system для TCP
  // sniff, gso, domain_strategy удалены в 1.13 — теперь через route rule actions

MixedInbound:
  - Listen     = "127.0.0.1"
  - ListenPort = profile.LocalSocksPort (2080)
  // sniff удалён в 1.13
```

**Outbounds (sing-box 1.13 — нет dns outbound):**

```
[proxy, direct, block]
  // dns outbound ПОЛНОСТЬЮ УДАЛЁН в 1.13
  // DNS перехват теперь через route rule { protocol: ["dns"], action: "hijack-dns" }
```

**VLESS outbound:**

```csharp
VlessOutbound {
    Server         = profile.ServerAddress,
    ServerPort     = profile.ServerPort,
    Uuid           = profile.Uuid,
    Flow           = (Transport == Tcp && Flow != "") ? profile.Flow : null,
    PacketEncoding = "xudp",
    Tls = TlsConfig {
        Enabled    = true,
        ServerName = profile.RealitySni,
        Utls       = { Enabled=true, Fingerprint=profile.Fingerprint },
        Reality    = { Enabled=true, PublicKey=profile.RealityPublicKey,
                       ShortId=profile.RealityShortId }
    },
    Transport = profile.Transport switch {
        Grpc      => { type="grpc", service_name=profile.GrpcService, idle_timeout="15s" },
        WebSocket => { type="ws", path="/" },
        Tcp       => null   // transport блок не нужен для TCP/Reality
    }
}
```

**Route rules (действия в порядке приоритета):**

```
Rule 1: { action: "sniff" }
    → Классификация протокола/домена для всего входящего трафика.
    → Заменяет inbound.sniff=true, удалённый в 1.13.

Rule 2: { protocol: ["dns"], action: "hijack-dns" }
    → Перехват DNS-запросов → обработка согласно DnsConfig.
    → Заменяет outbound типа "dns", удалённый в 1.13.

Rule 3: { process_name: [...BypassProcesses], outbound: "direct" }
    → sing-box.exe идёт напрямую — иначе петля маршрутизации.

Rule 4: { ip_cidr: [...BypassCidrs], outbound: "direct" }
    → LAN и loopback трафик не туннелируется.

Rule 5: { inbound: ["mixed-in"], outbound: "proxy" }
    → Явный SOCKS5/HTTP через mixed inbound → всегда в proxy.

Final: "proxy"
    → Весь остальной трафик → туннель.
```

---

### 6.3 KillSwitchService

Kill switch реализован в два эшелона.

**Эшелон 1 — StrictRoute (постоянный):**

`TunInbound.StrictRoute = true` — встроенный механизм sing-box. Пока процесс работает, весь трафик обязан идти через `void-tun`. Любой трафик, не проходящий через TUN-адаптер, дропается на уровне таблицы маршрутизации Windows.

**Эшелон 2 — Emergency Blackhole (при краше):**

Если sing-box упал неожиданно (`OnExited` с ненулевым кодом), `StrictRoute` пропадает вместе с процессом. В этот момент `ActivateEmergencyBlackholeAsync` добавляет два маршрута:

```
route ADD 0.0.0.0   MASK 128.0.0.0 224.0.0.1 METRIC 1
route ADD 128.0.0.0 MASK 128.0.0.0 224.0.0.1 METRIC 1
```

Вместе они покрывают весь IPv4-диапазон (0.0.0.0/0). Gateway `224.0.0.1` — мультикаст-адрес, которого нет ни в одной LAN. Windows не может доставить пакет и отвечает "Network unreachable". Трафик блокирован до тех пор, пока пользователь явно не нажмёт **Stop**.

**Почему не netsh advfirewall:**

```
advfirewall применяется ДО создания TUN-адаптера sing-box.
При firewall-правиле "блокировать всё кроме void-tun":
  - void-tun ещё не существует при старте
  - правило блокирует и исходящий трафик sing-box к серверу
  - туннель не устанавливается
  → Полная неработоспособность
```

Маршрутный подход работает корректно потому, что bypass-маршрут к серверу (через реальный gateway) добавляется до старта sing-box, а StrictRoute вступает в силу после создания TUN-адаптера.

---

### 6.4 NetworkService

**GetGatewayAsync:**

Итерирует все сетевые интерфейсы через `NetworkInterface.GetAllNetworkInterfaces()`, пропускает loopback и tunnel-адаптеры, ищет первый IPv4 gateway с адресом != `0.0.0.0`. Возвращает `string?` — `null` если gateway не найден.

**AddBypassAsync / RemoveBypassAsync:**

```
route ADD {serverIp} MASK 255.255.255.255 {gateway} METRIC 5
route DELETE {serverIp}
```

Host-маршрут (маска /32) с метриком 5 гарантирует приоритет перед дефолтным маршрутом через TUN. Это позволяет sing-box подключиться к серверу через реальный интерфейс, а не через собственный TUN (что создало бы петлю).

При ошибке `AddBypassAsync` бросает `RouteException` — `SingBoxService` ловит её как non-fatal и продолжает без bypass.

---

### 6.5 LogEncryptionService

**Модель безопасности:**

```
Ключ: 256-bit случайный → DPAPI.Protect(LocalMachine) → %LocalAppData%\VoidVPN\log.key
Алгоритм: AES-256-GCM
Nonce: 12 байт, уникальный для каждой строки лога
Tag: 16 байт (аутентификация)
```

Шифрование построчное: каждая строка лога шифруется независимо. Это позволяет читать частично записанные файлы и устойчиво к повреждению — порча одной строки не ломает остальные.

**Формат `.log.enc` файла:**

```
[4 байта: длина блока в little-endian]
[N байт: зашифрованный блок]
  ├── [12 байт: nonce]
  ├── [16 байт: GCM auth tag]
  └── [M байт: шифротекст]
[следующий 4-байтный заголовок...]
```

**SecureDelete:**

После шифрования исходный `sing-box.log` перезаписывается нулями (чанками по 4096 байт), затем удаляется. Это предотвращает восстановление через file carving.

**DPAPI-привязка:**

```csharp
// Защита ключа
ProtectedData.Protect(key, null, DataProtectionScope.LocalMachine)
// Восстановление
ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.LocalMachine)
```

`LocalMachine` — ключ привязан к машине, а не к пользователю. Файл `.log.enc` невозможно расшифровать на другой машине, даже имея копию `log.key`.

---

### 6.6 KeyParser

Статический класс. Парсит стандартные URI-форматы без внешних зависимостей.

**vless:// URI — формат:**

```
vless://{uuid}@{host}:{port}?type={tcp|grpc|ws}&security=reality
         &sni={sni}&fp={fingerprint}&pbk={publicKey}&sid={shortId}
         &flow={flow}&serviceName={grpcService}
         #{name (URL-encoded)}
```

Логика парсинга:
1. Отрезаем `#fragment` → декодируем как имя профиля
2. Парсим URI через `Uri.TryCreate`
3. `uuid` = `uri.UserInfo`, `host` = `uri.Host`, `port` = `uri.Port`
4. Query string через `HttpUtility.ParseQueryString`
5. `type` → `VlessTransport` (grpc/ws/tcp)
6. Если transport != Tcp → `flow = ""` (flow работает только с TCP/Reality)

**ss:// URI — два формата:**

```
Формат 1 (SIP002): ss://{base64(method:password)}@{host}:{port}#{name}
Формат 2 (legacy): ss://{base64(method:password@host:port)}#{name}
```

`TryB64` пробует base64-декодирование с паддингом и заменой URL-safe символов (`-`→`+`, `_`→`/`). Если не получилось — использует строку как есть.

---

### 6.7 ProfileRepository

Thread-safe хранилище профилей. Все операции защищены `SemaphoreSlim(1,1)`.

```
%LocalAppData%\VoidVPN\profiles.json
```

**API:**

```csharp
Task<List<VpnProfile>> LoadAllAsync(CancellationToken)
Task SaveAsync(VpnProfile, CancellationToken)    // upsert по Id
Task DeleteAsync(Guid, CancellationToken)
```

`SaveAsync` делает upsert: ищет профиль по `Id`, заменяет если найден, добавляет если нет. Сериализация с `WriteIndented=true` — файл читаем вручную.

---

### 6.8 SettingsService

Хранит три настройки приложения:

```csharp
public sealed class AppSettings
{
    public bool   IsDarkTheme       { get; set; } = true
    public string LastRawKey        { get; set; } = ""     // последний ключ в Simple View
    public bool   KillSwitchEnabled { get; set; } = true   // ВСЕГДА true, нельзя отключить
}
```

`KillSwitchEnabled` форсируется в `true` при каждом `LoadAsync` и `SaveAsync` — даже если кто-то вручную поставил `false` в JSON. Kill switch не является опцией.

---

## 7. Конечный автомат состояния подключения

```
                    ┌─────────────────────────────────────────────┐
                    │                                             │
                    ▼                                             │
              ┌──────────┐    ConnectAsync()    ┌─────────────┐  │
              │Disconnect│ ──────────────────►  │ Connecting  │  │
              │   -ed    │                      └─────────────┘  │
              └──────────┘                             │         │
                    ▲                        sing-box  │         │
                    │                        запущен   │         │
                    │                                  ▼         │
              ┌──────────┐   DisconnectAsync()  ┌──────────┐    │
              │Disconnect│  ◄────────────────── │Connected │    │
              │  -ing    │                      └──────────┘    │
              └──────────┘                             │         │
                    │                      OnExited    │         │
                    │                      (code != 0) │         │
                    │                                  ▼         │
                    │                           ┌──────────┐     │
                    │         DisconnectAsync() │  Error   │ ────┘
                    └────────────────────────── └──────────┘
```

Переходы инициируются из пользовательских действий (`ConnectAsync` / `DisconnectAsync`) и автоматически при завершении sing-box (`OnExited`).

Состояние хранится в `SingBoxService.State` и распространяется через `StateChanged` → `MainViewModel.SyncState` → WPF bindings.

---

## 8. Жизненный цикл подключения

### Успешное подключение

```
User: CONNECT
  │
  ├─ SingBoxService.ConnectAsync(profile)
  │    ├─ [mutex acquired]
  │    ├─ Push(Connecting)                      → UI: "connecting..."
  │    ├─ ConfigGeneratorService.WriteAsync()   → active.json
  │    ├─ KillSwitchService.EnableAsync()       → cleanup stale blackhole
  │    ├─ NetworkService.GetGatewayAsync()      → "192.168.1.1"
  │    ├─ NetworkService.AddBypassAsync()       → route ADD server/32 via 192.168.1.1
  │    ├─ CleanupTunInterfacesAsync()           → kill VPN processes, remove old TUN
  │    ├─ LaunchAsync(sing-box.exe, active.json)
  │    │    └─ PumpAsync(stdout) + PumpAsync(stderr) → LogLine events
  │    ├─ Task.Delay(1800ms)
  │    ├─ IsRunning == true ✓
  │    └─ Push(Connected)                       → UI: "CONNECTED"
```

### Нормальное отключение

```
User: STOP
  │
  ├─ SingBoxService.DisconnectAsync()
  │    ├─ [mutex acquired]
  │    ├─ Push(Stopping)                        → UI: "disconnecting..."
  │    ├─ KillAsync()
  │    │    ├─ _logCts.Cancel()                 → pump tasks stop
  │    │    ├─ proc.Exited -= OnExited          → no double cleanup
  │    │    ├─ proc.CloseMainWindow()
  │    │    ├─ proc.WaitForExit(5000ms)
  │    │    └─ proc.Dispose()
  │    ├─ CleanupAsync()                        → route DELETE server/32
  │    ├─ KillSwitchService.DisableAsync()      → remove blackhole (если был)
  │    ├─ EncryptSessionLogAsync()              → sing-box.log → session-*.log.enc
  │    └─ Push(Off)                             → UI: "disconnected"
```

### Аварийный краш sing-box

```
sing-box: ExitCode = 1
  │
  ├─ OnExited(code=1)
  │    └─ Task.Run:
  │         ├─ proc.Dispose()
  │         ├─ CleanupAsync()                   → route DELETE server/32
  │         ├─ KillSwitchService
  │         │    .ActivateEmergencyBlackholeAsync()
  │         │    → route ADD 0.0.0.0/128.0.0.0 METRIC 1 via 224.0.0.1
  │         │    → route ADD 128.0.0.0/128.0.0.0 METRIC 1 via 224.0.0.1
  │         │    → трафик ЗАБЛОКИРОВАН
  │         └─ Push(Error: "sing-box exited (code 1) — kill-switch ACTIVE")
  │
User: STOP
  │
  └─ DisconnectAsync()
       └─ KillSwitchService.DisableAsync()
            → route DELETE blackhole routes
            → трафик разблокирован
```

---

## 9. Генерация конфига sing-box 1.13.4

**Итоговый JSON для VLESS/Reality/TCP:**

```json
{
  "log": { "level": "info", "output": "sing-box.log", "timestamp": true },
  "ntp": { "enabled": true, "server": "time.apple.com", "interval": "30m" },
  "dns": {
    "servers": [
      {
        "type": "https", "tag": "remote-dns",
        "server": "8.8.8.8", "server_port": 443, "path": "/dns-query",
        "detour": "proxy", "domain_strategy": "prefer_ipv4"
      },
      { "type": "local", "tag": "local-dns" }
    ],
    "final": "remote-dns",
    "strategy": "prefer_ipv4",
    "independent_cache": true
  },
  "inbounds": [
    {
      "type": "tun", "tag": "tun-in",
      "interface_name": "void-tun",
      "address": ["172.19.0.1/30", "fdfe:dcba:9876::1/126"],
      "mtu": 9000, "auto_route": true,
      "strict_route": true, "stack": "mixed"
    },
    {
      "type": "mixed", "tag": "mixed-in",
      "listen": "127.0.0.1", "listen_port": 2080
    }
  ],
  "outbounds": [
    {
      "type": "vless", "tag": "proxy",
      "server": "1.2.3.4", "server_port": 443,
      "uuid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "flow": "xtls-rprx-vision",
      "packet_encoding": "xudp",
      "tls": {
        "enabled": true,
        "server_name": "www.example.com",
        "utls": { "enabled": true, "fingerprint": "chrome" },
        "reality": { "enabled": true, "public_key": "...", "short_id": "..." }
      }
    },
    { "type": "direct", "tag": "direct" },
    { "type": "block",  "tag": "block"  }
  ],
  "route": {
    "rules": [
      { "action": "sniff" },
      { "protocol": ["dns"], "action": "hijack-dns" },
      { "process_name": ["sing-box.exe"], "outbound": "direct" },
      { "ip_cidr": ["10.0.0.0/8","172.16.0.0/12","192.168.0.0/16","127.0.0.0/8"],
        "outbound": "direct" },
      { "inbound": ["mixed-in"], "outbound": "proxy" }
    ],
    "final": "proxy",
    "auto_detect_interface": true,
    "default_domain_resolver": "local-dns"
  },
  "experimental": {
    "cache_file": { "enabled": true, "path": "cache.db" }
  }
}
```

---

## 10. Kill Switch — детальный разбор

### Почему StrictRoute + Blackhole, а не firewall

| Подход | Проблема |
|---|---|
| `netsh advfirewall` — блокировать всё кроме void-tun | Правило применяется ДО создания адаптера. sing-box сам не может подключиться к серверу. Туннель не устанавливается. |
| `netsh advfirewall` — разрешить sing-box.exe | Уязвимость: утечка при использовании другого процесса |
| Blackhole без StrictRoute | Если убрать blackhole вручную → утечка до явного Disconnect |
| **StrictRoute + Emergency Blackhole** ✓ | StrictRoute — постоянная защита пока sing-box работает. Blackhole — защита при краше |

### Временна́я диаграмма безопасности

```
Start     KS.Enable  AddBypass  sing-box запущен  KS.Disable  Stop
  │          │           │             │                │        │
──┼──────────┼───────────┼─────────────┼────────────────┼────────┤
  │ stale    │ cleanup   │ bypass route│  StrictRoute   │cleanup │
  │ cleanup  │ complete  │ installed   │  active        │        │
  │          │           │             │                │        │
  │[возможна │[трафик    │[сервер      │[всё в TUN,     │[чисто] │
  │ блокировка│блокирован │достижим,    │утечка невозм.] │        │
  │ от краша]│           │но TUN ещё   │                │        │
  │          │           │не активен]  │                │        │
```

---

## 11. Шифрование логов

### Pipeline

```
sing-box stdout/stderr
    │
    ▼ (PumpAsync → LogLine event)
ObservableCollection<LogEntry>  ──►  UI ListView
    │
    │  При Disconnect:
    ▼
sing-box.log (plaintext, в папке sing-box.exe)
    │
    ▼ (LogEncryptionService.EncryptLogAsync)
Построчное шифрование:
  foreach line:
    nonce = RandomNumberGenerator.GetBytes(12)
    AesGcm.Encrypt(nonce, UTF8(line), ciphertext, tag[16])
    output: [4B len][12B nonce][16B tag][N ciphertext]
    │
    ▼
session-YYYYMMDD-HHmmss.log.enc
    │
    ▼ (SecureDelete)
sing-box.log перезаписан нулями → удалён
```

### Управление ключом

```
Первый запуск:
  RandomNumberGenerator.GetBytes(32) → key
  DPAPI.Protect(key, LocalMachine) → protectedKey
  File.WriteAllBytes("log.key", protectedKey)

Последующие запуски:
  File.ReadAllBytes("log.key") → protectedKey
  DPAPI.Unprotect(protectedKey, LocalMachine) → key

При повреждении log.key:
  Логируем warning, генерируем новый ключ
  Старые .log.enc файлы расшифровать невозможно
```

---

## 12. Парсинг URI-ключей

### vless:// — полный алгоритм

```
Input: "vless://uuid@host:443?type=grpc&serviceName=myservice&security=reality
                &sni=www.google.com&fp=chrome&pbk=XXXX&sid=abcd#My Server"

1. Ищем '#' → name = "My Server", raw = без фрагмента
2. Uri.TryCreate(raw) → uri
3. uuid = uri.UserInfo                     = "uuid"
4. host = uri.Host                         = "host"
5. port = uri.Port > 0 ? uri.Port : 443    = 443
6. q    = ParseQueryString(uri.Query)
7. sni  = q["sni"] ?? q["serverName"] ?? host
8. fp   = q["fp"] ?? "chrome"
9. pbk  = q["pbk"] ?? ""
10. sid  = q["sid"] ?? ""
11. type = q["type"] ?? "tcp"   → transport = Grpc
12. flow = q["flow"] ?? ""
13. svc  = q["serviceName"]     = "myservice"
14. transport == Grpc → flow = ""  (flow только для TCP)

Result: VpnProfile {
    Name="My Server", Protocol=Vless,
    ServerAddress="host", ServerPort=443,
    Uuid="uuid", Transport=Grpc, GrpcService="myservice",
    RealitySni="www.google.com", RealityPublicKey="XXXX",
    RealityShortId="abcd", Fingerprint="chrome"
}
```

### ss:// — два формата

```
Формат SIP002:
  ss://BASE64(method:password)@host:8388#name
    → decode base64 → "aes-256-gcm:mypassword"
    → split на ':' → method, password

Формат legacy:
  ss://BASE64(method:password@host:8388)#name
    → decode base64 → "aes-256-gcm:mypassword@1.2.3.4:8388"
    → split по последнему '@' → cred, addr
    → split cred по ':' → method, password
    → split addr по последнему ':' → host, port
```

---

## 13. UI-архитектура (MVVM + WPF)

### MainViewModel

Публичные свойства (source-gen через `[ObservableProperty]`):

| Свойство | Тип | Назначение |
|---|---|---|
| `StatusText` | string | "connected" / "disconnected" / "connecting..." |
| `StatusUpper` | string | для заголовка: "CONNECTED" |
| `DetailText` | string | "host:port  ·  :2080" |
| `IsConnected` | bool | переключает CONNECT/STOP |
| `IsBusy` | bool | блокирует кнопки во время операций |
| `IsAnimating` | bool | управляет DotsAnim Storyboard |
| `RawKey` | string | биндинг поля ввода ключа |
| `ParseError` | string | сообщение об ошибке парсинга |
| `KeyValid` | bool | ключ корректно распарсен |
| `SelectedProfile` | VpnProfile? | выбранный профиль из списка |
| `LocalPortText` | string | "socks  127.0.0.1:2080" |
| `LogCount` | int | счётчик строк лога |
| `KillSwitchActive` | bool | всегда `true`, биндинг для UI-индикатора |

Команды (source-gen через `[RelayCommand]`):

```
QuickConnectCommand   → Toggle: если IsConnected → Disconnect, иначе Connect из _parsed или SelectedProfile
ConnectCommand        → DoConnectAsync(SelectedProfile), CanExecute: SelectedProfile != null && !IsBusy
DisconnectCommand     → DisconnectAsync()
AddKeyCommand         → SaveAsync(_parsed) + LoadProfiles + RawKey=""
DeleteKeyCommand      → DeleteAsync(SelectedProfile.Id) + LoadProfiles
ClearLogCommand       → LogEntries.Clear()
CopyLogCommand        → Clipboard.SetText(все строки лога)
```

**`OnRawKeyChanged`** (partial method, вызывается source-gen при изменении `RawKey`):

```csharp
partial void OnRawKeyChanged(string v)
{
    ParseError = ""; KeyValid = false; _parsed = null;
    if (string.IsNullOrWhiteSpace(v)) return;

    if (KeyParser.TryParse(v, out var p, out string err))
        { _parsed = p; KeyValid = true; }
    else
        ParseError = err;

    _ = _settings.SetLastKeyAsync(v);  // fire-and-forget persist
}
```

**SyncState** — маппинг `ConnectionState` → свойства ViewModel:

```csharp
void SyncState(ConnectionState s)
{
    StatusText  = s.Label;
    StatusUpper = s.LabelUpper;
    DetailText  = s.Detail;
    IsConnected = s.Status == VpnStatus.Connected;
    IsAnimating = s.Status is Connecting or Disconnecting;

    if (s.Status is Connected or Disconnected or Error)
        IsBusy = false;  // разблокировать кнопки

    if (s.ErrorMessage != null)
        AddLog(AppLog.Error, s.ErrorMessage);
}
```

### Два UI-режима

**Simple View** — для быстрого подключения. Центрированный вертикальный стек: заголовок → статус → поле ввода + SAVE → дропдаун профилей + удаление → кнопка CONNECT. Переключатель тем + кнопка `β BETA`.

**Beta View** — расширенный. Заголовок с badge kill-switch → статус-карточка с кнопками CONNECT/STOP → поле ввода + ADD → список сохранённых профилей → лог в реальном времени (COPY/CLEAR) → футер с портом, REMOVE, кнопкой `← SIMPLE`.

Переключение через `BtnToggleMode_Click`: `Visibility.Collapsed/Visible` + `FadeIn` Storyboard (CubicEase, 200ms).

---

## 14. Тема и анимации

### Цветовые ресурсы

В `MainWindow.Resources` определены динамические кисти:

| Ключ | Dark | Light |
|---|---|---|
| `Br.Bg` | `#0C0C0C` | `#FFFFFF` |
| `Br.Fg` | `#FFFFFF` | `#0C0C0C` |
| `Br.Muted` | `#555555` | `#888888` |
| `Br.Border` | `#2A2A2A` | `#CCCCCC` |
| `Br.StatusDot` | `#333333` (off) / `#FFFFFF` (on) | `#BBBBBB` / `#0C0C0C` |

### AnimRes — кастомная анимация кистей

Стандартный `ColorAnimation` WPF мутирует `SolidColorBrush.Color` — это вызывает `InvalidOperationException` если кисть заморожена. Решение — заменять весь ресурс новым экземпляром каждый тик через `DispatcherTimer(16ms)`:

```csharp
void AnimRes(string key, WpfColor target, int ms)
{
    WpfColor from = (Resources[key] as SolidColorBrush)?.Color ?? target;

    var token = new object();          // уникальный токен для прерывания предыдущей анимации
    Resources[$"{key}.__token"] = token;

    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
    timer.Tick += (_, _) =>
    {
        if (!ReferenceEquals(Resources[$"{key}.__token"], token))
        { timer.Stop(); return; }  // новая анимация прервала эту

        double t = Math.Min(1.0, elapsed / ms);
        t = t < 0.5 ? 4*t*t*t : 1 - Math.Pow(-2*t+2, 3)/2;  // cubic ease-in-out

        // FIX: создаём НОВЫЙ brush вместо мутации существующего
        Resources[key] = new SolidColorBrush(WpfColor.FromScRgb(
            Lerp(from.ScA, target.ScA, t), Lerp(from.ScR, target.ScR, t),
            Lerp(from.ScG, target.ScG, t), Lerp(from.ScB, target.ScB, t)));

        if (t >= 1.0) timer.Stop();
    };
    timer.Start();
}
```

`DynamicResource` в XAML автоматически подхватывает замену ресурса без дополнительной нотификации.

### DotsAnim — индикатор загрузки

Три `Ellipse` на `Canvas`. Storyboard анимирует `Canvas.Left` каждой точки по ключевым кадрам с `CubicEase EaseInOut`, создавая эффект "гоночных точек". `RepeatBehavior="Forever"`. Управляется через `MainViewModel.IsAnimating` → `SyncAnim()` в code-behind.

### Кастомный ScrollBar

Горизонтальный scrollbar для лога: высота 4px, прозрачный фон, thumb с 2px corner radius, без стрелочных кнопок (RepeatButton с `Opacity=0`). `LogScrollViewer` — кастомный шаблон `ScrollViewer`, размещающий `PART_HorizontalScrollBar` под контентом в Grid.

---

## 15. Сборка и деплой

### Параметры проекта

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
<DebugType>none</DebugType>
```

### EmbeddedResource

sing-box.exe и wintun.dll вшиты в exe:

```xml
<EmbeddedResource Include="Assets\sing-box.exe" />
<EmbeddedResource Include="Assets\wintun.dll" />
```

`SingBoxService.FindExe()` ищет `sing-box.exe` в трёх местах:
1. `{BaseDirectory}/Assets/sing-box.exe`
2. `{BaseDirectory}/sing-box.exe`
3. Рекурсивно вверх по дереву директорий

### Команды сборки

```bash
# Debug
dotnet build

# Release (без single-file)
dotnet build -c Release

# Single-file executable
dotnet publish -c Release -r win-x64 --self-contained
# Результат: bin/x64/Release/net8.0-windows/win-x64/publish/VoidVPN.exe
```

### Требования к запуску

- Windows 10 / 11 x64
- Запуск от **Администратора** (UAC manifest: `requireAdministrator`)
- `sing-box.exe` v1.13.4 в папке Assets (или вшит как EmbeddedResource)

---

## 16. Манифест и привилегии

`app.manifest` определяет:

**UAC:** `requestedExecutionLevel level="requireAdministrator"` — Windows показывает UAC-prompt при запуске. Дублирующая проверка в коде:

```csharp
static bool IsAdmin()
{
    using var id = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
}
```

Если не admin — показывается `MessageBox` и `Shutdown(1)`.

**DPI Awareness:** `PerMonitorV2` (Windows 10 1703+) с fallback `true/pm`. WPF рендерит в физических пикселях монитора — нет размытия на HiDPI. `gdiScaling=false` — WPF обрабатывает DPI сам.

**Совместимость:** заявлена для Windows 8, 8.1, 10, 11 через `supportedOS` GUID.

---

## 17. Персистентность данных

Все данные хранятся в `%LocalAppData%\VoidVPN\`:

| Файл | Содержимое | Формат |
|---|---|---|
| `profiles.json` | Список VpnProfile | JSON, indented |
| `settings.json` | AppSettings (тема, ключ) | JSON, indented |
| `active.json` | Текущий конфиг sing-box | JSON, генерируется при Connect |
| `log.key` | DPAPI-защищённый AES-ключ | бинарный |
| `session-*.log.enc` | Зашифрованные логи сессий | бинарный (см. раздел 11) |
| `cache.db` | sing-box DNS/route кэш | SQLite (управляется sing-box) |

---

## 18. Критические миграции sing-box 1.12 → 1.13

VoidVPN явно написан под sing-box **1.13.4** и учитывает три критических изменения API:

### Удалён outbound типа `"dns"` (1.13)

```json
// НЕПРАВИЛЬНО (sing-box < 1.13):
{ "type": "dns", "tag": "dns-out" }

// ПРАВИЛЬНО (sing-box 1.13+) — в outbounds только proxy + direct + block.
// В route.rules добавляем:
{ "protocol": ["dns"], "action": "hijack-dns" }
```

### Удалены поля `sniff`, `gso`, `domain_strategy` из inbound (1.13)

```json
// НЕПРАВИЛЬНО:
{ "type": "tun", "sniff": true, "gso": true, "domain_strategy": "prefer_ipv4" }

// ПРАВИЛЬНО — поля убраны из inbound:
{ "type": "tun" }
// Sniff теперь через route rule:
{ "action": "sniff" }
```

### Deprecated DNS rule `"outbound: any"` → fatal в 1.14 (мигрировано в 1.12)

```json
// НЕПРАВИЛЬНО (deprecated с 1.12, fatal с 1.14):
{ "dns": { "rules": [{ "outbound": "any", "server": "local-dns" }] } }

// ПРАВИЛЬНО (sing-box 1.12+):
{ "route": { "default_domain_resolver": "local-dns" } }
```

Все три изменения явно задокументированы в комментариях `SingBoxConfig.cs` и `ConfigGeneratorService.cs`.

---

*Документация соответствует исходному коду, sing-box 1.13.4, .NET 8.0.*
