import { expect, test } from "@playwright/test";
import { demoLogin, shot } from "./helpers";

test.describe("client portal (demo version)", () => {
  test("client sees the package, asks for a change, and places an order", async ({ page }, info) => {
    await demoLogin(page, "client");
    await expect(page.getByTestId("portal")).toContainText("کافه نمونه");
    await shot(page, info, "portal-home");

    const approval = page.locator("[data-testid^=approval-]").first();
    if (await approval.isVisible()) {
      await approval.getByTestId("portal-changes").click();
      await approval.getByTestId("portal-note").fill("لطفاً لوگو بزرگ‌تر");
      await shot(page, info, "portal-changes");
      await approval.getByTestId("portal-send-changes").click();
      await expect(page.getByText("درخواست اصلاح برای تیم فرستاده شد.")).toBeVisible();
    }

    await page.getByTestId("portal-new").click();
    const service = page.getByTestId("portal-service");
    await service.selectOption((await service.locator("option", { hasText: "استوری" }).first().getAttribute("value"))!);
    await page.getByTestId("answer-topic").fill(`استوری تست ${info.project.name}`);
    await shot(page, info, "portal-new-order");
    await page.getByTestId("portal-order-save").click();
    await expect(page.getByText("سفارش شما ثبت شد؛ بعد از بررسی آژانس شروع می‌شود.")).toBeVisible();
    await expect(page.getByTestId("portal")).toContainText("منتظر بررسی آژانس");
    await page.locator("[data-testid^=portal-o-]").first().click();
    await shot(page, info, "portal-order");
  });
});
