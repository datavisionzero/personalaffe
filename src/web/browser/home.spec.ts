import { expect, test, type Page } from "@playwright/test";

import { signedIn } from "./theInstance";

/**
 * The home page and the search, in an engine that lays things out.
 *
 * What is checked here and nowhere else is what jsdom cannot say: that the
 * tiles are on the page at a phone's width, that a tile is a link into the
 * thing it is about, that the palette answers while somebody is typing, and
 * that the search field is reachable from the keyboard alone.
 *
 * The instance these run against keeps whatever the last check wrote, so
 * everything written here carries a word of its own that nothing else uses.
 */
const word = "pomegranate";

async function anEntry(page: Page, text: string) {
  await page.goto("/scratchpad");
  await expect(page.getByRole("heading", { name: "Scratchpad" })).toBeVisible();

  await page.getByRole("textbox", { name: "New entry" }).fill(text);
  await page.getByRole("button", { name: "Put it down" }).click();

  await expect(page.getByText(text, { exact: false }).first()).toBeVisible();
}

test.describe("the home page", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("draws its tiles and walks into one of them", async ({ page }) => {
    await anEntry(page, `a ${word} note for the home page`);

    await page.getByRole("navigation", { name: "The workspace" }).getByRole("link", { name: "Home" }).click();

    const scratchpad = page.getByRole("region", { name: "Scratchpad" });
    await expect(scratchpad).toBeVisible();

    // A tile is five rows and then "all of it", rather than five rows and a
    // dead end.
    await scratchpad.getByRole("link", { name: "All of it" }).click();
    await expect(page).toHaveURL(/\/scratchpad$/);
  });

  test("keeps its tiles on a phone, one under another", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/");

    const tasks = page.getByRole("region", { name: "Tasks" });
    const knowledge = page.getByRole("region", { name: "Knowledge" });

    await expect(tasks).toBeVisible();
    await expect(knowledge).toBeVisible();

    const above = await tasks.boundingBox();
    const below = await knowledge.boundingBox();

    // One column: the tile below starts under the one above it, rather than
    // beside it off the edge of the screen.
    expect(above!.y + above!.height).toBeLessThanOrEqual(below!.y + 1);
    expect(below!.width).toBeLessThanOrEqual(390);
  });

  test("hides a tile and brings it back, and the home page follows", async ({ page }) => {
    await page.goto("/settings/home");

    const tile = page.getByRole("listitem").filter({ hasText: "Open tasks" });
    await tile.getByRole("button", { name: "Hide Open tasks" }).click();
    await expect(tile.getByText("Hidden")).toBeVisible();

    await page.goto("/");
    await expect(page.getByRole("region", { name: "Tasks" })).toBeHidden();

    await page.goto("/settings/home");

    const back = page.getByRole("listitem").filter({ hasText: "Open tasks" });
    await back.getByRole("button", { name: "Show Open tasks" }).click();

    // That the instance has it before walking away from the screen that sent
    // it. The settings screen reads itself again after a write and draws what
    // came back, so "Hidden" gone is the write having landed — and a `goto`
    // sent before it is a navigation that cancels the request it is waiting on.
    await expect(back.getByText("Hidden")).toBeHidden();

    await page.goto("/");
    await expect(page.getByRole("region", { name: "Tasks" })).toBeVisible();
  });

  test("says what it can about the weather without holding anything up", async ({ page }) => {
    await page.goto("/");

    const weather = page.getByRole("region", { name: "Weather" });
    await expect(weather).toBeVisible();

    // Whatever this instance can say — a reading, no place, or a provider that
    // did not answer — the tiles above it are already drawn.
    await expect(page.getByRole("region", { name: "Knowledge" })).toBeVisible();
  });
});

test.describe("one search over the workspace", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("finds what was written, from the palette, while it is being typed", async ({ page }) => {
    await anEntry(page, `something about a ${word} in the palette`);

    await page.keyboard.press("ControlOrMeta+k");

    const field = page.getByRole("combobox", { name: "Go anywhere, or type a command" });
    await expect(field).toBeFocused();

    await field.fill(word);

    // The entry, and not "Search for …" — which is an option carrying the same
    // word, is offered the moment there is something typed, and is the one a
    // `.first()` picks while the findings are still on their way.
    const row = page.getByRole("option", { name: /something about a/i });
    await expect(row).toBeVisible();

    await row.click();
    await expect(page).toHaveURL(/\/scratchpad$/);
  });

  test("opens the whole search from the palette, and keeps the words in the address", async ({
    page,
  }) => {
    await anEntry(page, `a ${word} for the search screen`);

    await page.keyboard.press("ControlOrMeta+k");
    const field = page.getByRole("combobox", { name: "Go anywhere, or type a command" });
    await field.fill(word);

    await page.getByRole("option", { name: new RegExp(`Search for`) }).click();

    await expect(page).toHaveURL(new RegExp(`/search\\?q=${word}`));

    const found = page.getByRole("list", { name: "What was found" });
    await expect(found.getByRole("listitem").first()).toBeVisible();

    // The words that were asked for are marked up here and never by the
    // instance, which sends text (`docs/api.md`, The search).
    await expect(found.locator("mark").first()).toBeVisible();
  });

  test("is reachable and usable from the keyboard alone", async ({ page }) => {
    await page.goto(`/search?q=${word}`);

    const field = page.getByRole("searchbox", { name: "Search the workspace" });
    await expect(field).toBeFocused();
    await expect(field).toHaveValue(word);

    await field.fill("a");
    await expect(page.getByText("Type something to look for.")).toBeVisible();
  });
});
