import { expect, test } from "@playwright/test";
import { aPage, signedIn } from "./theInstance";

test.describe("the Knowledge document", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
    await aPage(page, `Editor ${Date.now()}`);
  });

  test("writes directly, formats inline, autosaves and keeps the same editor mounted", async ({ page }) => {
    const field = page.getByRole("textbox", { name: "The page" });
    await expect(page.getByRole("button", { name: "Edit page" })).toHaveCount(0);
    await expect(page.getByLabel("The page, preview")).toHaveCount(0);
    await field.evaluate(element => element.setAttribute("data-original", "yes"));
    await field.click();
    await page.keyboard.type("# A heading");
    await page.keyboard.press("Enter");
    await page.keyboard.type("Something **bold** ");
    await expect(field.getByRole("heading", { name: "A heading" })).toBeVisible();
    await expect(field.locator("strong")).toHaveText("bold");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await expect(field).toHaveAttribute("data-original", "yes");
    await expect(field).toBeFocused();
    await page.keyboard.type("and more");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await page.reload();
    await expect(field).toContainText("and more");
    await expect(field.locator("strong")).toHaveText("bold");
  });

  test("formats a selection using the keyboard-accessible toolbar", async ({ page }) => {
    const field = page.getByRole("textbox", { name: "The page" });
    await field.fill("Emphasis");
    await page.keyboard.press("ControlOrMeta+a");
    const bold = page.getByRole("group", { name: "Document formatting" }).getByRole("button", { name: "Bold", exact: true });
    await bold.focus();
    await page.keyboard.press("Enter");
    await expect(field.locator("strong")).toHaveText("Emphasis");
    await expect(field).toBeFocused();
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
  });

  test("preserves typing during a slow save and uses its returned version for the next save", async ({ page }) => {
    let release!: () => void;
    const held = new Promise<void>(resolve => { release = resolve; });
    let first = true;
    await page.route("**/api/knowledge/pages/*", async route => {
      if (route.request().method() !== "PUT" || !first) return route.continue();
      first = false;
      await held;
      await route.continue();
    });
    const field = page.getByRole("textbox", { name: "The page" });
    await field.click();
    await page.keyboard.type("First sentence. ");
    await expect(page.getByText("Saving…", { exact: true })).toBeVisible();
    await page.keyboard.type("Second sentence.");
    release();
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await expect(field).toBeFocused();
    const saved = await (await page.request.get(`/api/knowledge/pages/${page.url().split("/").pop()}`)).json();
    expect(saved.markdown).toContain("First sentence. Second sentence.");
    await expect(page.getByRole("region", { name: "Resolve changes" })).toHaveCount(0);
  });

  test("keeps failed writes locally and retries without leaving the document", async ({ page }) => {
    await page.route("**/api/knowledge/pages/*", route => route.request().method() === "PUT" ? route.abort() : route.continue());
    const field = page.getByRole("textbox", { name: "The page" });
    await field.fill("Keep this sentence");
    await expect(page.getByText("Not saved", { exact: true })).toBeVisible();
    await expect(field).toContainText("Keep this sentence");
    await page.unroute("**/api/knowledge/pages/*");
    await page.getByRole("button", { name: "Retry saving" }).click();
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await page.reload();
    await expect(field).toContainText("Keep this sentence");
  });

  test("does not rewrite Markdown on opening and preserves tables, links, HTML and line breaks when edited", async ({ page }) => {
    const endpoint = `/api/knowledge/pages/${page.url().split("/").pop()}`;
    const current = await (await page.request.get(endpoint)).json();
    const markdown = '# Existing\n\nFirst line\nSecond line\n\n| Name | Value |\n| --- | --- |\n| One | Two |\n\n- [x] Done\n\n[Internal](page:0199f0c6-0000-7000-8000-00000000000a)\n\n```js\nconst x = 1;\n```\n\n<span onclick="window.__ran=true">raw</span>\n';
    const result = await page.request.put(endpoint, {
      headers: { "If-Match": `"${current.updated_at}"`, "X-Personalaffe-CSRF": "1", Origin: new URL(page.url()).origin },
      data: { title: current.title, parent: current.parent, markdown },
    });
    expect(result.ok()).toBeTruthy();
    await page.reload();
    const field = page.getByRole("textbox", { name: "The page" });
    await expect(field.getByRole("heading", { name: "Existing" })).toBeVisible();
    await expect(field.getByRole("table")).toContainText("Two");
    await page.waitForTimeout(1500);
    expect((await (await page.request.get(endpoint)).json()).markdown).toBe(markdown);
    await field.click();
    await page.keyboard.press("ControlOrMeta+End");
    await page.keyboard.press("Enter");
    await page.keyboard.type("Added text");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    const saved = (await (await page.request.get(endpoint)).json()).markdown;
    expect(saved).toContain("page:0199f0c6-0000-7000-8000-00000000000a");
    expect(saved).toContain("const x = 1;");
    expect(saved).toContain("[x]");
    expect(saved).toContain('<span onclick="window.__ran=true">');
    expect(saved).toContain("Added text");
    expect(await page.evaluate(() => (window as { __ran?: boolean }).__ran)).toBeUndefined();
    await page.reload();
    await expect(field.locator("p").filter({ hasText: "First line" }).locator("br")).toHaveCount(1);
  });

  test("explains missing images without saving the error over their alt text", async ({ page }) => {
    const endpoint = `/api/knowledge/pages/${page.url().split("/").pop()}`;
    const current = await (await page.request.get(endpoint)).json();
    const markdown = '![Original description](file:0199f0c6-0000-7000-8000-00000000000a "Original title")';
    const result = await page.request.put(endpoint, {
      headers: { "If-Match": `"${current.updated_at}"`, "X-Personalaffe-CSRF": "1", Origin: new URL(page.url()).origin },
      data: { title: current.title, parent: current.parent, markdown },
    });
    expect(result.ok()).toBeTruthy();
    await page.reload();
    await expect(page.getByText("Image unavailable: Original description.", { exact: false })).toBeVisible();
    const field = page.getByRole("textbox", { name: "The page" });
    await field.click();
    await page.keyboard.press("ControlOrMeta+End");
    await page.keyboard.press("Enter");
    await page.keyboard.type("Still editable");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    const saved = (await (await page.request.get(endpoint)).json()).markdown;
    expect(saved).toContain(markdown);
    expect(saved).not.toContain("Image unavailable");
  });

  test("retains one usable document in focus mode and on a phone", async ({ page }) => {
    const field = page.getByRole("textbox", { name: "The page" });
    await field.fill("A focused document");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Focus", exact: true }).click();
    await expect(page.getByRole("navigation", { name: "The pages" })).toBeHidden();
    await expect(field).toHaveCount(1);
    await page.getByRole("button", { name: "Show navigation" }).click();
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(field).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
  });
});
