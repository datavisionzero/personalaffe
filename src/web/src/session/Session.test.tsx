import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { App } from "@/shell/App";
import {
  anInstance,
  refused,
  renderAt,
  theApplications,
  theOwner as owner,
} from "@/shared/anInstance";

const nothingBehindTheDoor = {
  "GET /api/session/lock": { body: { enabled: false, locked: false, locks_at: null } },
  "GET /api/applications": theApplications(),
  "GET /api/trash": { body: { items: [], has_more: false } },
  "GET /api/security": {
    body: {
      second_factor_enabled: false,
      enrolled_at: null,
      recovery_codes_remaining: 0,
      recovered_at: null,
    },
  },
  "GET /api/sessions": { body: [] },
  "GET /api/agents": { body: [] },
};

describe("the door", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("offers setup at an instance nobody has claimed, and nothing else", async () => {
    anInstance({ "GET /api/setup": { body: { required: true } } });

    renderAt("/", <App />);

    expect(await screen.findByRole("heading", { name: "Claim this workspace" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Sign in" })).not.toBeInTheDocument();
  });

  it("claims the instance and is signed in afterwards, without asking twice", async () => {
    const { asked } = anInstance({
      "GET /api/setup": [{ body: { required: true } }],
      "POST /api/setup": [{}],
      "POST /api/session": [{}],
      "GET /api/me": [{ body: owner }],
      ...nothingBehindTheDoor,
    });

    renderAt("/", <App />);

    await userEvent.type(
      await screen.findByLabelText("Email address"),
      "owner@example.com",
    );
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Claim it" }));

    expect(await screen.findByText("owner@example.com")).toBeInTheDocument();

    const setup = asked.find((request) => request.path === "/api/setup" && request.method === "POST");
    expect(setup?.body).toEqual({
      email: "owner@example.com",
      password: "correct horse battery staple",
    });
  });

  it("offers sign-in at an instance that has an owner and nobody signed in", async () => {
    anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": refused("unauthenticated", 401),
    });

    renderAt("/", <App />);

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("signs in and shows who is signed in", async () => {
    anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": [refused("unauthenticated", 401), { body: owner }],
      "POST /api/session": [{}],
      ...nothingBehindTheDoor,
    });

    renderAt("/", <App />);

    await userEvent.type(await screen.findByLabelText("Email address"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("owner@example.com")).toBeInTheDocument();
  });

  it("asks for the code when the instance says the password was not the whole of it", async () => {
    const { asked } = anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": [refused("unauthenticated", 401), { body: owner }],
      "POST /api/session": [refused("second-factor", 401, "Send it again with the code."), {}],
      ...nothingBehindTheDoor,
    });

    renderAt("/", <App />);

    await userEvent.type(await screen.findByLabelText("Email address"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    // Not shown as a refusal: the password was right, and what is wanted is a
    // field that was not on the screen.
    const code = await screen.findByLabelText("Code from your authenticator");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    await userEvent.type(code, "123456");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("owner@example.com")).toBeInTheDocument();

    const second = asked.filter((request) => request.path === "/api/session").at(-1);
    expect(second?.body).toMatchObject({ second_factor: "123456" });
  });

  it("says what the instance refused with, and does not ask for a code it was not asked for", async () => {
    anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": refused("unauthenticated", 401),
      "POST /api/session": refused("unauthenticated", 401, "The email address or the password is not correct."),
    });

    renderAt("/", <App />);

    await userEvent.type(await screen.findByLabelText("Email address"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "not the password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("not correct");
    expect(screen.queryByLabelText("Code from your authenticator")).not.toBeInTheDocument();
  });

  it("proves every write came from this application", async () => {
    const { asked } = anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": [refused("unauthenticated", 401), { body: owner }],
      "POST /api/session": [{}],
      ...nothingBehindTheDoor,
    });

    renderAt("/", <App />);

    await userEvent.type(await screen.findByLabelText("Email address"), "owner@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "correct horse battery staple");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));
    await screen.findByText("owner@example.com");

    const write = asked.find((request) => request.method === "POST");
    expect(write?.headers.get("X-Personalaffe-CSRF")).toBe("1");
  });

  it("goes back to sign-in when the session is ended", async () => {
    anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": [{ body: owner }],
      "DELETE /api/session": [{}],
      ...nothingBehindTheDoor,
    });

    renderAt("/", <App />);

    // Behind the door now: signing out is in the account menu, where a reader
    // of any web application looks for it.
    await userEvent.click(
      await screen.findByRole("button", { name: `Account: ${owner.email}` }),
    );
    await userEvent.click(await screen.findByRole("menuitem", { name: "Sign out" }));

    expect(await screen.findByRole("heading", { name: "Sign in" })).toBeInTheDocument();
  });

  it("shows only the lock screen when a locked browser reloads", async () => {
    anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": refused("locked", 423, "This browser is locked."),
    });

    renderAt("/settings/security", <App />);

    expect(await screen.findByRole("heading", { name: "Workspace locked" })).toBeInTheDocument();
    expect(screen.queryByText(owner.email!)).not.toBeInTheDocument();
    const pin = screen.getByLabelText("PIN");
    expect(pin).toHaveAttribute("type", "password");
    expect(pin).toHaveAttribute("inputmode", "numeric");
    expect(pin).toHaveAttribute("maxlength", "6");
  });

  it("unlocks with the PIN and validates the result with the server", async () => {
    const { asked } = anInstance({
      ...nothingBehindTheDoor,
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": { body: owner },
      "GET /api/session/lock": [
        { body: { enabled: true, locked: true, locks_at: null } },
        { body: { enabled: true, locked: false, locks_at: "2099-01-01T00:00:00Z" } },
      ],
      "POST /api/session/lock/unlock": [{}],
    });

    renderAt("/", <App />);
    const pin = await screen.findByLabelText("PIN");
    const preservedAccount = screen.getByRole("button", { name: `Account: ${owner.email}`, hidden: true });
    expect(preservedAccount.closest("[hidden]")).toBeTruthy();
    await userEvent.type(pin, "0042");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));

    expect(await screen.findByText(owner.email!)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: `Account: ${owner.email}` })).toBe(preservedAccount);
    expect(asked.find((request) => request.path === "/api/session/lock/unlock")?.body).toEqual({
      pin: "0042",
      password: null,
    });
    expect(asked.filter((request) => request.path === "/api/session/lock")).toHaveLength(2);
  });

  it("offers password fallback and explains when another attempt is allowed", async () => {
    anInstance({
      "GET /api/setup": { body: { required: false } },
      "GET /api/me": refused("locked", 423),
      "POST /api/session/lock/unlock": [{
        ...refused("throttled", 429, "That proof did not unlock this browser."),
        headers: { "Retry-After": "7" },
      }],
    });

    renderAt("/", <App />);
    await screen.findByRole("heading", { name: "Workspace locked" });
    await userEvent.click(screen.getByRole("button", { name: "Use my password" }));
    await userEvent.type(screen.getByLabelText("Current password"), "not the password");
    await userEvent.click(screen.getByRole("button", { name: "Unlock" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Try again in 7 seconds");
  });

  it("tells an instance that is not there apart from one that refused", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn<typeof globalThis.fetch>(() => Promise.reject(new TypeError("Failed to fetch"))),
    );

    renderAt("/", <App />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Nothing answered at this address");
  });
});
