import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, navTo, shot } from "./helpers";

/** Tehran day and time ~40 minutes from now: always in the future, whatever hour the suite runs. */
const soon = () => {
  const at = new Date(Date.now() + 40 * 60_000);
  const j = new Intl.DateTimeFormat("en-u-ca-persian-nu-latn", { year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "Asia/Tehran" }).formatToParts(at);
  const p = (t: string) => j.find((x) => x.type === t)!.value;
  return { day: `${p("year").replace(/\D/g, "")}/${p("month")}/${p("day")}`, time: `${p("hour")}:${p("minute")}` };
};

test.describe("M6: notifications, publishing calendar, client reminders and messages", () => {
  test("client publishes its own post and writes to the agency; the admin's bell and calendar", async ({ browser }, info) => {
    const mobile = info.project.name !== "desktop";
    const client = await (await browser.newContext()).newPage();
    await demoLogin(client, "client");
    // The café runs its own Telegram channel: the seeded post is due now (only the first device run gets to publish it).
    const pub = client.getByTestId("portal-publishing");
    await expect(pub).toBeVisible();
    await expect(pub).toContainText("cafe_nemoone");
    await shot(client, info, "m6-portal-publish");
    const done = pub.getByTestId("portal-pub-done");
    if ((await done.count()) > 0) {
      await pub.locator("input").first().fill("https://t.me/cafe_nemoone/12");
      await done.first().click();
      await expect(client.getByText("ممنون! ثبت شد.")).toBeVisible();
    }

    // A message about an order.
    await client.locator("[data-testid^=portal-o-]").first().click();
    await client.getByTestId("portal-msg").fill(`لطفاً رنگ‌ها گرم‌تر باشد (${info.project.name})`);
    await client.getByTestId("portal-msg-send").click();
    await expect(client.getByText("پیام فرستاده شد.")).toBeVisible();
    await expect(client.getByRole("dialog")).toContainText(`رنگ‌ها گرم‌تر باشد (${info.project.name})`);
    await shot(client, info, "m6-portal-message");

    const admin = await (await browser.newContext()).newPage();
    await demoLogin(admin, "admin");
    await expect(admin.getByTestId("bell-count")).toBeVisible();
    await admin.getByTestId("bell").click();
    await expect(admin.getByTestId("bell-panel")).toContainText("پیام کافه نمونه");
    await shot(admin, info, "m6-bell");
    await admin.getByTestId("bell-read-all").click();
    await expect(admin.getByTestId("bell-count")).toHaveCount(0);
    await admin.keyboard.press("Escape");

    // Publishing calendar: schedule a post for tonight.
    await navTo(admin, mobile, "publishing");
    await expect(admin.getByRole("heading", { name: "تقویم انتشار" })).toBeVisible();
    await expect(admin.getByTestId("pub-card").first()).toBeVisible();
    await shot(admin, info, "m6-calendar");
    await admin.getByTestId("publish-new").click();
    await admin.getByTestId("publish-client").selectOption({ label: "پوشاک نمونه" });
    await expect(admin.getByTestId("publish-page").locator("option")).toHaveCount(2);   // "choose…" + the one page
    await admin.getByTestId("publish-page").selectOption({ index: 1 });
    const when = soon();
    await admin.getByTestId("publish-day").fill(when.day);
    await admin.getByTestId("publish-time").fill(when.time);
    await admin.getByTestId("publish-caption").fill(`حراج آخر شب (${info.project.name})`);
    await admin.getByTestId("publish-tags").fill("#حراج پوشاک");
    await shot(admin, info, "m6-publish-editor");
    await admin.getByTestId("publish-editor-save").click();
    await expect(admin.getByText("زمان‌بندی ذخیره شد.")).toBeVisible();
    await expect(admin.getByTestId("pub-card").filter({ hasText: `حراج آخر شب (${info.project.name})` })).toBeVisible();

    // Notification settings in the profile.
    await admin.goto(`${DEMO_URL}/profile#notifications`);
    await expect(admin.getByTestId("notify-prefs")).toBeVisible();
    await expect(admin.getByTestId("prefs-bale")).toContainText("وصل نیست");
    await admin.getByTestId("notify-prefs").scrollIntoViewIfNeeded();
    await shot(admin, info, "m6-prefs");
  });
});
