using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Karnama.Infrastructure;

/// <summary>
/// Hosts such as Render hand out the database as a URL (<c>postgresql://user:pass@host:5432/db</c>); Npgsql wants
/// <c>Host=…;Username=…</c>. This rewrites a URL-style <c>ConnectionStrings:Default</c> in place, before anything reads it.
/// </summary>
public static class PostgresUrl
{
    public static void Normalize(IConfiguration config)
    {
        var value = config.GetConnectionString("Default");
        if (ToNpgsql(value) is { } converted) config["ConnectionStrings:Default"] = converted;
    }

    /// <summary>Null when the value is not a postgres URL (already in Npgsql form, or empty).</summary>
    public static string? ToNpgsql(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !(value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                                                  || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
            return null;
        var uri = new Uri(value);
        var user = uri.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
            // Internal host names (same data centre) have no TLS; external ones do. Prefer works for both.
            SslMode = SslMode.Prefer,
        };
        return b.ConnectionString;
    }
}
