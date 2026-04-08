using System;
using System.Text;
using System.Web;
using VoidVPN.Core.Models;

namespace VoidVPN.Core.Services
{
    /// Parses vless:// and ss:// share links into VpnProfile.
    public static class KeyParser
    {
        public static bool TryParse(string raw, out VpnProfile? profile, out string error)
        {
            profile = null; error = string.Empty;
            raw = raw?.Trim() ?? "";
            try
            {
                if (raw.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                    return ParseVless(raw, out profile, out error);
                if (raw.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                    return ParseSs(raw, out profile, out error);
                error = "Unsupported protocol. Use vless:// or ss://";
                return false;
            }
            catch (Exception ex) { error = $"Parse error: {ex.Message}"; return false; }
        }

        static bool ParseVless(string raw, out VpnProfile? p, out string err)
        {
            p = null;
            string name = "";
            int fi = raw.IndexOf('#');
            if (fi >= 0) { name = Uri.UnescapeDataString(raw[(fi+1)..]); raw = raw[..fi]; }
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) { err="Invalid URI"; return false; }

            string uuid = uri.UserInfo;
            string host = uri.Host;
            int    port = uri.Port > 0 ? uri.Port : 443;
            var    q    = HttpUtility.ParseQueryString(uri.Query);

            string sni  = q["sni"]  ?? q["serverName"] ?? host;
            string fp   = q["fp"]   ?? "chrome";
            string pbk  = q["pbk"]  ?? "";
            string sid  = q["sid"]  ?? "";
            string type = q["type"] ?? "tcp";
            string flow = q["flow"] ?? "";
            string svc  = q["serviceName"] ?? "";

            if (string.IsNullOrWhiteSpace(uuid)) { err="Missing UUID"; return false; }
            if (string.IsNullOrWhiteSpace(host)) { err="Missing host"; return false; }

            var transport = type.ToLower() switch { "grpc"=>"Grpc", "ws"=>"WebSocket", _=>"Tcp" };
            var t = Enum.Parse<VlessTransport>(transport);
            if (t != VlessTransport.Tcp) flow = "";

            p = new VpnProfile {
                Name=string.IsNullOrWhiteSpace(name)?$"{host}:{port}":name,
                Protocol=ProxyProtocol.Vless, ServerAddress=host, ServerPort=port,
                Uuid=uuid, Flow=flow, Transport=t, GrpcService=svc,
                RealitySni=sni, RealityPublicKey=pbk, RealityShortId=sid,
                Fingerprint=string.IsNullOrWhiteSpace(fp)?"chrome":fp
            };
            err = ""; return true;
        }

        static bool ParseSs(string raw, out VpnProfile? p, out string err)
        {
            p = null;
            string name = "";
            int fi = raw.IndexOf('#');
            if (fi >= 0) { name = Uri.UnescapeDataString(raw[(fi+1)..]); raw = raw[..fi]; }

            string body = raw[5..];
            string method="", password="", host=""; int port=8388;

            int at = body.IndexOf('@');
            if (at >= 0)
            {
                string info = TryB64(body[..at]) ?? body[..at];
                int c = info.IndexOf(':');
                if (c < 0) { err="Bad Shadowsocks userinfo"; return false; }
                method=info[..c]; password=info[(c+1)..];
                string hp = body[(at+1)..];
                int lc = hp.LastIndexOf(':');
                if (lc < 0) { err="Missing port"; return false; }
                host=hp[..lc];
                if (!int.TryParse(hp[(lc+1)..], out port)) { err="Bad port"; return false; }
            }
            else
            {
                string dec = TryB64(body) ?? body;
                int lat = dec.LastIndexOf('@');
                if (lat < 0) { err="Cannot parse Shadowsocks URI"; return false; }
                string cred=dec[..lat], addr=dec[(lat+1)..];
                int c=cred.IndexOf(':'); if(c<0){err="Bad creds";return false;}
                method=cred[..c]; password=cred[(c+1)..];
                int lc=addr.LastIndexOf(':'); if(lc<0){err="Missing port";return false;}
                host=addr[..lc];
                if(!int.TryParse(addr[(lc+1)..],out port)){err="Bad port";return false;}
            }

            p = new VpnProfile {
                Name=string.IsNullOrWhiteSpace(name)?$"{host}:{port}":name,
                Protocol=ProxyProtocol.Shadowsocks, ServerAddress=host, ServerPort=port,
                SsMethod=method, SsPassword=password
            };
            err=""; return true;
        }

        static string? TryB64(string s)
        {
            try {
                string pad = s.Replace('-','+').Replace('_','/');
                int mod = pad.Length%4; if(mod>0) pad+=new string('=',4-mod);
                return Encoding.UTF8.GetString(Convert.FromBase64String(pad));
            } catch { return null; }
        }
    }
}
