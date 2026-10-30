import { expect, test } from "@playwright/test";

import { aPage, signedIn } from "./theInstance";

/**
 * Knowledge in a real browser: the things jsdom cannot say.
 *
 * A page at its own address, a `page:` link followed inside the frame, a change
 * made elsewhere arriving on a screen nobody is touching — and, most of all,
 * that it does <em>not</em> arrive on a screen somebody is writing on. The
 * screen's own behaviour is proved in jsdom
 * (`src/knowledge/Knowledge.test.tsx`); this is the half that needs an engine.
 */
test.describe("Knowledge", () => {
  test.beforeEach(async ({ page }) => {
    await signedIn(page);
  });

  test("keeps a page at an address a link can carry", async ({ page }) => {
    const title = `Architektur ${Date.now()}`;

    await aPage(page, title);

    const address = page.url();

    const field = page.getByRole("textbox", { name: "The page" });

    await field.click();
    await page.keyboard.type("# Zuerst\n\nDas Wichtigste.");

    await expect(field).toContainText("Das Wichtigste");

    const renamed = page.getByLabel("Title");

    await expect(renamed).toHaveValue(title);
    await renamed.fill(`${title} (umbenannt)`);

    await expect(renamed).toHaveValue(`${title} (umbenannt)`);

    const saved = page.waitForResponse((response) => response.request().method() === "PUT" && response.url().includes("/api/knowledge/pages/"));
    expect((await saved).ok()).toBeTruthy();
    await expect(page.getByText("Saved.")).toBeVisible();

    await page.goto("/");
    await page.goto(address);

    await expect(page.getByLabel("Title")).toHaveValue(`${title} (umbenannt)`);

    await expect(page.getByRole("textbox", { name: "The page" })).toContainText("Das Wichtigste");

    const opened = page.getByRole("textbox", { name: "The page" });

    await opened.click();
    await page.keyboard.press("ControlOrMeta+End");
    await page.keyboard.type("\n\nUnd noch etwas.");

    await expect(opened).toContainText("Und noch etwas.");

    const savedAgain = page.waitForResponse((response) => response.request().method() === "PUT" && response.url().includes("/api/knowledge/pages/"));
    expect((await savedAgain).ok()).toBeTruthy();
    await expect(page.getByText("Saved.")).toBeVisible();

    await page.goto("/");
    await page.goto(address);

    const both = page.getByRole("textbox", { name: "The page" });

    await expect(both).toContainText("Das Wichtigste");
    await expect(both).toContainText("Und noch etwas.");
  });

  test("follows a page: link inside the frame rather than opening it", async ({ page }) => {
    const target = `Ziel ${Date.now()}`;

    await aPage(page, target);

    const id = page.url().split("/").pop();

    await aPage(page, `Quelle ${Date.now()}`);

    await page.getByRole("textbox", { name: "The page" }).click();
    await page.getByRole("button", { name: "Link page", exact: true }).click();
    await page.getByRole("textbox", { name: "Find a page by title" }).fill(target);
    await page.getByRole("dialog").getByRole("button", { name: target }).click();

    const preview = page.getByRole("textbox", { name: "The page" });
    const link = preview.getByRole("link", { name: target });

    await expect(link).toHaveAttribute("href", `/knowledge/${id}`);

    await expect(link).not.toHaveAttribute("target", "_blank");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await link.click({ modifiers: ["ControlOrMeta"] });
    await expect(page).toHaveURL(new RegExp(`/knowledge/${id}$`));
    await expect(page.getByRole("textbox", { name: "Title" })).toHaveValue(target);
  });

  test("brings a page written elsewhere onto the tree without a reload", async ({ page }) => {
    const title = `Von woanders ${Date.now()}`;

    await page.goto("/knowledge");
    await expect(page.getByRole("heading", { name: "Knowledge", exact: true })).toBeVisible();

    const written = await page.request.post("/api/knowledge/pages", {
      headers: {
        "X-Personalaffe-CSRF": "1",
        Origin: new URL(page.url()).origin,
      },
      data: { title, parent: null, markdown: "# Von woanders\n" },
    });

    expect(written.ok(), await written.text()).toBeTruthy();

    await expect(
      page.getByRole("navigation", { name: "The pages" }).getByRole("link", { name: title }),
    ).toBeVisible({ timeout: 30_000 });
  });

  test("never replaces what somebody is writing with what arrived", async ({ page }) => {
    await aPage(page, `Unterwegs ${Date.now()}`);

    const address = page.url();
    const id = address.split("/").pop();

    await page.getByRole("textbox", { name: "The page" }).click();
    await page.keyboard.type("was ich gerade schreibe");

    await expect(page.getByText(/Draft protected in this tab/)).toBeVisible();

    const read = await page.request.get(`/api/knowledge/pages/${id}`);
    const { updated_at: version } = (await read.json()) as { updated_at: string };

    const elsewhere = await page.request.put(`/api/knowledge/pages/${id}`, {
      headers: {
        "If-Match": `"${version}"`,
        "X-Personalaffe-CSRF": "1",
        Origin: new URL(page.url()).origin,
      },
      data: { title: `Unterwegs ${Date.now()}`, parent: null, markdown: "von woanders" },
    });

    expect(elsewhere.ok(), await elsewhere.text()).toBeTruthy();

    await page.waitForTimeout(20_000);

    await expect(page.getByRole("textbox", { name: "The page" })).toContainText(
      "was ich gerade schreibe",
    );
  });

  test("puts a page in the Trash and says nothing was destroyed", async ({ page }) => {
    const title = `Wegwerfen ${Date.now()}`;

    await aPage(page, title);

    await page.getByRole("button", { name: "Page actions" }).click();
    await page.getByRole("menuitem", { name: `Delete ${title}` }).click();

    await expect(page.getByRole("heading", { name: "Your knowledge" })).toBeVisible();

    await page.goto("/trash");
    await expect(page.getByText(title)).toBeVisible();
  });

  test("recovers a draft and finds its autosaved content", async ({ page }) => {
    const title = `Knowledge workflow ${Date.now()}`;
    await aPage(page, title);
    await page.getByRole("textbox", { name: "The page" }).fill("## Checklist\n\nUniquequartzcontent");
    page.once("dialog", dialog => dialog.accept());
    await page.reload();
    await expect(page.getByRole("textbox", { name: "The page" })).toContainText("Uniquequartzcontent");
    await expect(page.getByText("Saved.")).toBeVisible();
    await expect(page.getByRole("textbox", { name: "The page" })).toBeVisible();
    await page.getByRole("searchbox", { name: "Search Knowledge" }).fill("Uniquequartzcontent");
    await page.getByRole("checkbox", { name: "Only this page and its subpages" }).check();
    await expect(page.getByRole("list", { name: "Knowledge search results" }).getByRole("link", { name: title })).toBeVisible();
  });

  test("pins pages, links them and displays stored images", async ({ page }) => {
    const target = `Pinned target ${Date.now()}`;
    await aPage(page, target);
    const targetAddress = page.url();
    await page.getByRole("textbox", { name: "The page" }).fill("A pinned document");
    await expect(page.getByText("Saved.", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Pin page", exact: true }).click();
    await expect(page.getByRole("button", { name: "Unpin page", exact: true })).toBeVisible();
    await page.goto("/knowledge");
    await expect(page.getByRole("region", { name: "Pinned pages" }).getByRole("link", { name: target })).toBeVisible();
    const source = `Linked source ${Date.now()}`;
    await aPage(page, source);
    await page.getByRole("button", { name: "Link page", exact: true }).click();
    await page.getByRole("textbox", { name: "Find a page by title" }).fill(target);
    await page.getByRole("dialog").getByRole("button", { name: target }).click();
    const imageName = `pixel-${Date.now()}.png`;
    const uploaded = await page.request.post(`/api/files/content?name=${imageName}`, {
      headers: { "Content-Type": "image/png", "X-Personalaffe-CSRF": "1", Origin: new URL(page.url()).origin },
      data: Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=", "base64"),
    });
    expect(uploaded.ok()).toBeTruthy();
    await page.getByRole("button", { name: "Insert file", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: imageName }).click();
    await expect(page.getByText("Saved.")).toBeVisible();
    const image = page.getByRole("img", { name: imageName });
    await expect(image).toBeVisible();
    await expect.poll(() => image.evaluate((element: HTMLImageElement) => element.naturalWidth)).toBe(1);
    const stored = await (await page.request.get(`/api/knowledge/pages/${page.url().split("/").pop()}`)).json();
    expect(stored.markdown).toContain(`![${imageName}](file:`);
    await page.reload();
    await expect(image).toBeVisible();
    await expect.poll(() => image.evaluate((element: HTMLImageElement) => element.naturalWidth)).toBe(1);
    await page.goto(targetAddress);
    await expect(page.getByRole("region", { name: "Linked from" }).getByRole("link", { name: source })).toBeVisible();
  });

  test("is usable at a phone's width", async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 780 });
    await page.goto("/knowledge");

    await page.getByRole("button", { name: "Pages", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Knowledge", exact: true })).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );

    expect(overflow).toBeLessThanOrEqual(1);
  });
});
