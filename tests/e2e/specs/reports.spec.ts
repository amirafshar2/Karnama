import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N8: work report and scores", () => {
  test("manager: period report, details, chart, scores with the why, weights", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "reports");
    await page.getByTestId("period-last30").click();
    await expect(page.getByTestId("rpt-table")).toBeVisible();
    await expect(page.getByTestId("rpt-chart")).toBeVisible();
    await expect(page.getByTestId("rpt-excel")).toBeVisible();
    await shot(page, info, "n8-report");

    await page.getByTestId("rpt-row-علی (تدوینگر نمونه)").click();
    await expect(page.getByTestId("rpt-details")).toBeVisible();
    await expect(page.getByTestId("rpt-details")).toContainText("کارهای تمام‌شده");
    await shot(page, info, "n8-details");

    await page.getByTestId("rpt-tab-scores").click();
    const ali = page.getByTestId("score-علی (تدوینگر نمونه)");
    await expect(ali.getByTestId("score-total")).toBeVisible();
    await ali.getByRole("button").first().click();
    await expect(ali.getByTestId("score-why")).toContainText("به‌موقع بودن");
    await expect(ali.getByTestId("score-trend")).toBeVisible();
    await shot(page, info, "n8-scores");

    await page.getByTestId("rpt-tab-settings").click();
    await expect(page.getByTestId("score-settings")).toBeVisible();
    await page.getByTestId("weight-quality").fill("26");
    await page.getByTestId("score-settings-save").click();
    await expect(page.getByText("وزن‌ها ذخیره شد.")).toBeVisible();
    await shot(page, info, "n8-settings");
  });

  test("worker sees only their own report and score", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "editor");
    await navTo(page, mobile, "reports");
    await page.getByTestId("period-last30").click();
    await expect(page.getByTestId("rpt-table").locator("tbody > tr")).toHaveCount(1);
    await expect(page.getByTestId("rpt-excel")).toHaveCount(0);
    await page.getByTestId("rpt-tab-scores").click();
    await expect(page.getByTestId("score-why")).toBeVisible();
    await expect(page.getByTestId("rpt-tab-settings")).toHaveCount(0);
    await shot(page, info, "n8-worker-score");
  });
});
