using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Text;
using Karnama.Api.Endpoints;
using Karnama.Api.Infrastructure;
using Karnama.Api.Realtime;
using Karnama.Application;
using Karnama.Application.Abstractions;
using Karnama.Infrastructure;
using Karnama.Infrastructure.Persistence;
using Karnama.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using Serilog;

DotEnv.LoadForDevelopment(Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Host.UseSerilog((ctx, sp, log) =>
{
    log.ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
    var seq = ctx.Configuration["Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seq)) log.WriteTo.Seq(seq);
});

builder.Services.AddInfrastructure(config, builder.Environment);
builder.Services.AddApplication();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// --- Authentication (JWT, 15 minutes) ---
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
builder.Services.AddAuthentication("Bearer").AddJwtBearer("Bearer", o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = config["Jwt:Issuer"] ?? "karnama",
        ValidAudience = config["Jwt:Audience"] ?? "karnama",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:SigningKey"]!)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = JwtRegisteredClaimNames.Sub,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
    // QA mode: tokens are issued on the movable QA clock, so they are checked against the same clock.
    if (config.GetValue<bool>("Qa:Enabled"))
        o.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, p) =>
        {
            var now = (o.TimeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            return (notBefore is null || notBefore <= now + p.ClockSkew) && (expires is null || expires >= now - p.ClockSkew);
        };
    o.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        // Browsers can't set headers on WebSockets: accept the token from the query string for hubs only.
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
            return Task.CompletedTask;
        },
        // Removed roles, edited role grants and deactivation take effect at once: the token's security stamp must
        // still be the user's current one (cached ≤ 30 s, evicted on change). Otherwise 401 → the client refreshes.
        OnTokenValidated = async ctx =>
        {
            var principal = ctx.Principal!;
            if (!Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)) { ctx.Fail("invalid subject"); return; }
            var tokenStamp = int.TryParse(principal.FindFirst(Karnama.Application.Auth.SessionStamps.ClaimType)?.Value, out var s) ? s : 0;
            var state = await ctx.HttpContext.RequestServices.GetRequiredService<Karnama.Application.Auth.SessionStamps>()
                .GetAsync(userId, ctx.HttpContext.RequestAborted);
            if (state is null || !state.Active || state.Stamp != tokenStamp) ctx.Fail("session changed");
        },
    };
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

// --- CORS: whitelist only (web + Capacitor app origin) ---
var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins)
    .AllowCredentials()
    .WithHeaders("Authorization", "Content-Type", "Accept-Language", "If-None-Match", "If-Match", "X-Client", "X-Refresh-Token", "Idempotency-Key", "Upload-Offset", "X-Requested-With", "x-signalr-user-agent")
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
    .WithExposedHeaders("ETag", "Retry-After")
    .SetPreflightMaxAge(TimeSpan.FromHours(1))));

builder.Services.AddKarnamaRateLimits(config);
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddOutputCache(o =>
{
    o.AddPolicy(OutputCachePolicies.Public, p => p.Expire(TimeSpan.FromSeconds(60)).Tag("public"));
    // Public site reads (blog, home, packages, portfolio): the Next server asks often; 30s here, evicted on every content change.
    o.AddPolicy(PublicCache.Policy, p => p.Expire(TimeSpan.FromSeconds(30)).Tag(PublicCache.Tag));
});
builder.Services.AddSingleton<Karnama.Application.Abstractions.IPublicCache, PublicCache>();

// --- Realtime ---
var signalR = builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 32 * 1024).AddMessagePackProtocol();
var redis = config.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis)) signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("karnama"));

// --- Health ---
var health = builder.Services.AddHealthChecks()
    .AddNpgSql(config.GetConnectionString("Default")!, name: "postgres", tags: ["ready"]);
if (!string.IsNullOrWhiteSpace(redis)) health.AddRedis(redis, name: "redis", tags: ["ready"]);

builder.Services.AddOpenTelemetry().WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
if (builder.Environment.IsDevelopment()) builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var proxies = config.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
    var networks = config.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
    // Only replace the safe default (loopback) when real proxies are configured: with both lists empty, ASP.NET would
    // trust X-Forwarded-For from anyone, and every IP gate (office network, devices, camera hook) could be spoofed.
    if (proxies.Length + networks.Length == 0) return;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var proxy in proxies) o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    foreach (var network in networks) o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

var app = builder.Build();
var demoLogin = config.GetValue<bool>("DemoLogin:Enabled");

// --- Database: auto-migrate only where allowed (never in Production), then seed ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (config.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment()))
        await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(CancellationToken.None);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseKarnamaSecurityHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseSerilogRequestLogging(o =>
{
    // Path only: query strings (e.g. hub access tokens) are never logged, nor signed download tokens in the path.
    o.MessageTemplate = "HTTP {RequestMethod} {SafePath} responded {StatusCode} in {Elapsed:0} ms";
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        var path = http.Request.Path.Value ?? "";
        diag.Set("SafePath", path.StartsWith("/api/v1/files/dl/", StringComparison.Ordinal) ? "/api/v1/files/dl/***" : path);
    };
});
app.UseResponseCompression();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseOutputCache();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapAuth(demoLogin);
app.MapMe();
app.MapRoles();
app.MapI18n();
app.MapSetup();
app.MapPublic(demoLogin);
app.MapSettings();
app.MapCatalog();
app.MapWork();
app.MapAttendance();
app.MapPortal();
app.MapLeave();
app.MapNotify();
app.MapBlog();
app.MapSite();
app.MapCredentials();
app.MapMedia();
app.MapChat();
app.MapCrm();
app.MapReports();
app.MapAssets();
app.MapHelp();
app.MapDevices();
app.MapCctv();
if (config.GetValue<bool>("Qa:Enabled")) app.MapQa();
app.MapHub<NotificationHub>("/hubs/notifications");

if (demoLogin) app.Logger.LogWarning("Demo login is ENABLED. Never enable it in Production.");

await app.RunAsync();

public partial class Program;
