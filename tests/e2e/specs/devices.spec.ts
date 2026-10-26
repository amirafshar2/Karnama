import { writeFileSync } from "node:fs";
import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

const API = "http://localhost:5080";

test.describe("N2: attendance devices", () => {
  test("a pushing device shows up online; unknown numbers wait; an exported file is imported with a preview", async ({ page, request }, info) => {
    const mobile = info.project.name !== "desktop";
    const pin = { desktop: "881", pixel7: "882", phone360: "883" }[info.project.name] ?? "889";
    // The demo device talks to the API from the office network (127.0.0.1 in the demo settings).
    const hello = await request.get(`${API}/iclock/cdata?SN=DEMO0001&options=all`);
    expect(await hello.text()).toContain("TransFlag=TransData AttLog");
    const now = new Date(Date.now() + 3.5 * 3600_000).toISOString().replace("T", " ").slice(0, 19);
    expect(await (await request.post(`${API}/iclock/cdata?SN=DEMO0001&table=ATTLOG`, { data: `${pin}\t${now}\t0\t1\n` })).text()).toBe("OK: 1");
    expect((await request.get(`${API}/iclock/cdata?SN=NOTOURS1&options=all`)).status()).toBe(403);

    await demoLogin(page, "admin");
    await navTo(page, mobile, "attendance/devices");
    await expect(page.getByTestId("dev-list").getByText("وصل", { exact: true })).toBeVisible();
    await shot(page, info, "n2-devices");

    await page.getByTestId("dev-tab-pins").click();
    await expect(page.getByTestId("dev-unknown")).toContainText("ثبت");
    await expect(page.getByTestId("dev-pins")).toContainText("علی");
    await shot(page, info, "n2-pins");

    await page.getByTestId("dev-tab-records").click();
    await expect(page.getByTestId("dev-records")).toContainText("شماره‌ی ناشناس");

    await page.getByTestId("dev-tab-import").click();
    const file = `test-results/export-${info.project.name}.csv`;
    writeFileSync(file, `کد پرسنلی,تاریخ,ساعت\n${pin},1405/07/20,08:02\n${pin},1405/07/20,16:45\n`);
    await page.getByTestId("dev-file").setInputFiles(file);
    await expect(page.getByTestId("dev-preview")).toContainText("1405/07/20");
    await shot(page, info, "n2-import-preview");
    await page.getByTestId("dev-import-run").click();
    await expect(page.getByText(/ثبت تازه/)).toBeVisible();
  });
});
