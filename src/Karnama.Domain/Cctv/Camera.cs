using Karnama.Domain.Common;

namespace Karnama.Domain.Cctv;

/// <summary>
/// A camera in the office (PROMPT2 §5). Its RTSP addresses (which contain the camera password) are stored only
/// encrypted (<see cref="SealedSource"/>); the media box in the office pulls the stream, cameras are never exposed.
/// </summary>
public sealed class Camera : Entity
{
    private Camera() { Name = string.Empty; Path = string.Empty; MainCipher = []; MainKey = []; }

    public string Name { get; private set; }
    public string? Location { get; private set; }
    /// <summary>The stream name on the media box ("cam-3f2a…"): never contains the address or password.</summary>
    public string Path { get; private set; }
    public byte[] MainCipher { get; private set; }
    public byte[] MainKey { get; private set; }
    public byte[]? SubCipher { get; private set; }
    public byte[]? SubKey { get; private set; }
    public int KeyVersion { get; private set; }
    /// <summary>Address with the password hidden, for the admin form ("rtsp://admin:••••@192.168.1.64:554/…").</summary>
    public string MainMasked { get; private set; } = string.Empty;
    public string? SubMasked { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool Online { get; private set; }
    public DateTimeOffset? LastOnlineAt { get; private set; }
    public DateTimeOffset? OfflineWarnedAt { get; private set; }
    public uint Version { get; private set; }

    public static Result<Camera> Create(string name, string? location, int sortOrder)
    {
        var c = new Camera();
        c.Path = "cam-" + c.Id.ToString("N")[..12];
        var r = c.Update(name, location, sortOrder, true);
        return r.IsSuccess ? c : r.Error!;
    }

    public Result Update(string name, string? location, int sortOrder, bool isActive)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 60) return Error.Validation("cctv.name", "نام دوربین الزامی است (حداکثر ۶۰ کاراکتر).");
        Name = n;
        Location = string.IsNullOrWhiteSpace(location) ? null : location.Trim()[..Math.Min(location.Trim().Length, 80)];
        SortOrder = sortOrder;
        IsActive = isActive;
        return Result.Success();
    }

    public void SetSources(byte[] mainCipher, byte[] mainKey, string mainMasked, byte[]? subCipher, byte[]? subKey, string? subMasked, int keyVersion)
    {
        MainCipher = mainCipher; MainKey = mainKey; MainMasked = mainMasked;
        SubCipher = subCipher; SubKey = subKey; SubMasked = subMasked;
        KeyVersion = keyVersion;
    }

    /// <returns>True when the state changed from online to offline (the moment to warn).</returns>
    public bool Seen(bool online, DateTimeOffset now)
    {
        var wentDown = Online && !online;
        Online = online;
        if (online) { LastOnlineAt = now; OfflineWarnedAt = null; }
        return wentDown;
    }

    public void OfflineWarned(DateTimeOffset now) => OfflineWarnedAt = now;

    /// <summary>
    /// Only rtsp/rtsps to a private (office) address: the media box must never be pointed at the internet or at
    /// anything that isn't a camera on the office network.
    /// </summary>
    public static Result<Uri> ValidateSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("rtsp" or "rtsps"))
            return Error.Validation("cctv.url", "نشانی باید با rtsp:// شروع شود؛ مثلاً rtsp://admin:رمز@192.168.1.64:554/Streaming/Channels/101");
        if (!System.Net.IPAddress.TryParse(u.Host, out var ip) || !IsPrivate(ip))
            return Error.Validation("cctv.private", "دوربین باید نشانی داخلی دفتر داشته باشد (مثل 192.168.x.x)؛ دوربین را هرگز روی اینترنت باز نکنید.");
        return u;
    }

    public static string Mask(Uri u)
    {
        var user = u.UserInfo.Split(':')[0];
        var auth = u.UserInfo.Length == 0 ? "" : $"{Uri.UnescapeDataString(user)}:••••@";
        return $"{u.Scheme}://{auth}{u.Host}{(u.IsDefaultPort || u.Port < 0 ? "" : ":" + u.Port)}{u.PathAndQuery}";
    }

    private static bool IsPrivate(System.Net.IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.IsIPv6UniqueLocal || ip.IsIPv6LinkLocal;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
