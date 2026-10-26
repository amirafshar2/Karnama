using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Karnama.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Karnama.Infrastructure.Cctv;

/// <summary>Where the office media box is (always inside the WireGuard tunnel) and how viewers reach it.</summary>
public sealed class CctvOptions
{
    public const string Section = "Cctv";
    /// <summary>MediaMTX control API through the tunnel, e.g. http://10.66.0.2:9997 (empty = CCTV not set up).</summary>
    public string? ApiUrl { get; set; }
    /// <summary>MediaMTX RTSP through the tunnel, for snapshots, e.g. rtsp://10.66.0.2:8554</summary>
    public string? RtspUrl { get; set; }
    /// <summary>Public WebRTC base through our reverse proxy, e.g. https://cctv.example.ir</summary>
    public string? PublicUrl { get; set; }
    /// <summary>Internal account our server uses towards the box (secret; from .env).</summary>
    public string? InternalUser { get; set; }
    public string? InternalPassword { get; set; }
    /// <summary>Addresses allowed to call the authentication hook (the box's tunnel address).</summary>
    public List<string> HookAddresses { get; set; } = [];
    public string FfmpegPath { get; set; } = "ffmpeg";
}

/// <summary>MediaMTX v1 control API (/v3/config/paths/…, /v3/paths/list) and one-frame snapshots with ffmpeg.</summary>
internal sealed class MediaMtxServer(IHttpClientFactory http, IOptions<CctvOptions> options, ILogger<MediaMtxServer> logger) : IMediaServer
{
    private CctvOptions O => options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(O.ApiUrl) && !string.IsNullOrWhiteSpace(O.PublicUrl)
                                && !string.IsNullOrWhiteSpace(O.InternalUser) && !string.IsNullOrWhiteSpace(O.InternalPassword);

    private HttpClient Client()
    {
        var c = http.CreateClient("mediamtx");
        c.BaseAddress = new Uri(O.ApiUrl!.TrimEnd('/') + "/");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{O.InternalUser}:{O.InternalPassword}")));
        return c;
    }

    public async Task<bool> UpsertPathAsync(string path, string source, bool onDemand, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrEmpty(source)) return false;
        var body = new { source, sourceOnDemand = onDemand, rtspTransport = "tcp" };
        try
        {
            using var c = Client();
            var r = await c.PostAsJsonAsync($"v3/config/paths/add/{path}", body, ct);
            if (!r.IsSuccessStatusCode) r = await c.PostAsJsonAsync($"v3/config/paths/replace/{path}", body, ct);
            if (!r.IsSuccessStatusCode) logger.LogWarning("MediaMTX refused path {Path}: {Status}", path, (int)r.StatusCode);
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "MediaMTX unreachable while saving {Path}", path);
            return false;
        }
    }

    public async Task<bool> RemovePathAsync(string path, CancellationToken ct)
    {
        if (!IsConfigured) return false;
        try
        {
            using var c = Client();
            return (await c.DeleteAsync($"v3/config/paths/delete/{path}", ct)).IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<string, bool>?> ReadyPathsAsync(CancellationToken ct)
    {
        if (!IsConfigured) return null;
        try
        {
            using var c = Client();
            using var doc = JsonDocument.Parse(await c.GetStringAsync("v3/paths/list?itemsPerPage=1000", ct));
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (doc.RootElement.TryGetProperty("items", out var items))
                foreach (var i in items.EnumerateArray())
                    map[i.GetProperty("name").GetString() ?? ""] = i.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True;
            return map;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>One frame from the substream through the tunnel: ffmpeg, 8-second limit, JPEG on stdout.</summary>
    public async Task<byte[]?> SnapshotAsync(string path, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(O.RtspUrl)) return null;
        var rtsp = new UriBuilder(O.RtspUrl!.TrimEnd('/') + "/" + path) { UserName = Uri.EscapeDataString(O.InternalUser!), Password = Uri.EscapeDataString(O.InternalPassword!) }.Uri;
        var psi = new ProcessStartInfo(O.FfmpegPath)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-rtsp_transport", "tcp", "-timeout", "5000000", "-i", rtsp.ToString(), "-an", "-frames:v", "1", "-q:v", "6", "-f", "mjpeg", "pipe:1" })
            psi.ArgumentList.Add(a);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        Process? p = null;
        try
        {
            p = Process.Start(psi)!;
            using var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms, timeout.Token);
            _ = p.StandardError.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            await copy;
            return p.ExitCode == 0 && ms.Length > 100 ? ms.ToArray() : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogInformation("Snapshot of {Path} failed: {Message}", path, ex.Message);
            return null;
        }
        finally
        {
            // A hung ffmpeg (tunnel down) must not outlive the request.
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            p?.Dispose();
        }
    }

    /// <summary>Closes WebRTC sessions older than <paramref name="maxAge"/> (the page reconnects while someone is really watching).</summary>
    public async Task<int> KickOldSessionsAsync(TimeSpan maxAge, CancellationToken ct)
    {
        if (!IsConfigured) return 0;
        try
        {
            using var c = Client();
            using var doc = JsonDocument.Parse(await c.GetStringAsync("v3/webrtcsessions/list?itemsPerPage=1000", ct));
            var kicked = 0;
            if (!doc.RootElement.TryGetProperty("items", out var items)) return 0;
            foreach (var i in items.EnumerateArray())
            {
                if (!i.TryGetProperty("created", out var created) || !created.TryGetDateTimeOffset(out var at) || DateTimeOffset.UtcNow - at < maxAge) continue;
                var id = i.GetProperty("id").GetString();
                if ((await c.PostAsync($"v3/webrtcsessions/kick/{id}", null, ct)).IsSuccessStatusCode) kicked++;
            }
            return kicked;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return 0;
        }
    }

    public string WhepUrl(string path) => $"{O.PublicUrl!.TrimEnd('/')}/{path}/whep";

    public bool IsInternalAccount(string? user, string? password) =>
        IsConfigured && user is not null && password is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user), Encoding.UTF8.GetBytes(O.InternalUser!))
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(O.InternalPassword!));
}
