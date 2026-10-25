using Karnama.Application.Abstractions;
using Karnama.Application.Settings;
using Karnama.Domain.Catalog;
using Karnama.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Onboarding;

public sealed record OnboardingStepDto(string Key, bool Done, string Href);
public sealed record OnboardingDto(IReadOnlyList<OnboardingStepDto> Steps, int Percent, OnboardingStepDto? Next);

/// <summary>Setup assistant (docs/SKILLS.md §4.6): every step's state is computed from real data, never ticked by hand.</summary>
public sealed class OnboardingService(IAppDbContext db, BrandService brand)
{
    public async Task<OnboardingDto> GetAsync(CancellationToken ct)
    {
        var b = await brand.GetAsync(ct);
        var ownerKey = SystemRoles.Owner;
        var steps = new List<OnboardingStepDto>
        {
            new("brand", b.Name is not null && b.LogoUrl is not null, "/settings/brand"),
            new("colors", b.Version > 0, "/settings/brand"),
            new("languages", await db.Languages.CountAsync(l => l.IsActive, ct) >= 1, "/settings/texts"),
            new("roles", await db.Roles.AnyAsync(r => r.SystemKey == null, ct), "/roles"),
            new("firstWorker", await (from u in db.Users
                                      where u.Kind == UserKind.Staff && u.IsActive
                                      where !db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.SystemKey == ownerKey))
                                      select u.Id).AnyAsync(ct), "/users"),
            new("serviceType", await db.ServiceTypes.AnyAsync(ct), "/services"),
            new("package", await db.Packages.AnyAsync(p => p.Status == PackageStatus.Active, ct), "/packages"),
            new("firstClient", await db.Clients.AnyAsync(ct), "/clients"),
            new("schedule", await db.AttendancePlaces.AnyAsync(ct), "/attendance/setup"), // office location + clock-in methods
        };
        var done = steps.Count(s => s.Done);
        return new OnboardingDto(steps, (int)Math.Round(done * 100.0 / steps.Count), steps.FirstOrDefault(s => !s.Done));
    }
}
