# Karnama — Arbeitsmanagement für Social-Media-Agenturen

**Web-App + Android-App**, mit denen eine Social-Media-Agentur ihren ganzen Arbeitstag an einem Ort organisiert: Kundenaufträge, Aufgaben des Teams, Arbeitszeit, Abwesenheit und Urlaub, Verkaufspakete, Veröffentlichungsplan, Vertrieb (CRM) und Kundenportal. Alle Änderungen erscheinen sofort live bei allen Beteiligten.

> 🇮🇷 Die Oberfläche ist auf Persisch (RTL, persischer Kalender) und vollständig mehrsprachig. Eine persische Projektbeschreibung steht in [`README.fa.md`](README.fa.md).

---

## Entstanden mit Vibe Coding

Dieses Projekt ist mit **Vibe Coding** entstanden – es ist mein **viertes Vibe-Coding-Projekt**.

Vibe Coding heißt hier: Ich beschreibe als Entwickler, *was* gebaut werden soll und *warum*, und eine KI schreibt große Teile des Codes. Meine Rolle:

- **Anforderungen und Fachlogik** festlegen (Prompts mit Stufen M1–M10, N1–N12, Q0–Q7 in `docs/`)
- **Architektur- und Sicherheitsregeln** vorgeben (Clean Architecture, OWASP-Checklisten in `docs/SKILLS.md` und `docs/SECURITY_SKILLS.md`)
- **jede Stufe prüfen**: Tests grün, Build grün, Screenshots ansehen, Sicherheitsreview, erst dann die nächste Stufe
- **testen wie ein echter Nutzer**: Demo, Android-Emulator, Lasttest mit simulierten Nutzern
- Entscheidungen treffen, wenn etwas unklar ist (offene Fragen stehen in `docs/PROJECT_MAP.md`)

Was ich dabei gelernt habe: Mit KI wird man nicht schneller, wenn man weniger nachdenkt, sondern wenn man **präziser beschreibt und strenger prüft**. Die meisten echten Fehler (siehe unten) wurden erst durch systematische Tests sichtbar, nicht beim Schreiben.

---

## Funktionen

| Bereich | Was es kann |
|---|---|
| **Aufträge & Kanban** | Kunde bestellt über ein dynamisches Formular → Manager nimmt an → das System schlägt die passende Person vor (Rolle + Auslastung) → Live-Kanban mit Prüfschleife und Kundenfreigabe |
| **Arbeitszeit** | Ein-/Ausstempeln per wechselndem QR-Code am Büro-Tablet (alle 30 s neu), GPS mit Erkennung gefälschter Standorte, Fingerabdruck-/Gesichtsgeräte (ADMS-Protokoll), Timer pro Aufgabe, Offline-Warteschlange |
| **Abwesenheit & Urlaub** | Automatische Erkennung von Fehlzeiten und Verspätung, Begründung durch den Mitarbeiter, Urlaub tage- oder stundenweise, Monatsbericht für die Lohnabrechnung (Excel) |
| **Pakete & Kundenportal** | Verkaufspakete mit Kontingenten und Laufzeit, Kunde sieht Restlaufzeit, Fortschritt und gibt Inhalte frei; Erinnerung vor Paketende |
| **Telegram-Bot** | Mitarbeiter schicken Videos direkt an den Bot; Manager leitet sie per Klick weiter – die Datei läuft über Telegram, nicht über den Server |
| **CRM** | Leads aus Website-Formular, Verteilung im Rotationsprinzip, Anrufprotokoll mit Pflicht-Ergebnis, Wiedervorlagen |
| **Bewertung & Berichte** | Arbeitsbericht und Punktzahl pro Person (mit Erklärung, woher jeder Punkt kommt) für beliebige Zeiträume |
| **Ausstattung** | Kameras und Laptops mit Foto bei Übergabe und Rückgabe, Bestätigung durch den Empfänger |
| **Website & Blog** | Öffentliche, SEO-optimierte Website (SSR), Blog mit Redaktions-Workflow, Referenzen, Beratungsformular |
| **Verwaltung** | Dynamische Rollen mit Berechtigungs-Umfang (eigene / zugewiesene / alle), Marke & Farben, alle Texte und Sprachen im Panel editierbar |

## Technik

| Schicht | Technologien |
|---|---|
| Backend | **ASP.NET Core (.NET 10)**, Clean Architecture (Domain / Application / Infrastructure / Api), EF Core + **PostgreSQL 16**, HybridCache + Redis, SignalR (Live), Hangfire (geplante Jobs), FluentValidation, Serilog, OpenTelemetry |
| Frontend | **Next.js 16**, React 19, TypeScript, Tailwind CSS 4, RTL, PWA; UI-Komponenten aus 21st.dev |
| Android | **Capacitor 8** – dieselbe Codebasis als statischer Export, sichere Token-Ablage im Android Keystore |
| Betrieb | Docker Compose, Caddy (HTTPS, HTTP/3), tägliches Backup mit monatlichem Restore-Test; Demo-Blueprint für Render |

