import { expect, test } from "@playwright/test";

import { signedIn } from "./theInstance";

/**
 * Files in a real browser: the things jsdom cannot say.
 *
 * A real upload whose body is a real file, a real download whose bytes are
 * compared, a breadcrumb at a phone's width, and a change made elsewhere
 * arriving on a screen nobody is touching. The screen's own behaviour — the
 * dialogs, the refusals, the five states — is proved in jsdom
 * (`src/files/Files.test.tsx`); this is the half that needs an engine.
 */
test.describe("Files", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("takes a file, gives it back byte for byte, and keeps the link after a rename", async ({
    page,
  }) => {
    // Every byte value, so that anything that treats the body as text breaks
    // here rather than on somebody's photograph.
    const bytes = Buffer.from(Array.from({ length: 256 }, (_, value) => value));
    const name = `bytes-${Date.now()}.bin`;

    await page.goto("/files");
    await expect(page.getByRole("heading", { name: "Files" })).toBeVisible();

    await page.getByLabel("Files to upload").setInputFiles({
      name,
      mimeType: "application/octet-stream",
      buffer: bytes,
    });

    const row = page.getByRole("listitem").filter({ hasText: name });
    await expect(row).toBeVisible();

    // The download is the instance's own address, fetched with the session the
    // browser already has. What comes back is compared byte for byte.
    const download = await page.request.get(
      await row.getByRole("link", { name: `Download ${name}` }).getAttribute("href") ?? "",
    );

    expect(download.ok(), await download.text()).toBeTruthy();
    expect(Buffer.compare(Buffer.from(await download.body()), bytes)).toBe(0);
    expect(download.headers()["content-disposition"]).toContain("attachment");

    const address =
      (await row.getByRole("link", { name: `Download ${name}` }).getAttribute("href")) ?? "";

    // Renamed, and the same address still answers: the reference is the id
    // (`docs/mvp-plan.md`, PERSONAL-E6).
    const renamed = `renamed-${name}`;

    await row.getByRole("button", { name: `Rename or move ${name}` }).click();

    // Inside the dialog, because "Rename or move X" is what every row's own
    // button is called: `getByLabel` over the whole page would find those too.
    const dialog = page.getByRole("dialog");

    await dialog.getByRole("textbox", { name: "Name" }).fill(renamed);
    await dialog.getByRole("button", { name: "Save" }).click();

    await expect(page.getByRole("listitem").filter({ hasText: renamed })).toBeVisible();

    const again = await page.request.get(address);

    expect(again.ok(), await again.text()).toBeTruthy();
    expect(Buffer.compare(Buffer.from(await again.body()), bytes)).toBe(0);
  });

  test("walks into a folder at an address a link can carry", async ({ page }) => {
    const folder = `Reisen ${Date.now()}`;

    await page.goto("/files");

    await page.getByRole("button", { name: "New folder" }).click();

    const dialog = page.getByRole("dialog");

    await dialog.getByRole("textbox", { name: "Name" }).fill(folder);
    await dialog.getByRole("button", { name: "Make it" }).click();

    await page.getByRole("button", { name: folder, exact: true }).click();

    // The address changed, which is what makes a link into a folder a link.
    await expect(page).toHaveURL(/\/files\/[0-9a-f-]{36}$/);
    await expect(page.getByRole("navigation", { name: "Where you are" })).toContainText(folder);

    const inside = page.url();

    await page.goto("/");
    await page.goto(inside);

    await expect(page.getByRole("navigation", { name: "Where you are" })).toContainText(folder);

    await page.getByRole("button", { name: "All files" }).click();
    await expect(page).toHaveURL(/\/files$/);
  });

  test("brings a change made elsewhere onto the screen without a reload", async ({ page }) => {
    const name = `arrived-${Date.now()}.txt`;

    await page.goto("/files");
    await expect(page.getByRole("heading", { name: "Files" })).toBeVisible();

    // Another device, or an agent: the same API, the same session, and nothing
    // touching this page.
    const stored = await page.request.post(`/api/files/content?name=${encodeURIComponent(name)}`, {
      headers: {
        "Content-Type": "text/plain",
        "X-Personalaffe-CSRF": "1",
        Origin: new URL(page.url()).origin,
      },
      data: "written from somewhere else",
    });

    expect(stored.ok(), await stored.text()).toBeTruthy();

    await expect(page.getByRole("listitem").filter({ hasText: name })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("is usable at a phone's width, with a keyboard", async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 780 });
    await page.goto("/files");

    await expect(page.getByRole("heading", { name: "Files" })).toBeVisible();

    // Nothing runs off the side: a horizontal scrollbar on a file list is a
    // list nobody can read on a phone.
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(1);

    // The dialog opens, takes a name and closes, with the keyboard alone.
    await page.getByRole("button", { name: "New folder" }).click();
    await expect(page.getByRole("dialog")).toBeVisible();

    await page.keyboard.type(`Belege ${Date.now()}`);
    await page.keyboard.press("Escape");

    await expect(page.getByRole("dialog")).toBeHidden();
  });

  test("puts a deleted file in the Trash and says so", async ({ page }) => {
    const name = `going-${Date.now()}.txt`;

    await page.goto("/files");

    await page.getByLabel("Files to upload").setInputFiles({
      name,
      mimeType: "text/plain",
      buffer: Buffer.from("into the Trash"),
    });

    const row = page.getByRole("listitem").filter({ hasText: name });
    await expect(row).toBeVisible();

    await row.getByRole("button", { name: `Delete ${name}` }).click();

    // Nothing asked, because nothing was destroyed — the opposite of the
    // Scratchpad's dialog, and the sentence says where it went.
    await expect(page.getByText(`${name} is in the Trash`)).toBeVisible();

    await page.goto("/trash");
    await expect(page.getByText(name)).toBeVisible();
  });
});
