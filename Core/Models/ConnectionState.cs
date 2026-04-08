using System;

namespace VoidVPN.Core.Models
{
    public enum VpnStatus { Disconnected, Connecting, Connected, Disconnecting, Error }

    public sealed class ConnectionState
    {
        public static readonly ConnectionState Off = new(VpnStatus.Disconnected, null, null);

        public VpnStatus   Status        { get; }
        public VpnProfile? ActiveProfile { get; }
        public string?     ErrorMessage  { get; }

        public ConnectionState(VpnStatus s, VpnProfile? p, string? err)
        { Status = s; ActiveProfile = p; ErrorMessage = err; }

        public static ConnectionState Connecting(VpnProfile p)      => new(VpnStatus.Connecting,    p, null);
        public static ConnectionState Connected(VpnProfile p)        => new(VpnStatus.Connected,     p, null);
        public static ConnectionState Stopping(VpnProfile p)         => new(VpnStatus.Disconnecting, p, null);
        public static ConnectionState Fail(VpnProfile? p, string e)  => new(VpnStatus.Error,         p, e);

        public bool   IsActive   => Status is VpnStatus.Connected or VpnStatus.Connecting or VpnStatus.Disconnecting;
        public string Label      => Status switch {
            VpnStatus.Connecting    => "connecting...",
            VpnStatus.Connected     => "connected",
            VpnStatus.Disconnecting => "disconnecting...",
            VpnStatus.Error         => "error",
            _                       => "disconnected"
        };
        public string LabelUpper => Label.ToUpper();
        public string Detail     => ActiveProfile is null ? "" :
            $"{ActiveProfile.ServerAddress}:{ActiveProfile.ServerPort}  ·  :{ActiveProfile.LocalSocksPort}";
    }
}
