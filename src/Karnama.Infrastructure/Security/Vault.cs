using System.Security.Cryptography;
using System.Text;
using Karnama.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Karnama.Infrastructure.Security;

/// <summary>
/// AES-256-GCM envelope encryption. Master keys come from configuration only (<c>Vault__MasterKey</c>, base64 of 32 bytes,
/// plus <c>Vault__KeyVersion</c>; older keys stay readable as <c>Vault__PreviousKeys__{version}</c> during rotation).
/// Development without a key uses a random key file next to the uploaded files (never the database, never the repo),
/// so a database dump alone never opens a password. Production refuses to start without a key (StartupGuard).
/// Layout of both blobs: nonce (12) | tag (16) | ciphertext.
/// </summary>
internal sealed class Vault : IVault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly Dictionary<int, byte[]> _keys = [];
    private readonly int _current;

    public Vault(IConfiguration config, IHostEnvironment env, ILogger<Vault> logger)
    {
        _current = config.GetValue("Vault:KeyVersion", 1);
        if (TryKey(config["Vault:MasterKey"]) is { } key) _keys[_current] = key;
        foreach (var old in config.GetSection("Vault:PreviousKeys").GetChildren())
            if (int.TryParse(old.Key, out var v) && v != _current && TryKey(old.Value) is { } k) _keys[v] = k;

        if (!_keys.ContainsKey(_current) && env.IsDevelopment())
        {
            _keys[_current] = DevKey(config, env);
            logger.LogWarning("Vault master key not set: using the local development key file. Set Vault__MasterKey in production.");
        }
    }

    public bool IsConfigured => _keys.ContainsKey(_current);

    public SealedSecret Seal(byte[] plain, string context)
    {
        if (!IsConfigured) throw new InvalidOperationException("Vault master key is not configured.");
        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var cipher = Encrypt(dek, plain, Aad("data", _current, context));
            var wrapped = Encrypt(_keys[_current], dek, Aad("key", _current, context));
            return new SealedSecret(cipher, wrapped, _current);
        }
        finally { CryptographicOperations.ZeroMemory(dek); }
    }

    public byte[]? Open(SealedSecret s, string context)
    {
        if (!_keys.TryGetValue(s.KeyVersion, out var kek)) return null;
        var dek = Decrypt(kek, s.WrappedKey, Aad("key", s.KeyVersion, context));
        if (dek is null) return null;
        try { return Decrypt(dek, s.Cipher, Aad("data", s.KeyVersion, context)); }
        finally { CryptographicOperations.ZeroMemory(dek); }
    }

    private static byte[] Aad(string part, int version, string context) => Encoding.UTF8.GetBytes($"karnama.vault.{part}|v{version}|{context}");

    private static byte[] Encrypt(byte[] key, byte[] plain, byte[] aad)
    {
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), aad);
        return output;
    }

    private static byte[]? Decrypt(byte[] key, byte[] blob, byte[] aad)
    {
        if (blob.Length < NonceSize + TagSize) return null;
        var plain = new byte[blob.Length - NonceSize - TagSize];
        try
        {
            using var gcm = new AesGcm(key, TagSize);
            gcm.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), plain, aad);
            return plain;
        }
        catch (AuthenticationTagMismatchException) { return null; }
    }

    private static byte[]? TryKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var key = Convert.FromBase64String(base64.Trim());
            return key.Length == 32 ? key : throw new InvalidOperationException("Vault master key must be 32 bytes (base64).");
        }
        catch (FormatException) { throw new InvalidOperationException("Vault master key is not valid base64."); }
    }

    private static byte[] DevKey(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Storage:LocalPath"];
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "..", "..", "storage") : configured);
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, ".vault-dev.key");
        if (File.Exists(file) && TryKey(File.ReadAllText(file)) is { } existing) return existing;
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
            stream.Write(Encoding.ASCII.GetBytes(Convert.ToBase64String(key)));
            return key;
        }
        catch (IOException) when (File.Exists(file))
        {
            // Another process (API or worker) created it first.
            Thread.Sleep(50);
            return TryKey(File.ReadAllText(file)) ?? throw new InvalidOperationException("Development vault key file is damaged.");
        }
    }
}
