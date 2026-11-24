using System.Text.Json;
using Karnama.Application.Blog;
using Karnama.Domain.Content;
using Karnama.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Karnama.Infrastructure.Seeding;

/// <summary>
/// Starting help-center articles (PROMPT2 §13), in every install: they describe the product, not demo data, and are
/// editable (or removable) in the panel. Only added once — an install that deleted them doesn't get them back.
/// </summary>
internal static class HelpSeed
{
    private sealed record Article(string Title, string Slug, string Category, HelpAudience Audience, string? Page, object[] Blocks);

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.HelpArticles.IgnoreQueryFilters().AnyAsync(ct)) return;
        var order = 0;
        foreach (var a in Articles())
        {
            var json = JsonSerializer.Serialize(new { type = "doc", content = a.Blocks });
            var r = TiptapRenderer.Render(json);
            db.HelpArticles.Add(HelpArticle.Create(new HelpSpec(a.Title, a.Slug, a.Category, a.Audience, a.Page, json, order += 10, true),
                r.Html.ToString(), r.Plain.ToString()).Value!);
        }
        await db.SaveChangesAsync(ct);
    }

    private static object Text(string s) => new { type = "text", text = s };
    private static object P(string s) => new { type = "paragraph", content = new[] { Text(s) } };
    private static object H(string s) => new { type = "heading", attrs = new { level = 2 }, content = new[] { Text(s) } };
    private static object Steps(params string[] items) => new
    {
        type = "orderedList",
        content = items.Select(i => new { type = "listItem", content = new[] { P(i) } }).ToArray(),
    };
    private static object Bullets(params string[] items) => new
    {
        type = "bulletList",
        content = items.Select(i => new { type = "listItem", content = new[] { P(i) } }).ToArray(),
    };
    private static object Tip(string s) => new { type = "callout", attrs = new { tone = "tip" }, content = new[] { P(s) } };

    private static IEnumerable<Article> Articles() =>
    [
        new("Wie ich Kommen und Gehen erfasse", "kommen-und-gehen", "Anwesenheit & Zeiten", HelpAudience.Staff, "attendance",
        [
            P("Tippen Sie jeden Tag zu Arbeitsbeginn auf „Kommen“ und zum Feierabend auf „Gehen“. Arbeitszeit, Verspätung und Überstunden werden daraus berechnet."),
            H("Schritte"),
            Steps("Öffnen Sie im Menü „Anwesenheit & Zeiten“.", "Tippen Sie auf den großen Button „Kommen“.",
                "Falls der Manager es eingestellt hat, prüft die App auch das Büro-WLAN, Ihren Standort oder den Scan des Büro-QR-Codes; tun Sie einfach, was abgefragt wird.",
                "Für Mittagessen oder Pause tippen Sie auf „Pause starten“ und danach auf „Pause beenden“.", "Tippen Sie zum Feierabend auf „Gehen“."),
            Tip("Kein Internet? Ihr Kommen wird auf dem Handy gespeichert und automatisch gesendet, sobald Sie wieder online sind."),
            H("Ich habe das Gehen vergessen"),
            P("Wenn Sie nicht „Gehen“ tippen, wird die Schicht nachts zur Prüfung durch den Manager geschlossen. Tippen Sie auf derselben Seite auf „Zeitkorrektur beantragen“ und tragen Sie die richtige Uhrzeit ein."),
        ]),
        new("Wie ich Dateien und Videos mit dem Telegram-Bot schicke", "dateien-mit-dem-bot-senden", "Dateien", HelpAudience.Staff, "media",
        [
            P("Videos und große Dateien müssen Sie nicht auf der Website hochladen; schicken Sie sie an den Telegram-Bot der Agentur, der sie archiviert und der richtigen Aufgabe zuordnet."),
            H("Beim ersten Mal: Konto verbinden"),
            Steps("Öffnen Sie im Menü „Mein Konto“.", "Tippen Sie auf „Telegram verbinden“ und dann in Telegram auf „Start“.", "Die Meldung „Ihr Konto ist verbunden“ heißt, alles ist bereit."),
            H("Datei senden"),
            Steps("Schicken Sie die Datei wie gewohnt in Telegram an den Bot.", "Der Bot fragt „Für welche Aufgabe ist das?“; wählen Sie die Aufgabe über die Buttons.",
                "Steht die Aufgabe nicht in der Liste, ordnet der Manager sie im „Dateieingang“ der richtigen Aufgabe zu."),
            Tip("Große Dateien sind kein Problem; die Datei wird innerhalb von Telegram verschoben und verbraucht Ihr Datenvolumen nicht erneut."),
        ]),
        new("Wie ich Urlaub nehme", "urlaub-nehmen", "Anwesenheit & Zeiten", HelpAudience.Staff, "attendance/calendar",
        [
            P("Beantragen Sie tageweisen oder stundenweisen Urlaub direkt in der App, damit der Manager ihn dort genehmigt und er in der Lohnabrechnung richtig zählt."),
            Steps("Öffnen Sie „Anwesenheit & Zeiten“ und gehen Sie zum Bereich „Urlaub & Abwesenheit“.", "Tippen Sie auf „Urlaub beantragen“.",
                "Wählen Sie Urlaubsart, tage- oder stundenweise und von wann bis wann, und schreiben Sie kurz den Grund.", "senden. Sobald der Manager genehmigt oder ablehnt, erhalten Sie eine Benachrichtigung."),
            Tip("Im „Anwesenheitskalender“ sehen Sie, an welchen Tagen Sie Urlaub haben und wer aus dem Team fehlt."),
        ]),
        new("Wie ich ein Verkaufspaket anlege", "paket-anlegen", "Kunden und Pakete", HelpAudience.Staff, "packages",
        [
            P("Ein Paket ist ein Monatspaket: einige Posts, Reels oder Storys zu einem festen Preis. Wenn Sie einem Kunden ein Paket zuweisen, wird sein Kontingent automatisch mitgezählt."),
            Steps("Tippen Sie im Menü auf „Pakete“ und dann auf „Neues Paket“.", "Tragen Sie Name, Laufzeit (z. B. 30 Tage) und Preis ein.",
                "Tragen Sie unter „Kontingente“ ein, wie viel von jeder Leistung im Paket ist, z. B. 8 Posts und 4 Reels.", "speichern. Jetzt können Sie dieses Paket auf der Kundenseite für den Kunden aktivieren."),
            P("Einige Tage vor Paketende werden der Kunde und Sie erinnert. Den Zeitpunkt der Erinnerung ändern Sie auf derselben Seite unter „Nachrichten zum Paketende“."),
        ]),
        new("Wie ich Geräte übergebe und zurücknehme", "geraeteuebergabe", "Ausstattung", HelpAudience.Staff, "assets",
        [
            P("Jede Kamera, jeder Laptop und jedes Gerät, das bei Kollegen ist, wird unter „Ausstattung“ erfasst, damit immer klar ist, wo es ist und in welchem Zustand."),
            H("Übergabe"),
            Steps("Öffnen Sie das Gerät (oder lesen Sie sein QR-Etikett mit „Etikett scannen“).", "Tippen Sie auf „An Kollegen übergeben“ und tragen Sie Empfänger, Rückgabedatum, äußeren Zustand und Zubehör ein.",
                "Fotografieren Sie den Zustand des Geräts.", "Der Empfänger tippt in der App auf „Ich habe es übernommen“; bis dahin steht die Übergabe auf „Wartet auf Bestätigung des Empfängers“."),
            H("Rückgabe"),
            Steps("Der Kollege tippt auf „Ich bringe es zurück“ und schreibt dazu, falls etwas kaputt ist oder fehlt.", "Sie sehen das Gerät und tippen auf „Rückgabe erfassen“: im Lager, in Reparatur oder verloren."),
            Tip("Gibt ein Kollege es direkt an einen anderen weiter, tippt er auf „An Kollegen übergeben“; es muss nicht erst ins Lager zurück."),
        ]),
        new("Arbeit auf dem Aufgabenboard", "aufgabenboard", "Aufgaben", HelpAudience.Staff, "work",
        [
            P("Jeder Kundenauftrag wird in mehrere Schritte aufgeteilt, und jeder Schritt ist eine Karte auf dem „Aufgabenboard“."),
            Bullets("„Zu erledigen“: Aufgaben, mit denen man anfangen kann.", "„In Arbeit“: Sobald Sie anfangen, ziehen Sie die Karte hierher und starten den Timer.",
                "„Prüfung durch Admin“ und „Wartet auf Kundenfreigabe“: Die Arbeit ist fertig, Manager oder Kunde müssen sie ansehen.", "„Überarbeitung nötig“: Lesen Sie die Anmerkung im Chat derselben Karte."),
            Tip("Statt die Karte zu ziehen, nutzen Sie am Handy das Menü „Verschieben nach…“ auf der Karte."),
        ]),
        new("Vertrieb & Leads für den Vertrieb", "leads", "Umsatz", HelpAudience.Staff, "crm",
        [
            P("Jeder, der Kunde werden könnte, ist ein „Lead“. Website-Formular und Bot-Nachrichten legen automatisch Leads an."),
            Steps("Öffnen Sie den Lead und tippen Sie auf „Anrufen“; erfassen Sie danach das Ergebnis.", "Legen Sie den Zeitpunkt der nächsten Wiedervorlage fest; genau dann werden Sie erinnert.",
                "Wenn der Vertrag geschlossen ist, tippen Sie auf „In Kunden umwandeln“."),
            Tip("Schauen Sie jeden Morgen in den Tab „Heute“: die Anrufe, die heute fällig sind."),
        ]),
        new("Mein Arbeitsbericht & meine Punkte", "bericht-und-punkte", "Berichte", HelpAudience.Staff, "reports",
        [
            P("Unter „Arbeitsbericht & Punkte“ sehen Sie Ihre Anwesenheitszeit, produktive Zeit und erledigten Aufgaben für jeden Zeitraum."),
            P("Die Punktzahl setzt sich aus sieben Dingen zusammen: Qualität, Tempo, Pünktlichkeit, Arbeitsmenge, Anwesenheit sowie Bewertung von Manager und Kunde. Unter jedem steht, warum Sie diese Zahl bekommen haben und wie Sie sie verbessern."),
            Tip("Pünktlichkeit zählt ab Ihrer ersten Lieferung; gibt der Kunde spät frei, geht das nicht zu Ihren Lasten."),
        ]),
        new("Fingerabdruckgerät verbinden", "fingerabdruckgeraet", "Anwesenheit & Zeiten", HelpAudience.Staff, "attendance/devices",
        [
            P("Hat das Gerät die Option „ADMS“ oder „Cloud Server“, kommen die Kommen-/Gehen-Buchungen automatisch an; sonst importieren Sie den Export seiner Software."),
            Steps("Tippen Sie unter „Zeiterfassungsgeräte“ auf „Neues Gerät“ und tragen Sie die Seriennummer des Geräts ein.",
                "Tragen Sie im Gerätemenü genau die Serveradresse ein, die diese Seite anzeigt.",
                "Ordnen Sie im Tab „Personalnummern“ die Nummer jedes Mitarbeiters am Gerät seinem Namen zu.",
                "Ab jetzt ist jeder Fingerabdruck an diesem Gerät ein Kommen oder Gehen dieser Person."),
            Tip("Fingerabdruck und Gesichtsbild kommen nie auf den Server; nur die Personalnummer und die Uhrzeit."),
        ]),
        new("Kameras ansehen", "kameras", "Büro", HelpAudience.Staff, "cctv",
        [
            P("Unter „Kameras“ wird das Bild jeder Kamera alle 10 Sekunden aktualisiert. Für das Livebild tippen Sie auf die Kamera."),
            P("Das Livebild schließt nach 2 Minuten ohne Aktivität, um das Büro-Internet zu schonen. Jeder Aufruf wird unter Ihrem Namen protokolliert."),
        ]),
        new("Kunde: Wie gebe ich Inhalte frei", "inhaltsfreigabe", "Kundenleitfaden", HelpAudience.Clients, "dashboard",
        [
            P("Sobald das Team etwas für Sie vorbereitet hat, erscheint es oben auf der Startseite unter „Wartet auf Ihre Freigabe“."),
            Steps("Öffnen Sie die Datei und sehen Sie sie an.", "Wenn alles passt, tippen Sie auf „Ich gebe frei“; auf Wunsch können Sie auch Sterne vergeben.",
                "Wenn etwas geändert werden muss, tippen Sie auf „Überarbeitung nötig“ und schreiben Sie genau, was anders sein soll."),
            Tip("Sie haben eine Frage? Schreiben Sie dem Manager direkt im „Chat“."),
        ]),
        new("Kunde: Wann endet mein Paket", "paketende", "Kundenleitfaden", HelpAudience.Clients, null,
        [
            P("Auf der Startseite zeigt die Paketbox, wie viele Tage noch übrig sind und wie viel Sie von jeder Leistung genutzt haben."),
            P("Einige Tage vor Ablauf erhalten Sie eine SMS und eine Benachrichtigung. Zum Verlängern tippen Sie unter „Mein Konto“ auf „Verlängerung anfragen“, dann meldet sich die Agentur bei Ihnen."),
        ]),
    ];
}
