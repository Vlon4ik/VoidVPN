using System.Collections.Generic;
using System.Text.Json.Serialization;

// sing-box 1.13.4 — KEY MIGRATION NOTES:
//   • "dns" outbound REMOVED → use route rule { action: "hijack-dns" }
//   • inbound "sniff" field REMOVED → use route rule { action: "sniff" }
//   • TUN "gso" / "domain_strategy" REMOVED (now automatic)
//   • DNS server "strategy" → moved to global DnsConfig.Strategy

namespace VoidVPN.Core.Models;

public sealed class SingBoxConfig
{
	[JsonPropertyName("log")] public LogConfig Log { get; set; } = new();
	[JsonPropertyName("dns")] public DnsConfig Dns { get; set; } = new();
	[JsonPropertyName("ntp")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public NtpConfig? Ntp { get; set; }
	[JsonPropertyName("inbounds")] public List<object> Inbounds { get; set; } = new();
	[JsonPropertyName("outbounds")] public List<object> Outbounds { get; set; } = new();
	[JsonPropertyName("route")] public RouteConfig Route { get; set; } = new();
	[JsonPropertyName("experimental")] public ExperimentalConfig Experimental { get; set; } = new();
}

public sealed class LogConfig
{
	[JsonPropertyName("level")] public string Level { get; set; } = "info";
	[JsonPropertyName("output")] public string Output { get; set; } = "sing-box.log";
	[JsonPropertyName("timestamp")] public bool Timestamp { get; set; } = true;
}

public sealed class NtpConfig
{
	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("server")] public string Server { get; set; } = "time.apple.com";
	[JsonPropertyName("interval")] public string Interval { get; set; } = "30m";
}

// ── DNS ───────────────────────────────────────────────────────────────────────

