using System.Security.Cryptography;
using Karnama.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Karnama.Infrastructure.Security;

/// <summary>Data Protection (keys in the database, rotated) with an expiry: tamper-proof and short-lived.</summary>
internal sealed class LinkSigner(IDataProtectionProvider provider) : ILinkSigner
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("Karnama.Links.v1").ToTimeLimitedDataProtector();

    public string Sign(string payload, TimeSpan lifetime) => _protector.Protect(payload, lifetime);

    public string? Verify(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2000) return null;
        try { return _protector.Unprotect(token, out _); }
        catch (CryptographicException) { return null; }
    }
}
