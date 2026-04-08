using System;
namespace VoidVPN.Core.Exceptions
{
    public class VpnException : Exception { public VpnException(string m):base(m){} public VpnException(string m,Exception e):base(m,e){} }
    public sealed class SingBoxNotFoundException : VpnException { public SingBoxNotFoundException(string p):base($"sing-box not found: {p}"){} }
    public sealed class SingBoxStartException    : VpnException { public SingBoxStartException(string m):base(m){} }
    public sealed class RouteException           : VpnException { public RouteException(string c,int x,string o):base($"Route failed (exit {x}): {c}\n{o}"){} }
    public sealed class ConfigException          : VpnException { public ConfigException(string m):base(m){} }
}
