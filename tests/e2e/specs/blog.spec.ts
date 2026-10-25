import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, navTo, shot } from "./helpers";

test.describe("N9: blog with automatic SEO", () => {
  test("admin writes and publishes; the public page has full SEO without filling any SEO field", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "content/blog");
    await expect(page.getByTestId("blog-posts")).toContainText("ریلز یا پست");
    await shot(page, info, "n9-blog-list");

    await page.getByTestId("blog-new").click();
    await expect(page.getByTestId("post-editor")).toBeVisible();
    const title = `عکاسی محصول با موبایل برای فروشگاه آنلاین ${info.project.name}`;
    await page.getByTestId("post-title").fill(title);
    const body = page.getByTestId("editor-body");
    await body.click();
    await page.keyboard.type("عکاسی محصول با موبایل اگر نور و پس‌زمینه درست باشد، برای فروشگاه آنلاین کاملاً کافی است و هزینه‌ی عکاس را کم می‌کند. در این مطلب قدم‌به‌قدم می‌گوییم چه کنید.");
    await page.keyboard.press("Enter");
    await page.keyboard.type("## نور طبیعی کنار پنجره");
    await page.keyboard.press("Enter");
    await page.keyboard.type("صبح‌ها کنار پنجره بهترین نور را دارید؛ پرده‌ی سفید نور را نرم می‌کند و سایه‌ها را کم می‌کند.");
    await page.keyboard.press("Enter");
    await page.keyboard.type("## پس‌زمینه‌ی ساده");
    await page.keyboard.press("Enter");
    await page.keyboard.type("- مقوای سفید");
    await page.keyboard.press("Enter");
    await page.keyboard.type("پارچه‌ی بدون چروک");
    await expect(page.getByTestId("editor-body").locator("h2")).toHaveCount(2);
    await expect(page.getByTestId("editor-body").locator("li")).toHaveCount(2);
    // The automatic SEO follows the text.
    await expect(page.getByTestId("seo-google-title")).toContainText("عکاسی محصول با موبایل", { timeout: 10000 });
    await expect(page.getByTestId("seo-keyword")).toHaveAttribute("placeholder", /عکاسی محصول/);
    await shot(page, info, "n9-editor");
    await page.getByTestId("post-save").click();
    await expect(page.getByText("ذخیره شد.")).toBeVisible();
    await page.getByTestId("post-publish").click();
    await expect(page.getByTestId("post-status")).toContainText("منتشر شده");
    const href = await page.getByTestId("post-view").getAttribute("href");
    expect(href).toMatch(/^\/blog\//);

    // Public page: server-rendered, with title, description, canonical, OG image, JSON-LD — all automatic.
    await page.goto(`${DEMO_URL}${href}`);
    await expect(page.locator("h1")).toHaveText(title);
    await expect(page.locator(".prose-fa h2")).toHaveCount(2);
    expect(await page.locator('meta[name="description"]').getAttribute("content")).toContain("عکاسی محصول");
    expect(await page.locator('link[rel="canonical"]').getAttribute("href")).toContain("/blog/");
    const og = await page.locator('meta[property="og:image"]').getAttribute("content");
    expect(og).toContain("/og/blog/");
    const ld = JSON.parse((await page.locator('script[type="application/ld+json"]').textContent())!);
    expect(ld["@graph"].map((g: { "@type": string }) => g["@type"])).toEqual(expect.arrayContaining(["BlogPosting", "BreadcrumbList"]));
    expect(await page.locator('meta[name="robots"]').getAttribute("content")).toContain("index");
    await shot(page, info, "n9-public-post");

    const img = await page.request.get(new URL(og!).pathname.replace(/^/, DEMO_URL));
    expect(img.headers()["content-type"]).toContain("image/png");
    const sitemap = await (await page.request.get(`${DEMO_URL}/sitemap.xml`)).text();
    // The sitemap is cached for a minute (ISR); the published sample posts are in it.
    expect(decodeURIComponent(sitemap)).toContain("/blog/ریلز-یا-پست");

    // The list is cached for up to a minute (ISR): readers see a new post shortly after it's published.
    await expect(async () => {
      await page.goto(`${DEMO_URL}/blog`);
      await expect(page.getByTestId("blog-list")).toContainText(title, { timeout: 1000 });
    }).toPass({ timeout: 90_000, intervals: [5000] });
    await shot(page, info, "n9-public-list");
  });

  test("writer drafts and submits; can't publish", async ({ page }, info) => {
    await demoLogin(page, "writer");
    await page.goto(`${DEMO_URL}/content/blog`);
    await expect(page.getByTestId("blog-posts")).toContainText("کپشن‌نویسی");
    await expect(page.getByTestId("blog-posts")).not.toContainText("ریلز یا پست");
    await page.getByTestId("blog-new").click();
    await page.getByTestId("post-title").fill(`ایده‌های استوری برای کافه ${info.project.name}`);
    await page.getByTestId("editor-body").click();
    await page.keyboard.type("استوری روزانه از پشت صحنه‌ی کافه مشتری را نزدیک‌تر می‌کند.");
    await expect(page.getByTestId("post-publish")).toHaveCount(0);
    await page.getByTestId("post-submit").click();
    await expect(page.getByTestId("post-status")).toContainText("منتظر بازبینی");
    await shot(page, info, "n9-writer");
  });
});