public sealed class DnsConfig
{
	[JsonPropertyName("servers")] public List<object> Servers { get; set; } = new();
	[JsonPropertyName("rules")] public List<DnsRule> Rules { get; set; } = new();
	[JsonPropertyName("final")] public string Final { get; set; } = "remote-dns";
	[JsonPropertyName("strategy")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Strategy { get; set; }
	[JsonPropertyName("independent_cache")] public bool IndependentCache { get; set; } = true;
}

public sealed class DnsServerHttps
{
	[JsonPropertyName("type")] public string Type { get; set; } = "https";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "";
	[JsonPropertyName("server")] public string Server { get; set; } = "";
	[JsonPropertyName("server_port")] public int ServerPort { get; set; } = 443;
	[JsonPropertyName("path")] public string Path { get; set; } = "/dns-query";
	[JsonPropertyName("detour")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Detour { get; set; }
	[JsonPropertyName("domain_strategy")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DomainStrategy { get; set; }
}

public sealed class DnsServerLocal
{
	[JsonPropertyName("type")] public string Type { get; set; } = "local";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "";
	// sing-box 1.12+ migration: replaces deprecated "outbound: any" DNS rule
	[JsonPropertyName("domain_resolver")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DomainResolver { get; set; }
}

public sealed class DnsRule
{
	[JsonPropertyName("outbound")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? Outbound { get; set; }

	[JsonPropertyName("server")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Server { get; set; }

	[JsonPropertyName("action")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Action { get; set; }
}

// ── Inbounds ──────────────────────────────────────────────────────────────────

public sealed class TunInbound
{
	[JsonPropertyName("type")] public string Type { get; set; } = "tun";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "tun-in";
	[JsonPropertyName("interface_name")] public string InterfaceName { get; set; } = "void-tun";
	[JsonPropertyName("address")] public List<string> Address { get; set; } = new();
	[JsonPropertyName("mtu")] public int Mtu { get; set; } = 9000;
	[JsonPropertyName("auto_route")] public bool AutoRoute { get; set; } = true;
	[JsonPropertyName("strict_route")] public bool StrictRoute { get; set; } = true;
	[JsonPropertyName("stack")] public string Stack { get; set; } = "mixed";
	// sniff / gso / domain_strategy removed in 1.13 → route rule actions
}

public sealed class MixedInbound
{
	[JsonPropertyName("type")] public string Type { get; set; } = "mixed";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "";
	[JsonPropertyName("listen")] public string Listen { get; set; } = "127.0.0.1";
	[JsonPropertyName("listen_port")] public int ListenPort { get; set; }
}

// ── Outbounds ─────────────────────────────────────────────────────────────────
// "dns" outbound type was REMOVED in sing-box 1.13.
// Use route rule { action: "hijack-dns" } instead.

public sealed class VlessOutbound
{
	[JsonPropertyName("type")] public string Type { get; set; } = "vless";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "proxy";
	[JsonPropertyName("server")] public string Server { get; set; } = "";
	[JsonPropertyName("server_port")] public int ServerPort { get; set; }
	[JsonPropertyName("uuid")] public string Uuid { get; set; } = "";
	[JsonPropertyName("flow")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Flow { get; set; }
	[JsonPropertyName("packet_encoding")] public string PacketEncoding { get; set; } = "xudp";
	[JsonPropertyName("tls")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public TlsConfig? Tls { get; set; }
	[JsonPropertyName("transport")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public TransportConfig? Transport { get; set; }
}

public sealed class ShadowsocksOutbound
{
	[JsonPropertyName("type")] public string Type { get; set; } = "shadowsocks";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "proxy";
	[JsonPropertyName("server")] public string Server { get; set; } = "";
	[JsonPropertyName("server_port")] public int ServerPort { get; set; }
	[JsonPropertyName("method")] public string Method { get; set; } = "aes-256-gcm";
	[JsonPropertyName("password")] public string Password { get; set; } = "";
}

public sealed class DirectOutbound
{
	[JsonPropertyName("type")] public string Type { get; set; } = "direct";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "direct";
}

public sealed class BlockOutbound
{
	[JsonPropertyName("type")] public string Type { get; set; } = "block";
	[JsonPropertyName("tag")] public string Tag { get; set; } = "block";
}

// ── TLS / Reality ─────────────────────────────────────────────────────────────

public sealed class TlsConfig
{
	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("server_name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ServerName { get; set; }
	[JsonPropertyName("utls")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public UtlsConfig? Utls { get; set; }
	[JsonPropertyName("reality")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RealityConfig? Reality { get; set; }
}

public sealed class UtlsConfig
{
	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "chrome";
}

public sealed class RealityConfig
{
	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("public_key")] public string PublicKey { get; set; } = "";
	[JsonPropertyName("short_id")] public string ShortId { get; set; } = "";
}

public sealed class TransportConfig
{
	[JsonPropertyName("type")] public string Type { get; set; } = "";
	[JsonPropertyName("service_name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? ServiceName { get; set; }
	[JsonPropertyName("path")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Path { get; set; }
	[JsonPropertyName("idle_timeout")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? IdleTimeout { get; set; }
}

// ── Route ─────────────────────────────────────────────────────────────────────

public sealed class RouteConfig
{
	[JsonPropertyName("rules")] public List<RouteRule> Rules { get; set; } = new();
	[JsonPropertyName("final")] public string Final { get; set; } = "proxy";
	[JsonPropertyName("auto_detect_interface")] public bool AutoDetectInterface { get; set; } = true;
	// sing-box 1.12+ migration: global bootstrap resolver replaces deprecated
	// "outbound: any" DNS rule. All outbounds use local-dns to resolve server hostnames.
	[JsonPropertyName("default_domain_resolver")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? DefaultDomainResolver { get; set; }
}

/// <summary>
/// sing-box 1.13 route rule.
/// Action-only:  set Action ("sniff" | "hijack-dns"), Outbound = null.
/// Route-only:   set Outbound, Action = null.
/// </summary>
public sealed class RouteRule
{
	[JsonPropertyName("protocol")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? Protocol { get; set; }

	[JsonPropertyName("inbound")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? Inbound { get; set; }

	[JsonPropertyName("ip_cidr")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? IpCidr { get; set; }

	[JsonPropertyName("process_name")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? ProcessName { get; set; }

	[JsonPropertyName("action")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Action { get; set; }

	[JsonPropertyName("outbound")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Outbound { get; set; }
}

// ── Experimental ──────────────────────────────────────────────────────────────

public sealed class ExperimentalConfig
{
	[JsonPropertyName("cache_file")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CacheFileConfig? CacheFile { get; set; }
}

public sealed class CacheFileConfig
{
	[JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
	[JsonPropertyName("path")] public string Path { get; set; } = "cache.db";
}