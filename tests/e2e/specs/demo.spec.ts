import { expect, test } from "@playwright/test";
import { DEMO_URL, demoLogin, menuHas, navTo, shot } from "./helpers";

test.describe("demo install", () => {
  test("login page: OTP form and one-tap demo roles", async ({ page }, info) => {
    await page.goto(`${DEMO_URL}/login`);
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await expect(page.getByTestId("login-phone")).toBeVisible();
    for (const r of ["admin", "editor", "writer", "client", "marketer"]) await expect(page.getByTestId(`demo-${r}`)).toBeVisible();
    const box = await page.getByTestId("demo-admin").boundingBox();
    expect(box!.height).toBeGreaterThanOrEqual(48);
    await shot(page, info, "login");

    await page.getByTestId("login-phone").fill("0912");
    await page.getByTestId("login-send").click();
    await expect(page.getByText("Die Handynummer ist ungültig")).toBeVisible();
    await shot(page, info, "login-invalid-phone");
  });

  test("admin: dashboard, roles CRUD with undo, English + LTR, dark theme", async ({ page }, info) => {
    const isMobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await expect(page.getByText("Demo-Modus")).toBeVisible();
    await shot(page, info, "admin-dashboard");

    await navTo(page, isMobile, "roles");
    await expect(page).toHaveURL(/\/roles/);
    const rows = page.locator(isMobile ? "[data-testid^=role-card-]" : "[data-testid^=role-row-]");
    await expect(rows.filter({ hasText: "Inhaber" }).first()).toBeVisible();
    await shot(page, info, "roles-list");

    // Create
    const run = Date.now().toString(36);
    const name = `Grafiker ${info.project.name} ${run}`;
    await page.getByTestId("role-new").click();
    await page.getByTestId("role-name-fa").fill(name);
    await page.getByTestId("scope-work.view-assigned").click();
    await page.getByTestId("scope-work.run-assigned").click();
    await shot(page, info, "role-create-form");
    await page.getByTestId("role-save").click();
    await expect(page.getByText("Rolle angelegt.")).toBeVisible();
    const row = rows.filter({ hasText: name });
    await expect(row).toContainText("2 Berechtigungen");
    await shot(page, info, "role-created");

    // Edit
    await row.getByRole("button", { name: "Bearbeiten" }).click();
    await page.getByTestId("scope-work.view-all").click();
    await page.getByTestId("scope-attendance.self-own").click();
    await page.getByTestId("role-save").click();
    await expect(page.getByText("Rollenänderungen gespeichert.")).toBeVisible();
    await expect(row).toContainText("3 Berechtigungen");
    await shot(page, info, "role-edited");

    // Search
    await page.getByTestId("role-search").fill(run);
    await expect(row).toBeVisible();
    await expect(rows.filter({ hasText: "Inhaber" })).toHaveCount(0);
    await page.getByTestId("role-search").fill("");

    // Delete with in-page confirmation, then undo from the toast
    await row.getByRole("button", { name: "Löschen" }).click();
    await shot(page, info, "role-delete-confirm");
    await page.getByRole("button", { name: "Ja, löschen" }).click();
    await expect(row).toHaveCount(0);
    await shot(page, info, "role-deleted-undo-toast");
    await page.getByRole("button", { name: "Wiederherstellen" }).click();
    await expect(page.getByText("Rolle wiederhergestellt.")).toBeVisible();
    await expect(row).toBeVisible();

    // Delete for good and see it under "show deleted"
    await row.getByRole("button", { name: "Löschen" }).click();
    await page.getByRole("button", { name: "Ja, löschen" }).click();
    await expect(row).toHaveCount(0);
    await page.getByTestId("role-show-deleted").check();
    await expect(row).toContainText("Gelöscht");
    await shot(page, info, "roles-show-deleted");

    // System role cannot be deleted
    const owner = rows.filter({ hasText: "Inhaber" }).first();
    await expect(owner.getByRole("button", { name: "Löschen" })).toHaveCount(0);

    // Language: English switches direction without reload
    await navTo(page, isMobile, "profile");
    await page.getByTestId("lang-en").check();
    await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
    await expect(page.getByRole("heading", { name: "My account" })).toBeVisible();
    await shot(page, info, "profile-english-ltr");
    await page.getByTestId("theme-dark").check();
    await expect(page.locator("html")).toHaveClass(/dark/);
    await shot(page, info, "profile-english-dark");
    await page.getByTestId("lang-fa").check();
    await page.getByTestId("theme-light").check();
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  });

  test("editor and client see only what their roles allow", async ({ page }, info) => {
    const isMobile = info.project.name !== "desktop";
    await demoLogin(page, "editor");
    expect(await menuHas(page, isMobile, "roles")).toBe(false);
    expect(await menuHas(page, isMobile, "settings/brand")).toBe(false);
    await expect(page.getByText("Arbeit und Timer")).toBeVisible();
    await shot(page, info, "editor-dashboard");
    await page.goto(`${DEMO_URL}/roles`);
    await expect(page).toHaveURL(/\/dashboard/);

    await navTo(page, isMobile, "profile");
    await page.getByTestId("logout").click();
    await expect(page).toHaveURL(/\/login/);

    await demoLogin(page, "client");
    await expect(page.getByTestId("portal")).toBeVisible();
    await shot(page, info, "client-dashboard");
  });

  test("session survives a reload (refresh cookie) and 404 page", async ({ page }, info) => {
    await demoLogin(page, "writer");
    await page.reload();
    await expect(page.getByTestId("dashboard-hello")).toBeVisible();
    await page.goto(`${DEMO_URL}/no-such-page`);
    await expect(page.getByText("Diese Seite wurde nicht gefunden")).toBeVisible();
    await shot(page, info, "not-found");
  });
});
