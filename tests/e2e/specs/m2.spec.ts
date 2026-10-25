import { expect, type Page, test } from "@playwright/test";
import { DEMO_URL, demoLogin, shot } from "./helpers";

const rowsOf = (page: Page, id: string, mobile: boolean) => page.locator(mobile ? `[data-testid^=${id}-card-]` : `[data-testid^=${id}-row-]`);

test.describe("M2: clients, packages, services, users, platforms", () => {
  test("admin: catalog and clients end to end", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    const run = `${info.project.name}-${Date.now().toString(36)}`;
    await demoLogin(page, "admin");
    await expect(page.getByTestId("onboarding")).toBeVisible();
    await shot(page, info, "m2-dashboard-onboarding");

    // Services: seeded list, add a stage + form field to "ریلز"
    await page.goto(`${DEMO_URL}/services`);
    const services = rowsOf(page, "services", mobile);
    await expect(services.filter({ hasText: "ریلز" }).first()).toBeVisible();
    await shot(page, info, "m2-services");
    await services.filter({ hasText: "ریلز" }).first().getByRole("button", { name: "ویرایش" }).click();
    await expect(page.getByTestId("stage-0")).toBeVisible();
    await shot(page, info, "m2-service-editor");
    await page.getByRole("button", { name: "انصراف" }).click();

    // Packages: create one with a quota
    await page.goto(`${DEMO_URL}/packages`);
    const packages = rowsOf(page, "packages", mobile);
    await expect(packages.filter({ hasText: "طلایی" }).first()).toBeVisible();
    await shot(page, info, "m2-packages");
    const pkgName = `پکیج تست ${run}`;
    await page.getByTestId("packages-new").click();
    await page.getByTestId("package-name").fill(pkgName);
    await page.getByTestId("package-sku").fill(`T-${run}`.slice(0, 30));
    await page.getByTestId("package-status").selectOption("active");
    await page.getByTestId("package-price").fill("۱۲۰۰۰۰۰۰");
    await page.getByTestId("quota-add").click();
    await page.getByTestId("quota-0").locator("select").selectOption({ index: 1 });
    await shot(page, info, "m2-package-form");
    await page.getByTestId("package-editor-save").click();
    await expect(page.getByText("ساخته شد.")).toBeVisible();
    const pkgRow = packages.filter({ hasText: pkgName });
    await expect(pkgRow).toContainText("۱۲٬۰۰۰٬۰۰۰");

    // Clients: create, then assign the new package
    await page.goto(`${DEMO_URL}/clients`);
    const clients = rowsOf(page, "clients", mobile);
    await expect(clients.filter({ hasText: "کافه نمونه" }).first()).toBeVisible();
    await shot(page, info, "m2-clients");
    const clientName = `مشتری تست ${run}`;
    await page.getByTestId("clients-new").click();
    await page.getByTestId("client-business").fill(clientName);
    await page.getByTestId("client-mobile").fill("09171234567");
    await page.getByTestId("account-add").click();
    await page.getByTestId("account-0").getByRole("textbox").fill("test_page");
    await page.getByTestId("client-editor-save").click();
    await expect(page.getByText("ساخته شد.")).toBeVisible();
    const clientRow = clients.filter({ hasText: clientName });
    await expect(clientRow).toContainText("بدون پکیج فعال");
    await clientRow.getByRole("button", { name: "ویرایش" }).click();
    const option = page.getByTestId("assign-package").locator("option", { hasText: pkgName });
    await page.getByTestId("assign-package").selectOption((await option.getAttribute("value"))!);
    await page.getByTestId("assign-submit").click();
    await expect(page.getByText("پکیج تخصیص داده شد.")).toBeVisible();
    await expect(page.getByTestId("client-packages")).toContainText(pkgName);
    await shot(page, info, "m2-client-package");
    await page.getByRole("button", { name: "انصراف" }).click();
    await expect(clientRow).toContainText(pkgName);

    // Delete + undo
    await clientRow.getByRole("button", { name: "حذف" }).click();
    await page.getByTestId("clients-confirm-delete").filter({ visible: true }).click();
    await expect(clientRow).toHaveCount(0);
    await page.getByRole("button", { name: "برگرداندن" }).click();
    await expect(clientRow).toBeVisible();

    // Users
    await page.goto(`${DEMO_URL}/users`);
    await expect(rowsOf(page, "users", mobile).first()).toBeVisible();
    await shot(page, info, "m2-users");
    await page.getByTestId("users-new").click();
    await page.getByTestId("user-name").fill(`تدوینگر ${run}`);
    await page.getByTestId("user-phone").fill(`0919${String(Date.now()).slice(-7)}`);
    await shot(page, info, "m2-user-form");
    await page.getByRole("button", { name: "انصراف" }).click();

    // Platforms
    await page.goto(`${DEMO_URL}/settings/platforms`);
    await expect(page.getByTestId("platform-instagram")).toBeVisible();
    await shot(page, info, "m2-platforms");
  });
});
