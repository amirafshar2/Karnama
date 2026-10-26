import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N11: cameras", () => {
  test("before the office box is installed the page says how; a camera can be prepared with the work-area promise", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "cctv");
    await expect(page.getByTestId("cctv-not-configured")).toBeVisible();
    await page.getByTestId("cctv-add").click();
    await page.getByTestId("cctv-name").fill(`سالن ${info.project.name}`);
    await page.getByTestId("cctv-main").fill("rtsp://viewer:pass@192.168.1.64:554/Streaming/Channels/101");
    await page.getByTestId("cctv-editor-save").click();
    await expect(page.getByText("فقط در فضای کار")).toBeVisible();
    await page.getByTestId("cctv-ethics").click();
    await shot(page, info, "n11-camera-form");
    await page.getByTestId("cctv-editor-save").click();
    await expect(page.getByTestId("cctv-grid")).toContainText(`سالن ${info.project.name}`);
    await expect(page.getByTestId("cctv-grid")).not.toContainText("pass@");
    await shot(page, info, "n11-grid");
  });
});
