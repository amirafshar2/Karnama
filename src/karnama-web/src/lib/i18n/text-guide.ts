/**
 * A plain-Persian guide for the "site texts" editor: for every group of texts, what it is and where the person sees it,
 * written for someone who knows nothing about computers. Keys are grouped by their first part (e.g. "att.*").
 * This is a guide for the editor page only, so it is not itself an editable text.
 */
export type TextSection = {
  /** Short, plain name of the part of the program. */
  name: string;
  /** Where on the screen these texts show up, step by step from the menu. */
  where: string;
  /** Who sees them. */
  who: "Alle" | "Mitarbeiter" | "Manager" | "Kunden" | "Website-Besucher";
  /** A page to open and look at the texts in place (panel or public site). */
  href?: string;
};

export const TEXT_SECTIONS: Record<string, TextSection> = {
  // ---------- everywhere ----------
  common: { name: "Buttons und Wörter, die überall vorkommen", where: "Wörter, die auf vielen Seiten vorkommen, z. B. „Speichern“, „Abbrechen“, „Löschen“ und „Wird gespeichert…“. Ändern Sie eines, ändert es sich überall.", who: "Alle" },
  crud: { name: "Listen und Tabellen", where: "Oben über jeder Liste (z. B. Kunden- oder Mitarbeiterliste): Suchfeld, Button „Excel-Export“, „Gelöschte anzeigen“ und die Meldung „Nichts gefunden“.", who: "Mitarbeiter" },
  nav: { name: "Seiten- und unteres Menü", where: "Name jedes Bereichs im Menü links (am Computer) oder in der unteren Leiste (am Handy), z. B. „Aufgabenboard“ oder „Kunden“.", who: "Alle", href: "/dashboard" },
  brand: { name: "Name und Slogan der Agentur", where: "Name der Agentur und der kurze Satz darunter; auf der Anmeldeseite, oben im Menü und im Titel des Browserfensters. (Den Hauptnamen ändern Sie unter „Marke & Design“.)", who: "Alle", href: "/settings/brand" },
  demo: { name: "Orange Leiste im Demo-Modus", where: "Die orange Leiste oben auf der Seite, die darauf hinweist, dass die Daten Beispiele sind, und der Button „Rolle wechseln“. Nur in der Demo-Version sichtbar.", who: "Alle" },
  notFound: { name: "Seite „Nicht gefunden“", where: "Diese Texte sieht jemand, der eine falsche Adresse aufruft.", who: "Alle" },
  bell: { name: "Benachrichtigungsglocke", where: "Das Glockensymbol oben auf der Seite und das Fenster, das sich beim Antippen öffnet (Liste der neuen Nachrichten).", who: "Alle", href: "/dashboard" },
  realtime: { name: "Meldung zur Berechtigungsänderung", where: "Die kleine Meldung unten auf der Seite, wenn der Manager die Berechtigungen einer Person ändert.", who: "Mitarbeiter" },
  pwa: { name: "Anleitung zur Installation auf dem iPhone", where: "Das Fenster, das auf dem iPhone zeigt, wie man die Website wie eine App auf den Home-Bildschirm legt.", who: "Alle" },
  tour: { name: "Schritt-für-Schritt-Anleitung für die erste Anmeldung", where: "Hilfeblasen, die nach der ersten Anmeldung nacheinander zeigen, wozu jeder Bereich dient.", who: "Alle" },
  glossary: { name: "Bedeutung der Fachbegriffe", where: "Kurze Erklärung von Begriffen wie „Paket“ und „Kontingent“, die in der Hilfe und neben einigen Feldern angezeigt wird.", who: "Alle", href: "/help" },
  help: { name: "Hilfe", where: "Die Seite „Hilfe“ im Menü und das Fenster, das sich mit dem Fragezeichen-Button (?) oben auf jeder Seite öffnet.", who: "Alle", href: "/help" },

  // ---------- sign in & account ----------
  login: { name: "Anmeldeseite", where: "Erste Seite: Feld für die Handynummer, SMS-Code, Anmelde-Button und Fehlermeldungen bei der Anmeldung.", who: "Alle", href: "/login" },
  dashboard: { name: "Erste Seite nach der Anmeldung", where: "Menü → „Dashboard“: Begrüßung, heutiges Datum und Übersicht der Aufgaben.", who: "Mitarbeiter", href: "/dashboard" },
  onboarding: { name: "Karte Schritt-für-Schritt-Einrichtung", where: "Eine Karte im Dashboard des Managers, die die ersten Einrichtungsschritte zeigt (z. B. „Fügen Sie den ersten Kunden hinzu“).", who: "Manager", href: "/dashboard" },
  profile: { name: "Mein Konto", where: "Menü → „Mein Konto“: Sprache, helle oder dunkle Darstellung und persönliche Daten.", who: "Alle", href: "/profile" },
  notifyPrefs: { name: "Benachrichtigungen einstellen", where: "Menü → „Mein Konto“ → Bereich „Benachrichtigungen“: wählen, ob Nachrichten auch per Telegram oder SMS kommen sollen.", who: "Alle", href: "/profile#notifications" },
  setup: { name: "Ersteinrichtung und Anwesenheit einrichten", where: "An zwei Stellen: (1) die erste Seite, die nur einmal bei der Installation erscheint, (2) Menü → „Anwesenheit einrichten“ (Arbeitszeiten, Arbeitstage, Urlaubsarten).", who: "Manager", href: "/attendance/setup" },

  // ---------- daily work ----------
  work: { name: "Aufgabenboard", where: "Menü → „Aufgabenboard“: Spalten (Ausstehend, In Arbeit, Prüfung…), die Karte jeder Aufgabe und das Fenster, das sich beim Tippen auf die Karte öffnet.", who: "Mitarbeiter", href: "/work" },
  orders: { name: "Aufträge", where: "Menü → „Aufträge“: Auftragsliste, Button „Neuer Auftrag“ und Bestellformular.", who: "Mitarbeiter", href: "/orders" },
  stars: { name: "Bewertungssterne", where: "Sterne von 1 bis 5, die Manager oder Kunde nach Abschluss einer Aufgabe vergeben (Aufgabenfenster und Kundenseite).", who: "Alle" },
  publish: { name: "Veröffentlichungskalender", where: "Menü → „Veröffentlichungskalender“: welcher Post an welchem Tag auf welcher Seite erscheint, und Erinnerungen.", who: "Mitarbeiter", href: "/publishing" },
  chat: { name: "Chat", where: "Menü → „Chat“: Liste der Chats, Eingabefeld für Nachrichten und der Button „Neuer Chat“.", who: "Alle", href: "/chat" },
  media: { name: "Dateieingang", where: "Menü → „Dateieingang“: Dateien, die Kollegen über den Telegram-Bot geschickt haben, und der Button zum Weiterleiten an die nächste Person.", who: "Manager", href: "/media" },
  crm: { name: "Vertrieb & Leads", where: "Menü → „Vertrieb & Leads“: potenzielle Kunden, Stufen, Erfassen des Anrufergebnisses und Vertriebsbericht.", who: "Mitarbeiter", href: "/crm" },
  as: { name: "Ausstattung", where: "Menü → „Ausstattung“: Laptops und Kameras, Übergabe an Kollegen, Fotos bei Übergabe und Rückgabe.", who: "Mitarbeiter", href: "/assets" },

  // ---------- team & attendance ----------
  att: { name: "Anwesenheit & Zeiten (Kommen und Gehen)", where: "Menü → „Anwesenheit & Zeiten“: großer Kommen/Gehen-Button, Aufgaben-Timer und die heutigen Zeiten.", who: "Mitarbeiter", href: "/attendance" },
  kiosk: { name: "Büro-QR-Code", where: "Menü → „Büro-QR“: die Seite, die auf dem Tablet am Büroeingang offen ist und deren Code die Kollegen scannen.", who: "Mitarbeiter", href: "/attendance/kiosk" },
  leave: { name: "Urlaub", where: "Menü → „Anwesenheit & Zeiten“ → Bereich Urlaub: Urlaubsantrag und die Seite, auf der der Manager Urlaub genehmigt oder ablehnt.", who: "Mitarbeiter", href: "/attendance" },
  abs: { name: "Abwesenheiten", where: "Menü → „Anwesenheit & Zeiten“: die Meldung „Kommen nicht erfasst“ und das Feld, in das der Kollege den Grund seiner Abwesenheit schreibt; sowie die Abwesenheitsliste für den Manager.", who: "Mitarbeiter", href: "/attendance" },
  cal: { name: "Anwesenheitskalender", where: "Menü → „Anwesenheitskalender“: Monatstabelle, jedes Feld ein Tag (anwesend, verspätet, Urlaub, abwesend).", who: "Mitarbeiter", href: "/attendance/calendar" },
  live: { name: "Team live", where: "Menü → „Team live“: wer gerade arbeitet und an welcher Aufgabe.", who: "Manager", href: "/attendance/live" },
  rep: { name: "Anwesenheitsbericht (für die Lohnabrechnung)", where: "Menü → „Anwesenheitsbericht“: Tabelle mit Arbeitszeit, Verspätung, früherem Gehen und Urlaub jeder Person in einem Zeitraum.", who: "Manager", href: "/attendance/report" },
  rpt: { name: "Arbeitsbericht", where: "Menü → „Arbeitsbericht & Punkte“: produktive Stunden, erledigte Aufgaben, Tagesdiagramm.", who: "Mitarbeiter", href: "/reports" },
  score: { name: "Punkte der Kollegen", where: "Menü → „Arbeitsbericht & Punkte“ → Tab Punkte: Punktzahl jeder Person und woher sie kommt.", who: "Mitarbeiter", href: "/reports" },
  dev: { name: "Fingerabdruckgerät", where: "Menü → „Zeiterfassungsgeräte“: Fingerabdruck- oder Gesichtserkennungsgerät des Büros verbinden.", who: "Manager", href: "/attendance/devices" },
  cctv: { name: "Bürokameras", where: "Menü → „Kameras“: Bild der Überwachungskameras im Büro.", who: "Manager", href: "/cctv" },

  // ---------- clients, packages, services ----------
  clients: { name: "Kunden", where: "Menü → „Kunden“: Liste der Kundenunternehmen, Akte jedes Kunden und Formular für neue Kunden.", who: "Mitarbeiter", href: "/clients" },
  vault: { name: "Passwörter der Kundenkonten", where: "Menü → „Kunden“ → ein Kunde → Bereich „Zugangsdaten der Konten“ (Passwörter der Seiten, mit Schloss). Der Kunde sieht sie auch auf seiner eigenen Seite.", who: "Alle", href: "/clients" },
  packages: { name: "Verkaufspakete", where: "Menü → „Pakete“: jedes Paket mit Preis, Anzahl Posts und Reels, Laufzeit und Beschreibung.", who: "Manager", href: "/packages" },
  notices: { name: "Nachricht zum Paketende", where: "Menü → „Pakete“ → „Nachrichten zum Paketende“: welche Nachricht der Kunde wie viele Tage vor Paketende erhält.", who: "Manager", href: "/packages" },
  services: { name: "Leistungsarten", where: "Menü → „Leistungsarten“: Reel, Post, Story usw. und das Formular, das der Kunde für die jeweilige Bestellung ausfüllt.", who: "Manager", href: "/services" },
  platforms: { name: "Soziale Netzwerke", where: "Menü → „Netzwerke“: Instagram, TikTok und weitere Netzwerke, auf denen Kunden eine Seite haben.", who: "Manager", href: "/settings/platforms" },

  // ---------- client panel ----------
  portal: { name: "Kundenseite", where: "Die Seite, die der Kunde nach der Anmeldung sieht: sein Paket, die verbleibenden Tage, Aufträge und Inhalte zur Freigabe.", who: "Kunden", href: "/dashboard" },
  portalMsg: { name: "Nachricht des Kunden zum Auftrag", where: "Auf der Kundenseite → ein Auftrag → Feld „Nachricht an die Agentur“.", who: "Kunden" },
  portalPub: { name: "Posts, die der Kunde selbst veröffentlicht", where: "Auf der Kundenseite → „Posts, die Sie selbst veröffentlichen“: Datei herunterladen und Caption kopieren.", who: "Kunden", href: "/publishing" },
  profileClient: { name: "Unternehmensprofil des Kunden", where: "Auf der Kundenseite → „Profil“: Firmenname, Nummer und Seiten.", who: "Kunden", href: "/profile" },

  // ---------- website & content ----------
  site: { name: "Öffentliche Website", where: "Die Website, die alle sehen (ohne Anmeldung): oberes Menü, Buttons, Fußzeile und Beratungsformular.", who: "Website-Besucher", href: "/" },
  home: { name: "Startseite der Website verwalten", where: "Menü → „Startseite der Website“: Formulare, mit denen Sie Text, Fotos, Video und FAQ der Startseite ändern.", who: "Manager", href: "/content/home" },
  cases: { name: "Referenzen", where: "An zwei Stellen: Menü → „Referenzen“ (Formular zum Anlegen einer Referenz) und die Referenzseite der öffentlichen Website.", who: "Alle", href: "/content/cases" },
  blog: { name: "Blog", where: "Menü → „Blog“: Liste der Beiträge, neuen Beitrag schreiben und zur Prüfung senden.", who: "Mitarbeiter", href: "/content/blog" },
  editor: { name: "Seite zum Schreiben eines Beitrags", where: "Menü → „Blog“ → ein Beitrag: Titelfeld, Text sowie Buttons zum Speichern und Veröffentlichen.", who: "Mitarbeiter", href: "/content/blog" },
  toolbar: { name: "Buttons zur Textformatierung", where: "Buttonleiste über dem Schreibfeld für Beiträge: Überschrift, fett, Liste, Link, Bild.", who: "Mitarbeiter", href: "/content/blog" },
  image: { name: "Bild im Beitrag", where: "Wenn Sie in einem Beitrag oder einer Referenz ein Bild einfügen: die Felder „Alternativtext“ und „Bildunterschrift“.", who: "Mitarbeiter" },
  seo: { name: "Google-Einstellungen (SEO)", where: "Unten auf der Seite zum Schreiben eines Beitrags: Titel und Beschreibung, die im Google-Suchergebnis erscheinen.", who: "Mitarbeiter", href: "/content/blog" },

  // ---------- admin & settings ----------
  users: { name: "Mitarbeiter", where: "Menü → „Mitarbeiter“: Liste der Personen, die mit dem Panel arbeiten, und Formular zum Hinzufügen.", who: "Manager", href: "/users" },
  roles: { name: "Rollen & Berechtigungen", where: "Menü → „Rollen & Berechtigungen“: jede Rolle (z. B. „Cutter“) und was sie darf.", who: "Manager", href: "/roles" },
  perm: { name: "Name jeder Berechtigung", where: "Menü → „Rollen & Berechtigungen“ → eine Rolle: Jede Zeile ist eine Berechtigung, z. B. „Aufgaben ansehen“ oder „Aufgaben an Personen zuweisen“.", who: "Manager", href: "/roles" },
  permGroup: { name: "Berechtigungsgruppen", where: "Menü → „Rollen & Berechtigungen“ → eine Rolle: Überschrift jeder Berechtigungsgruppe, z. B. „Aufgaben“ oder „Kunden“.", who: "Manager", href: "/roles" },
  scope: { name: "Umfang der Berechtigung", where: "Menü → „Rollen & Berechtigungen“ → eine Rolle: Neben jeder Berechtigung wählen Sie „keine“, „nur eigene“ oder „alle“.", who: "Manager", href: "/roles" },
  brandSettings: { name: "Marke & Design", where: "Menü → „Marke & Design“: Agenturname, Logo und Farben von Website und App.", who: "Manager", href: "/settings/brand" },
  texts: { name: "Diese Seite (Website-Texte)", where: "Die Texte genau der Seite, auf der Sie gerade sind.", who: "Manager", href: "/settings/texts" },
  dg: { name: "Demo-Leitfaden", where: "Die Hinweiskarte oben im Dashboard, die nur in der Demo-Version erscheint: für jede Rolle die Dinge, die man ausprobieren sollte.", who: "Alle", href: "/dashboard" },
  appdl: { name: "Download-Seite der Android-App", where: "Menü → „Android-App“: Download-Button, QR-Code und Schritt-für-Schritt-Installationsanleitung.", who: "Alle", href: "/app-download" },
  langs: { name: "Sprachen der App", where: "Diese Seite → Karte „Sprachen der App“ oben: neue Sprache hinzufügen.", who: "Manager", href: "/settings/texts" },
};

