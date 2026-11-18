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

  test("moves past a completed task in one step, and keeps the keyboard on what it moved", async ({ page }) => {
    await aList(page, `Einkauf ${Date.now()}`);

    for (const title of ["eins", "zwei", "drei"]) {
      await page.getByLabel("What has to be done").fill(title);
      await page.keyboard.press("Enter");
      await expect(page.getByRole("listitem").filter({ hasText: title })).toBeVisible();
    }

    // The one in the middle is done: it is still between the other two in the
    // order the instance keeps, and nowhere near them on the screen.
    await page.getByRole("checkbox", { name: "Complete zwei" }).click();
    await expect(async () => expect(await titles(page)).toEqual(["eins", "drei", "zwei"])).toPass();

    await page.getByRole("button", { name: "Move drei up" }).focus();
    await page.keyboard.press("Enter");

    await expect(async () => expect(await titles(page)).toEqual(["drei", "eins", "zwei"])).toPass();

    // At the top there is no further up, so the keyboard is on the way back
    // down — and it is on something, which is the point.
    await expect(page.getByRole("button", { name: "Move drei down" })).toBeFocused();

    await page.keyboard.press("Enter");

    await expect(async () => expect(await titles(page)).toEqual(["eins", "drei", "zwei"])).toPass();
    await expect(page.getByRole("button", { name: "Move drei up" })).toBeFocused();

    // A tick takes the row to the other group, and the keyboard goes with it.
    await page.getByRole("checkbox", { name: "Complete drei" }).focus();
    await page.keyboard.press("Space");

    await expect(page.getByRole("checkbox", { name: "Reopen drei" })).toBeFocused();
  });

  test("captures with a day, shows the notes, and is pointed at from the home page", async ({ page }) => {
    const title = `Geschenk ${Date.now()}`;

    await aList(page, `Einkauf ${Date.now()}`);

    const id = page.url().split("/").pop();

    await page.getByLabel("What has to be done").fill(title);
    await page.getByLabel("When it is due").fill("2026-12-24");
    await page.getByLabel("What has to be done").press("Enter");

    const row = page.getByRole("listitem").filter({ hasText: title });

    await expect(row).toContainText("2026-12-24");
    await expect(page.getByLabel("When it is due")).toHaveValue("");

    // The notes are written somewhere else — a console, an agent — and what
    // this screen owes them is saying that they are there.
    const read = await page.request.get(`/api/tasks/lists/${id}/tasks`);
    const { items } = (await read.json()) as { items: Record<string, unknown>[] };
    const task = items.find((one) => one.title === title)!;

    const written = await page.request.put(`/api/tasks/${task.id as string}`, {
      headers: {
        "If-Match": `"${task.updated_at as string}"`,
        "X-Personalaffe-CSRF": "1",
        Origin: new URL(page.url()).origin,
      },
      data: {
        list: task.list,
        title: task.title,
        description: "aus dem **Buchladen** am Markt",
        due_on: task.due_on,
        completed: task.completed,
        after: task.after,
      },
    });

    expect(written.ok(), await written.text()).toBeTruthy();

    await page.reload();

    await expect(page.getByText("Buchladen")).toBeHidden();
    await row.getByRole("button", { name: `Show the notes on ${title}` }).click();
    await expect(row.locator("strong", { hasText: "Buchladen" })).toBeVisible();

    await page.goto("/");

    await page.getByRole("region", { name: "Tasks" }).getByRole("link", { name: title }).click();

    await expect(page).toHaveURL(new RegExp(`/tasks/${id}\\?task=${task.id as string}$`));
    await expect(row).toHaveAttribute("aria-current", "true");
    await expect(row.locator("strong", { hasText: "Buchladen" })).toBeVisible();
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

  test("renames a list and puts it and its tasks in the Trash", async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 780 });
    const original = `Einkauf ${Date.now()}`;
    const renamed = `Wochenmarkt ${Date.now()}`;
    await aList(page, original);
    const address = page.url();
    await page.getByLabel("What has to be done").fill("Milch holen");
    await page.keyboard.press("Enter");
    await expect(page.getByRole("checkbox", { name: "Complete Milch holen" })).toBeVisible();
    await page.getByRole("button", { name: "Rename list", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Rename list" });
    await expect(dialog.getByLabel("Name")).toHaveValue(original);
    await dialog.getByLabel("Name").fill(renamed);
    await dialog.getByLabel("Name").press("Enter");
    await expect(dialog).toBeHidden();
    await expect(page.getByRole("heading", { name: renamed })).toBeVisible();
    await expect(page).toHaveURL(address);
    await page.reload();
    await expect(page.getByRole("heading", { name: renamed })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
    await page.getByRole("button", { name: "Delete list", exact: true }).click();
    await expect(page).toHaveURL(/\/tasks$/);
    await expect(page.getByRole("status")).toHaveText(`${renamed} and everything on it are in the Trash.`);
    await page.goto("/trash");
    await expect(page.getByText(renamed, { exact: true })).toBeVisible();
    await page.getByRole("listitem").filter({ has: page.getByText(renamed, { exact: true }) })
      .getByRole("button", { name: "Put it back" }).click();
    await expect(page.getByText(renamed, { exact: true })).toBeHidden();
    await page.goto(address);
    await expect(page.getByRole("heading", { name: renamed })).toBeVisible();
    await expect(page.getByRole("checkbox", { name: "Complete Milch holen" })).toBeVisible();
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

  test("puts back what was just deleted, from where it was deleted", async ({ page }) => {
    const stamp = Date.now();
    const list = `Einkauf ${stamp}`;

    await aList(page, list);

    const address = page.url();

    for (const title of [`bleibt ${stamp}`, `fertig ${stamp}`, `auch fertig ${stamp}`]) {
      await page.getByLabel("What has to be done").fill(title);
      await page.keyboard.press("Enter");
      await expect(page.getByRole("listitem").filter({ hasText: title })).toBeVisible();
    }

    // One task.
    await page.getByRole("button", { name: `Delete bleibt ${stamp}` }).click();
    await expect(page.getByRole("status")).toHaveText(`bleibt ${stamp} is in the Trash.`);
    await expect(page.getByRole("checkbox", { name: `Complete bleibt ${stamp}` })).toHaveCount(0);

    await page.getByRole("button", { name: "Put it back" }).click();
    await expect(page.getByRole("checkbox", { name: `Complete bleibt ${stamp}` })).toBeVisible();
    await expect(page.getByRole("button", { name: "Put it back" })).toHaveCount(0);

    // Everything completed, and only that.
    await page.getByRole("checkbox", { name: `Complete fertig ${stamp}` }).click();
    await expect(page.getByRole("checkbox", { name: `Reopen fertig ${stamp}` })).toBeVisible();
    await page.getByRole("checkbox", { name: `Complete auch fertig ${stamp}` }).click();
    await expect(page.getByRole("checkbox", { name: `Reopen auch fertig ${stamp}` })).toBeVisible();

    await page.getByRole("button", { name: "Delete completed" }).click();
    await expect(page.getByRole("status")).toHaveText("2 completed tasks are in the Trash.");
    await expect(page.getByRole("heading", { name: "Done" })).toHaveCount(0);
    await expect(page.getByRole("checkbox", { name: `Complete bleibt ${stamp}` })).toBeVisible();

    await page.getByRole("button", { name: "Put them back" }).click();
    await expect(page.getByRole("checkbox", { name: `Reopen fertig ${stamp}` })).toBeVisible();
    await expect(page.getByRole("checkbox", { name: `Reopen auch fertig ${stamp}` })).toBeVisible();

    // The list itself.
    await page.getByRole("button", { name: "Delete list", exact: true }).click();
    await expect(page).toHaveURL(/\/tasks$/);

    await page.getByRole("button", { name: "Put it back" }).click();
    await expect(page).toHaveURL(address);
    await expect(page.getByRole("heading", { name: list })).toBeVisible();
    await expect(page.getByRole("checkbox", { name: `Complete bleibt ${stamp}` })).toBeVisible();
  });
});

/** A list of its own for each check, opened and ready to capture into. */
async function aList(page: Page, name: string) {
  await page.goto("/tasks");
  const lists = page.getByRole("button", { name: "Lists", exact: true });
  if ((page.viewportSize()?.width ?? 1280) < 768) await lists.click();
  await expect(page.getByRole("heading", { name: "Tasks" })).toBeVisible();

  await page.getByRole("button", { name: "New list" }).click();

  const dialog = page.getByRole("dialog", { name: "New list", exact: true });

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
