using Karnama.Domain.Common;

namespace Karnama.Domain.Attendance;

/// <summary>How a device is read: it pushes to us (ZKTeco ADMS), or its software exports a file we import.</summary>
public enum DeviceKind { Adms = 1, FileImport = 2 }

/// <summary>
/// Toggle: every punch flips in/out (devices without in/out keys always send status 0).
/// StatusKey: the person presses "ورود"/"خروج" on the device and its status code decides.
/// </summary>
public enum PunchMode { Toggle = 1, StatusKey = 2 }

/// <summary>
/// A fingerprint / face device in the office (PROMPT2 §6). Only the person's number on the device (PIN) and the time
/// ever reach us — never fingerprints or face photos.
/// </summary>
public sealed class BiometricDevice : Entity
{
    private BiometricDevice() { Name = string.Empty; Serial = string.Empty; AllowedIps = []; PendingCommands = []; }

    public string Name { get; private set; }
    /// <summary>The device's serial number (SN): the only thing that lets it talk to us.</summary>
    public string Serial { get; private set; }
    public DeviceKind Kind { get; private set; }
    public PunchMode Mode { get; private set; }
    public string? Location { get; private set; }
    /// <summary>Addresses/networks the device may call from; empty = the office networks in attendance settings.</summary>
    public List<string> AllowedIps { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string? LastIp { get; private set; }
    /// <summary>Last ATTLOG stamp the device reported, sent back on handshake so it only pushes newer records.</summary>
    public long AttStamp { get; private set; }
    /// <summary>Device clock minus server clock at the last real-time punch (seconds); null until measured.</summary>
    public int? ClockSkewSeconds { get; private set; }
    public DateTimeOffset? SkewWarnedAt { get; private set; }
    public DateTimeOffset? OfflineWarnedAt { get; private set; }
    /// <summary>Commands waiting for the device's next poll ("C:id:command" lines).</summary>
    public List<string> PendingCommands { get; private set; }
    public int NextCommandId { get; private set; } = 1;
    public uint Version { get; private set; }

    public static Result<BiometricDevice> Create(string name, string serial, DeviceKind kind, PunchMode mode, string? location, IEnumerable<string>? allowedIps)
    {
        var d = new BiometricDevice { Kind = kind };
        var s = (serial ?? "").Trim().ToUpperInvariant();
        if (kind == DeviceKind.FileImport && s.Length == 0) s = "FILE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        if (s.Length is < 4 or > 40 || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return Error.Validation("devices.serial", "شماره‌ی سریال دستگاه را دقیق وارد کنید (۴ تا ۴۰ حرف و عدد انگلیسی).");
        d.Serial = s;
        var r = d.Update(name, mode, location, allowedIps, true);
        return r.IsSuccess ? d : r.Error!;
    }

    public Result Update(string name, PunchMode mode, string? location, IEnumerable<string>? allowedIps, bool isActive)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 60) return Error.Validation("devices.name", "نام دستگاه الزامی است (حداکثر ۶۰ کاراکتر).");
        var ips = (allowedIps ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().Take(20).ToList();
        if (ips.Any(x => x.Length > 50)) return Error.Validation("devices.ips", "نشانی IP خیلی بلند است.");
        Name = n;
        Mode = mode;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim()[..Math.Min(location.Trim().Length, 80)];
        AllowedIps = ips;
        IsActive = isActive;
        return Result.Success();
    }

    public void Seen(DateTimeOffset now, string? ip)
    {
        LastSeenAt = now;
        LastIp = ip is { Length: > 45 } ? ip[..45] : ip;
        OfflineWarnedAt = null;
    }

    public void Stamp(long stamp) { if (stamp > AttStamp) AttStamp = stamp; }

    public void MeasuredSkew(int seconds) => ClockSkewSeconds = seconds;

    public void SkewWarned(DateTimeOffset now) => SkewWarnedAt = now;

    public void OfflineWarned(DateTimeOffset now) => OfflineWarnedAt = now;

    public void Queue(string command)
    {
        if (PendingCommands.Count >= 20) PendingCommands.RemoveAt(0);
        PendingCommands = [.. PendingCommands, $"C:{NextCommandId++}:{command}"];
    }

    public IReadOnlyList<string> TakeCommands()
    {
        var list = PendingCommands;
        PendingCommands = [];
        return list;
    }
}

/// <summary>Person number on the devices (PIN) ↔ a user. One PIN per person, the same on every device.</summary>
public sealed class DevicePin : Entity
{
    private DevicePin() { Pin = string.Empty; }

    public string Pin { get; private set; }
    public Guid UserId { get; private set; }

    public static Result<DevicePin> Create(string pin, Guid userId)
    {
        var p = Normalize(pin);
        if (p is null) return Error.Validation("devices.pin", "شماره‌ی کاربر روی دستگاه فقط عدد است (حداکثر ۹ رقم).");
        return new DevicePin { Pin = p, UserId = userId };
    }

    /// <summary>"0012" and "12" are the same person on most devices.</summary>
    public static string? Normalize(string? pin)
    {
        var t = (pin ?? "").Trim();
        if (t.Length is 0 or > 9 || !t.All(char.IsAsciiDigit)) return null;
        var n = t.TrimStart('0');
        return n.Length == 0 ? "0" : n;
    }
}

public enum PunchOutcome { Pending = 0, ClockIn = 1, ClockOut = 2, BreakStart = 3, BreakEnd = 4, Ignored = 5, UnknownPin = 6, OutOfOrder = 7 }

/// <summary>One raw record from a device or an imported file. (Device, PIN, time) is unique, so a re-sent record is stored once.</summary>
public sealed class DevicePunch
{
    private DevicePunch() { Pin = string.Empty; }

    public long Id { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Pin { get; private set; }
    public DateTimeOffset At { get; private set; }
    /// <summary>Device status code: 0 in, 1 out, 2 break out, 3 break in, 4/5 overtime; -1 unknown (imported).</summary>
    public int Status { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public Guid? UserId { get; private set; }
    public PunchOutcome Outcome { get; private set; }
    public string? Note { get; private set; }

    public static DevicePunch Create(Guid deviceId, string pin, DateTimeOffset at, int status, DateTimeOffset receivedAt) => new()
    {
        DeviceId = deviceId, Pin = pin, At = at, Status = status, ReceivedAt = receivedAt, Outcome = PunchOutcome.Pending,
    };

    public void Applied(Guid? userId, PunchOutcome outcome, string? note = null)
    {
        UserId = userId;
        Outcome = outcome;
        Note = note is { Length: > 200 } ? note[..200] : note;
    }
}
