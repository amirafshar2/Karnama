using System.Text.Json;
using Karnama.Application.Attendance;
using Karnama.Domain.Attendance;
using Karnama.Domain.Settings;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Default work week and clock-in rules (PROMPT2 §2.2), editable from the panel. Demo installs also get a sample office
/// place and accept localhost as the "office network" so clock-in shows green on a developer machine.
/// </summary>
internal static class AttendanceSeed
{
    public static async Task SeedAsync(AppDbContext db, bool demo, CancellationToken ct)
    {
        if (!await db.WorkSchedules.AnyAsync(s => s.UserId == null, ct))
        {
            var nine = new TimeOnly(9, 0);
            // Saturday–Wednesday 9–17, Thursday 9–13, Friday off.
            List<ScheduleDay> days = [.. Enumerable.Range(0, 5).Select(d => new ScheduleDay(d, nine, new TimeOnly(17, 0))), new ScheduleDay(5, nine, new TimeOnly(13, 0))];
            db.WorkSchedules.Add(WorkSchedule.Create(null, days, 60, 10, false, 44).Value!);
        }

        if (!await db.AttendancePolicies.AnyAsync(ct))
        {
            db.AttendancePolicies.Add(AttendancePolicy.Create("کارمند دفتر",
                demo ? "OfficeNetwork | RotatingQr | Geofence" : "OfficeNetwork & (RotatingQr | BiometricDevice)", [], [], true).Value!);
            db.AttendancePolicies.Add(AttendancePolicy.Create("فیلمبردار و عکاس بیرون", "Geofence & Selfie", [], [], false).Value!);
        }

        if (demo)
        {
            if (!await db.AttendancePlaces.AnyAsync(ct))
                db.AttendancePlaces.Add(AttendancePlace.Create("دفتر شیراز (نمونه)", 29.6100, 52.5300, 150).Value!);
            if (!await db.SiteSettings.AnyAsync(s => s.Key == AttendanceSettingsStore.Key, ct))
                db.SiteSettings.Add(new SiteSetting(AttendanceSettingsStore.Key,
                    JsonSerializer.Serialize(AttendanceSettingsDto.Default with { OfficeNetworks = ["127.0.0.1", "::1"] }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        }
        await db.SaveChangesAsync(ct);
    }
}
