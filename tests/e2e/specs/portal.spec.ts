import { expect, test } from "@playwright/test";
import { demoLogin, shot } from "./helpers";

test.describe("client portal (demo version)", () => {
  test("client sees the package, asks for a change, and places an order", async ({ page }, info) => {
    await demoLogin(page, "client");
    await expect(page.getByTestId("portal")).toContainText("Café Beispiel");
    await shot(page, info, "portal-home");

    const approval = page.locator("[data-testid^=approval-]").first();
    if (await approval.isVisible()) {
      await approval.getByTestId("portal-changes").click();
      await approval.getByTestId("portal-note").fill("Bitte das Logo größer");
      await shot(page, info, "portal-changes");
      await approval.getByTestId("portal-send-changes").click();
      await expect(page.getByText("Die Überarbeitungsanfrage wurde an das Team gesendet.")).toBeVisible();
    }

    await page.getByTestId("portal-new").click();
    const service = page.getByTestId("portal-service");
    await service.selectOption((await service.locator("option", { hasText: "Story" }).first().getAttribute("value"))!);
    await page.getByTestId("answer-topic").fill(`Test-Story ${info.project.name}`);
    await shot(page, info, "portal-new-order");
    await page.getByTestId("portal-order-save").click();
    await expect(page.getByText("Ihr Auftrag ist erfasst; er beginnt nach der Prüfung durch die Agentur.")).toBeVisible();
    await expect(page.getByTestId("portal")).toContainText("Wartet auf Prüfung durch die Agentur");
    await page.locator("[data-testid^=portal-o-]").first().click();
    await shot(page, info, "portal-order");
  });
});
