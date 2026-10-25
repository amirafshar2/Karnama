using System.Globalization;
using System.Text.Json;
using Karnama.Application.Abstractions;
using Karnama.Application.Attendance;
using Karnama.Domain.Attendance;
using Karnama.Domain.Common;
using Karnama.Domain.Identity;
using Karnama.Domain.Settings;
using Karnama.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Application.Reports;

public sealed record ServiceCountDto(Guid ServiceTypeId, string Name, int Count);
public sealed record WorkRowDto(Guid UserId, string Name, int PresenceMinutes, int TimerMinutes, double? UsefulPercent, int OvertimeMinutes, int LateMinutes, int LateCount,
    int UnexcusedAbsences, int ExcusedAbsences, int LeaveDays, int DoneCount, IReadOnlyList<ServiceCountDto> DoneByService, int EstimatedMinutes, int ActualMinutes,
    double? OnTimePercent, double? FirstTimePercent, int Revisions, int BotFiles, int? Score);
public sealed record DayPointDto(DateOnly Day, int PresenceMinutes, int TimerMinutes, int Done);
public sealed record WorkReportDto(DateOnly From, DateOnly To, IReadOnlyList<WorkRowDto> Rows, IReadOnlyList<DayPointDto> Days);

public sealed record ShiftLineDto(DateOnly Day, DateTimeOffset In, DateTimeOffset? Out, int Worked, int Late);
public sealed record TimerLineDto(DateTimeOffset Start, DateTimeOffset? Stop, int Minutes, string Title, long OrderNumber);
public sealed record DoneLineDto(Guid Id, string Title, long OrderNumber, string Service, int Estimated, int Actual, DateTimeOffset? Deadline, DateTimeOffset? Delivered,
    bool? OnTime, int Revisions, int? ManagerStars, int? ClientStars);
public sealed record WorkDetailsDto(IReadOnlyList<ShiftLineDto> Shifts, IReadOnlyList<TimerLineDto> Timers, IReadOnlyList<DoneLineDto> Done);

public sealed record ScoreWeights(int Quality = 25, int Speed = 15, int OnTime = 25, int Volume = 15, int Discipline = 10, int Manager = 5, int Client = 5);
public sealed record ScoreSettingsDto(ScoreWeights Weights, int MinSamples, bool RankingForAll, Dictionary<Guid, double>? ServiceWeights)
{
    public static ScoreSettingsDto Default => new(new ScoreWeights(), 5, false, []);
}
public sealed record IndicatorDto(string Key, int? Score, string Value, int Weight, string Hint);
public sealed record ScoreDto(Guid UserId, string Name, int? Total, bool Enough, int Samples, IReadOnlyList<IndicatorDto> Indicators, IReadOnlyList<string> Badges);
public sealed record ScoresDto(DateOnly From, DateOnly To, int MinSamples, IReadOnlyList<ScoreDto> People, bool RankingVisible);
public sealed record TrendPointDto(string Label, DateOnly From, int? Total);

/// <summary>
/// Work report for a period (PROMPT2 §9) and worker scores (§11). Everything is computed from events (shifts, timers,
/// work status changes, ratings) — never stored or edited by hand. Staff without reports.view:All see only themselves.
/// </summary>
public sealed class PerformanceService(IAppDbContext db, ICurrentUser me, TimeProvider clock, IAuditWriter audit, IExcelExporter excel)
{
    public const string SettingsKey = "scores";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid Me => me.UserId ?? Guid.Empty;
    private bool SeesAllWork => me.Kind == UserKind.Staff && me.Has(Permissions.ReportsView, PermissionScope.All);
    private bool SeesOwnWork => me.Kind == UserKind.Staff && (me.Has(Permissions.ReportsView, PermissionScope.Own) || me.Has(Permissions.AttendanceSelf, PermissionScope.Own));
    private bool SeesAllScores => me.Kind == UserKind.Staff && me.Has(Permissions.ScoresView, PermissionScope.All);
    private bool SeesOwnScore => me.Kind == UserKind.Staff && me.Has(Permissions.ScoresView, PermissionScope.Own);

    // ================= settings =================

