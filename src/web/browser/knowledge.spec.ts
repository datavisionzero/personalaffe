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

    // That the text is in the field before it is saved. Without it, a save
    // button still disabled because CodeMirror never got the keystrokes reads
    // as a timeout on the button rather than as what it is.
    await expect(field).toContainText("Das Wichtigste");

    // Renamed in the same breath, because one write carries the title and the
    // body: they are one row (`docs/api.md`, Knowledge). Renaming <em>after</em>
    // a save would be typing into a screen that is reading itself again, which
    // is a moment, not a workflow — and what it used to cost is PERSONAL-66's
    // one-line guard in `change`.
    const renamed = page.getByLabel("Title");

    await expect(renamed).toHaveValue(title);
    await renamed.fill(`${title} (umbenannt)`);

    // That the screen has taken both changes before it is asked to save them.
    // A filled field is a value in the DOM; a Save that is alive is the screen
    // having the draft — and a click that lands before it is a save of what was
    // on the screen a moment earlier.
    await expect(renamed).toHaveValue(`${title} (umbenannt)`);
    await expect(page.getByRole("button", { name: "Save" })).toBeEnabled();

    const saved = page.waitForResponse((response) => response.request().method() === "PUT" && response.url().includes("/api/knowledge/pages/"));
    await page.getByRole("button", { name: "Save" }).click();
    expect((await saved).ok()).toBeTruthy();
    await expect(page.getByText("Saved.")).toBeVisible();

    // The same address still opens it: the id is the identity, and a rename
    // does not move it.
    await page.goto("/");
    await page.goto(address);

    await expect(page.getByLabel("Title")).toHaveValue(`${title} (umbenannt)`);

    // `toHaveValue` is for the title, which is an `<input>`. The field is
    // CodeMirror, which writes on a `contenteditable`, so what it holds is
    // text and not a value — which is the whole reason these checks exist in a
    // browser at all.
    await expect(page.getByRole("textbox", { name: "The page" })).toContainText("Das Wichtigste");

    // And a second write, from a screen that was opened rather than saved on:
    // the guard takes the version this read carried, so writing twice to one
    // page is a workflow and not a conflict.
    const opened = page.getByRole("textbox", { name: "The page" });

    await opened.click();
    await page.keyboard.press("ControlOrMeta+End");
    await page.keyboard.type("\n\nUnd noch etwas.");

    await expect(opened).toContainText("Und noch etwas.");
    await expect(page.getByRole("button", { name: "Save" })).toBeEnabled();

    const savedAgain = page.waitForResponse((response) => response.request().method() === "PUT" && response.url().includes("/api/knowledge/pages/"));
    await page.getByRole("button", { name: "Save" }).click();
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
    await page.keyboard.type(`Siehe [das Ziel](page:${id}).`);

    const preview = page.getByLabel("The page, preview");
    const link = preview.getByRole("link", { name: "das Ziel" });

    await expect(link).toHaveAttribute("href", `/knowledge/${id}`);

    // No new tab: the frame stays where it is and the tree beside it does not
    // flicker.
    await expect(link).not.toHaveAttribute("target", "_blank");
  });

  test("brings a page written elsewhere onto the tree without a reload", async ({ page }) => {
    const title = `Von woanders ${Date.now()}`;

    await page.goto("/knowledge");
    await expect(page.getByRole("heading", { name: "Knowledge" })).toBeVisible();

    // Another device, or an agent: the same API, the same session, and nothing
    // touching this page.
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

    await expect(page.getByText("Unsaved.")).toBeVisible();

    // The same page, written by something else, while a person is mid-sentence.
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

    // Long enough for a refresh to have happened if the hold were not there.
    await page.waitForTimeout(20_000);

    await expect(page.getByRole("textbox", { name: "The page" })).toContainText(
      "was ich gerade schreibe",
    );
  });

  test("puts a page in the Trash and says nothing was destroyed", async ({ page }) => {
    const title = `Wegwerfen ${Date.now()}`;

    await aPage(page, title);

    await page.getByRole("button", { name: `Delete ${title}` }).click();

    await expect(page.getByText("Pick a page, or write one.")).toBeVisible();

    await page.goto("/trash");
    await expect(page.getByText(title)).toBeVisible();
  });

  test("is usable at a phone's width", async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 780 });
    await page.goto("/knowledge");

    await expect(page.getByRole("heading", { name: "Knowledge" })).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );

    expect(overflow).toBeLessThanOrEqual(1);
  });
});
