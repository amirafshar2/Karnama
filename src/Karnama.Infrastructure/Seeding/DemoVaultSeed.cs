using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Domain.Clients;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>Demo vault: the café (FullAccess) has a sample login; the clothing shop gave access through Business Suite.</summary>
internal static class DemoVaultSeed
{
    public static async Task SeedAsync(AppDbContext db, IVault vault, CancellationToken ct)
    {
        if (!vault.IsConfigured || await db.ClientCredentials.AnyAsync(ct)) return;
        var instagram = await db.Platforms.Where(p => p.Key == "instagram").Select(p => p.Id).FirstOrDefaultAsync(ct);
        if (instagram == Guid.Empty) return;
        var clients = await db.Clients.AsNoTracking().ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        if (clients.FirstOrDefault(c => c.BusinessName == "Café Beispiel") is { } cafe)
        {
            var cred = ClientCredential.Create(cafe.Id, instagram, cafe.Accounts.First(a => a.PlatformId == instagram).Handle, false, CredentialSource.Client, null, now).Value!;
            // Clearly fake sample values (demo installs hold sample data only).
            var plain = JsonSerializer.SerializeToUtf8Bytes(new { password = "Nemoone-1404!", recoveryEmail = "cafe@example.com", recoveryPhone = "015100000004",
                twoFactorNote = "Der Zwei-Faktor-Code geht an das Handy von Frau Reiter." }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var sealedSecret = vault.Seal(plain, cred.Id.ToString("N"));
            cred.SetSecret(sealedSecret.Cipher, sealedSecret.WrappedKey, sealedSecret.KeyVersion, CredentialSource.Client, null, now);
            db.ClientCredentials.Add(cred);
        }
        if (clients.FirstOrDefault(c => c.BusinessName == "Mode Beispiel") is { } shop)
            db.ClientCredentials.Add(ClientCredential.Create(shop.Id, instagram, shop.Accounts.First(a => a.PlatformId == instagram).Handle, true, CredentialSource.Staff, null, now).Value!);
        await db.SaveChangesAsync(ct);
    }
}
