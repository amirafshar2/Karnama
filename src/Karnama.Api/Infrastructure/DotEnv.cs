namespace Karnama.Api.Infrastructure;

/// <summary>
/// Development convenience: loads the repository's <c>.env</c> into environment variables
/// (never overriding variables that are already set). Production uses real environment variables.
/// </summary>
internal static class DotEnv
{
    public static void LoadForDevelopment(string startDirectory)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        // Only when Development is explicit: an unset variable means Production to ASP.NET Core.
        if (!string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)) return;
        if (Environment.GetEnvironmentVariable("KARNAMA_SKIP_DOTENV") == "1") return;

        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".env"))) dir = dir.Parent;
        if (dir is null) return;

        foreach (var raw in File.ReadAllLines(Path.Combine(dir.FullName, ".env")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..];
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0) value = value[..comment];
            value = value.Trim().Trim('"');
            if (value.Length == 0 || key.StartsWith("NEXT_PUBLIC_", StringComparison.Ordinal)) continue;
            if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
        }
    }
}
