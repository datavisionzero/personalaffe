import { expect, test, type Page } from "@playwright/test";

import { signedIn } from "./theInstance";

/**
 * The Scratchpad in a real browser: the things jsdom cannot say.
 *
 * What it lays out, what it downloads, and what the clipboard actually
 * receives. The screen's own behaviour — the dialog, the hold, the states — is
 * proved in jsdom (`src/scratchpad/Scratchpad.test.tsx`); this is the half that
 * needs an engine.
 */
test.describe("the Scratchpad", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("downloads no editor on this route", async ({ page }) => {
    const chunks: string[] = [];

    page.on("request", (request) => {
      if (request.resourceType() === "script") {
        chunks.push(new URL(request.url()).pathname);
      }
    });

    await page.goto("/scratchpad");
    await expect(page.getByRole("heading", { name: "Scratchpad" })).toBeVisible();
    await page.waitForLoadState("networkidle");

    // The Scratchpad is plain text, so the capture box is a `<textarea>` and
    // nothing here pulls in the Markdown pipeline or CodeMirror behind it. The
    // lazy chunks of PERSONAL-E4 exist for exactly this, and asserting it
    // against what the browser asked for is the only way to keep it true.
    expect(
      chunks.filter((path) => /\/(Editor|Editing)-/.test(path)),
      `an editor chunk was fetched: ${chunks.join(", ")}`,
    ).toEqual([]);
  });

  test("captures with the keyboard, copies with one click, and deletes only when asked", async ({
    page,
    context,
  }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);

    const text = `a note from a browser check ${Date.now()}`;

    await page.goto("/scratchpad");

    const box = page.getByRole("textbox", { name: "New entry" });
    await box.click();
    await page.keyboard.type(text);
    await page.keyboard.press("ControlOrMeta+Enter");

    const entry = page.getByRole("listitem").filter({ hasText: text });
    await expect(entry).toBeVisible();

    // The clipboard of whichever device is looking at it, with the exact text.
    await entry.getByRole("button", { name: /^Copy / }).click();
    await expect(entry.getByText("Copied to this device's clipboard.")).toBeVisible();
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(text);

    // Pinning takes the clock off it, and says so where the expiry was.
    await entry.getByRole("button", { name: /^Pin / }).click();
    await expect(entry.getByText(/pinned, so it never expires/)).toBeVisible();

    await entry.getByRole("button", { name: /^Unpin / }).click();
    await expect(entry.getByText(/^\d/)).toBeVisible();

    // Deleting asks first, and cancelling leaves the entry where it was.
    await entry.getByRole("button", { name: /^Delete / }).click();
    await expect(page.getByRole("dialog").getByText(/does not go to the Trash/)).toBeVisible();
    await page.getByRole("button", { name: "Keep it" }).click();
    await expect(entry).toBeVisible();

    await deleted(page, text);

    // And it is gone for good: nothing in this product puts it in the Trash.
    await page.goto("/trash");
    await expect(page.getByText(text)).toHaveCount(0);
  });

  test("is usable by keyboard from the box to the acts on a row", async ({ page }) => {
    const text = `reachable by keyboard ${Date.now()}`;

    await page.goto("/scratchpad");

    const box = page.getByRole("textbox", { name: "New entry" });
    await box.click();
    await page.keyboard.type(text);
    await page.keyboard.press("ControlOrMeta+Enter");

    const entry = page.getByRole("listitem").filter({ hasText: text });
    await expect(entry).toBeVisible();

    // Every act in a row has a name a screen reader reads, and the excerpt in
    // it is what tells three rows of "Copy" apart.
    for (const act of [/^Copy /, /^Pin /, /^Delete /]) {
      const button = entry.getByRole("button", { name: act });

      await button.focus();
      await expect(button).toBeFocused();
    }

    await deleted(page, text);
  });
});

/** Takes one entry away again, so that the next check starts where this did. */
async function deleted(page: Page, text: string) {
  const entry = page.getByRole("listitem").filter({ hasText: text });

  await entry.getByRole("button", { name: /^Delete / }).click();
  await page.getByRole("button", { name: "Delete for good" }).click();

  await expect(entry).toHaveCount(0);
}
