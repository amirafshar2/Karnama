import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N12: help, page help and first-run tour", () => {
  test("first visit shows the tour; it can be skipped and opened again from help", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await page.addInitScript(() => { sessionStorage.setItem("tour-on", "1"); localStorage.removeItem("karnama.tour.off"); });
    await demoLogin(page, "editor");
    await expect(page.getByTestId("tour")).toBeVisible();
    await shot(page, info, "n12-tour-1");
    await page.getByTestId("tour-next").click();
    await expect(page.getByTestId("tour")).toContainText("تابلوی کار");
    await shot(page, info, "n12-tour-2");
    await page.getByTestId("tour-skip").click();
    await expect(page.getByTestId("tour")).toHaveCount(0);
    await page.reload();
    await expect(page.getByTestId("dashboard-hello")).toBeVisible();
    await page.waitForTimeout(1200);
    await expect(page.getByTestId("tour")).toHaveCount(0);

    await navTo(page, mobile, "help");
    await page.getByTestId("help-tour").click();
    await expect(page.getByTestId("tour")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.getByTestId("tour")).toHaveCount(0);
  });

  test("worker: the ? button explains the page and links the article; search and glossary", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "editor");
    await navTo(page, mobile, "attendance");
    await page.getByTestId("page-help").click();
    await expect(page.getByTestId("page-help-text")).toContainText("ورود، خروج");
    await expect(page.getByTestId("page-help-article")).toContainText("ورود و خروج");
    await shot(page, info, "n12-page-help");
    await page.getByTestId("page-help-article").click();
    await expect(page.getByTestId("help-article")).toContainText("خروج یادم رفت");
    await shot(page, info, "n12-article");

    await page.getByText("همه‌ی مقاله‌ها").click();
    await page.getByTestId("help-search").fill("مرخصی");
    await expect(page.getByTestId("help-list")).toContainText("چطور مرخصی بگیرم");
    await expect(page.getByTestId("help-list")).not.toContainText("مشتری:");
    await page.getByTestId("help-tab-glossary").click();
    await expect(page.getByTestId("help-glossary")).toContainText("سهمیه");
    await shot(page, info, "n12-glossary");
  });

  test("client: simpler help with its own articles and a direct question button", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "client");
    await navTo(page, mobile, "help");
    await expect(page.getByTestId("help-list")).toContainText("چطور محتوا را تأیید کنم");
    await expect(page.getByTestId("help-list")).not.toContainText("ورود و خروج");
    await expect(page.getByTestId("help-ask")).toBeVisible();
    await shot(page, info, "n12-client-help");
  });
});