```
src/
  Karnama.Domain           Entitäten und Geschäftsregeln (z. B. Zustandsautomaten für Aufträge)
  Karnama.Application      Anwendungsfälle, DTOs, Validierung
  Karnama.Infrastructure   EF Core, Cache, SMS, Speicher, Tresor (verschlüsselte Zugangsdaten)
  Karnama.Api              Minimal-API-Endpunkte, Auth, SignalR-Hub
  Karnama.Worker           Geplante Jobs und Versand (Outbox)
  Karnama.TelegramGateway  Signierter Proxy zur Telegram-Bot-API
  karnama-web              Website + Panel + Android-App (Next.js)
mobile/                    Android-Projekt (Capacitor)
tests/                     Unit-, Integrations-, E2E-, Crowd- und Lasttests
```

## Sicherheit (Auszug)

- Kurzlebige JWT-Access-Tokens; Refresh-Token im `HttpOnly; Secure; SameSite=Strict`-Cookie mit Rotation und Wiederverwendungs-Erkennung
- Entzogene Rechte wirken sofort (Security-Stamp im Token, nicht erst nach Ablauf)
- Rollen können nicht über die eigenen Rechte hinaus vergeben werden
- Rate Limiting für Login, OTP und öffentliche Formulare; Datei-Uploads per Magic Bytes geprüft
- Zugangsdaten der Kunden mit AES-GCM verschlüsselt; jedes Anzeigen wird protokolliert
- Unveränderliches Audit-Log; keine Schlüssel im Repository
- Sicherheitsreview pro Stufe in [`docs/security/`](docs/security/)

## Qualität und Tests

| Test | Umfang |
|---|---|
| Unit- & Integrationstests | **210** xUnit-Tests gegen echtes PostgreSQL (keine In-Memory-DB), inkl. Nebenläufigkeit (zwei Manager gleichzeitig, doppelter Klick, doppelte Callbacks) |
| Visuelle E2E-Tests | Playwright auf Desktop, Pixel 7 und 360 px, plus simuliertes 3G |
| Szenario-Tests (Q1) | 14 Szenarien eines Agenturtags mit simulierter Uhr – **198/198** Prüfungen grün, 9 Datenbank-Invarianten |
| Crowd-Test (Q2) | **83 Personen gleichzeitig** einen ganzen Arbeitstag lang: API-Akteure plus 5 echte Browser (Desktop, Handy, Kiosk-Tablet, Android-App) – zwei Läufe in Folge ohne Fehler |

Der Crowd-Test hat Fehler gefunden, die nur unter gleichzeitiger Last auftreten – z. B. wurden Nutzer abgemeldet, wenn eine Seite während der Token-Erneuerung neu geladen wurde, und ein abgebrochener Request konnte allen wartenden Requests einen Fehler 500 liefern. Details und Zahlen: [`docs/qa/CROWD_RUN.md`](docs/qa/CROWD_RUN.md), alle Funde: [`docs/qa/ISSUES.md`](docs/qa/ISSUES.md).

## Schnellstart (lokal)

Voraussetzungen: .NET 10 SDK, Node.js 22, Docker (PostgreSQL + Redis), Python 3.

```bash
python tools/new-env.py            # .env mit zufälligen Schlüsseln anlegen
docker compose up -d               # PostgreSQL + Redis
start-dev.cmd                      # Demo mit Beispieldaten und Schnell-Login pro Rolle (Windows)
dotnet test                        # Tests
```

Android: `cd mobile && npx cap sync android && npx cap open android`

**Demo online:** Das Blueprint [`render.yaml`](render.yaml) startet eine Demo-Instanz (Beispieldaten, Login per Klick je Rolle) auf Render – Anleitung in [`deploy/RENDER.md`](deploy/RENDER.md). Für den echten Betrieb gibt es Docker Compose mit Caddy: [`deploy/README.md`](deploy/README.md).

## Dokumentation

- [`docs/PROJECT_MAP.md`](docs/PROJECT_MAP.md) – Stand, Architektur, Entscheidungen, nächste Schritte
- [`docs/SKILLS.md`](docs/SKILLS.md) und [`docs/SECURITY_SKILLS.md`](docs/SECURITY_SKILLS.md) – verbindliche Standards
- [`docs/qa/`](docs/qa/) – Testplan, Abdeckung, Crowd-Test, gefundene Fehler

---

Entwickelt von **Amir Reza Afshar** – im Rahmen meiner Umschulung zum Fachinformatiker für Anwendungsentwicklung.
