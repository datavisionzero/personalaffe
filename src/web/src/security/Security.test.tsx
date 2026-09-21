import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Security } from "@/security/Security";
import { anInstance, refused } from "@/shared/anInstance";

const off = {
  second_factor_enabled: false,
  enrolled_at: null,
  recovery_codes_remaining: 0,
  recovered_at: null,
  inactivity_lock_enabled: false,
  inactivity_minutes: 5,
};
const on = {
  second_factor_enabled: true,
  enrolled_at: "2026-09-13T12:00:00.000000Z",
  recovery_codes_remaining: 10,
  recovered_at: null,
  inactivity_lock_enabled: false,
  inactivity_minutes: 5,
};

describe("how the owner signs in", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("offers a second factor when there is none, and asks for the password to begin", async () => {
    const { asked } = anInstance({
      "GET /api/security": [{ body: off }],
      "GET /api/sessions": [{ body: [] }],
      "POST /api/security/second-factor": [
        { body: { secret: "JBSWY3DPEHPK3PXP", uri: "otpauth://totp/personalaffe:owner" } },
      ],
    });

    render(<Security />);

    await userEvent.type(
      await screen.findByLabelText("Your password, to begin"),
      "correct horse battery staple",
    );
    await userEvent.click(screen.getByRole("button", { name: "Begin" }));

    // Offered, and the screen says nothing has changed yet.
    expect(await screen.findByText("JBSWY3DPEHPK3PXP")).toBeInTheDocument();
    expect(screen.getByText(/nothing has changed/i)).toBeInTheDocument();

    expect(asked.at(-1)?.body).toEqual({ password: "correct horse battery staple" });
  });

  it("turns it on with a code and shows the recovery codes exactly once", async () => {
    anInstance({
      "GET /api/security": [{ body: off }, { body: on }],
      "GET /api/sessions": [{ body: [] }, { body: [] }],
      "POST /api/security/second-factor": [{ body: { secret: "JBSW", uri: "otpauth://x" } }],
      "POST /api/security/second-factor/confirm": [
        { body: { recovery_codes: ["ABCDE-FGHJK", "KLMNP-QRSTV"] } },
      ],
    });

    render(<Security />);

    await userEvent.type(await screen.findByLabelText("Your password, to begin"), "a password");
    await userEvent.click(screen.getByRole("button", { name: "Begin" }));

    await userEvent.type(await screen.findByLabelText("The code it shows"), "123456");
    await userEvent.click(screen.getByRole("button", { name: "Turn it on" }));

    const shown = await screen.findByRole("status");
    expect(within(shown).getByText("ABCDE-FGHJK")).toBeInTheDocument();
    expect(within(shown).getByText(/only time they are shown/i)).toBeInTheDocument();

    await userEvent.click(within(shown).getByRole("button", { name: "I have them" }));
    expect(screen.queryByText("ABCDE-FGHJK")).not.toBeInTheDocument();
  });

  it("says what the instance refused with, and changes nothing", async () => {
    anInstance({
      "GET /api/security": [{ body: off }],
      "GET /api/sessions": [{ body: [] }],
      "POST /api/security/second-factor": [refused("forbidden", 403, "That is not the current password.")],
    });

    render(<Security />);

    await userEvent.type(await screen.findByLabelText("Your password, to begin"), "not it");
    await userEvent.click(screen.getByRole("button", { name: "Begin" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("not the current password");
    expect(screen.queryByLabelText("The code it shows")).not.toBeInTheDocument();
  });

  it("lists where the owner is signed in and marks the browser that is asking", async () => {
    anInstance({
      "GET /api/security": [{ body: off }],
      "GET /api/sessions": [
        {
          body: [
            {
              id: "0199a0e0-0000-7000-8000-000000000001",
              description: "a laptop",
              created_at: "2026-09-13T12:00:00.000000Z",
              last_used_at: "2026-09-13T12:00:00.000000Z",
              expires_at: "2026-10-13T12:00:00.000000Z",
              current: true,
            },
            {
              id: "0199a0e0-0000-7000-8000-000000000002",
              description: "a phone",
              created_at: "2026-09-13T12:00:00.000000Z",
              last_used_at: "2026-09-13T12:00:00.000000Z",
              expires_at: "2026-10-13T12:00:00.000000Z",
              current: false,
            },
          ],
        },
      ],
    });

    render(<Security />);

    expect(await screen.findByText(/a laptop/)).toBeInTheDocument();
    expect(screen.getByText("— this one")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign it out" })).toBeInTheDocument();
  });

  it("says when the instance was last recovered from its server", async () => {
    anInstance({
      "GET /api/security": [{ body: { ...off, recovered_at: "2026-09-14T09:00:00.000000Z" } }],
      "GET /api/sessions": [{ body: [] }],
    });

    render(<Security />);

    // A recovery nobody performed is a recovery somebody else performed.
    expect(await screen.findByText(/recovered from its server/)).toBeInTheDocument();
  });

  it("changes the password and says every other browser is signed out", async () => {
    const { asked } = anInstance({
      "GET /api/security": [{ body: off }, { body: off }],
      "GET /api/sessions": [{ body: [] }, { body: [] }],
      "POST /api/security/password": [{}],
    });

    render(<Security />);

    expect(await screen.findByText(/Every other browser is signed out/)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText("Your password now"), "the old one at least twelve");
    await userEvent.type(screen.getByLabelText("Your new password"), "the new one at least twelve");
    await userEvent.click(screen.getByRole("button", { name: "Change it" }));

    expect(await screen.findByText("Changed.")).toBeInTheDocument();

    const changed = asked.find((request) => request.path === "/api/security/password");
    expect(changed?.body).toEqual({
      current_password: "the old one at least twelve",
      password: "the new one at least twelve",
    });
  });

  it("turns on the inactivity lock with a confirmed numeric PIN", async () => {
    const enabled = { ...off, inactivity_lock_enabled: true, inactivity_minutes: 15 };
    const { asked } = anInstance({
      "GET /api/security": [{ body: off }, { body: enabled }],
      "GET /api/sessions": [{ body: [] }, { body: [] }],
      "PUT /api/security/inactivity-lock": [{}],
    });

    render(<Security />);

    const pin = await screen.findByLabelText("PIN");
    expect(pin).toHaveAttribute("type", "password");
    expect(pin).toHaveAttribute("inputmode", "numeric");
    await userEvent.type(pin, "0042");
    await userEvent.type(screen.getByLabelText("Confirm PIN"), "0042");
    await userEvent.clear(screen.getByLabelText("Lock after this many inactive minutes"));
    await userEvent.type(screen.getByLabelText("Lock after this many inactive minutes"), "15");
    await userEvent.type(screen.getByLabelText("Your current password"), "the current password");
    await userEvent.click(screen.getByRole("button", { name: "Enable inactivity lock" }));

    expect(await screen.findByText("Changed.")).toBeInTheDocument();
    expect(asked.find((request) => request.path === "/api/security/inactivity-lock")?.body).toEqual({
      enabled: true,
      pin: "0042",
      inactivity_minutes: 15,
      current_password: "the current password",
    });
  });

  it("does not send PINs that do not match", async () => {
    const { asked } = anInstance({
      "GET /api/security": [{ body: off }],
      "GET /api/sessions": [{ body: [] }],
    });

    render(<Security />);
    await userEvent.type(await screen.findByLabelText("PIN"), "1234");
    await userEvent.type(screen.getByLabelText("Confirm PIN"), "1235");
    await userEvent.type(screen.getByLabelText("Your current password"), "the current password");
    await userEvent.click(screen.getByRole("button", { name: "Enable inactivity lock" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("do not match");
    expect(asked.some((request) => request.path === "/api/security/inactivity-lock")).toBe(false);
  });
});
