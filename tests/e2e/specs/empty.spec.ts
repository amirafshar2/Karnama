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
    await page.getByLabel("Ihr Name").fill("Agenturinhaber");
    await page.getByLabel("Handynummer des Inhabers").fill("۰۹۱۷۱۲۳۴۵۶۷");
    await page.getByLabel("Agenturname (optional)").fill("Agentur Stuttgart");
    await page.getByRole("button", { name: "Inhaberkonto anlegen" }).click();
    await expect(page.getByText("Inhaberkonto angelegt")).toBeVisible();
    await shot(page, info, "empty-setup-done");

    await page.goto(`${EMPTY_URL}/login`);
    await expect(page.getByText("Agentur Stuttgart").first()).toBeVisible();
    await page.getByTestId("login-phone").fill("015171234567");
    await page.getByTestId("login-send").click();
    await expect(page.getByText("6-stelliger Code")).toBeVisible();
    await shot(page, info, "empty-otp");
    await page.waitForTimeout(500);
    const code = otpFromLog(API_LOG, "0917***4567");
    await page.keyboard.type(code);

    await expect(page.getByText("Zwei-Faktor-Anmeldung aktivieren")).toBeVisible();
    await shot(page, info, "empty-2fa-setup");
    const secret = (await page.locator("code").textContent())!.trim();
    await page.keyboard.type(totp(secret));
    await expect(page).toHaveURL(/\/dashboard/);
    await expect(page.getByTestId("dashboard-hello")).toContainText("Agenturinhaber");
    await shot(page, info, "empty-owner-dashboard");
  });
});
