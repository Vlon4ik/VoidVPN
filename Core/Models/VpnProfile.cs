using System;
using System.Collections.Generic;

namespace VoidVPN.Core.Models
{
    public enum ProxyProtocol    { Vless, Shadowsocks }
    public enum VlessTransport   { Tcp, Grpc, WebSocket }

    public sealed class VpnProfile
    {
        public Guid   Id       { get; set; } = Guid.NewGuid();
        public string Name     { get; set; } = string.Empty;

        // Protocol
        public ProxyProtocol  Protocol  { get; set; } = ProxyProtocol.Vless;

        // Server
        public string ServerAddress { get; set; } = string.Empty;
        public int    ServerPort    { get; set; } = 443;

        // VLESS
        public string         Uuid        { get; set; } = string.Empty;
        public string         Flow        { get; set; } = "xtls-rprx-vision";
        public VlessTransport Transport   { get; set; } = VlessTransport.Tcp;
        public string         GrpcService { get; set; } = string.Empty;

        // Reality TLS
        public string RealitySni       { get; set; } = string.Empty;
        public string RealityPublicKey { get; set; } = string.Empty;
        public string RealityShortId   { get; set; } = string.Empty;
        public string Fingerprint      { get; set; } = "chrome";

        // Shadowsocks
        public string SsMethod   { get; set; } = "aes-256-gcm";
        public string SsPassword { get; set; } = string.Empty;

        // Local ports
        public int LocalSocksPort { get; set; } = 2080;
        public int LocalHttpPort  { get; set; } = 2081;

        // TUN
        public string TunAddress   { get; set; } = "172.19.0.1/30";
        public string TunAddressV6 { get; set; } = "fdfe:dcba:9876::1/126";
        public string DnsServer    { get; set; } = "8.8.8.8";

        // Bypass
        public List<string> BypassProcesses { get; set; } = new() { "sing-box.exe" };
        public List<string> BypassCidrs     { get; set; } = new()
            { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8" };

        // Display
        public string DisplayHost  => $"{ServerAddress}:{ServerPort}";
        public string DisplayProto => Protocol == ProxyProtocol.Shadowsocks ? "ss" :
            Transport switch {
                VlessTransport.Grpc      => "vless/grpc",
                VlessTransport.WebSocket => "vless/ws",
                _                        => "vless/reality"
            };
    }
}
