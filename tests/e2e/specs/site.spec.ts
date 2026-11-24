import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, navTo, shot } from "./helpers";

test.describe("N10: public website (home, packages, portfolio, consultation)", () => {
  test("visitor reads the site and asks for a consultation; the admin sees the request", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await page.goto(`${DEMO_URL}/`);
    await expect(page.getByTestId("home-hero")).toBeVisible();
    await expect(page.locator("#services")).toBeVisible();
    await expect(page.locator("#packages")).toBeVisible();
    await expect(page.locator('section[aria-labelledby="cases"]').getByText("Beispiel", { exact: true }).first()).toBeVisible();
    const ld = JSON.parse((await page.locator('script[type="application/ld+json"]').first().textContent())!);
    const types = ld["@graph"].flatMap((g: { "@type": string | string[] }) => g["@type"]);
    expect(types).toEqual(expect.arrayContaining(["ProfessionalService", "FAQPage", "WebSite"]));
    expect(await page.locator('meta[name="description"]').getAttribute("content")).toBeTruthy();
    await shot(page, info, "n10-home");

    const phone = { desktop: "015120000301", pixel7: "015120000302", phone360: "015120000303" }[info.project.name] ?? "015120000309";
    const form = page.getByTestId("consult-form");
    await form.getByLabel("Ihr Name").fill(`Gast ${info.project.name}`);
    await form.getByLabel("Handynummer").fill(phone);
    await form.getByLabel("Unternehmen (optional)").fill("Bäckerei");
    await form.getByRole("button", { name: "Rückruf anfragen" }).click();
    await expect(page.getByTestId("consult-sent")).toBeVisible();

    await page.goto(`${DEMO_URL}/plans`);
    await page.getByTestId("packages-list").getByRole("link").first().click();
    await expect(page.locator("h1")).toBeVisible();
    await expect(page.getByText("€").first()).toBeVisible();
    await shot(page, info, "n10-package");
    await page.goto(`${DEMO_URL}/cases`);
    await page.getByTestId("cases-list").getByRole("link").first().click();
    await expect(page.getByTestId("case")).toContainText("Beispiel");
    await shot(page, info, "n10-case");

    await demoLogin(page, "admin");
    await navTo(page, mobile, "crm");
    await expect(page.getByTestId("crm-board")).toContainText(`Gast ${info.project.name}`);
    await shot(page, info, "n10-leads");
  });

  test("admin edits the home page and the portfolio; a case without consent can't go live", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "content/home");
    await expect(page.getByTestId("home-editor")).toBeVisible();
    const hero = `Ihre Seite in professionellen Händen (${info.project.name})`;
    await page.getByTestId("home-hero-title").fill(hero);
    await page.getByTestId("home-seo-desc").fill("Kurz");
    await page.getByTestId("home-save").click();
    await expect(page.getByTestId("home-editor").getByRole("alert")).toBeVisible();
    await page.getByTestId("home-seo-desc").fill("Instagram-Betreuung und Content-Produktion für Unternehmen in Stuttgart mit Veröffentlichungsplan, Monatsbericht und eigenem Panel.");
    await page.getByTestId("home-save").click();
    await expect(page.getByText("Startseite gespeichert")).toBeVisible();
    await shot(page, info, "n10-home-editor");

    await navTo(page, mobile, "content/cases");
    await expect(page.getByTestId("cases-admin")).toContainText("Café Beispiel");
    await page.getByTestId("case-new").click();
    await page.getByTestId("case-name").fill(`Echte Konditorei ${info.project.name}`);
    await page.getByTestId("case-fb").fill("800");
    await page.getByTestId("case-fa").fill("2400");
    await shot(page, info, "n10-case-editor");
    await page.getByTestId("case-editor-save").click();
    await expect(page.getByText("Referenz gespeichert.")).toBeVisible();
    const card = page.getByTestId("cases-admin").locator("li", { hasText: `Echte Konditorei ${info.project.name}` });
    await card.getByRole("button", { name: "Auf der Website veröffentlichen" }).click();
    await expect(page.getByText("Ohne schriftliche Einwilligung des Kunden (und deren Datei)")).toBeVisible();

    // The public home follows (cached up to a minute).
    await expect(async () => {
      await page.goto(`${DEMO_URL}/`);
      await expect(page.getByTestId("home-hero")).toHaveText(hero, { timeout: 1000 });
    }).toPass({ timeout: 90_000, intervals: [5000] });
  });
});
