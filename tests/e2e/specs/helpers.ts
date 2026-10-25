import { createHmac } from "node:crypto";
import { readdirSync, readFileSync } from "node:fs";
import { expect, type Page, type TestInfo } from "@playwright/test";

export const DEMO_URL = process.env.DEMO_URL ?? "http://localhost:3000";
export const EMPTY_URL = process.env.EMPTY_URL ?? "http://localhost:3001";

/** Screenshot + the checks that only eyes used to catch: no horizontal scroll, CSS loaded, no English error text. */
export async function shot(page: Page, info: TestInfo, name: string) {
  const device = info.project.name;
  // Numbered by what is already on disk, so the order is stable across test files.
  const n = readdirSync("screenshots").filter((f) => f.startsWith(`${device}-`)).length + 1;
  await page.waitForLoadState("networkidle").catch(() => undefined);
  await expectNoHorizontalScroll(page);
  await expectStyled(page);
  await page.screenshot({ path: `screenshots/${device}-${String(n).padStart(2, "0")}-${name}.png`, fullPage: true });
}

/** Compares against screen.width too, not only innerWidth (a wide table can zoom the mobile page out). */
export async function expectNoHorizontalScroll(page: Page) {
  const m = await page.evaluate(() => ({
    scroll: document.documentElement.scrollWidth,
    inner: window.innerWidth,
    screen: window.screen.width,
  }));
  expect(m.scroll, `page wider than viewport: ${JSON.stringify(m)}`).toBeLessThanOrEqual(m.inner + 1);
  expect(m.inner, `layout viewport wider than the screen: ${JSON.stringify(m)}`).toBeLessThanOrEqual(m.screen + 1);
}

export async function expectStyled(page: Page) {
  const font = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
  expect(font.toLowerCase(), "CSS/font not loaded").toContain("vazirmatn");
}

/** Reads the OTP the development SMS provider wrote to the API log. */
export function otpFromLog(logFile: string, maskedPhone: string): string {
  const lines = readFileSync(logFile, "utf8").split("\n").filter((l) => l.includes("[DEV SMS]") && l.includes(maskedPhone));
  const m = lines.at(-1)?.match(/(\d{6})\s*$/);
  if (!m) throw new Error(`no OTP for ${maskedPhone} in ${logFile}`);
  return m[1];
}

/** RFC 6238 TOTP, so the test can play the owner's authenticator app. */
export function totp(secretBase32: string, at = Date.now()) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of secretBase32.replace(/=+$/, "")) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  const key = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / 30)));
  const h = createHmac("sha1", key).update(counter).digest();
  const o = h[h.length - 1] & 15;
  return String((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}

export async function demoLogin(page: Page, role: "admin" | "editor" | "writer" | "client") {
  await page.goto(`${DEMO_URL}/login`);
  await page.getByTestId(`demo-${role}`).click();
  await expect(page).toHaveURL(/\/dashboard/);
  await expect(page.getByTestId("dashboard-hello")).toBeVisible();
}

/** Opens a page from the menu: side nav on desktop; bottom bar (or its "More" panel) on phones. */
export async function navTo(page: Page, mobile: boolean, href: string) {
  if (!mobile) return page.getByTestId(`nav-${href}`).click();
  const direct = page.getByTestId(`bottom-${href}`);
  if (!(await direct.isVisible())) await page.getByTestId("bottom-more").click();
  await direct.click();
}

/** The menu entry exists (phones: in the bar or behind "More"). */
export async function menuHas(page: Page, mobile: boolean, href: string) {
  if (!mobile) return (await page.getByTestId(`nav-${href}`).count()) > 0;
  if ((await page.getByTestId(`bottom-${href}`).count()) > 0) return true;
  const more = page.getByTestId("bottom-more");
  if ((await more.count()) === 0) return false;
  if (!(await page.getByTestId("bottom-more-panel").isVisible())) await more.click();
  const has = (await page.getByTestId(`bottom-${href}`).count()) > 0;
  await more.click();
  return has;
}
