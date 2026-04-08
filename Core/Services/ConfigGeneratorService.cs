using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoidVPN.Core.Exceptions;
using VoidVPN.Core.Models;

namespace VoidVPN.Core.Services;

/// <summary>
/// Generates valid sing-box 1.13.4 JSON configuration.
///
/// CRITICAL CHANGES from legacy versions:
///   1. "dns" outbound type REMOVED from sing-box 1.13.
///      Outbounds list: proxy + direct + block (NO dns outbound).
///   2. DNS interception uses route rule { action: "hijack-dns" }.
///   3. Traffic sniffing uses route rule { action: "sniff" }.
///   4. Inbound fields "sniff", "gso", "domain_strategy" removed.
/// </summary>
public sealed class ConfigGeneratorService(ILogger<ConfigGeneratorService> logger)
{
	static readonly JsonSerializerOptions s_opts = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	// ── Public API ────────────────────────────────────────────────────────────

	public SingBoxConfig Generate(VpnProfile p)
	{
		Validate(p);
		logger.LogInformation("Generating config for [{Name}] protocol={Proto}", p.Name, p.DisplayProto);

		return new SingBoxConfig
		{
			Log = new LogConfig { Level = "info", Output = "sing-box.log", Timestamp = true },
			Ntp = new NtpConfig(),
			Dns = BuildDns(p),
			Inbounds = BuildInbounds(p),
			Outbounds = BuildOutbounds(p),
			Route = BuildRoute(p),
			Experimental = new ExperimentalConfig { CacheFile = new CacheFileConfig() }
		};
	}

