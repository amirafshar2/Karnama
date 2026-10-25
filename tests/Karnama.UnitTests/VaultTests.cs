using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Karnama.Domain.Clients;
using Karnama.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Karnama.UnitTests;

public class VaultTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static Vault Make(Dictionary<string, string?> settings, string env = "Production") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new Env(env), NullLogger<Vault>.Instance);

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Seals_and_opens_only_for_the_same_record()
    {
        var vault = Make(new() { ["Vault:MasterKey"] = Key() });
        var plain = Encoding.UTF8.GetBytes("رمز-Secret!");
        var s = vault.Seal(plain, "record-a");
        Encoding.UTF8.GetString(s.Cipher).Should().NotContain("Secret");
        vault.Open(s, "record-a").Should().Equal(plain);
        vault.Open(s, "record-b").Should().BeNull("a ciphertext moved to another row must not open");
    }

    [Fact]
    public void Each_record_gets_its_own_data_key()
    {
        var vault = Make(new() { ["Vault:MasterKey"] = Key() });
        var a = vault.Seal([1, 2, 3], "x");
        var b = vault.Seal([1, 2, 3], "x");
        a.WrappedKey.Should().NotEqual(b.WrappedKey);
        a.Cipher.Should().NotEqual(b.Cipher);
    }

    [Fact]
    public void Tampered_data_or_wrong_master_key_never_opens()
    {
        var vault = Make(new() { ["Vault:MasterKey"] = Key() });
        var s = vault.Seal([9, 9, 9, 9], "r");
        var bad = (byte[])s.Cipher.Clone();
        bad[^1] ^= 1;
        vault.Open(s with { Cipher = bad }, "r").Should().BeNull();
        Make(new() { ["Vault:MasterKey"] = Key() }).Open(s, "r").Should().BeNull();
    }

    [Fact]
    public void Rotation_keeps_old_records_readable_and_seals_new_ones_with_the_new_key()
    {
        var oldKey = Key();
        var before = Make(new() { ["Vault:MasterKey"] = oldKey }).Seal([4, 2], "r");
        var rotated = Make(new() { ["Vault:MasterKey"] = Key(), ["Vault:KeyVersion"] = "2", ["Vault:PreviousKeys:1"] = oldKey });
        rotated.Open(before, "r").Should().Equal(new byte[] { 4, 2 });
        rotated.Seal([1], "r").KeyVersion.Should().Be(2);
    }

    [Fact]
    public void Production_without_a_key_is_not_configured_and_bad_keys_are_rejected()
    {
        Make([]).IsConfigured.Should().BeFalse();
        var act = () => Make(new() { ["Vault:MasterKey"] = Convert.ToBase64String(new byte[16]) });
        act.Should().Throw<InvalidOperationException>();
    }
}

public class PackageNoticeTests
{
    private static ClientPackage Package()
    {
        var p = (ClientPackage)Activator.CreateInstance(typeof(ClientPackage), nonPublic: true)!;
        typeof(ClientPackage).GetProperty(nameof(ClientPackage.Status))!.SetValue(p, ClientPackageStatus.Active);
        return p;
    }

    [Fact]
    public void Each_threshold_once_and_only_the_closest_after_missed_days()
    {
        var p = Package();
        int[] days = [7, 3, 1, 0];
        p.NeedsExpiryNotice(10, days, out _).Should().BeFalse();
        p.NeedsExpiryNotice(6, days, out var t).Should().BeTrue();
        t.Should().Be(7);
        p.NeedsExpiryNotice(6, days, out _).Should().BeFalse("same day, second run");
        p.NeedsExpiryNotice(1, days, out t).Should().BeTrue();
        t.Should().Be(1, "missed the 3-day run: only the closest reminder, no burst");
        p.NeedsExpiryNotice(0, days, out t).Should().BeTrue();
        t.Should().Be(0);
        p.NeedsExpiryNotice(0, days, out _).Should().BeFalse();
    }
}
