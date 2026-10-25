using Karnama.Domain.Common;

namespace Karnama.Domain.Attendance;

/// <summary>One working day of the week (0 = Saturday … 6 = Friday, the Iranian week).</summary>
public sealed record ScheduleDay(int Day, TimeOnly Start, TimeOnly End);

/// <summary>
/// When someone is expected at work. One agency default (UserId null) plus optional per-person schedules.
/// Times are Iran local time (Asia/Tehran); "flexible" means only a weekly hour cap counts, not start times.
/// </summary>
public sealed class WorkSchedule : Entity
{
    private WorkSchedule() { Days = []; }

    public Guid? UserId { get; private set; }
    public List<ScheduleDay> Days { get; private set; }
    public int BreakMinutes { get; private set; }
    /// <summary>Minutes after the start time before an arrival counts as late (e.g. 10).</summary>
    public int GraceMinutes { get; private set; }
    public bool Flexible { get; private set; }
    public int WeeklyHours { get; private set; }
    public uint Version { get; private set; }

    public static Result<WorkSchedule> Create(Guid? userId, IReadOnlyList<ScheduleDay> days, int breakMinutes, int graceMinutes, bool flexible, int weeklyHours)
    {
        var s = new WorkSchedule { UserId = userId };
        var r = s.Update(days, breakMinutes, graceMinutes, flexible, weeklyHours);
        return r.IsSuccess ? s : r.Error!;
    }

    public Result Update(IReadOnlyList<ScheduleDay> days, int breakMinutes, int graceMinutes, bool flexible, int weeklyHours)
    {
        if (days.Any(d => d.Day is < 0 or > 6) || days.Select(d => d.Day).Distinct().Count() != days.Count)
            return Error.Validation("schedule.days", "روزهای هفته معتبر نیستند.");
        if (days.Any(d => d.End <= d.Start)) return Error.Validation("schedule.hours", "ساعت پایان باید بعد از ساعت شروع باشد.");
        if (breakMinutes is < 0 or > 240) return Error.Validation("schedule.break", "استراحت باید بین ۰ تا ۲۴۰ دقیقه باشد.");
        if (graceMinutes is < 0 or > 120) return Error.Validation("schedule.grace", "مهلت تأخیر باید بین ۰ تا ۱۲۰ دقیقه باشد.");
        if (weeklyHours is < 0 or > 80) return Error.Validation("schedule.weekly", "سقف ساعت هفتگی معتبر نیست.");
        Days = days.OrderBy(d => d.Day).ToList();
        BreakMinutes = breakMinutes;
        GraceMinutes = graceMinutes;
        Flexible = flexible;
        WeeklyHours = weeklyHours;
        return Result.Success();
    }

    /// <summary>Iranian week index of a date: Saturday = 0.</summary>
    public static int DayIndex(DateOnly date) => ((int)date.DayOfWeek + 1) % 7;

    public ScheduleDay? For(DateOnly date) => Days.FirstOrDefault(d => d.Day == DayIndex(date));
}

/// <summary>Official holiday (from the panel; the Persian calendar has many moving holidays).</summary>
public sealed class Holiday : Entity
{
    private Holiday() { Name = string.Empty; }
    public Holiday(DateOnly date, string name) { Date = date; Name = name.Trim(); }
    public DateOnly Date { get; private set; }
    public string Name { get; private set; }
}
