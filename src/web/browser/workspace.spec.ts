import { expect, test } from "@playwright/test";

import { signedIn, switchApplication } from "./theInstance";

test.describe("the workspace in a browser", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("offers the four applications and walks into one and back", async ({ page }) => {
    const navigation = page.getByRole("navigation", { name: "The workspace" });

    for (const label of ["Home", "Scratchpad", "Knowledge", "Tasks", "Files", "Trash", "Settings"]) {
      await expect(navigation.getByRole("link", { name: label })).toBeVisible();
    }

    // Tasks is the one application left with no screen, so it is the one that
    // proves the frame draws something for an application that has none.
    await navigation.getByRole("link", { name: "Tasks" }).click();

    await expect(page).toHaveURL(/\/tasks$/);
    await expect(page.getByText("Tasks is not in this build yet.")).toBeVisible();

    await navigation.getByRole("link", { name: "Home" }).click();
    await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
  });

  test("opens an application from a pasted address, and says so for one that is not there", async ({
    page,
  }) => {
    await page.goto("/files");
    await expect(page.getByRole("heading", { name: "Files" })).toBeVisible();

    // Answered inside the frame rather than redirected away: landing somewhere
    // else silently hides the typo.
    await page.goto("/calendar");
    await expect(page.getByText("Nothing at this address.")).toBeVisible();
    await expect(page.getByRole("navigation", { name: "The workspace" })).toBeVisible();
  });

  test("goes where the palette is told to, from the keyboard alone", async ({ page }) => {
    await page.keyboard.press("ControlOrMeta+k");

    const field = page.getByRole("combobox", { name: "Go anywhere, or type a command" });
    await expect(field).toBeFocused();

    await field.fill("scratch");
    await page.keyboard.press("Enter");

    await expect(page).toHaveURL(/\/scratchpad$/);
    await expect(page.getByRole("heading", { name: "Scratchpad" })).toBeVisible();
  });

  test("shows every key it binds, and leaves bare keys to whatever is being typed into", async ({
    page,
  }) => {
    await page.keyboard.press("?");

    const overview = page.getByRole("dialog");
    await expect(overview.getByText("Search or jump to anything")).toBeVisible();
    await expect(overview.getByText("Go home")).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(overview).toBeHidden();

    await page.keyboard.press("ControlOrMeta+k");
    const field = page.getByRole("combobox", { name: "Go anywhere, or type a command" });
    await field.pressSequentially("h?");

    // The frame does not answer a bare key while somebody is typing one.
    await expect(field).toHaveValue("h?");
    await page.keyboard.press("Escape");
  });

  test("reaches the whole frame with the tab key", async ({ page }) => {
    await page.keyboard.press("Tab");

    // Whatever the first stop is, it is a real one and it is visible: a frame
    // whose first Tab lands on nothing is a frame nobody can use without a
    // mouse.
    const first = page.locator(":focus");
    await expect(first).toBeVisible();

    const reached: string[] = [];

    for (let step = 0; step < 25; step += 1) {
      const name = await page.evaluate(() => {
        const at = document.activeElement as HTMLElement | null;
        return at?.getAttribute("aria-label") ?? at?.textContent?.trim().slice(0, 40) ?? "";
      });

      reached.push(name);
      await page.keyboard.press("Tab");
    }

    expect(reached.join(" | ")).toContain("Knowledge");
    expect(reached.join(" | ")).toContain("Settings");
  });
});

test.describe("the application switch", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test.afterEach(async ({ page }) => {
    await switchApplication(page, "tasks", true);
  });

  test("takes an application out of the workspace and puts it back", async ({ page }) => {
    await page.getByRole("navigation", { name: "The workspace" }).getByRole("link", { name: "Settings" }).click();
    await expect(page).toHaveURL(/\/settings\/applications$/);

    const tasks = page.getByRole("listitem").filter({ hasText: "Tasks" });
    await tasks.getByRole("button", { name: "Switch off Tasks" }).click();

    await expect(tasks.getByText("Off")).toBeVisible();

    // Out of the navigation, and out of the palette with it.
    const navigation = page.getByRole("navigation", { name: "The workspace" });
    await expect(navigation.getByRole("link", { name: "Tasks" })).toBeHidden();

    // And the address still answers, with the one thing the owner can act on.
    await page.goto("/tasks");
    await expect(page.getByText("Tasks is switched off.")).toBeVisible();

    await page.getByRole("link", { name: "Switch it on" }).click();
    await page.getByRole("button", { name: "Switch on Tasks" }).click();

    await expect(navigation.getByRole("link", { name: "Tasks" })).toBeVisible();
  });

  test("brings a change made somewhere else onto the screen without a reload", async ({ page }) => {
    const navigation = page.getByRole("navigation", { name: "The workspace" });
    await expect(navigation.getByRole("link", { name: "Tasks" })).toBeVisible();

    // The write is made the way another device or an agent would make it. The
    // page is not reloaded, not clicked and not touched.
    await switchApplication(page, "tasks", false);

    await expect(navigation.getByRole("link", { name: "Tasks" })).toBeHidden();

    await switchApplication(page, "tasks", true);

    await expect(navigation.getByRole("link", { name: "Tasks" })).toBeVisible();
  });
});
