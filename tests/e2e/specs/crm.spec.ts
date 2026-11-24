import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N7: CRM", () => {
  test("lead → call → call result → won → client", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    const name = `Herr Lead ${info.project.name}`;
    const phone = { desktop: "015189990001", pixel7: "015189990002", phone360: "015189990003" }[info.project.name] ?? "015189990009";
    await demoLogin(page, "marketer");
    await navTo(page, mobile, "crm");
    await expect(page.getByTestId("crm-board")).toBeVisible();
    await shot(page, info, "n7-board");

    await page.getByTestId("crm-new").click();
    await page.getByTestId("crm-name").fill(name);
    await page.getByTestId("crm-phone").fill(phone);
    await page.getByTestId("crm-business").fill(`Konditorei ${info.project.name}`);
    await page.getByTestId("crm-lead-sheet-save").click();
    await expect(page.getByText("Angelegt.")).toBeVisible();

    // Open it, "call" (the app comes back → call result), record the result.
    await page.getByTestId(`crm-lead-${name}`).getByRole("button").first().click();
    await page.getByTestId("crm-action-call").click();
    await page.getByTestId("crm-call-minutes").fill("4");
    await page.getByTestId("crm-outcome-interested").click();
    await page.getByTestId("crm-call-note").fill("Möchte das Silber-Paket");
    await shot(page, info, "n7-call-result");
    await page.getByTestId("crm-call-save").click();
    await expect(page.getByText("Anruf erfasst.")).toBeVisible();
    await expect(page.getByTestId("crm-timeline")).toContainText("Interessiert");

    await page.getByTestId("crm-convert").click();
    await expect(page.getByText("Kunde angelegt.")).toBeVisible();
    await expect(page.getByTestId("crm-open-client")).toBeVisible();
    await expect(page.getByTestId("crm-timeline")).toContainText("Wurde Kunde");
    await shot(page, info, "n7-won");
  });

  test("today's follow-ups and the report", async ({ page }, info) => {
    await demoLogin(page, "admin");
    await navTo(page, info.project.name !== "desktop", "crm");
    await page.getByTestId("crm-tab-today").click();
    await expect(page.getByTestId("crm-today")).toContainText("Herr Neumann (Beispiel)");
    await page.getByTestId("crm-tab-report").click();
    await expect(page.getByTestId("crm-report")).toContainText("Tim (Beispiel-Vertriebler)");
    await shot(page, info, "n7-report");
  });
});
