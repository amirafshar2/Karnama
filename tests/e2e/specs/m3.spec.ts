import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, navTo, shot } from "./helpers";

test.describe("M3: orders and the live work board", () => {
  test("admin places an order, works the board, assigns, comments and uploads", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    const run = `${info.project.name}-${Date.now().toString(36)}`;
    await demoLogin(page, "admin");

    // Board with the demo cards
    await navTo(page, mobile, "work");
    await expect(page).toHaveURL(/\/work/);
    if (mobile) {
      await page.getByTestId("tab-inProgress").click();
      await expect(page.locator("[data-testid^=card-]:visible").first()).toBeVisible();
    } else {
      await expect(page.getByTestId("col-inProgress").locator("[data-testid^=card-]").first()).toBeVisible();
    }
    await shot(page, info, "m3-board");

    // New order (auto-accepted) for the café
    await page.goto(`${DEMO_URL}/orders`);
    await shot(page, info, "m3-orders");
    await page.getByTestId("orders-new").click();
    const client = page.getByTestId("order-client");
    await client.selectOption((await client.locator("option", { hasText: "Café Beispiel" }).getAttribute("value"))!);
    const service = page.getByTestId("order-service");
    await service.selectOption((await service.locator("option", { hasText: "Reels" }).first().getAttribute("value"))!);
    await page.getByTestId("answer-topic").fill(`Test-Reel ${run}`);
    await page.getByTestId("order-title").fill(`Test-Auftrag ${run}`);
    await shot(page, info, "m3-order-form");
    await page.getByTestId("order-create-save").click();
    await expect(page.getByText("Auftrag erfasst.")).toBeVisible();
    await expect(page.getByTestId("order-sheet")).toContainText("Konzept und Caption");
    await shot(page, info, "m3-order-detail");
    await page.keyboard.press("Escape");

    // The new card is on the board in "to do"
    await page.goto(`${DEMO_URL}/work`);
    // The first stage is the one that can start now (later stages wait in the queue column).
    const card = (mobile ? page : page.getByTestId("col-todo")).locator("[data-testid^=card-]", { hasText: `Test-Auftrag ${run}` }).filter({ visible: true });
    if (mobile) await page.getByTestId("tab-todo").click();
    await expect(card.first()).toBeVisible();

    // Assign the suggested person, comment, upload
    await card.first().getByRole("button").first().click();
    await expect(page.getByTestId("work-sheet")).toBeVisible();
    await page.getByTestId("assign-suggested").click();
    await expect(page.getByText("Aufgabe zugewiesen.")).toBeVisible();
    await page.getByTestId("comment-body").fill("Bitte bis morgen");
    await page.getByTestId("comment-send").click();
    await expect(page.getByTestId("comments")).toContainText("Bitte bis morgen");
    await page.getByTestId("files-input").setInputFiles("specs/fixtures/logo.png");
    await expect(page.getByText("Datei hochgeladen.")).toBeVisible();
    await expect(page.getByTestId("files")).toContainText("logo.png");
    await shot(page, info, "m3-work-sheet");
    await page.keyboard.press("Escape");

    // Move: drag on desktop, "move to…" menu on phones
    if (mobile) {
      const id = (await card.first().getAttribute("data-testid"))!.replace("card-", "");
      await page.getByTestId(`move-${id}`).filter({ visible: true }).selectOption("inProgress");
      await page.getByTestId("tab-inProgress").click();
      await expect(card.first()).toBeVisible();
    } else {
      await card.first().dragTo(page.getByTestId("col-inProgress"));
      await expect(page.getByTestId("col-inProgress").locator("[data-testid^=card-]", { hasText: `Test-Auftrag ${run}` })).toBeVisible();
    }
    await shot(page, info, "m3-moved");
  });

  test("worker sees only own cards; changes reach another screen live", async ({ browser }, info) => {
    test.skip(info.project.name !== "desktop", "live sync is checked once, on desktop");
    const editorPage = await (await browser.newContext()).newPage();
    const adminPage = await (await browser.newContext()).newPage();
    await demoLogin(editorPage, "editor");
    await demoLogin(adminPage, "admin");
    await editorPage.goto(`${DEMO_URL}/work`);
    await adminPage.goto(`${DEMO_URL}/work`);

    // The editor (Ali) has the café reel's editing stage in progress.
    const editorCard = editorPage.locator("[data-testid^=card-]", { hasText: "Reel zur Herbstkarte" }).filter({ visible: true }).first();
    await expect(editorCard).toBeVisible();
    await expect(editorPage.locator("[data-testid^=card-]", { hasText: "Slide Größentabelle" })).toHaveCount(0); // someone else's
    await shot(editorPage, info, "m3-editor-board");

    await editorCard.getByRole("button").first().click();
    await expect(editorPage.getByTestId("action-inReview")).toBeVisible();
    await expect(editorPage.getByTestId("action-done")).toHaveCount(0); // review can't be skipped
    await editorPage.getByTestId("action-inReview").click();
    await expect(editorPage.getByText("Karte verschoben.")).toBeVisible();

    // Admin's board updates without a reload.
    await expect(adminPage.getByTestId("col-inReview").locator("[data-testid^=card-]", { hasText: "Reel zur Herbstkarte" })).toBeVisible({ timeout: 8000 });
    await shot(adminPage, info, "m3-admin-live");
  });
});
