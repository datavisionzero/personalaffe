import { expect, test, type Page } from "@playwright/test";

import { signedIn } from "./theInstance";

/**
 * What this instance is called, in a real browser: the sidebar, the tab, the
 * favicon and the sign-in screen — the four places the product name used to be
 * the only thing that could appear.
 *
 * The last of them is why it is worth checking here rather than only in jsdom:
 * the sign-in screen is drawn before anybody has signed in, which means the
 * appearance is read without a credential by a browser that has none. A unit
 * test standing in front of `fetch` cannot tell that apart from one that does.
 */
test.describe("the instance says whose it is", () => {
  const name = "Haus und Hof";

  /** Puts it back, so the instance these run against is left as it was found. */
  test.afterEach(async ({ page }) => {
    await setAppearance(page, { title: null, colour: "violet", shape: "square" });
  });

  test("names the instance everywhere the product name used to be", async ({ page }) => {
    await signedIn(page);

    await page.goto("/settings/appearance");

    await page.getByLabel("Name").fill(name);
    await page.getByLabel("Colour").selectOption("teal");
    await page.getByLabel("Mark").selectOption("circle");
    await page.getByRole("button", { name: "Save" }).click();

    // The sidebar, without a reload.
    await expect(page.locator('[data-slot="sidebar-header"]').getByText(name)).toBeVisible();

    // The tab.
    await expect(page).toHaveTitle(name);

    // And the icon, which is drawn here and fetched from nowhere.
    const icon = page.locator('link[rel="icon"]');
    await expect(icon).toHaveAttribute("href", /^data:image\/svg\+xml,/);
    await expect(icon).toHaveAttribute("href", /circle/);

    // The colour is an attribute on the document and never a style string.
    await expect(page.locator("html")).toHaveAttribute("data-mark-colour", "teal");
  });

  test("says it at the sign-in screen, to a browser with no credential", async ({ page }) => {
    await signedIn(page);
    await setAppearance(page, { title: name, colour: "amber", shape: "square" });

    // A browser that has never been here: no cookie, no session, nothing.
    const stranger = await page.context().browser()!.newContext();
    const fresh = await stranger.newPage();

    await fresh.goto("/");

    await expect(fresh.getByRole("heading", { name })).toBeVisible();
    await expect(fresh).toHaveTitle(name);

    // And that is the whole of what it gives away.
    const me = await fresh.request.get("/api/me");
    expect(me.status()).toBe(401);

    await stranger.close();
  });

  test("writes a name containing markup out as text", async ({ page }) => {
    await signedIn(page);

    const trouble = "<script>alert(1)</script>";

    await page.goto("/settings/appearance");
    await page.getByLabel("Name").fill(trouble);
    await page.getByRole("button", { name: "Save" }).click();

    await expect(page.locator('[data-slot="sidebar-header"]').getByText(trouble)).toBeVisible();
    await expect(page).toHaveTitle(trouble);

    // Nothing was parsed as markup: the sidebar holds the characters, not a
    // script element.
    expect(await page.locator("script:not([src]):not([type])").count()).toBe(0);
  });

  test("goes back to the product's own name and icon when the name is cleared", async ({
    page,
  }) => {
    await signedIn(page);
    await setAppearance(page, { title: name, colour: "red", shape: "circle" });

    await page.goto("/settings/appearance");
    await page.getByLabel("Name").fill("   ");
    await page.getByLabel("Colour").selectOption("violet");
    await page.getByLabel("Mark").selectOption("square");
    await page.getByRole("button", { name: "Save" }).click();

    await expect(page.locator('[data-slot="sidebar-header"]').getByText("personalaffe")).toBeVisible();
    await expect(page).toHaveTitle("personalaffe");

    // The icon the document shipped with, byte for byte: an instance nobody has
    // named is not given a generated one that is meant to look the same.
    await expect(page.locator('link[rel="icon"]')).toHaveAttribute("href", /%235b6ee1/);
  });

});

/** A write made the way another device would make it (`theInstance.ts`). */
async function setAppearance(
  page: Page,
  appearance: { title: string | null; colour: string; shape: string },
) {
  const read = await page.request.get("/api/appearance");
  const { updated_at: version } = (await read.json()) as { updated_at: string };

  const written = await page.request.put("/api/appearance", {
    headers: {
      "If-Match": `"${version}"`,
      "X-Personalaffe-CSRF": "1",
      Origin: new URL(page.url()).origin,
    },
    data: appearance,
  });

  expect(written.ok(), await written.text()).toBeTruthy();
}
