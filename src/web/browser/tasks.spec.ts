import { expect, test, type Page } from "@playwright/test";

import { signedIn } from "./theInstance";

/**
 * Tasks in a real browser: the things jsdom cannot say.
 *
 * The keyboard from capture to tick, a change made elsewhere arriving on a
 * screen nobody is touching, and the whole thing at a phone's width. The
 * screen's own behaviour — the grouping, the refusals, the date — is proved in
 * jsdom (`src/tasks/Tasks.test.tsx`).
 */
test.describe("Tasks", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("captures, ticks and reopens with the keyboard alone", async ({ page }) => {
    const title = `Milch holen ${Date.now()}`;

    await aList(page, `Einkauf ${Date.now()}`);

    // Capture is one field and Enter: anything that took a dialog to add a line
    // to a list would not be "quickly capturing" (VISION §6.4).
    const box = page.getByLabel("What has to be done");

    await box.click();
    await page.keyboard.type(title);
    await page.keyboard.press("Enter");

    const row = page.getByRole("listitem").filter({ hasText: title });
    await expect(row).toBeVisible();

    const box2 = row.getByRole("checkbox", { name: `Complete ${title}` });

    await box2.focus();
    await page.keyboard.press("Space");

    await expect(page.getByRole("heading", { name: "Done" })).toBeVisible();
    await expect(row.getByRole("checkbox", { name: `Reopen ${title}` })).toBeChecked();

    await row.getByRole("checkbox", { name: `Reopen ${title}` }).focus();
    await page.keyboard.press("Space");

    await expect(row.getByRole("checkbox", { name: `Complete ${title}` })).not.toBeChecked();
  });

  test("moves a task a step at a time, without a pointer", async ({ page }) => {
    const list = `Einkauf ${Date.now()}`;

    await aList(page, list);

    for (const title of ["eins", "zwei", "drei"]) {
      await page.getByLabel("What has to be done").fill(title);
      await page.keyboard.press("Enter");
      await expect(page.getByRole("listitem").filter({ hasText: title })).toBeVisible();
    }

    await expect(titles(page)).resolves.toEqual(["eins", "zwei", "drei"]);

    await page.getByRole("button", { name: "Move drei up" }).click();
    await expect(async () => expect(await titles(page)).toEqual(["eins", "drei", "zwei"])).toPass();

    await page.getByRole("button", { name: "Move eins down" }).click();
    await expect(async () => expect(await titles(page)).toEqual(["drei", "eins", "zwei"])).toPass();
  });

  test("brings a task captured elsewhere onto the list without a reload", async ({ page }) => {
    const list = `Einkauf ${Date.now()}`;
    const title = `von woanders ${Date.now()}`;

    await aList(page, list);

    const id = page.url().split("/").pop();

    // Another device, or an agent: the same API, the same session, and nothing
    // touching this page.
    const captured = await page.request.post(`/api/tasks/lists/${id}/tasks`, {
      headers: {
        "X-Personalaffe-CSRF": "1",
        Origin: new URL(page.url()).origin,
      },
      data: { title, description: "", due_on: null },
    });

    expect(captured.ok(), await captured.text()).toBeTruthy();

    await expect(page.getByRole("listitem").filter({ hasText: title })).toBeVisible({
      timeout: 30_000,
    });
  });

  test("is usable at a phone's width", async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 780 });

    await aList(page, `Einkauf ${Date.now()}`);

    await page.getByLabel("What has to be done").fill("Milch holen");
    await page.keyboard.press("Enter");

    await expect(page.getByRole("listitem").filter({ hasText: "Milch holen" })).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );

    expect(overflow).toBeLessThanOrEqual(1);
  });

  test("puts a deleted task in the Trash", async ({ page }) => {
    const title = `wegwerfen ${Date.now()}`;

    await aList(page, `Einkauf ${Date.now()}`);

    await page.getByLabel("What has to be done").fill(title);
    await page.keyboard.press("Enter");

    const row = page.getByRole("listitem").filter({ hasText: title });
    await expect(row).toBeVisible();

    await row.getByRole("button", { name: `Delete ${title}` }).click();
    await expect(row).toHaveCount(0);

    await page.goto("/trash");
    await expect(page.getByText(title)).toBeVisible();
  });
});

/** A list of its own for each check, opened and ready to capture into. */
async function aList(page: Page, name: string) {
  await page.goto("/tasks");
  await expect(page.getByRole("heading", { name: "Tasks" })).toBeVisible();

  await page.getByRole("button", { name: "New list" }).click();

  const dialog = page.getByRole("dialog");

  await dialog.getByRole("textbox", { name: "Name" }).fill(name);
  await dialog.getByRole("button", { name: "Make it" }).click();

  await page.getByRole("navigation", { name: "The lists" }).getByRole("link", { name }).click();

  await expect(page).toHaveURL(/\/tasks\/[0-9a-f-]{36}$/);
  await expect(page.getByLabel("What has to be done")).toBeVisible();
}

/** The titles on the screen, in the order they are drawn. */
async function titles(page: Page): Promise<string[]> {
  return page.getByRole("listitem").filter({ has: page.getByRole("checkbox") }).evaluateAll(
    (rows) => rows.map((row) => row.querySelector("span")?.textContent?.trim() ?? ""),
  );
}
