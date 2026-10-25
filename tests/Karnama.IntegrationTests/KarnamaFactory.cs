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
}

public class KarnamaFactory(string connectionString, bool demoLogin = true, string seedMode = "Demo", string environment = "Development")
    : WebApplicationFactory<Program>
{
    public const string SetupToken = "test-setup-token-123456";

    public CapturingSms Sms { get; } = new();
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

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
        builder.ConfigureServices(s =>
        {
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
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var setup = await Factory.CreateClient().PostAsJsonAsync("/api/v1/setup",
            new { token = KarnamaFactory.SetupToken, phone = OwnerPhone, displayName = "مالک تست" });
        setup.EnsureSuccessStatusCode();
    }
}

public sealed class EmptyAppFixture() : AppFixture(demoLogin: false, seedMode: "Empty");
