import { expect, test, type Page } from "@playwright/test";

import { owner, signedIn } from "./theInstance";

test("locks across browser sessions and preserves unsaved work after password recovery", async ({ browser }) => {
  const first = await browser.newContext();
  const second = await browser.newContext();
  const page = await first.newPage();
  const settings = await second.newPage();

  try {
    await signedIn(page);
    await signedIn(settings);

    // The settings form itself is part of the journey: a leading-zero PIN is
    // a string, masked, and accompanied by an explicit timeout and password.
    await settings.goto("/settings/security");
    const pin = settings.getByLabel("PIN", { exact: true });
    await expect(pin).toHaveAttribute("inputmode", "numeric");
    await pin.fill("0042");
    await settings.getByLabel("Confirm PIN").fill("0042");
    await settings.getByLabel("Lock after this many inactive minutes").fill("5");
    await settings.getByLabel("Your current password").fill(owner.password);
    await settings.getByRole("button", { name: "Enable inactivity lock" }).click();
    await expect(settings.getByText("Changed.")).toBeVisible();

    // Reload once so this browser has the server's absolute deadline, then
    // leave text unsaved. A PIN change from the other session locks this one.
    await page.goto("/scratchpad");
    const draft = `still here after locking ${Date.now()}`;
    await page.getByRole("textbox", { name: "New entry" }).fill(draft);

    await configure(settings, true, "0055", 5);
    await page.bringToFront();
    await page.evaluate(() => window.dispatchEvent(new Event("focus")));

    await expect(page.getByRole("heading", { name: "Workspace locked" })).toBeVisible();
    await expect(page).toHaveURL(/\/scratchpad$/);
    await expect(page.getByRole("heading", { name: "Scratchpad" })).toBeHidden();

    // A failed PIN says when it may be tried again. The independent password
    // path remains available immediately and does not discard the mounted UI.
    await page.getByLabel("PIN").fill("9999");
    await page.getByRole("button", { name: "Unlock" }).click();
    await expect(page.getByRole("alert")).toContainText("Try again in");
    await page.getByRole("button", { name: "Use my password" }).click();
    await page.getByLabel("Current password").fill(owner.password);
    await page.getByRole("button", { name: "Unlock" }).click();

    await expect(page.getByRole("heading", { name: "Scratchpad" })).toBeVisible();
    await expect(page.getByRole("textbox", { name: "New entry" })).toHaveValue(draft);
  } finally {
    await configure(settings, false, null, null).catch(() => undefined);
    await first.close();
    await second.close();
  }
});

async function configure(
  page: Page,
  enabled: boolean,
  pin: string | null,
  inactivityMinutes: number | null,
) {
  const response = await page.request.put("/api/security/inactivity-lock", {
    headers: {
      "X-Personalaffe-CSRF": "1",
      Origin: new URL(page.url()).origin,
    },
    data: {
      enabled,
      pin,
      inactivity_minutes: inactivityMinutes,
      current_password: owner.password,
    },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
}
