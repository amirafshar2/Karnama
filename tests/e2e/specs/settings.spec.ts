import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, shot } from "./helpers";

test.describe("white-label: brand, colours, logo and texts from the admin panel", () => {
  test("admin changes name and colours; every page follows", async ({ page }, info) => {
    test.skip(info.project.name !== "desktop" && info.project.name !== "phone360", "two devices are enough for this flow");
    const isMobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await page.goto(`${DEMO_URL}/settings/brand`);
    await expect(page.getByRole("heading", { name: "برند و ظاهر" })).toBeVisible();
    await shot(page, info, "brand-settings");

    const run = Date.now().toString(36).slice(-4);
    await page.getByTestId("brand-name").fill(`آژانس ام ${run}`);
    await page.getByTestId("brand-color-primary").fill("#00C2FF");
    await page.getByTestId("brand-save").click();
    await expect(page.getByText("برند ذخیره شد").first()).toBeVisible();
    const primary = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--primary").trim());
    expect(primary).toBe("#00c2ff");
    await expect(page.getByText(`آژانس ام ${run}`).filter({ visible: true }).first()).toBeVisible();
    await shot(page, info, "brand-saved-blue");

    // Logo upload (PNG) shows up in the header mark
    await page.getByTestId("brand-logo-input").setInputFiles("specs/fixtures/logo.png");
    await expect(page.getByText("لوگو عوض شد.")).toBeVisible();
    await expect(page.locator("img[src*='/media/public/brand/']").filter({ visible: true }).first()).toBeVisible();

    // Back to the client's yellow/black so other screenshots stay on-brand
    await page.getByTestId("brand-name").fill("");
    await page.getByRole("button", { name: "رنگ‌های پیش‌فرض" }).click();
    await page.getByTestId("brand-save").click();
    await expect(page.getByText("برند ذخیره شد").first()).toBeVisible();
    await shot(page, info, "brand-back-to-default");

    // Texts: change the login title, see it on the login page, then reset
    await page.goto(`${DEMO_URL}/settings/texts`);
    await page.getByTestId("texts-search").fill("login.title");
    const input = page.getByTestId("text-input-login.title");
    await input.fill("ورود همکاران آژانس");
    await input.blur();
    await expect(page.getByText("متن ذخیره شد.")).toBeVisible();
    await shot(page, info, "texts-edited");
    await page.goto(`${DEMO_URL}/profile`);
    await page.getByTestId("logout").click();
    await expect(page.getByRole("heading", { name: "ورود همکاران آژانس" })).toBeVisible();
    await shot(page, info, "login-custom-title");

    await demoLogin(page, "admin");
    await page.goto(`${DEMO_URL}/settings/texts`);
    await page.getByTestId("texts-search").fill("login.title");
    await page.getByTestId("text-row-login.title").getByRole("button", { name: "برگرداندن به پیش‌فرض" }).click();
    await expect(page.getByText("متن به پیش‌فرض برگشت.")).toBeVisible();
    if (isMobile) await expect(page.getByTestId("bottom-settings/texts")).toBeVisible();
  });

  test("web app manifest follows the brand (Android install / iPhone home screen)", async ({ request }) => {
    const res = await request.get(`${DEMO_URL}/manifest.webmanifest`);
    expect(res.ok()).toBeTruthy();
    const m = await res.json();
    expect(m.display).toBe("standalone");
    expect(m.dir).toBe("rtl");
    expect(m.icons.length).toBeGreaterThanOrEqual(3);
  });
});
