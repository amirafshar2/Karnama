import { expect, test } from "@playwright/test";
import { demoLogin, menuHas, navTo, shot } from "./helpers";

test.describe("N5: files inbox (files sent to the Telegram bot)", () => {
  test("admin sees files from the bot and assigns one to a worker", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "media");
    const list = page.getByTestId("media-list");
    await expect(list).toContainText("Rohmaterial vom Dreh für die Herbstkarte des Cafés");
    await shot(page, info, "n5-inbox");

    await page.getByTestId("media-filter-all").click();
    await expect(list).toContainText("Übergeben");

    const card = list.locator("li").filter({ hasText: "Kampagnen-Briefing Klinik" });
    await card.getByTestId("media-assign").click();
    const person = page.getByTestId("media-person");
    const option = person.locator("option", { hasText: "Lukas" }).first();
    await person.selectOption((await option.getAttribute("value"))!);
    await shot(page, info, "n5-assign");
    await page.getByTestId("media-assign-sheet-save").click();
    await expect(page.getByText("Zugewiesen; der Bot schickt die Datei.")).toBeVisible();
    await expect(card).toContainText("Zugewiesen an");
    await expect(card.getByTestId("media-status")).toHaveText("Wird gesendet");
  });

  test("a worker has no files inbox", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "editor");
    expect(await menuHas(page, mobile, "media")).toBe(false);
  });
});
