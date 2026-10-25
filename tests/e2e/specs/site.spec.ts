import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, navTo, shot } from "./helpers";

test.describe("N10: public website (home, packages, portfolio, consultation)", () => {
  test("visitor reads the site and asks for a consultation; the admin sees the request", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await page.goto(`${DEMO_URL}/`);
    await expect(page.getByTestId("home-hero")).toBeVisible();
    await expect(page.locator("#services")).toBeVisible();
    await expect(page.locator("#packages")).toBeVisible();
    await expect(page.locator('section[aria-labelledby="cases"]').getByText("نمونه", { exact: true }).first()).toBeVisible();
    const ld = JSON.parse((await page.locator('script[type="application/ld+json"]').first().textContent())!);
    const types = ld["@graph"].flatMap((g: { "@type": string | string[] }) => g["@type"]);
    expect(types).toEqual(expect.arrayContaining(["ProfessionalService", "FAQPage", "WebSite"]));
    expect(await page.locator('meta[name="description"]').getAttribute("content")).toBeTruthy();
    await shot(page, info, "n10-home");

    const phone = { desktop: "09120000301", pixel7: "09120000302", phone360: "09120000303" }[info.project.name] ?? "09120000309";
    const form = page.getByTestId("consult-form");
    await form.getByLabel("نام شما").fill(`مهمان ${info.project.name}`);
    await form.getByLabel("شماره‌ی موبایل").fill(phone);
    await form.getByLabel("کسب‌وکار (اختیاری)").fill("نانوایی");
    await form.getByRole("button", { name: "درخواست تماس" }).click();
    await expect(page.getByTestId("consult-sent")).toBeVisible();

    await page.goto(`${DEMO_URL}/plans`);
    await page.getByTestId("packages-list").getByRole("link").first().click();
    await expect(page.locator("h1")).toBeVisible();
    await expect(page.getByText("تومان").first()).toBeVisible();
    await shot(page, info, "n10-package");
    await page.goto(`${DEMO_URL}/cases`);
    await page.getByTestId("cases-list").getByRole("link").first().click();
    await expect(page.getByTestId("case")).toContainText("نمونه");
    await shot(page, info, "n10-case");

    await demoLogin(page, "admin");
    await navTo(page, mobile, "leads");
    await expect(page.getByTestId("leads")).toContainText(`مهمان ${info.project.name}`);
    await shot(page, info, "n10-leads");
  });

  test("admin edits the home page and the portfolio; a case without consent can't go live", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "content/home");
    await expect(page.getByTestId("home-editor")).toBeVisible();
    const hero = `پیج شما در دستان حرفه‌ای (${info.project.name})`;
    await page.getByTestId("home-hero-title").fill(hero);
    await page.getByTestId("home-seo-desc").fill("کوتاه");
    await page.getByTestId("home-save").click();
    await expect(page.getByTestId("home-editor").getByRole("alert")).toBeVisible();
    await page.getByTestId("home-seo-desc").fill("مدیریت پیج اینستاگرام و تولید محتوا برای کسب‌وکارهای شیراز با برنامه‌ی انتشار، گزارش ماهانه و پنل اختصاصی.");
    await page.getByTestId("home-save").click();
    await expect(page.getByText("صفحه‌ی اصلی ذخیره شد")).toBeVisible();
    await shot(page, info, "n10-home-editor");

    await navTo(page, mobile, "content/cases");
    await expect(page.getByTestId("cases-admin")).toContainText("کافه نمونه");
    await page.getByTestId("case-new").click();
    await page.getByTestId("case-name").fill(`قنادی واقعی ${info.project.name}`);
    await page.getByTestId("case-fb").fill("800");
    await page.getByTestId("case-fa").fill("2400");
    await shot(page, info, "n10-case-editor");
    await page.getByTestId("case-editor-save").click();
    await expect(page.getByText("نمونه‌کار ذخیره شد.")).toBeVisible();
    const card = page.getByTestId("cases-admin").locator("li", { hasText: `قنادی واقعی ${info.project.name}` });
    await card.getByRole("button", { name: "انتشار روی سایت" }).click();
    await expect(page.getByText("بدون رضایت کتبی مشتری (و فایل آن)")).toBeVisible();

    // The public home follows (cached up to a minute).
    await expect(async () => {
      await page.goto(`${DEMO_URL}/`);
      await expect(page.getByTestId("home-hero")).toHaveText(hero, { timeout: 1000 });
    }).toPass({ timeout: 90_000, intervals: [5000] });
  });
});
