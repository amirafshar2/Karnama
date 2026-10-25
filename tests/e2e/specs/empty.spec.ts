import { expect, test } from "@playwright/test";
import { EMPTY_URL, otpFromLog, shot, totp } from "./helpers";

const SETUP_TOKEN = process.env.EMPTY_SETUP_TOKEN ?? "";
const API_LOG = process.env.EMPTY_API_LOG ?? "";

// Runs once (desktop) because setup can only happen once per install; the login screens are checked on every device.
test.describe("empty install (new customer)", () => {
  test("no demo buttons on the login page", async ({ page }, info) => {
    await page.goto(`${EMPTY_URL}/login`);
    await expect(page.getByTestId("login-phone")).toBeVisible();
    await expect(page.locator("[data-testid^=demo-]")).toHaveCount(0);
    await shot(page, info, "empty-login");
  });

  test("setup -> owner OTP -> mandatory two-step enrollment -> dashboard", async ({ page }, info) => {
    test.skip(info.project.name !== "desktop", "setup runs once per install");
    test.skip(!SETUP_TOKEN || !API_LOG, "EMPTY_SETUP_TOKEN / EMPTY_API_LOG not provided");

    await page.goto(`${EMPTY_URL}/setup`);
    await shot(page, info, "empty-setup-form");
    await page.getByTestId("setup-token").fill(SETUP_TOKEN);
    await page.getByLabel("نام شما").fill("مالک آژانس");
    await page.getByLabel("شماره‌ی موبایل مالک").fill("۰۹۱۷۱۲۳۴۵۶۷");
    await page.getByLabel("نام آژانس (اختیاری)").fill("آژانس شیراز");
    await page.getByRole("button", { name: "ساختن حساب مالک" }).click();
    await expect(page.getByText("حساب مالک ساخته شد")).toBeVisible();
    await shot(page, info, "empty-setup-done");

    await page.goto(`${EMPTY_URL}/login`);
    await expect(page.getByText("آژانس شیراز").first()).toBeVisible();
    await page.getByTestId("login-phone").fill("09171234567");
    await page.getByTestId("login-send").click();
    await expect(page.getByText("کد ۶ رقمی")).toBeVisible();
    await shot(page, info, "empty-otp");
    await page.waitForTimeout(500);
    const code = otpFromLog(API_LOG, "0917***4567");
    await page.keyboard.type(code);

    await expect(page.getByText("فعال‌کردن ورود دومرحله‌ای")).toBeVisible();
    await shot(page, info, "empty-2fa-setup");
    const secret = (await page.locator("code").textContent())!.trim();
    await page.keyboard.type(totp(secret));
    await expect(page).toHaveURL(/\/dashboard/);
    await expect(page.getByTestId("dashboard-hello")).toContainText("مالک آژانس");
    await shot(page, info, "empty-owner-dashboard");
  });
});
