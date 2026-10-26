using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Karnama.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Karnama.IntegrationTests;

/// <summary>
/// Real PostgreSQL for every test class (never EF InMemory).
/// Uses Testcontainers (Docker). Without Docker, set KARNAMA_TEST_PG to a server connection string
/// (e.g. "Host=localhost;Username=karnama;Password=...") and a throwaway database is created per run.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string? _externalDb;
    private string? _externalServer;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _externalServer = Environment.GetEnvironmentVariable("KARNAMA_TEST_PG");
        if (!string.IsNullOrWhiteSpace(_externalServer))
        {
            _externalDb = "karnama_test_" + Guid.NewGuid().ToString("N")[..12];
            await using var conn = new NpgsqlConnection(_externalServer + ";Database=postgres");
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {_externalDb}", conn);
            await cmd.ExecuteNonQueryAsync();
            ConnectionString = $"{_externalServer};Database={_externalDb};Maximum Pool Size=50";
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
        if (_externalDb is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(_externalServer + ";Database=postgres");
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_externalDb} WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

public sealed class CapturingSms : ISmsSender
{
    public ConcurrentDictionary<string, string> LastCode { get; } = new();
    public int Sent;

    public Task SendOtpAsync(string phone, string code, CancellationToken ct)
    {
        LastCode[phone] = code;
        Interlocked.Increment(ref Sent);
        return Task.CompletedTask;
    }

    public ConcurrentQueue<(string Phone, string Text)> Texts { get; } = new();

    public Task SendTextAsync(string phone, string text, CancellationToken ct)
    {
        Texts.Enqueue((phone, text));
        return Task.CompletedTask;
    }

    /// <summary>SMS are sent in the background; wait until a new code for the phone arrives.</summary>
    public async Task<string> WaitForCodeAsync(string phone, string? previous = null)
    {
        for (var i = 0; i < 50; i++)
        {
            if (LastCode.TryGetValue(phone, out var c) && c != previous) return c;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No OTP sent to {phone}");
    }
}

public sealed class CapturingMessenger : Karnama.Infrastructure.Notify.IMessengerSender
{
    public ConcurrentQueue<(Karnama.Domain.Notify.Messenger M, long ChatId, string Text)> Sent { get; } = new();

    public Task SendAsync(Karnama.Domain.Notify.Messenger messenger, long chatId, string text, CancellationToken ct)
    {
        Sent.Enqueue((messenger, chatId, text));
        return Task.CompletedTask;
    }
}

/// <summary>Stands in for the Telegram gateway / Bale API: records calls; chats listed in <see cref="Blocked"/> answer 403.</summary>
/// <summary>Stands in for the office media box (MediaMTX): remembers streams, answers with a tiny JPEG.</summary>
public sealed class FakeMediaServer : Karnama.Application.Abstractions.IMediaServer
{
    public ConcurrentDictionary<string, (string Source, bool OnDemand)> Paths { get; } = new();
    public HashSet<string> Down { get; } = [];
    public int Snapshots;
    /// <summary>Configured but switched off / network down: every call fails like the real client does.</summary>
    public volatile bool Unreachable;
    public bool IsConfigured => true;
    public Task<bool> UpsertPathAsync(string path, string source, bool onDemand, CancellationToken ct)
    {
        if (Unreachable) return Task.FromResult(false);
        Paths[path] = (source, onDemand);
        return Task.FromResult(true);
    }
    public Task<bool> RemovePathAsync(string path, CancellationToken ct) => Task.FromResult(!Unreachable && Paths.TryRemove(path, out _));
    public Task<IReadOnlyDictionary<string, bool>?> ReadyPathsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, bool>?>(Unreachable ? null : Paths.Keys.ToDictionary(k => k, k => !Down.Contains(k)));
    public Task<byte[]?> SnapshotAsync(string path, CancellationToken ct)
    {
        Interlocked.Increment(ref Snapshots);
        return Task.FromResult<byte[]?>(Unreachable || Down.Contains(path) ? null : [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9]);
    }
    public string WhepUrl(string path) => $"https://cctv.test/{path}/whep";
    public Task<int> KickOldSessionsAsync(TimeSpan maxAge, CancellationToken ct) => Task.FromResult(0);
    public bool IsInternalAccount(string? user, string? password) => user == "internal" && password == "internal-secret";
}

public sealed class CapturingBotGateway : Karnama.Application.Abstractions.IBotGateway
{
    public ConcurrentQueue<(Karnama.Domain.Notify.Messenger M, string Method, System.Text.Json.Nodes.JsonObject Args)> Calls { get; } = new();
    public HashSet<long> Blocked { get; } = [];
    private long _messageId = 1000;

    public bool IsConfigured(Karnama.Domain.Notify.Messenger messenger) => true;

    public Task<Karnama.Application.Abstractions.BotCallResult> CallAsync(Karnama.Domain.Notify.Messenger m, string method, System.Text.Json.Nodes.JsonObject args, CancellationToken ct)
    {
        Calls.Enqueue((m, method, (System.Text.Json.Nodes.JsonObject)args.DeepClone()));
        if (args["chat_id"] is { } chat && Blocked.Contains(chat.GetValue<long>()))
            return Task.FromResult(new Karnama.Application.Abstractions.BotCallResult(false, true, null, "403: Forbidden: bot was blocked by the user"));
        var id = Interlocked.Increment(ref _messageId);
        return Task.FromResult(new Karnama.Application.Abstractions.BotCallResult(true, false, $"{{\"message_id\":{id}}}", null));
    }

    public Task<byte[]?> ThumbnailAsync(Karnama.Domain.Notify.Messenger m, string fileId, CancellationToken ct) =>
        Task.FromResult<byte[]?>([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46]);
}

/// <param name="tokensOnFakeClock">Check JWT lifetimes against <see cref="Clock"/> (tokens are issued on it), so a test can move
/// the clock by hours or days and keep calling the API with freshly issued tokens.</param>
public class KarnamaFactory(string connectionString, bool demoLogin = true, string seedMode = "Demo", string environment = "Development", bool tokensOnFakeClock = false)
    : WebApplicationFactory<Program>
{
    public const string SetupToken = "test-setup-token-123456";

    public CapturingSms Sms { get; } = new();
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public CapturingMessenger Messenger { get; } = new();
    public CapturingBotGateway Bots { get; } = new();
    public FakeMediaServer Media { get; } = new();
    public const long ArchiveChat = -1001234567890;
    public const string BaleSecret = "bale-webhook-secret-test-0123456789";
    public const string GatewaySecret = "telegram-gateway-secret-test-0123456789";

    static KarnamaFactory() => Environment.SetEnvironmentVariable("KARNAMA_SKIP_DOTENV", "1");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789-abcdef");
        builder.UseSetting("Otp:HashKey", "integration-test-otp-hash-key-0123456789-abcdef");
        builder.UseSetting("Setup:Token", SetupToken);
        builder.UseSetting("Vault:MasterKey", "dGVzdC12YXVsdC1tYXN0ZXIta2V5LTMyLWJ5dGVzISE=");
        builder.UseSetting("Seed:Mode", seedMode);
        builder.UseSetting("DemoLogin:Enabled", demoLogin ? "true" : "false");
        builder.UseSetting("Database:AutoMigrate", "true");
        builder.UseSetting("Sms:Provider", environment == "Production" ? "Kavenegar" : "Console");
        builder.UseSetting("RateLimits:OtpPerIpPer10Minutes", "1000");
        builder.UseSetting("RateLimits:AuthPerIpPerMinute", "1000");
        builder.UseSetting("RateLimits:UserBurst", "1000");
        builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "karnama-test-storage"));
        builder.UseSetting("Messengers:BaleWebhookSecret", BaleSecret);
        builder.UseSetting("Messengers:TelegramGatewaySecret", GatewaySecret);
        builder.UseSetting("Notify:BaleBotUsername", "karnama_test_bot");
        builder.UseSetting("Cctv:HookAddresses:0", "10.66.0.2");
        builder.UseSetting("Notify:TelegramArchiveChatId", ArchiveChat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<Karnama.Infrastructure.Notify.IMessengerSender>();
            s.AddSingleton<Karnama.Infrastructure.Notify.IMessengerSender>(Messenger);
            s.RemoveAll<Karnama.Application.Abstractions.IBotGateway>();
            s.AddSingleton<Karnama.Application.Abstractions.IBotGateway>(Bots);
            s.RemoveAll<Karnama.Application.Abstractions.IMediaServer>();
            s.AddSingleton<Karnama.Application.Abstractions.IMediaServer>(Media);
            s.RemoveAll<ISmsSender>();
            s.AddSingleton<ISmsSender>(Sms);
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
            if (tokensOnFakeClock)
                s.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                    Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, o =>
                        o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, p) =>
                        {
                            var now = Clock.GetUtcNow().UtcDateTime;
                            return (notBefore is null || notBefore <= now + p.ClockSkew) && (expires is null || expires >= now - p.ClockSkew);
                        });
        });
    }
}

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>(Json));

    public static HttpClient WithToken(this HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public static async Task<string> DemoTokenAsync(this KarnamaFactory f, string role)
    {
        var r = await f.CreateClient().PostAsJsonAsync("/api/v1/auth/demo", new { role });
        r.EnsureSuccessStatusCode();
        return (await r.JsonAsync()).GetProperty("accessToken").GetString()!;
    }

    /// <summary>
    /// A real (non-demo, non-owner) staff user with the given roles, signed in through OTP as the Android client,
    /// so the refresh token comes back in the body. Each phone can only be used once per factory.
    /// </summary>
    public static async Task<(Guid UserId, string Access, string Refresh)> StaffLoginAsync(this KarnamaFactory f, string phone, params Guid[] roleIds)
    {
        Guid userId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Karnama.Infrastructure.Persistence.AppDbContext>();
            var user = Karnama.Domain.Identity.User.Create(phone, "کاربر " + phone[^4..], Karnama.Domain.Identity.UserKind.Staff).Value;
            foreach (var id in roleIds) user.AssignRole(id);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
        }
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Client", "android");
        var req = await c.PostAsJsonAsync("/api/v1/auth/otp/request", new { phone });
        req.EnsureSuccessStatusCode();
        var challengeId = (await req.JsonAsync()).GetProperty("challengeId").GetGuid();
        var code = await f.Sms.WaitForCodeAsync(phone);
        var verify = await c.PostAsJsonAsync("/api/v1/auth/otp/verify", new { challengeId, code });
        verify.EnsureSuccessStatusCode();
        var session = (await verify.JsonAsync()).GetProperty("session");
        return (userId, session.GetProperty("accessToken").GetString()!, session.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>Android-style refresh; null when the API refuses (401).</summary>
    public static async Task<(string Access, string Refresh)?> RefreshAsync(this KarnamaFactory f, string refresh)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Client", "android");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("X-Refresh-Token", refresh);
        var r = await c.SendAsync(req);
        if (!r.IsSuccessStatusCode) return null;
        var body = await r.JsonAsync();
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    /// <summary>Creates a role through the API (as the demo admin) and returns its id.</summary>
    public static async Task<Guid> CreateRoleAsync(this HttpClient admin, string name, params (string Code, string Scope)[] grants)
    {
        var r = await admin.PostAsJsonAsync("/api/v1/roles", new
        {
            name = new Dictionary<string, string> { ["fa"] = name },
            permissions = grants.Select(g => new { code = g.Code, scope = g.Scope }).ToArray(),
        }, Json);
        r.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.JsonAsync()).GetProperty("id").GetGuid();
    }
}

