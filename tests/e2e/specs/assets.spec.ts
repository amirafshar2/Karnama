import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N3: equipment", () => {
  test("admin registers, hands over; worker accepts and returns with damage; admin confirms", async ({ browser }, info) => {
    const mobile = info.project.name !== "desktop";
    const name = `دوربین تست ${info.project.name}`;
    const ctx = await browser.newContext({ ...info.project.use });
    const page = await ctx.newPage();
    await demoLogin(page, "admin");
    await navTo(page, mobile, "assets");
    await expect(page.getByTestId("as-list")).toBeVisible();
    await shot(page, info, "n3-list");

    await page.getByTestId("as-new").click();
    await page.getByTestId("as-name").fill(name);
    await page.getByTestId("as-category").selectOption({ label: "دوربین" });
    await page.getByTestId("as-serial").fill(`SN-${info.project.name}`);
    await page.getByTestId("as-editor-save").click();
    await expect(page.getByTestId("as-sheet")).toBeVisible();

    await page.getByTestId("as-handover").click();
    await page.getByTestId("as-person").selectOption({ label: "علی (تدوینگر نمونه)" });
    await page.getByTestId("as-condition").fill("سالم، بدون خط");
    await page.getByTestId("as-accessories").fill("باتری ×۲، بند");
    await shot(page, info, "n3-handover");
    await page.getByTestId("as-handover-save").click();
    await expect(page.getByTestId("as-current")).toContainText("منتظر تأیید گیرنده");
    await shot(page, info, "n3-pending");

    // The worker accepts in the app, then brings it back with a note about damage.
    const w = await (await browser.newContext({ ...info.project.use })).newPage();
    await demoLogin(w, "editor");
    await navTo(w, mobile, "assets");
    await w.getByTestId("as-mine").getByText(name).click();
    await expect(w.getByTestId("as-accept")).toBeVisible();
    await shot(w, info, "n3-worker-accept");
    await w.getByTestId("as-accept").click();
    await expect(w.getByTestId("as-current")).toContainText("تحویل گرفته");
    await w.getByTestId("as-return").click();
    await w.getByTestId("as-return-damage").fill("بند پاره شده");
    await w.getByTestId("as-mode-submit").click();
    await expect(w.getByTestId("as-current")).toContainText("برگشت منتظر تأیید مدیر");

    await page.reload();
    await expect(page.getByTestId("as-current")).toContainText("بند پاره شده");
    await page.getByTestId("as-confirm-return").click();
    await page.getByTestId("as-return-status").selectOption("inRepair");
    await page.getByTestId("as-mode-submit").click();
    await expect(page.getByTestId("as-history")).toContainText("برگشت تأیید شد");
    await shot(page, info, "n3-history");

    await page.getByTestId("as-label").click();
    await expect(page.getByTestId("as-label-card")).toContainText("KN-");
    await shot(page, info, "n3-label");
  });

  test("worker scans a label and sees only their own equipment", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "editor");
    await navTo(page, mobile, "assets");
    await expect(page.getByTestId("as-tab-all")).toHaveCount(0);
    await page.getByTestId("as-scan").click();
    await page.getByTestId("qr-manual").fill("KN-0003");
    await page.getByTestId("qr-manual").blur();
    await expect(page.getByTestId("as-sheet")).toContainText("مک‌بوک");
    await page.keyboard.press("Escape");
    await page.getByTestId("as-scan").click();
    await page.getByTestId("qr-manual").fill("KN-0001");
    await page.getByTestId("qr-manual").blur();
    await expect(page.getByText("تجهیز پیدا نشد.")).toBeVisible();
    await shot(page, info, "n3-scan-denied");
  });
});
