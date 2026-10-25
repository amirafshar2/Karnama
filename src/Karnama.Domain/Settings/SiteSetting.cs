namespace Karnama.Domain.Settings;

/// <summary>One white-label settings section (brand, theme, contact, ...) as a JSON document.</summary>
public sealed class SiteSetting
{
    private SiteSetting() { Key = string.Empty; Json = "{}"; }

    public SiteSetting(string key, string json)
    {
        Key = key;
        Json = json;
    }

    public string Key { get; private set; }
    public string Json { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public uint Version { get; private set; }

    public void Replace(string json, Guid? actor, DateTimeOffset now)
    {
        Json = json;
        UpdatedBy = actor;
        UpdatedAt = now;
    }
}
