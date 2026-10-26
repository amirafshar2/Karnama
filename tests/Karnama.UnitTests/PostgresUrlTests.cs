using FluentAssertions;
using Karnama.Infrastructure;
using Npgsql;

namespace Karnama.UnitTests;

public sealed class PostgresUrlTests
{
    [Fact]
    public void A_render_style_url_becomes_an_npgsql_connection_string()
    {
        var cs = PostgresUrl.ToNpgsql("postgresql://karnama:p%40ss%3Aword@dpg-abc123-a.frankfurt-postgres.render.com:5433/karnama_db");
        var b = new NpgsqlConnectionStringBuilder(cs);
        b.Host.Should().Be("dpg-abc123-a.frankfurt-postgres.render.com");
        b.Port.Should().Be(5433);
        b.Database.Should().Be("karnama_db");
        b.Username.Should().Be("karnama");
        b.Password.Should().Be("p@ss:word");
    }

    [Fact]
    public void An_internal_url_without_a_port_uses_5432()
    {
        new NpgsqlConnectionStringBuilder(PostgresUrl.ToNpgsql("postgres://u:p@dpg-abc123-a/db")).Port.Should().Be(5432);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=localhost;Database=karnama;Username=karnama")]
    public void Anything_else_is_left_alone(string? value) => PostgresUrl.ToNpgsql(value).Should().BeNull();
}
