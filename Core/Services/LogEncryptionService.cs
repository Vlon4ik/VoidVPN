using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VoidVPN.Core.Services
{
    /// <summary>
    /// Encrypts the sing-box log file using AES-256-GCM with a machine-bound key
    /// derived from Windows DPAPI (Data Protection API).
    ///
    /// Security model:
    ///   - The encryption key is generated once, then protected with DPAPI
    ///     (machine scope) so only this machine can decrypt it.
    ///   - Log entries are encrypted individually so partial reads are still possible.
    ///   - The plain-text log is wiped after encryption (overwrite + delete).
    ///   - Since the project is open-source, security comes from the OS-level
    ///     DPAPI binding — not from the algorithm being secret.
    ///
    /// Decryption is intentionally only possible on the same machine, preventing
    /// log exfiltration even if the files are copied elsewhere.
    /// </summary>
    public sealed class LogEncryptionService
    {
        const int KeyBytes  = 32;  // AES-256
        const int NonceBytes = 12; // GCM standard nonce
        const int TagBytes  = 16;  // GCM auth tag

        readonly string _keyFile;
        readonly ILogger<LogEncryptionService> _log;

        byte[]? _key;

        public LogEncryptionService(ILogger<LogEncryptionService> log)
        {
            _log = log;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoidVPN");
            Directory.CreateDirectory(dir);
            _keyFile = Path.Combine(dir, "log.key");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Initialize()
        {
            _key = LoadOrCreateKey();
            _log.LogInformation("Log encryption: AES-256-GCM key loaded (DPAPI-bound)");
        }

        /// <summary>
        /// Encrypts the plaintext log at <paramref name="plainPath"/> and writes
        /// an encrypted version to <paramref name="encPath"/>.
        /// The original file is securely wiped.
        /// </summary>
        public async Task EncryptLogAsync(string plainPath, string encPath)
        {
            if (_key == null) return;
            if (!File.Exists(plainPath)) return;

            try
            {
                var lines = await File.ReadAllLinesAsync(plainPath);
                using var outStream = new FileStream(encPath, FileMode.Create,
                    FileAccess.Write, FileShare.None);

                foreach (var line in lines)
                {
                    byte[] cipherLine = EncryptLine(line);
                    // 4-byte length prefix + ciphertext
                    byte[] lenBytes = BitConverter.GetBytes(cipherLine.Length);
                    await outStream.WriteAsync(lenBytes);
                    await outStream.WriteAsync(cipherLine);
                }

                await outStream.FlushAsync();
                SecureDelete(plainPath);
                _log.LogInformation("Log encrypted: {Path}", encPath);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Log encryption failed (non-fatal)");
            }
        }

        /// <summary>
        /// Decrypts an encrypted log file back to readable lines.
        /// Only works on the same machine (DPAPI).
        /// </summary>
        public async Task<string[]> DecryptLogAsync(string encPath)
        {
            if (_key == null) return [];
            if (!File.Exists(encPath)) return [];

            try
            {
                using var inStream = new FileStream(encPath, FileMode.Open,
                    FileAccess.Read, FileShare.Read);

                var lines = new System.Collections.Generic.List<string>();
                byte[] lenBuf = new byte[4];

                while (true)
                {
                    int read = await inStream.ReadAsync(lenBuf.AsMemory(0, 4));
                    if (read < 4) break;

                    int len = BitConverter.ToInt32(lenBuf, 0);
                    if (len <= 0 || len > 1_000_000) break; // sanity guard

                    byte[] cipher = new byte[len];
                    await inStream.ReadExactlyAsync(cipher);
                    lines.Add(DecryptLine(cipher));
                }

                return [.. lines];
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Log decryption failed");
                return [];
            }
        }

        // ── Crypto ────────────────────────────────────────────────────────────

        byte[] EncryptLine(string plaintext)
        {
            byte[] plain = Encoding.UTF8.GetBytes(plaintext);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            byte[] cipher = new byte[plain.Length];
            byte[] tag    = new byte[TagBytes];

            using var aes = new AesGcm(_key!, TagBytes);
            aes.Encrypt(nonce, plain, cipher, tag);

            // Layout: nonce(12) + tag(16) + ciphertext(N)
            var result = new byte[NonceBytes + TagBytes + cipher.Length];
            nonce.CopyTo(result, 0);
            tag.CopyTo(result, NonceBytes);
            cipher.CopyTo(result, NonceBytes + TagBytes);
            return result;
        }

        string DecryptLine(byte[] data)
        {
            if (data.Length < NonceBytes + TagBytes)
                return "[corrupt]";

            byte[] nonce  = data[..NonceBytes];
            byte[] tag    = data[NonceBytes..(NonceBytes + TagBytes)];
            byte[] cipher = data[(NonceBytes + TagBytes)..];
            byte[] plain  = new byte[cipher.Length];

            using var aes = new AesGcm(_key!, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }

        // ── Key management (DPAPI-bound) ──────────────────────────────────────

        byte[] LoadOrCreateKey()
        {
            try
            {
                if (File.Exists(_keyFile))
                {
                    byte[] protectedKey = File.ReadAllBytes(_keyFile);
                    return System.Security.Cryptography.ProtectedData.Unprotect(
                        protectedKey, null,
                        System.Security.Cryptography.DataProtectionScope.LocalMachine);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not load log key, generating new one");
            }

            // Generate a new random 256-bit key
            byte[] key = RandomNumberGenerator.GetBytes(KeyBytes);
            try
            {
                byte[] protectedKey = System.Security.Cryptography.ProtectedData.Protect(
                    key, null,
                    System.Security.Cryptography.DataProtectionScope.LocalMachine);
                File.WriteAllBytes(_keyFile, protectedKey);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not persist log key — log encryption will be in-memory only");
            }

            return key;
        }

        static void SecureDelete(string path)
        {
            try
            {
                // Overwrite with zeros before deleting to prevent recovery
                long length = new FileInfo(path).Length;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
                byte[] zeros = new byte[Math.Min(length, 4096)];
                long written = 0;
                while (written < length)
                {
                    int chunk = (int)Math.Min(zeros.Length, length - written);
                    fs.Write(zeros, 0, chunk);
                    written += chunk;
                }
            }
            catch { /* best-effort wipe */ }

            try { File.Delete(path); }
            catch { /* best-effort delete */ }
        }
    }
}
