import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, shot } from "./helpers";

test.describe("M5: leave, absence, calendar and report", () => {
  test("worker asks for leave; the manager approves; calendar and payroll report", async ({ browser }, info) => {
    const writer = await (await browser.newContext()).newPage();
    await demoLogin(writer, "writer");
    await writer.goto(`${DEMO_URL}/attendance`);
    await expect(writer.getByTestId("my-leave")).toBeVisible();
    // Cancel anything pending from an earlier device run so the dates don't overlap.
    // Cancelling one re-renders the list, so always take the first remaining one.
    const cancel = writer.getByRole("button", { name: "لغو درخواست" });
    for (let i = 0; i < 20 && (await cancel.count()) > 0; i++) {
      const before = await cancel.count();
      await cancel.first().click();
      await expect(cancel).toHaveCount(before - 1);
    }
    await writer.getByTestId("leave-open").click();
    await writer.getByTestId("leave-type").selectOption({ index: 1 });
    await writer.getByText("ساعتی", { exact: true }).click();
    const days = { desktop: 10, pixel7: 11, phone360: 12 }[info.project.name] ?? 13;
    const d = new Date(Date.now() + days * 864e5);
    const j = new Intl.DateTimeFormat("en-u-ca-persian-nu-latn", { year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(d);
    const p = (t: string) => j.find((x) => x.type === t)!.value;
    await writer.getByTestId("leave-from").fill(`${p("year").replace(/\D/g, "")}/${p("month")}/${p("day")}`);
    await writer.getByTestId("leave-reason").fill(`کار بانکی ${info.project.name}`);
    await shot(writer, info, "m5-leave-form");
    await writer.getByTestId("leave-save").click();
    await expect(writer.getByText("درخواست فرستاده شد")).toBeVisible();
    await expect(writer.getByTestId("my-leave")).toContainText("در انتظار تأیید");
    await shot(writer, info, "m5-my-leave");

    const admin = await (await browser.newContext()).newPage();
    await demoLogin(admin, "admin");
    await admin.goto(`${DEMO_URL}/attendance/live`);
    const row = admin.getByTestId("leave-pending").locator("li", { hasText: `کار بانکی ${info.project.name}` });
    await expect(row).toBeVisible();
    await expect(admin.getByTestId("absences-open")).toContainText("محمد");
    await shot(admin, info, "m5-approvals");
    await row.getByTestId("leave-approve").click();
    await expect(row).toHaveCount(0);
    // Live: the writer's page updates without reload.
    await expect(writer.getByTestId("my-leave")).toContainText("تأیید شد", { timeout: 8000 });

    await admin.goto(`${DEMO_URL}/attendance/calendar`);
    await expect(admin.getByTestId("calendar")).toContainText("مریم");
    await shot(admin, info, "m5-calendar");
    await admin.goto(`${DEMO_URL}/attendance/report`);
    await expect(admin.getByTestId("report")).toContainText("مریم");
    const download = admin.waitForEvent("download");
    await admin.getByTestId("report-excel").click();
    expect((await download).suggestedFilename()).toMatch(/\.xlsx$/);
    await shot(admin, info, "m5-report");
  });
});
