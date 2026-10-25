import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, navTo, shot } from "./helpers";

test.describe("M4: attendance", () => {
  test("worker clocks in, takes a break, runs a timer on a card and clocks out", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    // Writer (Maryam): office network (localhost in demo) passes the demo rule.
    await demoLogin(page, "writer");
    await navTo(page, mobile, "attendance");
    await expect(page.getByTestId("clock-card")).toBeVisible();
    const state = page.getByTestId("clock-state");
    if ((await page.getByTestId("clock-out").count()) > 0) {
      // A previous device run left the shift open: close it first.
      await page.getByTestId("clock-out").click();
      if (await page.getByTestId("clock-out-confirm").isVisible()) await page.getByTestId("clock-out-confirm").click();
    }
    await expect(page.getByTestId("clock-in")).toBeVisible();
    await shot(page, info, "m4-out");
    await page.getByTestId("clock-in").click();
    await expect(page.getByText("ورود ثبت شد.")).toBeVisible();
    await expect(state).toContainText("در شیفت");
    await shot(page, info, "m4-in");

    await page.getByTestId("break").click();
    await expect(state).toContainText("استراحت");
    await page.getByTestId("break").click();
    await expect(state).toContainText("در شیفت");

    // Timer from a card of hers on the board
    await page.goto(`${DEMO_URL}/work?mine=1`);
    await page.getByTestId("work-mine").check();
    if (mobile) await page.getByTestId("tab-todo").click();
    const card = page.locator("[data-testid^=card-]").filter({ visible: true }).first();
    await card.getByRole("button").first().click();
    await page.getByTestId("timer-toggle").click();
    await expect(page.getByTestId("timer-toggle")).toContainText("توقف تایمر");
    await page.keyboard.press("Escape");
    await page.goto(`${DEMO_URL}/attendance`);
    await expect(page.getByTestId("timer")).toContainText("روی «");
    await shot(page, info, "m4-timer");

    await page.getByTestId("clock-out").click();
    if (await page.getByTestId("clock-out-confirm").isVisible()) {
      await page.locator("#early").fill("جلسه با مشتری");
      await shot(page, info, "m4-early-leave");
      await page.getByTestId("clock-out-confirm").click();
    }
    await expect(page.getByText("خروج ثبت شد.")).toBeVisible();
    await expect(page.getByTestId("timer")).not.toContainText("روی «");   // clock-out stops the timer

    // Correction request
    await page.getByTestId("correction-open").click();
    await page.getByTestId("correction-day").fill("1405/08/01");
    await page.getByTestId("correction-in").fill("09:00");
    await page.getByTestId("correction-out").fill("17:00");
    await page.getByTestId("correction-reason").fill("گوشی‌ام خاموش بود");
    await shot(page, info, "m4-correction");
    await page.getByTestId("correction-save").click();
    await expect(page.getByText("درخواست اصلاح فرستاده شد.")).toBeVisible();
  });

  test("manager: live presence, review, office QR kiosk and setup", async ({ page }, info) => {
    await demoLogin(page, "admin");
    await page.goto(`${DEMO_URL}/attendance/live`);
    await expect(page.getByTestId("presence")).toContainText("مریم");
    await expect(page.getByTestId("corrections")).toContainText("گوشی‌ام خاموش بود");
    await shot(page, info, "m4-live");

    await page.goto(`${DEMO_URL}/attendance/kiosk`);
    await expect(page.getByTestId("kiosk-qr")).toBeVisible();
    await shot(page, info, "m4-kiosk");

    await page.goto(`${DEMO_URL}/attendance/setup`);
    await expect(page.getByTestId("schedule-save")).toBeVisible();
    await page.getByTestId("schedule-save").click();
    await expect(page.getByText("ذخیره شد.")).toBeVisible();
    await shot(page, info, "m4-setup");
  });
});