	public async Task WriteAsync(VpnProfile p, string path, CancellationToken ct = default)
	{
		var cfg = Generate(p);
		var json = JsonSerializer.Serialize(cfg, s_opts);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, json, ct);
		logger.LogInformation("Config written → {Path}", path);
	}

	// ── DNS ───────────────────────────────────────────────────────────────────

	static DnsConfig BuildDns(VpnProfile p) => new()
	{
		Servers = new List<object>
		{
            // Remote DoH — tunnelled through proxy outbound
            new DnsServerHttps
			{
				Tag            = "remote-dns",
				Server         = p.DnsServer,
				ServerPort     = 443,
				Path           = "/dns-query",
				Detour         = "proxy",
				DomainStrategy = "prefer_ipv4"
			},
            // Local — bootstrap resolver for outbound hostname resolution.
            // Referenced by route.default_domain_resolver below (sing-box 1.12+ migration).
            new DnsServerLocal { Tag = "local-dns" }
		},
		// outbound DNS rule ("outbound: any") REMOVED — deprecated in 1.12, FATAL in 1.14.
		// Bootstrap is now handled by route.default_domain_resolver = "local-dns".
		Rules = new List<DnsRule>(),
		Strategy = "prefer_ipv4",
		Final = "remote-dns",
		IndependentCache = true
	};

	// ── Inbounds ──────────────────────────────────────────────────────────────

	static List<object> BuildInbounds(VpnProfile p) => new()
	{
		new TunInbound
		{
			Tag           = "tun-in",
			InterfaceName = "void-tun",
			Address       = new List<string> { p.TunAddress, p.TunAddressV6 },
			Mtu           = 9000,
			AutoRoute     = true,
			StrictRoute   = true,
			Stack         = "mixed"
            // sniff / gso / domain_strategy removed in 1.13
        },
		new MixedInbound
		{
			Tag        = "mixed-in",
			Listen     = "127.0.0.1",
			ListenPort = p.LocalSocksPort
            // sniff removed in 1.13
        }
	};

	// ── Outbounds ─────────────────────────────────────────────────────────────
	// sing-box 1.13: only proxy + direct + block
	// "dns" outbound type is GONE — DNS is handled via route action "hijack-dns"

	List<object> BuildOutbounds(VpnProfile p)
	{
		object proxy = p.Protocol == ProxyProtocol.Shadowsocks ? BuildSs(p) : BuildVless(p);
		return new List<object>
		{
			proxy,
			new DirectOutbound(),
			new BlockOutbound()
            // ← NO DnsOutbound: removed in sing-box 1.13
        };
	}

	static VlessOutbound BuildVless(VpnProfile p)
	{
		var ob = new VlessOutbound
		{
			Tag = "proxy",
			Server = p.ServerAddress,
			ServerPort = p.ServerPort,
			Uuid = p.Uuid,
			Flow = p.Transport == VlessTransport.Tcp && !string.IsNullOrWhiteSpace(p.Flow)
							 ? p.Flow : null,
			PacketEncoding = "xudp",
			Tls = new TlsConfig
			{
				Enabled = true,
				ServerName = p.RealitySni,
				Utls = new UtlsConfig { Enabled = true, Fingerprint = p.Fingerprint },
				Reality = new RealityConfig
				{
					Enabled = true,
					PublicKey = p.RealityPublicKey,
					ShortId = p.RealityShortId
				}
			}
		};

		ob.Transport = p.Transport switch
		{
			VlessTransport.Grpc => new TransportConfig
			{
				Type = "grpc",
				ServiceName = p.GrpcService,
				IdleTimeout = "15s"
			},
			VlessTransport.WebSocket => new TransportConfig { Type = "ws", Path = "/" },
			_ => null
		};

		return ob;
	}

	static ShadowsocksOutbound BuildSs(VpnProfile p) => new()
	{
		Tag = "proxy",
		Server = p.ServerAddress,
		ServerPort = p.ServerPort,
		Method = p.SsMethod,
		Password = p.SsPassword
	};

	// ── Route ─────────────────────────────────────────────────────────────────
	// sing-box 1.13 action-based rules replace legacy field-level config.

	static RouteConfig BuildRoute(VpnProfile p) => new()
	{
		Rules = new List<RouteRule>
		{
            // Rule 1: sniff — classify protocol/domain for all inbound traffic
            // (replaces inbound-level sniff=true removed in 1.13)
            new() { Action = "sniff" },

            // Rule 2: hijack-dns — intercept DNS queries and resolve via DNS config
            // THIS replaces the old "dns" outbound type which was removed in 1.13.
            // protocol:dns matches all DNS traffic; action:hijack-dns handles it.
            new() { Protocol = new List<string> { "dns" }, Action = "hijack-dns" },

            // Rule 3: bypass sing-box process (prevents routing loop)
            new() { ProcessName = new List<string>(p.BypassProcesses), Outbound = "direct" },

            // Rule 4: bypass private/LAN CIDRs
            new() { IpCidr = new List<string>(p.BypassCidrs), Outbound = "direct" },

            // Rule 5: explicit proxy via SOCKS/HTTP inbound
            new() { Inbound = new List<string> { "mixed-in" }, Outbound = "proxy" }
		},
		Final = "proxy",
		AutoDetectInterface = true,
		// sing-box 1.12+ migration: replaces deprecated "outbound: any" DNS rule.
		// All outbounds resolve server hostnames via local-dns (bootstrap).
		DefaultDomainResolver = "local-dns"
	};

	// ── Validation ────────────────────────────────────────────────────────────

	static void Validate(VpnProfile p)
	{
		var errs = new List<string>();
		if (string.IsNullOrWhiteSpace(p.ServerAddress)) errs.Add("ServerAddress required");
		if (p.ServerPort is < 1 or > 65535) errs.Add($"Invalid port: {p.ServerPort}");

		if (p.Protocol == ProxyProtocol.Vless)
		{
			if (string.IsNullOrWhiteSpace(p.Uuid)) errs.Add("UUID required");
			if (string.IsNullOrWhiteSpace(p.RealityPublicKey)) errs.Add("Reality PublicKey required");
			if (string.IsNullOrWhiteSpace(p.RealitySni)) errs.Add("Reality SNI required");
		}
		else
		{
			if (string.IsNullOrWhiteSpace(p.SsPassword)) errs.Add("Shadowsocks password required");
		}

		if (errs.Count > 0)
			throw new ConfigException("Config validation:\n" + string.Join("\n", errs));
	}
}