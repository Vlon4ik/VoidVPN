using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VoidVPN.Core.Models;

namespace VoidVPN.Core.Services
{
    public sealed class ProfileRepository
    {
        static readonly JsonSerializerOptions s_opts=new(){WriteIndented=true};
        readonly string _path;
        readonly ILogger<ProfileRepository> _log;
        readonly SemaphoreSlim _lk=new(1,1);

        public ProfileRepository(ILogger<ProfileRepository> log) {
            _log=log;
            string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VoidVPN");
            Directory.CreateDirectory(dir);
            _path=Path.Combine(dir,"profiles.json");
        }

        public async Task<List<VpnProfile>> LoadAllAsync(CancellationToken ct=default) {
            await _lk.WaitAsync(ct);
            try { return await ReadAsync(ct); } finally { _lk.Release(); }
        }

        public async Task SaveAsync(VpnProfile p,CancellationToken ct=default) {
            await _lk.WaitAsync(ct);
            try {
                var all=await ReadAsync(ct);
                int i=all.FindIndex(x=>x.Id==p.Id);
                if(i>=0) all[i]=p; else all.Add(p);
                await WriteAsync(all,ct);
            } finally { _lk.Release(); }
        }

        public async Task DeleteAsync(Guid id,CancellationToken ct=default) {
            await _lk.WaitAsync(ct);
            try {
                var all=await ReadAsync(ct);
                all.RemoveAll(x=>x.Id==id);
                await WriteAsync(all,ct);
            } finally { _lk.Release(); }
        }

        async Task<List<VpnProfile>> ReadAsync(CancellationToken ct) {
            if(!File.Exists(_path)) return new();
            try {
                var json=await File.ReadAllTextAsync(_path,ct);
                return JsonSerializer.Deserialize<List<VpnProfile>>(json,s_opts)??new();
            } catch(Exception ex){_log.LogWarning(ex,"Read profiles failed");return new();}
        }

        async Task WriteAsync(List<VpnProfile> all,CancellationToken ct)
            =>await File.WriteAllTextAsync(_path,JsonSerializer.Serialize(all,s_opts),ct);
    }
}
