using Karnama.Domain.Common;

namespace Karnama.Domain.Attendance;

/// <summary>
/// How a clock-in must be verified, e.g. "OfficeNetwork & (RotatingQr | BiometricDevice)". Attached to roles or people.
/// A failed check never blocks the worker: the shift is recorded as "needs review" and a manager decides.
/// </summary>
public sealed class AttendancePolicy : Entity
{
    public static readonly string[] Methods = ["OfficeNetwork", "RotatingQr", "Geofence", "Selfie", "BiometricDevice", "NfcTag", "ManagerApproval"];
    private AttendancePolicy() { Name = string.Empty; Rule = string.Empty; RoleIds = []; UserIds = []; }

    public string Name { get; private set; }
    public string Rule { get; private set; }
    public List<Guid> RoleIds { get; private set; }
    public List<Guid> UserIds { get; private set; }
    public bool IsDefault { get; private set; }
    public uint Version { get; private set; }

    public static Result<AttendancePolicy> Create(string name, string rule, IReadOnlyList<Guid> roleIds, IReadOnlyList<Guid> userIds, bool isDefault)
    {
        var p = new AttendancePolicy();
        var r = p.Update(name, rule, roleIds, userIds, isDefault);
        return r.IsSuccess ? p : r.Error!;
    }

    public Result Update(string name, string rule, IReadOnlyList<Guid> roleIds, IReadOnlyList<Guid> userIds, bool isDefault)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100) return Error.Validation("policies.name", "نام سیاست الزامی است.");
        if (PolicyRule.Parse(rule) is { IsFailure: true } bad) return bad.Error!;
        Name = name;
        Rule = rule.Trim();
        RoleIds = roleIds.Distinct().ToList();
        UserIds = userIds.Distinct().ToList();
        IsDefault = isDefault;
        return Result.Success();
    }
}

/// <summary>Tiny boolean language: method names joined by &amp; (and), | (or), parentheses. No other input is accepted.</summary>
public abstract record PolicyRule
{
    public sealed record Method(string Name) : PolicyRule;
    public sealed record And(PolicyRule Left, PolicyRule Right) : PolicyRule;
    public sealed record Or(PolicyRule Left, PolicyRule Right) : PolicyRule;

    public bool Evaluate(Func<string, bool> passed) => this switch
    {
        Method m => passed(m.Name),
        And a => a.Left.Evaluate(passed) && a.Right.Evaluate(passed),
        Or o => o.Left.Evaluate(passed) || o.Right.Evaluate(passed),
        _ => false,
    };

    public IEnumerable<string> Names() => this switch
    {
        Method m => [m.Name],
        And a => a.Left.Names().Concat(a.Right.Names()),
        Or o => o.Left.Names().Concat(o.Right.Names()),
        _ => [],
    };

    public static Result<PolicyRule> Parse(string text)
    {
        var tokens = Tokenize(text ?? "");
        if (tokens.Count is 0 or > 50) return Error.Validation("policies.rule", "قانون خالی یا خیلی طولانی است.");
        var pos = 0;
        PolicyRule? Expr() { var l = Term(); while (l is not null && pos < tokens.Count && tokens[pos] == "|") { pos++; var r = Term(); if (r is null) return null; l = new Or(l, r); } return l; }
        PolicyRule? Term() { var l = Factor(); while (l is not null && pos < tokens.Count && tokens[pos] == "&") { pos++; var r = Factor(); if (r is null) return null; l = new And(l, r); } return l; }
        PolicyRule? Factor()
        {
            if (pos >= tokens.Count) return null;
            var t = tokens[pos++];
            if (t == "(") { var e = Expr(); if (pos >= tokens.Count || tokens[pos++] != ")") return null; return e; }
            return AttendancePolicy.Methods.Contains(t) ? new Method(t) : null;
        }
        var rule = Expr();
        return rule is null || pos != tokens.Count
            ? Error.Validation("policies.rule", "قانون معتبر نیست. مثال: OfficeNetwork & (RotatingQr | BiometricDevice)")
            : rule;
    }

    private static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c is '&' or '|' or '(' or ')') { list.Add(c.ToString()); i++; continue; }
            var start = i;
            while (i < s.Length && char.IsAsciiLetter(s[i])) i++;
            if (i == start) return [];
            list.Add(s[start..i]);
        }
        return list;
    }
}

/// <summary>A place where clock-in by location is allowed (office, or a client's address for a shoot).</summary>
public sealed class AttendancePlace : Entity
{
    private AttendancePlace() { Name = string.Empty; }
    public static Result<AttendancePlace> Create(string name, double lat, double lng, int radiusMeters)
    {
        if (string.IsNullOrWhiteSpace(name)) return Error.Validation("places.name", "نام مکان الزامی است.");
        if (lat is < -90 or > 90 || lng is < -180 or > 180) return Error.Validation("places.coords", "مختصات معتبر نیست.");
        if (radiusMeters is < 20 or > 5000) return Error.Validation("places.radius", "شعاع باید بین ۲۰ تا ۵۰۰۰ متر باشد.");
        return new AttendancePlace { Name = name.Trim(), Latitude = lat, Longitude = lng, RadiusMeters = radiusMeters };
    }
    public string Name { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public int RadiusMeters { get; private set; }

    public double DistanceMeters(double lat, double lng)
    {
        const double R = 6_371_000;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat - Latitude);
        var dLng = Rad(lng - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(Latitude)) * Math.Cos(Rad(lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

/// <summary>A used office QR code. The primary key makes each code single-use, even under concurrent scans.</summary>
public sealed class KioskCodeUse
{
    private KioskCodeUse() { }
    public KioskCodeUse(long step, int n, Guid userId, DateTimeOffset at) { Step = step; N = n; UserId = userId; At = at; }
    public long Step { get; private set; }
    public int N { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset At { get; private set; }
}
