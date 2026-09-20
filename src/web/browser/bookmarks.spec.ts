import { test, expect } from "@playwright/test";
import { signedIn, switchApplication } from "./theInstance";

test("saved links can be added, pinned, ordered and opened directly", async ({ page }) => {
  await signedIn(page);
  await switchApplication(page, "bookmarks", true);
  await page.goto("/bookmarks");
  const title = `Browser link ${Date.now()}`;
  await page.getByRole("button", { name: "Add bookmark", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("URL", { exact: true }).fill("https://example.com/bookmarks");
  await dialog.getByLabel("Title", { exact: true }).fill(title);
  await dialog.getByRole("button", { name: "Add bookmark", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  const recent = page.getByRole("region", { name: "Recently added" });
  await recent.getByRole("button", { name: `Pin ${title}`, exact: true }).click();
  const favorite = page.getByRole("region", { name: "Favorites" });
  await expect(favorite.getByRole("link", { name: new RegExp(title) })).toHaveAttribute("href", "https://example.com/bookmarks");
  await expect(favorite.getByRole("button", { name: `Move ${title} later`, exact: true })).toBeVisible();
  // Stop at the target boundary; recording must still happen independently.
  await page.context().route("https://example.com/**", (route) => route.fulfill({ body: "Saved link target" }));
  const recorded = page.waitForResponse((response) => response.url().endsWith("/open") && response.request().method() === "POST");
  const popup = page.waitForEvent("popup");
  await favorite.getByRole("link", { name: new RegExp(title) }).click();
  expect((await recorded).ok()).toBeTruthy();
  await (await popup).close();
});

test("private mode clears private cards and resets on reload at phone width", async ({ page }) => {
  await signedIn(page); await switchApplication(page, "bookmarks", true);
  const headers = { "X-Personalaffe-CSRF": "1", Origin: new URL(page.url()).origin, "Personalaffe-Private": "true" };
  const folderResponse = await page.request.post("/api/bookmarks/folders", { headers, data: { name: `Private ${Date.now()}`, parent: null, private: true } });
  expect(folderResponse.ok()).toBeTruthy();
  const folder = await folderResponse.json();
  const title = `Hidden browser link ${Date.now()}`;
  const created = await page.request.post("/api/bookmarks", { headers, data: { title, url: "https://example.com/private", description: "", folder: folder.id } });
  expect(created.ok()).toBeTruthy();
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto("/bookmarks");
  await expect(page.getByRole("heading", { name: "Recently added" })).toBeVisible();
  await expect(page.getByText(title, { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Private mode off", exact: true }).click();
  await expect(page.getByText(title, { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.getByRole("button", { name: "Private mode on", exact: true }).last().click();
  await expect(page.getByText(title, { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Private mode off", exact: true }).click();
  await expect(page.getByText(title, { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("button", { name: "Private mode off", exact: true })).toBeVisible();
  await expect(page.getByText(title, { exact: true })).toHaveCount(0);
});
