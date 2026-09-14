import { expect, test } from "@playwright/test";

import { aPage, signedIn } from "./theInstance";

/**
 * CodeMirror, in a browser, because there is nowhere else it can be checked.
 * It writes on a `contenteditable` and measures a selection it draws; jsdom
 * lays nothing out and answers every measurement with zero, so every jsdom test
 * of the field writes into `StandInEditor` instead. What that leaves unproven
 * is the editor itself, and this is where it is proven.
 *
 * <b>It is driven through Knowledge</b>, which is the screen that writes
 * Markdown. Until PERSONAL-E7 these checks drove `/editor`, a scaffold that
 * existed because the field had no screen yet and said so in its own text; it
 * is gone, and this is the real work it was standing in for.
 */
test.describe("the shared editor", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
    await aPage(page, `Editor ${Date.now()}`);
  });

  test("takes typing, and shows it rendered beside the source", async ({ page }) => {
    const field = page.getByRole("textbox", { name: "The page" });
    await expect(field).toBeVisible();

    await field.click();
    await page.keyboard.type("# A heading\n\nSomething **bold**, and `code`.");

    const preview = page.getByLabel("The page, preview");

    await expect(preview.getByText("A heading")).toBeVisible();
    await expect(preview.locator("strong")).toHaveText("bold");
    await expect(preview.locator("code")).toHaveText("code");
  });

  test("marks a selection from the toolbar", async ({ page }) => {
    const field = page.getByRole("textbox", { name: "The page" });

    await field.click();
    await page.keyboard.type("emphasis");
    await page.keyboard.press("ControlOrMeta+a");

    await page.getByRole("button", { name: "Bold" }).click();

    await expect(page.getByLabel("The page, preview").locator("strong")).toHaveText("emphasis");
  });

  test("reaches the toolbar with the keyboard and moves inside it with the arrows", async ({
    page,
  }) => {
    const toolbar = page.getByRole("toolbar", { name: "The page, formatting" });
    const bold = toolbar.getByRole("button", { name: "Bold" });

    // The marks are dead until the editor behind them is there, so this waits
    // for the chunk rather than racing it.
    await expect(bold).toBeEnabled();

    // One tab stop, not nine: a toolbar in front of the text would otherwise
    // put every mark between the keyboard and the field it acts on.
    await expect(bold).toHaveAttribute("tabindex", "0");
    await expect(toolbar.getByRole("button", { name: "Italic" })).toHaveAttribute("tabindex", "-1");

    await bold.focus();
    await page.keyboard.press("ArrowRight");

    await expect(toolbar.getByRole("button", { name: "Italic" })).toBeFocused();

    await page.keyboard.press("ArrowLeft");
    await expect(bold).toBeFocused();
  });

  test("never interprets HTML that arrives in a body", async ({ page }) => {
    const field = page.getByRole("textbox", { name: "The page" });

    await field.click();
    await page.keyboard.type('before <img src=x onerror="window.__ran = true"> after');

    const preview = page.getByLabel("The page, preview");
    await expect(preview).toContainText("before");

    expect(await preview.locator("img").count()).toBe(0);
    expect(await page.evaluate(() => (window as { __ran?: boolean }).__ran)).toBeUndefined();
  });

  test("writes nothing until it is asked to, and then writes once", async ({ page }) => {
    const writes: string[] = [];

    page.on("request", (request) => {
      if (request.method() !== "GET" && request.url().includes("/api/knowledge")) {
        writes.push(`${request.method()} ${new URL(request.url()).pathname}`);
      }
    });

    const field = page.getByRole("textbox", { name: "The page" });
    await field.click();
    await page.keyboard.type("nothing is kept until it is saved");

    await page.waitForTimeout(1000);
    expect(writes).toEqual([]);

    // ⌘/Ctrl + Enter from inside the text is what the field's hint promises.
    await page.keyboard.press("ControlOrMeta+Enter");

    await expect(page.getByText("Saved.")).toBeVisible();
    expect(writes.filter((one) => one.startsWith("PUT"))).toHaveLength(1);
  });
});
