import { expect, test } from "@playwright/test";
import { demoLogin, navTo, shot } from "./helpers";

test.describe("N6: chat", () => {
  test("a worker writes to the manager, the manager answers live, read marks show", async ({ browser }, info) => {
    const mobile = info.project.name !== "desktop";
    const text = `راش‌های کافه آپلود شد (${info.project.name})`;
    const reply = `عالی، ممنون (${info.project.name})`;

    const editor = await (await browser.newContext(info.project.use)).newPage();
    await demoLogin(editor, "editor");
    await navTo(editor, mobile, "chat");
    await editor.getByTestId("chat-new").click();
    await editor.getByTestId("chat-contact-سارا (مدیر نمونه)").click();
    await editor.getByTestId("chat-input").fill(text);
    await editor.getByTestId("chat-send").click();
    await expect(editor.getByTestId("chat-messages")).toContainText(text);

    const admin = await (await browser.newContext(info.project.use)).newPage();
    await demoLogin(admin, "admin");
    await navTo(admin, mobile, "chat");
    const item = admin.getByTestId("chat-item-علی (تدوینگر نمونه)");
    await expect(item).toBeVisible();
    await shot(admin, info, "n6-chat-list");
    await item.click();
    await expect(admin.getByTestId("chat-messages")).toContainText(text);
    await admin.getByTestId("chat-input").fill(reply);
    await admin.getByTestId("chat-input").press("Enter");
    await expect(admin.getByTestId("chat-messages")).toContainText(reply);
    await shot(admin, info, "n6-chat-thread");

    // Live on the worker's screen, and the worker's message shows as seen.
    await expect(editor.getByTestId("chat-messages")).toContainText(reply, { timeout: 15_000 });
    await shot(editor, info, "n6-chat-worker");
    await expect(admin.getByTestId("chat-seen").first()).toBeVisible({ timeout: 15_000 });
  });

  test("the manager makes a group; a client only sees the managers", async ({ page }, info) => {
    const mobile = info.project.name !== "desktop";
    await demoLogin(page, "admin");
    await navTo(page, mobile, "chat");
    await page.getByTestId("chat-new-group").click();
    await page.getByTestId("chat-group-title").fill(`تیم تدوین ${info.project.name}`);
    await page.getByTestId("chat-member-علی (تدوینگر نمونه)").check();
    await shot(page, info, "n6-chat-group");
    await page.getByTestId("chat-group-save").click();
    await expect(page.getByTestId("chat-thread")).toContainText(`تیم تدوین ${info.project.name}`);

    const client = await (await page.context().browser()!.newContext(info.project.use)).newPage();
    await demoLogin(client, "client");
    await navTo(client, mobile, "chat");
    await client.getByTestId("chat-new").click();
    const contacts = client.getByTestId("chat-contacts");
    await expect(contacts).toContainText("سارا (مدیر نمونه)");
    await expect(contacts).not.toContainText("علی (تدوینگر نمونه)");
  });
});
