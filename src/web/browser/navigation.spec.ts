import { expect, test } from "@playwright/test";
import { aPage, signedIn } from "./theInstance";

test.beforeEach(async ({ page }) => { await signedIn(page); });

test("keeps a page favorite across reloads and resumes it from the app switcher", async ({ page }) => {
  const title = `Navigation page ${Date.now()}`;
  await aPage(page, title);
  const address = page.url();
  await page.getByRole("button", { name: `Favorite ${title}`, exact: true }).click();
  await page.getByRole("link", { name: /: Home$/ }).click();
  await page.reload();
  const favorites = page.getByRole("region", { name: "Favorites", exact: true });
  await expect(favorites.getByRole("link", { name: `${title} Knowledge` })).toBeVisible();
  await page.getByRole("button", { name: /^Switch application:/ }).click();
  await page.getByRole("navigation", { name: "The workspace" }).getByRole("link", { name: "Knowledge", exact: true }).click();
  await expect(page).toHaveURL(address);
  await expect(page.getByRole("navigation", { name: "The workspace" })).toBeHidden();
  await page.getByRole("button", { name: "Hide pages", exact: true }).click();
  await expect(page.getByRole("navigation", { name: "The pages" })).toBeHidden();
  await page.getByRole("button", { name: "Show pages", exact: true }).click();
  await expect(page.getByRole("navigation", { name: "The pages" })).toBeVisible();
});

test("arranges and removes application favorites without clearing recent visits", async ({ page }) => {
  await page.getByRole("button", { name: "Favorite Tasks", exact: true }).click();
  await page.getByRole("button", { name: "Favorite Files", exact: true }).click();
  await page.getByRole("button", { name: "Arrange favorites" }).click();
  await page.getByRole("button", { name: "Move Files earlier" }).click();
  const favorites = page.getByRole("region", { name: "Favorites", exact: true });
  await expect(favorites.getByRole("link").first()).toHaveText("Files");
  await page.reload();
  await expect(favorites.getByRole("link").first()).toHaveText("Files");
  await page.getByRole("button", { name: "Arrange favorites" }).click();
  await page.getByRole("button", { name: "Remove Files from favorites" }).click();
  await expect(favorites.getByRole("link", { name: "Files", exact: true })).toBeHidden();
});

test("uses a bottom app sheet and closes the page drawer after navigation at 320px", async ({ page }) => {
  const title = `Mobile navigation ${Date.now()}`;
  await aPage(page, title);
  const address = page.url();
  await page.setViewportSize({ width: 320, height: 568 });
  await page.getByRole("button", { name: /^Switch application:/ }).click();
  const sheet = page.locator('[data-slot="sheet-content"]');
  await expect(sheet).toHaveAttribute("data-side", "bottom");
  await sheet.getByRole("link", { name: "Home", exact: true }).click();
  await expect(sheet).toBeHidden();
  await page.getByRole("button", { name: /^Switch application:/ }).click();
  await sheet.getByRole("link", { name: "Knowledge", exact: true }).click();
  await expect(page).toHaveURL(address);
  await page.getByRole("button", { name: "Pages", exact: true }).click();
  await expect(sheet).toHaveAttribute("data-side", "left");
  await sheet.getByRole("link", { name: title, exact: true }).click();
  await expect(sheet).toBeHidden();
  const width = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(width).toBeLessThanOrEqual(1);
  await expect(page.getByRole("textbox", { name: "The page" })).toBeVisible();
});

test("creates and selects a list in the mobile drawer", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 });
  await page.goto("/tasks");
  await page.getByRole("button", { name: "Lists", exact: true }).click();
  await page.getByRole("button", { name: "New list", exact: true }).click();
  const name = `Mobile list ${Date.now()}`;
  const dialog = page.getByRole("dialog", { name: "New list", exact: true });
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
  await dialog.getByRole("button", { name: "Make it", exact: true }).click();
  await page.getByRole("navigation", { name: "The lists" }).getByRole("link", { name, exact: true }).click();
  await expect(page.getByRole("navigation", { name: "The lists" })).toBeHidden();
  await expect(page.getByRole("textbox", { name: "New task", exact: true })).toBeVisible();
});
