import { expect, test } from "@playwright/test";

import { signedIn } from "./theInstance";

/**
 * The same application at a phone's width. Not a reduced one: the navigation is
 * the same component behind a button, and everything reachable on a desk is
 * reachable here.
 */
test.describe("on a phone", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("keeps the navigation behind a button and closes it behind whoever walks through", async ({
    page,
  }) => {
    // Off the screen until it is asked for: the width is the whole of the
    // reason, and there is no second navigation for small screens.
    await expect(
      page.getByRole("navigation", { name: "The workspace" }).getByRole("link", { name: "Files" }),
    ).toBeHidden();

    await page.getByRole("button", { name: /sidebar/i }).click();

    const drawer = page.getByRole("dialog");
    await drawer.getByRole("link", { name: "Files" }).click();

    await expect(page).toHaveURL(/\/files$/);
    await expect(drawer).toBeHidden();
    await expect(page.getByRole("heading", { name: "Files" })).toBeVisible();
  });

  test("takes a Scratchpad entry on a phone, and copies one back", async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);

    const text = `written on a phone ${Date.now()}`;

    await page.goto("/scratchpad");

    const box = page.getByRole("textbox", { name: "New entry" });
    await box.click();
    await page.keyboard.type(text);
    await page.getByRole("button", { name: "Put it down" }).click();

    const entry = page.getByRole("listitem").filter({ hasText: text });
    await expect(entry).toBeVisible();

    await entry.getByRole("button", { name: /^Copy / }).click();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(text);

    // Taken away again, so that the next check starts where this one did.
    await entry.getByRole("button", { name: /^Delete / }).click();
    await page.getByRole("button", { name: "Delete for good" }).click();
    await expect(entry).toHaveCount(0);
  });

  test("never scrolls sideways", async ({ page }) => {
    for (const address of ["/", "/scratchpad", "/files", "/knowledge", "/tasks", "/settings/applications", "/trash"]) {
      await page.goto(address);
      await page.waitForLoadState("networkidle");

      const overflowing = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
      );

      expect(overflowing, `${address} scrolls sideways by ${overflowing}px`).toBeLessThanOrEqual(1);
    }
  });

  test("reaches the account menu and the palette", async ({ page }) => {
    await page.getByRole("button", { name: "Command palette" }).click();
    await expect(page.getByRole("combobox", { name: "Go anywhere, or type a command" })).toBeVisible();
    await page.keyboard.press("Escape");

    await page.getByRole("button", { name: /^Account:/ }).click();
    await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeVisible();
  });
});
