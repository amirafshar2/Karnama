using System.Security.Cryptography;
using System.Text;
using Karnama.Application.Abstractions;
using Karnama.Application.Common;
using Microsoft.Extensions.Options;

namespace Karnama.Infrastructure.Security;

internal sealed class OtpCodeService(IOptions<OtpOptions> options) : IOtpCodeService
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.HashKey);

    public string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    // Bound to the challenge id and phone, so a hash can't be replayed for another challenge.
    public string Hash(Guid challengeId, string phone, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{challengeId:N}|{phone}|{code}")));

    public bool Matches(string expectedHash, string actualHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHash), Encoding.ASCII.GetBytes(actualHash));
}
