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

  await expect(page.getByRole("heading", { name: "Your workspace" })).toBeVisible();
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
