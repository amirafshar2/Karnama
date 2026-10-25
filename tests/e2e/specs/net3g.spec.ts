import { expect, test } from "@playwright/test";
import { DEMO_URL } from "./helpers";

/**
 * 3G check (docs/PROMPT.md M7): a phone on a regular 3G connection (750 kb/s down, 250 kb/s up, 100 ms latency),
 * empty cache. The login page must be usable and the panel must open within the budgets below.
 */
test("pages stay usable on 3G", async ({ page, context }, info) => {
  test.skip(info.project.name !== "pixel7", "one phone is enough");
  test.setTimeout(120_000);
  const cdp = await context.newCDPSession(page);
  await cdp.send("Network.enable");
  await cdp.send("Network.emulateNetworkConditions", { offline: false, latency: 100, downloadThroughput: (750 * 1024) / 8, uploadThroughput: (250 * 1024) / 8 });
  await cdp.send("Network.setCacheDisabled", { cacheDisabled: true });

  let t = Date.now();
  await page.goto(`${DEMO_URL}/login`);
  await expect(page.getByTestId("demo-editor")).toBeVisible();
  const login = Date.now() - t;

  t = Date.now();
  await page.getByTestId("demo-editor").click();
  await expect(page.getByTestId("dashboard-hello")).toBeVisible({ timeout: 30_000 });
  const panel = Date.now() - t;

  t = Date.now();
  await page.goto(`${DEMO_URL}/`);
  await expect(page.getByTestId("home-hero")).toBeVisible();
  const home = Date.now() - t;

  console.log(`[3G] login ${login} ms · panel after sign-in ${panel} ms · public home ${home} ms`);
  expect(login).toBeLessThan(8000);
  expect(panel).toBeLessThan(10_000);
  expect(home).toBeLessThan(6000);
});
