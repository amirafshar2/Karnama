import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N4: credential vault, client profile, package reminders", () => {
  test("admin opens a FullAccess client's password with an SMS code; it hides itself", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "clients");
    const row = page.locator("tr, li").filter({ hasText: "کافه نمونه" }).filter({ visible: true }).first();
    await row.getByRole("button", { name: "ویرایش" }).click();
    const vault = page.getByTestId("vault");
    await expect(vault).toContainText("رمز ثبت شده");
    await expect(page.getByTestId("client-quotas")).toBeVisible();
    await vault.scrollIntoViewIfNeeded();
    await shot(page, info, "n4-vault-list");

    await page.getByTestId("vault-reveal-کافه_نمونه").click();
    const demo = page.getByTestId("vault-demo-code");
    await expect(demo).toBeVisible();
    const code = (await demo.textContent())!.replace(/[^0-9۰-۹]/g, "").replace(/[۰-۹]/g, (d) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)));
    expect(code).toHaveLength(6);
    await shot(page, info, "n4-vault-otp");
    await page.getByTestId("vault-code").fill(code);
    const secret = page.getByTestId("vault-secret");
    // The client may have changed the password in an earlier project run; the recovery email stays.
    await expect(secret).toContainText("cafe@example.com");
    const password = (await page.getByTestId("vault-value-رمز عبور").textContent())!;
    expect(password.length).toBeGreaterThan(5);
    await shot(page, info, "n4-vault-revealed");
    await page.getByTestId("vault-hide").click();
    await expect(secret).toHaveCount(0);
    await expect(page.getByText(password, { exact: true })).toHaveCount(0);
  });

  test("admin sets the reminder days for package endings", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "packages");
    await page.getByTestId("notices-open").click();
    await expect(page.getByTestId("notices-days")).toContainText("۷ روز قبل");
    await page.getByTestId("notices-day").fill("14");
    await page.getByTestId("notices-day-add").click();
    await expect(page.getByTestId("notices-days")).toContainText("۱۴ روز قبل");
    await shot(page, info, "n4-notices");
    await page.getByTestId("notices-save").click();
    await expect(page.getByText("ذخیره شد.")).toBeVisible();
  });

  test("client sees its profile, asks for renewal once and changes its own password (write-only)", async ({ page }, info) => {
    await demoLogin(page, "client");
    await page.getByTestId("portal-profile-link").click();
    const profile = page.getByTestId("portal-profile");
    await expect(profile).toContainText("کافه نمونه");
    await expect(page.getByTestId("profile-package")).toBeVisible();
    await shot(page, info, "n4-client-profile");

    if (await page.getByTestId("renew-open").isVisible()) {
      await page.getByTestId("renew-open").click();
      await page.getByTestId("renew-note").fill("همین پکیج را برای یک ماه دیگر می‌خواهیم");
      await page.getByTestId("renew-send").click();
      await expect(page.getByTestId("renew-done")).toBeVisible();
    }

    await page.getByTestId("vault-edit-کافه_نمونه").click();
    await expect(page.getByRole("dialog")).toContainText("Business Suite");
    await page.getByTestId("vault-password").fill(`Client-New-${info.project.name}`);
    await shot(page, info, "n4-client-password");
    await page.getByTestId("vault-form-save").click();
    await expect(page.getByText("ذخیره شد.")).toBeVisible();
    await expect(page.getByTestId("vault-item-کافه_نمونه")).toContainText("ثبت توسط مشتری");
    await expect(page.getByText(`Client-New-${info.project.name}`)).toHaveCount(0);
  });
});
