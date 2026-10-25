using System.Text;
using FluentAssertions;
using Karnama.Application.Common;
using Karnama.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Karnama.UnitTests;

public class TotpTests
{
    // RFC 6238 Appendix B, SHA-1 secret "12345678901234567890" (last 6 of the 8-digit values).
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Matches_rfc_6238_vectors(long unixTime, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");
        TotpService.Compute(key, unixTime / 30).Should().Be(expected);
    }

    [Fact]
    public void Verify_accepts_adjacent_step_and_returns_step()
    {
        var svc = new TotpService();
        var secret = svc.GenerateSecret();
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = now.ToUnixTimeSeconds() / 30;
        var previous = TotpService.Compute(TotpService.Base32Decode(secret), step - 1);

        svc.Verify(secret, previous, now).Should().Be(step - 1);
        svc.Verify(secret, "12345a", now).Should().BeNull();
    }

    [Fact]
    public void Base32_round_trips()
    {
        var bytes = Enumerable.Range(0, 20).Select(i => (byte)(i * 13)).ToArray();
        TotpService.Base32Decode(TotpService.Base32Encode(bytes)).Should().Equal(bytes);
    }
}

public class OtpCodeServiceTests
{
    private readonly OtpCodeService _svc = new(Options.Create(new OtpOptions { HashKey = new string('k', 40) }));

    [Fact]
    public void Codes_are_six_digits()
    {
        for (var i = 0; i < 200; i++) _svc.GenerateCode().Should().MatchRegex("^[0-9]{6}$");
    }

    [Fact]
    public void Hash_is_bound_to_challenge_and_phone()
    {
        var id = Guid.NewGuid();
        var h = _svc.Hash(id, "09121234567", "123456");
        _svc.Matches(h, _svc.Hash(id, "09121234567", "123456")).Should().BeTrue();
        _svc.Matches(h, _svc.Hash(Guid.NewGuid(), "09121234567", "123456")).Should().BeFalse();
        _svc.Matches(h, _svc.Hash(id, "09351234567", "123456")).Should().BeFalse();
        h.Should().NotContain("123456");
    }
}

public class MediaSniffTests
{
    [Fact]
    public void Recognises_png_jpeg_webp_and_rejects_svg()
    {
        Karnama.Infrastructure.Storage.LocalMediaStore.Sniff(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }).Should().Be("png");
        Karnama.Infrastructure.Storage.LocalMediaStore.Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }).Should().Be("jpg");
        Karnama.Infrastructure.Storage.LocalMediaStore.Sniff("RIFF\0\0\0\0WEBPVP8 "u8).Should().Be("webp");
        Karnama.Infrastructure.Storage.LocalMediaStore.Sniff("<svg xmlns="u8).Should().BeNull();
    }
}
