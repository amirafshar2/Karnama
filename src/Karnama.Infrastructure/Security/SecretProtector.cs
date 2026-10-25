using Karnama.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Karnama.Infrastructure.Security;

internal sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Karnama.Secrets.v1");

    public string Protect(string plain) => _protector.Protect(plain);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
