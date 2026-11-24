namespace Karnama.Application.Auth;

/// <summary>Whitelist of demo accounts reachable through one-click demo login (only in demo installs).</summary>
public static class DemoAccounts
{
    public sealed record Account(string Key, string Phone, string NameFa, string DescriptionFa, string Icon);

    public static IReadOnlyList<Account> All { get; } =
    [
        new("admin", "015100000001", "Admin", "Kunden, Aufgaben, Team und Einstellungen verwalten", "shield"),
        new("editor", "015100000002", "Cutter", "Meine Aufgaben, Timer und Videoabgabe", "clapperboard"),
        new("writer", "015100000003", "Texter", "Captions, Fotos und zugewiesene Texte", "pen-line"),
        new("client", "015100000004", "Kunde", "Aufträge verfolgen und Inhalte freigeben", "store"),
        new("marketer", "015100000008", "Vertrieb", "Leads, Anrufe und Vertriebs-Wiedervorlagen", "phone"),
    ];

    public static Account? Find(string key) => All.FirstOrDefault(a => a.Key == key);
}