/// <summary>One database + one running app per test class (xUnit creates a new class instance per test).</summary>
public class AppFixture(bool demoLogin = true, string seedMode = "Demo", bool tokensOnFakeClock = false) : IAsyncLifetime
{
    public const string OwnerPhone = "09121112233";
    public PostgresFixture Pg { get; } = new();
    public KarnamaFactory Factory { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await Pg.InitializeAsync();
        Factory = new KarnamaFactory(Pg.ConnectionString, demoLogin, seedMode, tokensOnFakeClock: tokensOnFakeClock);
        _ = Factory.Server;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Pg.DisposeAsync();
    }
}

public class DemoAppFixture : AppFixture
{
    public DemoAppFixture() : this(false) { }
    protected DemoAppFixture(bool tokensOnFakeClock) : base(tokensOnFakeClock: tokensOnFakeClock) { }


    /// <summary>Setup is refused in demo installs, so the test owner is written straight to the database.</summary>
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Karnama.Infrastructure.Persistence.AppDbContext>();
        var ownerRole = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Roles, r => r.SystemKey == "owner");
        var user = Karnama.Domain.Identity.User.Create(OwnerPhone, "مالک تست", Karnama.Domain.Identity.UserKind.Staff).Value;
        user.AssignRole(ownerRole.Id);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}

/// <summary>Demo install whose tokens are checked against the fake clock (for tests that move time by hours or days).</summary>
public sealed class FakeClockDemoAppFixture() : DemoAppFixture(tokensOnFakeClock: true);

public sealed class EmptyAppFixture() : AppFixture(demoLogin: false, seedMode: "Empty");
