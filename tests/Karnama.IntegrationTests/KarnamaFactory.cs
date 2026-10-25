using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public class KarnamaFactory(string connectionString, bool demoLogin = true, string seedMode = "Demo", string environment = "Development")
    : WebApplicationFactory<Program>
{
    public const string SetupToken = "test-setup-token-123456";

    public CapturingSms Sms { get; } = new();
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public CapturingMessenger Messenger { get; } = new();
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
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<Karnama.Infrastructure.Notify.IMessengerSender>();
            s.AddSingleton<Karnama.Infrastructure.Notify.IMessengerSender>(Messenger);
            s.RemoveAll<ISmsSender>();
            s.AddSingleton<ISmsSender>(Sms);
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(Clock);
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
}

/// <summary>One database + one running app per test class (xUnit creates a new class instance per test).</summary>
public class AppFixture(bool demoLogin = true, string seedMode = "Demo") : IAsyncLifetime
{
    public const string OwnerPhone = "09121112233";
    public PostgresFixture Pg { get; } = new();
    public KarnamaFactory Factory { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await Pg.InitializeAsync();
        Factory = new KarnamaFactory(Pg.ConnectionString, demoLogin, seedMode);
        _ = Factory.Server;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Pg.DisposeAsync();
    }
}

public sealed class DemoAppFixture : AppFixture
{
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

public sealed class EmptyAppFixture() : AppFixture(demoLogin: false, seedMode: "Empty");
