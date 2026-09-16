import { expect, type Page } from "@playwright/test";

/**
 * What every browser check starts with: an instance with an owner, and a
 * browser that is signed in to it.
 *
 * The instance these run against starts empty, so the first check through here
 * claims it and the rest sign in. Both are done through the screens rather than
 * through the API, because the door is one of the things being checked — and
 * because a test that signed in with a cookie it made itself would prove
 * nothing about the form.
 */
export const owner = {
  email: "owner@example.test",
  password: "correct horse battery staple",
};

export async function signedIn(page: Page) {
  await page.goto("/");

  const claim = page.getByRole("heading", { name: "Claim this workspace" });
  const signIn = page.getByRole("heading", { name: "Sign in" });

  await expect(claim.or(signIn)).toBeVisible();

  if (await claim.isVisible()) {
    await page.getByLabel("Email address").fill(owner.email);
    await page.getByLabel("Password").fill(owner.password);
    await page.getByRole("button", { name: "Claim it" }).click();
  } else {
    await page.getByLabel("Email address").fill(owner.email);
    await page.getByLabel("Password").fill(owner.password);
    await page.getByRole("button", { name: "Sign in" }).click();
  }

  await expect(
    page.getByRole("heading", { name: "What is useful or pending" }),
  ).toBeVisible();
}

/**
 * A write made the way something else would make it — another device, or an
 * agent over the API. It goes through the page's own request context, so it
 * carries the session this browser is signed in with and the two things a
 * browser write has to prove (`docs/api.md`, The door).
 */
export async function switchApplication(page: Page, application: string, enabled: boolean) {
  const read = await page.request.get("/api/applications");
  const { items } = (await read.json()) as {
    items: { application: string; updated_at: string }[];
  };

  const state = items.find((item) => item.application === application);

  if (state === undefined) {
    throw new Error(`the instance has no application called ${application}`);
  }

  const written = await page.request.put(`/api/applications/${application}`, {
    headers: {
      "If-Match": `"${state.updated_at}"`,
      // Both halves of what a browser write proves. A browser sets `Origin`
      // itself on anything that is not a GET; a request context is not a
      // browser and has to say so (`docs/api.md`, The door).
      "X-Personalaffe-CSRF": "1",
      Origin: new URL(page.url()).origin,
    },
    data: { enabled },
  });

  expect(written.ok(), await written.text()).toBeTruthy();
}

/**
 * A knowledge page of its own, opened and ready to write in.
 *
 * Every check that needs somewhere to write makes one rather than sharing: the
 * instance these run against keeps whatever the last check wrote, and two
 * checks editing one page would be two checks with a guard between them.
 */
export async function aPage(page: Page, title: string) {
  await page.goto("/knowledge");
  await expect(page.getByRole("heading", { name: "Knowledge" })).toBeVisible();

  await page.getByRole("button", { name: "New page", exact: true }).click();

  const dialog = page.getByRole("dialog");

  await dialog.getByRole("textbox", { name: "Title" }).fill(title);
  await dialog.getByRole("button", { name: "Write it" }).click();

  await expect(page).toHaveURL(/\/knowledge\/[0-9a-f-]{36}$/);
  await expect(page.getByRole("textbox", { name: "The page" })).toBeVisible();

  // The editor is a lazy chunk and the toolbar is dead until it is there, so
  // waiting for a mark to come alive is waiting for CodeMirror rather than
  // racing it. Typing before that goes to the page instead of into the field.
  await expect(page.getByRole("button", { name: "Bold" })).toBeEnabled();
}
