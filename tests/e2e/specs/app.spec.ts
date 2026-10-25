import { expect, test } from "@playwright/test";
import { shot } from "./helpers";

const APP_URL = process.env.APP_URL ?? "http://localhost:3002";

/**
 * The Android app's web part: the static export with CLIENT_KIND=android, loaded from another origin like the app does
 * (https://localhost in the WebView). Login keeps the refresh token in secure storage (here the plugin's web fallback),
 * so a reload stays signed in; every page is a static file (trailing-slash folders).
 */
test.describe("Android app build (static export)", () => {
  test("signs in, survives a reload, and the main pages open", async ({ page }, info) => {
    test.skip(info.project.name === "desktop", "phones only");
    await page.goto(`${APP_URL}/`);
    await expect(page).toHaveURL(/\/login\/?$/);
    await page.getByTestId("demo-editor").click();
    await expect(page.getByTestId("dashboard-hello")).toBeVisible();
    await shot(page, info, "app-dashboard");

    await page.reload();
    await expect(page.getByTestId("dashboard-hello")).toBeVisible();

    for (const path of ["/work/", "/attendance/", "/profile/"]) {
      await page.goto(`${APP_URL}${path}`);
      await expect(page.locator("main")).toBeVisible();
      await expect(page.locator("main h1").first()).toBeVisible();
    }
    await shot(page, info, "app-profile");
    // The website-only pages are not part of the app.
    const blog = await page.request.get(`${APP_URL}/blog/`);
    expect(blog.status()).toBe(404);
  });
});
