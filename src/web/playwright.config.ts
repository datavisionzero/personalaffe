import { defineConfig, devices } from "@playwright/test";

/**
 * The browser checks (`docs/codebase.md`).
 *
 * <b>They run against a real instance, in a real browser, and nothing here
 * stands in for either.</b> Everything under `src/web/src/**.test.tsx` runs in
 * jsdom, which lays nothing out: it cannot say whether the sidebar is a drawer
 * on a phone, whether CodeMirror — which measures a selection it draws — works
 * at all, or whether a screen brings a change made elsewhere onto itself while
 * nobody touches it. Those are what these are for, and they are the reason CI
 * has a seventh job.
 *
 * The address is the instance's own, so the application is served by the
 * installation rather than by a dev server with a proxy in front of it: what is
 * checked is what an operator starts.
 */
const instance = process.env.PERSONALAFFE_URL ?? "http://127.0.0.1:5142";

export default defineConfig({
  testDir: "./browser",

  // One worker, and that is not a performance compromise. There is one instance
  // with one owner and one database; two workers would be two browsers claiming
  // the same workspace and switching the same applications off underneath each
  // other.
  workers: 1,
  fullyParallel: false,

  // A refresh this waits for is fifteen seconds away by design
  // (`src/web/src/shared/ask.ts`), so the assertion that waits for one is given
  // room to. Nothing else here is slow.
  timeout: 60_000,
  expect: { timeout: 30_000 },

  // A flake retried is a flake nobody reads. These run against a database that
  // starts empty and a browser that starts clean, so a failure is a failure.
  retries: 0,
  forbidOnly: !!process.env.CI,

  reporter: process.env.CI ? [["github"], ["list"]] : [["list"]],

  use: {
    baseURL: instance,
    trace: "retain-on-failure",
    video: "retain-on-failure",
  },

  projects: [
    {
      name: "desktop",
      use: { ...devices["Desktop Chrome"] },
      testIgnore: /phone\.spec\.ts/,
    },
    {
      // The other half of "responsive": the same application at a phone's
      // width, where the sidebar is a drawer and the header carries the button
      // that opens it.
      name: "phone",
      use: { ...devices["Pixel 7"] },
      testMatch: /phone\.spec\.ts/,
    },
  ],
});