/** Fallback for a group that has no entry above (new texts added later). */
export const OTHER_SECTION: TextSection = { name: "Weitere Texte", where: "Texte, die noch keine eigene Erklärung haben. Finden Sie sie über die Suche.", who: "Alle" };

/**
 * What kind of text one key is, from the last part of its name — e.g. "orders.title" is the big heading of a page.
 * Checked in order; the first match wins.
 */
const KINDS: [RegExp, string][] = [
  [/(^|\.)title$/i, "Große Überschrift oben auf der Seite oder im Kasten"],
  [/(^|\.)subtitle$/i, "Erklärender Satz unter der Überschrift"],
  [/placeholder$/i, "Grauer Text im leeren Feld (bis jemand etwas schreibt)"],
  [/help$|hint$/i, "Kleiner Hinweis unter oder neben einem Feld"],
  [/(^|\.)empty/i, "Meldung, die angezeigt wird, solange noch nichts da ist"],
  [/(error|invalid|failed|fail|wrong|denied|forbidden)/i, "Fehlermeldung (wenn etwas nicht geklappt hat)"],
  [/(saved|done|sent|created|deleted|restored|success|copied|updated)$/i, "Kurze Erfolgsmeldung (nach dem Tippen auf den Button)"],
  [/confirm/i, "Rückfrage vor dem Ausführen"],
  [/(^|\.)tab\./i, "Name des Tabs (Reiter oben auf der Seite)"],
  [/(^|\.)(status|state)\./i, "Name eines Status (z. B. „In Arbeit“)"],
  [/(^|\.)(new|add|save|cancel|delete|submit|send|edit|create|open|close|back|next|prev|reset|search|export|restore|approve|reject|accept|assign|start|stop|pause|resume|upload|download|copy|renew|retry|logout|login|more|clear|done|confirmBtn)$/i, "Text eines Buttons"],
  [/(^|\.)group\./i, "Überschrift einer Kategorie im Menü oder in einer Liste"],
  [/label$|(^|\.)(name|phone|email|from|to|date|note|reason)$/i, "Name eines Formularfelds"],
];

export function textKind(key: string): string | null {
  const rest = key.slice(key.indexOf(".") + 1);
  for (const [re, kind] of KINDS) if (re.test(rest)) return kind;
  return null;
}

export const sectionOf = (key: string) => key.split(".")[0];
