using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoidVPN.Core.Exceptions;

namespace VoidVPN.Core.Services
{
    public sealed class NetworkService
    {
        readonly ILogger<NetworkService> _log;
        public NetworkService(ILogger<NetworkService> log)=>_log=log;

        public Task<string?> GetGatewayAsync(CancellationToken ct=default)=>Task.Run(()=>{
            foreach(var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if(ni.OperationalStatus!=OperationalStatus.Up) continue;
                if(ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach(var gw in ni.GetIPProperties().GatewayAddresses) {
                    if(gw.Address.AddressFamily==AddressFamily.InterNetwork && gw.Address.ToString()!="0.0.0.0")
                        return gw.Address.ToString();
                }
            }
            return (string?)null;
        },ct);

        public async Task AddBypassAsync(string ip,string gw,CancellationToken ct=default) {
            await RunRoute($"ADD {ip} MASK 255.255.255.255 {gw} METRIC 5",ct);
            _log.LogInformation("Bypass route: {IP}→{GW}",ip,gw);
        }

        public async Task RemoveBypassAsync(string ip,CancellationToken ct=default) {
            try { await RunRoute($"DELETE {ip}",ct); } catch { }
        }

        async Task RunRoute(string args,CancellationToken ct) {
            var(_,err,code)=await RunAsync("route",args,ct);
            if(code!=0) throw new RouteException(args,code,err);
        }

        static async Task<(string,string,int)> RunAsync(string file,string args,CancellationToken ct) {
            using var p=new Process { StartInfo=new ProcessStartInfo {
                FileName=file,Arguments=args,UseShellExecute=false,
                RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true
            }};
            p.Start();
            var o=p.StandardOutput.ReadToEndAsync(ct);
            var e=p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return(await o,await e,p.ExitCode);
        }
    }
}