    public async Task<ScoreSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var json = await db.SiteSettings.AsNoTracking().Where(s => s.Key == SettingsKey).Select(s => s.Json).FirstOrDefaultAsync(ct);
        if (json is null) return ScoreSettingsDto.Default;
        try { return JsonSerializer.Deserialize<ScoreSettingsDto>(json, Json) ?? ScoreSettingsDto.Default; }
        catch (JsonException) { return ScoreSettingsDto.Default; }
    }

    public async Task<Result<ScoreSettingsDto>> SaveSettingsAsync(ScoreSettingsDto s, CancellationToken ct)
    {
        if (!me.Has(Permissions.ScoresConfigure, PermissionScope.All)) return Error.Forbidden();
        var w = s.Weights;
        int[] all = [w.Quality, w.Speed, w.OnTime, w.Volume, w.Discipline, w.Manager, w.Client];
        if (all.Any(x => x is < 0 or > 100) || all.Sum() == 0) return Error.Validation("scores.weights", "وزن هر شاخص بین ۰ تا ۱۰۰ و حداقل یکی بیشتر از صفر.");
        if (s.MinSamples is < 1 or > 50) return Error.Validation("scores.minSamples", "حداقل نمونه بین ۱ تا ۵۰.");
        if (s.ServiceWeights?.Values.Any(v => v is <= 0 or > 10) == true) return Error.Validation("scores.serviceWeight", "وزن هر خدمت بین ۰٫۱ تا ۱۰.");
        var json = JsonSerializer.Serialize(s, Json);
        var row = await db.SiteSettings.FirstOrDefaultAsync(x => x.Key == SettingsKey, ct);
        var before = row?.Json;
        if (row is null) db.SiteSettings.Add(new SiteSetting(SettingsKey, json));
        else row.Replace(json, me.UserId, clock.GetUtcNow());
        // Weight changes move everyone's score: always in the audit log, with the old values.
        audit.Add("scores.settingsChanged", nameof(SiteSetting), SettingsKey, new { before, after = json });
        await db.SaveChangesAsync(ct);
        return s;
    }

    // ================= work report =================

    public async Task<Result<WorkReportDto>> WorkAsync(DateOnly from, DateOnly to, Guid? userId, CancellationToken ct)
    {
        if (!SeesAllWork && !SeesOwnWork) return Error.Forbidden();
        if (to < from || to.DayNumber - from.DayNumber > 92) return Error.Validation("report.range", "بازه حداکثر ۹۲ روز.");
        var ids = await PeopleAsync(SeesAllWork, userId, ct);
        var data = await LoadAsync(from, to, ids, ct);
        var settings = await SettingsAsync(ct);
        var scores = Score(data, settings);
        var rows = data.People.Select(p => Row(p.Id, p.Name, data, scores.FirstOrDefault(s => s.UserId == p.Id)?.Total)).ToList();
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i =>
        {
            var d = from.AddDays(i);
            return new DayPointDto(d, data.Shifts.Where(s => s.Day == d).Sum(s => s.WorkedMinutes(data.Now)),
                data.Timers.Where(x => Iran.Today(x.StartedAt) == d).Sum(x => TimerMinutes(x, data.Now)),
                data.Done.Count(w => Iran.Today(w.DoneAt!.Value) == d));
        }).ToList();
        return new WorkReportDto(from, to, rows, days);
    }

    public async Task<Result<WorkDetailsDto>> DetailsAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!(SeesAllWork || (SeesOwnWork && userId == Me))) return Error.NotFound("report.notFound", "پیدا نشد.");
        if (to < from || to.DayNumber - from.DayNumber > 92) return Error.Validation("report.range", "بازه حداکثر ۹۲ روز.");
        var data = await LoadAsync(from, to, [userId], ct);
        var titles = await db.WorkItems.AsNoTracking().Where(w => data.Timers.Select(t => t.WorkItemId).Contains(w.Id)).Select(w => new { w.Id, w.Title, w.OrderNumber })
            .ToDictionaryAsync(w => w.Id, ct);
        return new WorkDetailsDto(
            data.Shifts.OrderBy(s => s.ClockInAt).Select(s => new ShiftLineDto(s.Day, s.ClockInAt, s.ClockOutAt, s.WorkedMinutes(data.Now), s.LateMinutes)).ToList(),
            data.Timers.OrderBy(x => x.StartedAt).Select(x => new TimerLineDto(x.StartedAt, x.StoppedAt, TimerMinutes(x, data.Now),
                titles.GetValueOrDefault(x.WorkItemId)?.Title ?? "—", titles.GetValueOrDefault(x.WorkItemId)?.OrderNumber ?? 0)).ToList(),
            data.Done.OrderBy(w => w.DoneAt).Select(w =>
            {
                var delivered = w.FirstSubmittedAt ?? w.SubmittedAt ?? w.DoneAt;
                return new DoneLineDto(w.Id, w.Title, w.OrderNumber, data.ServiceNames.GetValueOrDefault(w.ServiceTypeId, "—"), w.EstimatedMinutes,
                    data.ActualByItem.GetValueOrDefault(w.Id), w.Deadline, delivered, w.Deadline is { } dl && delivered is { } dv ? dv <= dl : null, w.RevisionCount,
                    w.ManagerStars, w.ClientStars);
            }).ToList());
    }

    public async Task<Result<byte[]>> ExcelAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var r = await WorkAsync(from, to, null, ct);
        if (r.IsFailure) return r.Error!;
        static string H(int m) => $"{m / 60}:{m % 60:00}";
        static string P(double? p) => p is { } v ? $"{v:0}%" : "—";
        return excel.Export("Work report", ["نام", "حضور", "روی کار (تایمر)", "کار مفید", "اضافه‌کار", "تأخیر (دقیقه)", "دفعات تأخیر", "غیبت غیرموجه", "غیبت موجه", "روز مرخصی",
                "کار تمام‌شده", "زمان تخمینی", "زمان واقعی", "به‌موقع", "تأیید بار اول", "دفعات اصلاح", "فایل از ربات", "امتیاز"],
            r.Value!.Rows.Select(x => (IReadOnlyList<object?>)[x.Name, H(x.PresenceMinutes), H(x.TimerMinutes), P(x.UsefulPercent), H(x.OvertimeMinutes), x.LateMinutes, x.LateCount,
                x.UnexcusedAbsences, x.ExcusedAbsences, x.LeaveDays, x.DoneCount, H(x.EstimatedMinutes), H(x.ActualMinutes), P(x.OnTimePercent), P(x.FirstTimePercent), x.Revisions,
                x.BotFiles, x.Score?.ToString(CultureInfo.InvariantCulture) ?? "—"]));
    }

    // ================= scores =================

    public async Task<Result<ScoresDto>> ScoresAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!SeesAllScores && !SeesOwnScore) return Error.Forbidden();
        if (to < from || to.DayNumber - from.DayNumber > 370) return Error.Validation("report.range", "بازه حداکثر یک سال.");
        // Workers look at their own score: a quarter is enough and keeps the team-wide calculation light.
        if (!SeesAllScores && to.DayNumber - from.DayNumber > 92) return Error.Validation("report.range", "بازه حداکثر ۹۲ روز.");
        var settings = await SettingsAsync(ct);
        // Volume is relative to the team, so the whole team is always computed; what is shown depends on the viewer.
        var data = await LoadAsync(from, to, await PeopleAsync(true, null, ct), ct);
        var all = Score(data, settings);
        var rankingVisible = SeesAllScores || settings.RankingForAll;
        IReadOnlyList<ScoreDto> people = SeesAllScores
            ? all
            : rankingVisible
                ? all.Select(s => s.UserId == Me ? s : s with { Indicators = [], Badges = s.Badges }).ToList()
                : all.Where(s => s.UserId == Me).ToList();
        return new ScoresDto(from, to, settings.MinSamples, people.OrderByDescending(p => p.Total ?? -1).ThenBy(p => p.Name).ToList(), rankingVisible);
    }

    /// <summary>The last six Persian months of one person's total (the monthly trend chart).</summary>
    public async Task<Result<IReadOnlyList<TrendPointDto>>> TrendAsync(Guid userId, CancellationToken ct)
    {
        if (!(SeesAllScores || (SeesOwnScore && userId == Me))) return Error.NotFound("report.notFound", "پیدا نشد.");
        var settings = await SettingsAsync(ct);
        var cal = new PersianCalendar();
        var today = Iran.Local(clock.GetUtcNow()).DateTime;
        var people = await PeopleAsync(true, null, ct);
        var points = new List<TrendPointDto>();
        for (var i = 5; i >= 0; i--)
        {
            var monthDate = cal.AddMonths(today, -i);
            var y = cal.GetYear(monthDate);
            var m = cal.GetMonth(monthDate);
            var from = DateOnly.FromDateTime(cal.ToDateTime(y, m, 1, 0, 0, 0, 0));
            var to = DateOnly.FromDateTime(cal.ToDateTime(y, m, cal.GetDaysInMonth(y, m), 0, 0, 0, 0));
            var data = await LoadAsync(from, to, people, ct);
            var mine = Score(data, settings).FirstOrDefault(s => s.UserId == userId);
            points.Add(new TrendPointDto($"{y}/{m:00}", from, mine?.Total));
        }
        return points;
    }

    // ================= computation =================

    private sealed record Person(Guid Id, string Name);

    private sealed class Data
    {
        public required DateTimeOffset Now { get; init; }
        public required List<Person> People { get; init; }
        public required List<Shift> Shifts { get; init; }
        public required List<WorkTimer> Timers { get; init; }
        public required List<WorkItem> Done { get; init; }
        public required Dictionary<Guid, int> ActualByItem { get; init; }
        public required List<Absence> Absences { get; init; }
        public required List<LeaveRequest> Leaves { get; init; }
        public required Dictionary<Guid, int> BotFiles { get; init; }
        public required Dictionary<Guid, string> ServiceNames { get; init; }
        public required int DayMinutes { get; init; }
    }

    private async Task<List<Guid>> PeopleAsync(bool everyone, Guid? only, CancellationToken ct)
    {
        if (!everyone) return [Me];
        var q = db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && u.IsActive && (!u.IsDemo || me.IsDemo));
        if (only is { } o) q = q.Where(u => u.Id == o);
        return await q.Select(u => u.Id).ToListAsync(ct);
    }

    private async Task<Data> LoadAsync(DateOnly from, DateOnly to, List<Guid> ids, CancellationToken ct)
    {
        var start = Iran.At(from, TimeOnly.MinValue);
        var end = Iran.At(to.AddDays(1), TimeOnly.MinValue);
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.DisplayName).Select(u => new Person(u.Id, u.DisplayName)).ToListAsync(ct);
        var done = await db.WorkItems.AsNoTracking()
            .Where(w => w.AssigneeId != null && ids.Contains(w.AssigneeId.Value) && w.Status == WorkStatus.Done && w.DoneAt >= start && w.DoneAt < end).ToListAsync(ct);
        var doneIds = done.Select(w => w.Id).ToList();
        var actual = await db.WorkTimers.AsNoTracking().Where(t => doneIds.Contains(t.WorkItemId)).GroupBy(t => t.WorkItemId)
            .Select(g => new { g.Key, M = g.Sum(t => t.Minutes) }).ToDictionaryAsync(g => g.Key, g => g.M, ct);
        var schedule = await db.WorkSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == null, ct);
        var firstDay = schedule?.Days.FirstOrDefault();
        return new Data
        {
            Now = clock.GetUtcNow(),
            People = people,
            Shifts = await db.Shifts.AsNoTracking().Where(s => ids.Contains(s.UserId) && s.Day >= from && s.Day <= to).ToListAsync(ct),
            Timers = await db.WorkTimers.AsNoTracking().Where(t => ids.Contains(t.UserId) && t.StartedAt >= start && t.StartedAt < end).ToListAsync(ct),
            Done = done,
            ActualByItem = actual,
            Absences = await db.Absences.AsNoTracking().Where(a => ids.Contains(a.UserId) && a.Day >= from && a.Day <= to).ToListAsync(ct),
            Leaves = await db.LeaveRequests.AsNoTracking().Where(l => ids.Contains(l.UserId) && l.Status == LeaveStatus.Approved && l.From < end && l.To > start).ToListAsync(ct),
            BotFiles = await db.MediaAssets.AsNoTracking().Where(m => ids.Contains(m.SenderId) && m.ReceivedAt >= start && m.ReceivedAt < end).GroupBy(m => m.SenderId)
                .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct),
            ServiceNames = (await db.ServiceTypes.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct)).ToDictionary(s => s.Id, s => s.Name.Get(me.LanguageCode)),
            DayMinutes = firstDay is null ? 480 : Math.Max(60, (int)(firstDay.End - firstDay.Start).TotalMinutes - schedule!.BreakMinutes),
        };
    }

    private static int TimerMinutes(WorkTimer t, DateTimeOffset now) => t.StoppedAt is null ? (int)Math.Max(0, (now - t.StartedAt).TotalMinutes) : t.Minutes;

    private static WorkRowDto Row(Guid id, string name, Data d, int? score)
    {
        var shifts = d.Shifts.Where(s => s.UserId == id).ToList();
        var presence = shifts.Sum(s => s.WorkedMinutes(d.Now));
        var timers = d.Timers.Where(t => t.UserId == id).Sum(t => TimerMinutes(t, d.Now));
        var done = d.Done.Where(w => w.AssigneeId == id).ToList();
        var withDeadline = done.Where(w => w.Deadline is not null).ToList();
        var onTime = withDeadline.Count(w => (w.FirstSubmittedAt ?? w.SubmittedAt ?? w.DoneAt) <= w.Deadline);
        var absences = d.Absences.Where(a => a.UserId == id).ToList();
        var leaveDays = d.Leaves.Where(l => l.UserId == id && !l.Hourly).Sum(l => Math.Max(1, (int)Math.Round((l.To - l.From).TotalDays)));
        return new WorkRowDto(id, name, presence, timers, presence > 0 ? Math.Round(100.0 * timers / presence, 1) : null,
            shifts.Sum(s => Math.Max(0, s.WorkedMinutes(d.Now) - d.DayMinutes)), shifts.Sum(s => s.LateMinutes) + absences.Sum(a => a.LateMinutes), shifts.Count(s => s.LateMinutes > 0),
            absences.Count(a => a.Status == AbsenceStatus.Unexcused), absences.Count(a => a.Status == AbsenceStatus.Excused), leaveDays,
            done.Count, done.GroupBy(w => w.ServiceTypeId).Select(g => new ServiceCountDto(g.Key, d.ServiceNames.GetValueOrDefault(g.Key, "—"), g.Count())).OrderByDescending(x => x.Count).ToList(),
            done.Sum(w => w.EstimatedMinutes), done.Sum(w => d.ActualByItem.GetValueOrDefault(w.Id)),
            withDeadline.Count > 0 ? Math.Round(100.0 * onTime / withDeadline.Count, 1) : null,
            done.Count > 0 ? Math.Round(100.0 * done.Count(w => w.RevisionCount == 0) / done.Count, 1) : null,
            done.Sum(w => w.RevisionCount), d.BotFiles.GetValueOrDefault(id), score);
    }

    private static List<ScoreDto> Score(Data d, ScoreSettingsDto s)
    {
        var w = s.Weights;
        double ServiceWeight(Guid serviceTypeId) => s.ServiceWeights?.GetValueOrDefault(serviceTypeId) is { } x and > 0 ? x : 1;
        var volumes = d.People.ToDictionary(p => p.Id, p => d.Done.Where(x => x.AssigneeId == p.Id).Sum(x => ServiceWeight(x.ServiceTypeId)));
        var maxVolume = volumes.Values.DefaultIfEmpty(0).Max();
        var result = new List<ScoreDto>();
        foreach (var p in d.People)
        {
            var done = d.Done.Where(x => x.AssigneeId == p.Id).ToList();
            var enough = done.Count >= s.MinSamples;
            var indicators = new List<IndicatorDto>();

            // Quality: first-time approvals, minus each round of changes.
            var firstTime = done.Count(x => x.RevisionCount == 0);
            var revisions = done.Sum(x => x.RevisionCount);
            int? quality = done.Count == 0 ? null : Clamp(100.0 * firstTime / done.Count - 10.0 * revisions / done.Count);
            indicators.Add(new IndicatorDto("quality", quality, $"{Fa(firstTime)} از {Fa(done.Count)} بار اول تأیید شد؛ {Fa(revisions)} اصلاح", w.Quality,
                revisions == 0 ? "عالی؛ همین‌طور ادامه دهید." : "قبل از فرستادن، توضیح سفارش و نظرهای قبلی مشتری را دوباره مرور کنید."));

            // Speed: real time ÷ estimate, capped so rushing earns nothing extra.
            var timed = done.Where(x => x.EstimatedMinutes > 0 && d.ActualByItem.GetValueOrDefault(x.Id) > 0).ToList();
            double? ratio = timed.Count == 0 ? null : timed.Average(x => (double)d.ActualByItem[x.Id] / x.EstimatedMinutes);
            int? speed = ratio is { } r ? Clamp((2 - Math.Max(r, 0.8)) / 1.2 * 100) : null;
            indicators.Add(new IndicatorDto("speed", speed, ratio is { } rr ? $"زمان واقعی {Fa((int)Math.Round(rr * 100))}٪ زمان تخمینی" : "تایمر روی کارها روشن نبوده", w.Speed,
                ratio is null ? "هنگام کار تایمر را روشن کنید تا زمان واقعی ثبت شود." : ratio > 1.2 ? "کار را به قدم‌های کوچک بشکنید و اول کارهای تکراری را آماده کنید." : "سرعت خوب است؛ عجله لازم نیست."));

            // On time: first delivery before the deadline (waiting for the client does not count against anyone).
            var withDeadline = done.Where(x => x.Deadline is not null).ToList();
            var onTimeCount = withDeadline.Count(x => (x.FirstSubmittedAt ?? x.SubmittedAt ?? x.DoneAt) <= x.Deadline);
            int? onTime = withDeadline.Count == 0 ? null : Clamp(100.0 * onTimeCount / withDeadline.Count);
            indicators.Add(new IndicatorDto("onTime", onTime, $"{Fa(onTimeCount)} از {Fa(withDeadline.Count)} قبل از ددلاین", w.OnTime,
                onTime is null or 100 ? "عالی." : "کارهای نزدیک به ددلاین را اول بردارید؛ اگر وقت کم است زودتر به مدیر بگویید."));

            // Volume: weighted by service (a reel weighs more than a story), relative to the busiest person in the period.
            int? volume = maxVolume > 0 ? Clamp(100 * volumes[p.Id] / maxVolume) : null;
            indicators.Add(new IndicatorDto("volume", volume, $"{Fa(done.Count)} کار تمام‌شده (وزن‌دار: {FaD(volumes[p.Id], "0.#")})", w.Volume,
                "حجم نسبت به پرکارترین نفر همین بازه سنجیده می‌شود."));

            // Discipline: lateness and unexcused absence.
            var late = d.Shifts.Count(x => x.UserId == p.Id && x.LateMinutes > 0);
            var absent = d.Absences.Count(x => x.UserId == p.Id && x.Status == AbsenceStatus.Unexcused);
            var presentDays = d.Shifts.Count(x => x.UserId == p.Id);
            int? discipline = presentDays + absent == 0 ? null : Clamp(100 - 3 * late - 15 * absent);
            indicators.Add(new IndicatorDto("discipline", discipline, $"{Fa(late)} تأخیر، {Fa(absent)} غیبت غیرموجه", w.Discipline,
                late + absent == 0 ? "بدون تأخیر؛ عالی." : "اگر دیر می‌رسید، قبلش در اپ مرخصی ساعتی بگیرید تا غیبت حساب نشود."));

            var managerStars = done.Where(x => x.ManagerStars is not null).Select(x => (double)x.ManagerStars!.Value).ToList();
            int? manager = managerStars.Count == 0 ? null : Clamp(managerStars.Average() * 20);
            indicators.Add(new IndicatorDto("manager", manager, managerStars.Count == 0 ? "امتیازی ثبت نشده" : $"میانگین {FaD(managerStars.Average(), "0.0")} از ۵", w.Manager, "امتیاز مدیر روی کارهای تمام‌شده."));
            var clientStars = done.Where(x => x.ClientStars is not null).Select(x => (double)x.ClientStars!.Value).ToList();
            int? client = clientStars.Count == 0 ? null : Clamp(clientStars.Average() * 20);
            indicators.Add(new IndicatorDto("client", client, clientStars.Count == 0 ? "امتیازی ثبت نشده" : $"میانگین {FaD(clientStars.Average(), "0.0")} از ۵", w.Client, "امتیاز مشتری بعد از تأیید محتوا."));

            var counted = indicators.Where(i => i.Score is not null && i.Weight > 0).ToList();
            int? total = enough && counted.Count > 0 ? (int)Math.Round(counted.Sum(i => (double)i.Score!.Value * i.Weight) / counted.Sum(i => i.Weight)) : null;

            var badges = new List<string>();
            if (enough && onTime == 100 && withDeadline.Count >= s.MinSamples) badges.Add("۱۰۰٪ به‌موقع");
            if (enough && revisions == 0) badges.Add("بدون برگشت");
            if (presentDays >= 10 && late == 0 && absent == 0) badges.Add("بدون تأخیر");
            if (enough && maxVolume > 0 && volumes[p.Id] == maxVolume) badges.Add("پرکارترین");

            result.Add(new ScoreDto(p.Id, p.Name, total, enough, done.Count, indicators, badges));
        }
        return result;
    }

    private static int Clamp(double v) => (int)Math.Round(Math.Clamp(v, 0, 100));
    private static string FaD(double n, string format) =>
        string.Concat(n.ToString(format, CultureInfo.InvariantCulture).Select(c => char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c == '.' ? '٫' : c));
    private static string Fa(long n) => string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c));
}
