using Hangfire;
using Hangfire.PostgreSql;
using Karnama.Application;
using Karnama.Application.Abstractions;
using Karnama.Infrastructure;
using Karnama.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
DevDotEnv.Load(builder.Environment);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddSerilog((sp, log) => log.ReadFrom.Configuration(builder.Configuration).WriteTo.Console());
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddApplication();
builder.Services.AddScoped<ICurrentUser, SystemUser>();
builder.Services.AddSingleton<IRealtimeNotifier, NoopRealtimeNotifier>();
builder.Services.AddSingleton<IRequestContext, NoRequestContext>();
builder.Services.AddSingleton<IPublicCache, NoopPublicCache>();

builder.Services.AddHangfire(c => c
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default")),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));
builder.Services.AddScoped<MaintenanceJobs>();
builder.Services.AddScoped<AttendanceJobs>();
builder.Services.AddScoped<NotifyJobs>();
builder.Services.AddHostedService<JobRegistrar>();
builder.Services.AddSingleton<Karnama.Infrastructure.Notify.OutboxDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Karnama.Infrastructure.Notify.OutboxDispatcher>());

var host = builder.Build();
await host.RunAsync();
