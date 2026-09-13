import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Agents } from "@/agents/Agents";
import { anInstance, refused } from "@/shared/anInstance";

const reading = {
  scratchpad: "none",
  knowledge: "read",
  tasks: "none",
  files: "none",
};

const agent = {
  id: "0199a0e0-0000-7000-8000-000000000001",
  name: "the deploy agent",
  permissions: reading,
  token_prefix: "pea_AbCdEfGh",
  created_at: "2026-09-13T12:00:00.000000Z",
  token_issued_at: "2026-09-13T12:00:00.000000Z",
  last_used_at: null,
  revoked_at: null,
};

describe("agent access", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("says what it is and what it is not", async () => {
    anInstance({ "GET /api/agents": [{ body: [] }] });

    render(<Agents />);

    expect(
      await screen.findByText(/cannot issue credentials, change how you sign in/i),
    ).toBeInTheDocument();
  });

  it("lets an agent in with a permission per application and shows the token once", async () => {
    const { asked } = anInstance({
      "GET /api/agents": [{ body: [] }, { body: [agent] }],
      "POST /api/agents": [{ status: 201, body: { agent, token: "pea_AbCdEfGhIjKl" } }],
    });

    render(<Agents />);

    await userEvent.type(await screen.findByLabelText("Name"), "the deploy agent");
    await userEvent.selectOptions(screen.getAllByLabelText("knowledge")[0], "read");
    await userEvent.click(screen.getByRole("button", { name: "Let it in" }));

    const shown = await screen.findByRole("status");
    expect(within(shown).getByText("pea_AbCdEfGhIjKl")).toBeInTheDocument();
    expect(within(shown).getByText(/only time it is shown/i)).toBeInTheDocument();

    expect(asked.find((request) => request.method === "POST")?.body).toEqual({
      name: "the deploy agent",
      permissions: reading,
    });

    await userEvent.click(within(shown).getByRole("button", { name: "I have it" }));
    expect(screen.queryByText("pea_AbCdEfGhIjKl")).not.toBeInTheDocument();
  });

  it("shows the head of the token and never the rest", async () => {
    anInstance({ "GET /api/agents": [{ body: [agent] }] });

    render(<Agents />);

    expect(await screen.findByText("pea_AbCdEfGh…")).toBeInTheDocument();
  });

  it("changes what an agent reaches", async () => {
    const { asked } = anInstance({
      "GET /api/agents": [{ body: [agent] }, { body: [agent] }],
      "PATCH /api/agents/0199a0e0-0000-7000-8000-000000000001": [{ body: agent }],
    });

    render(<Agents />);

    const row = (await screen.findByRole("listitem"));
    await userEvent.selectOptions(within(row).getByLabelText("tasks"), "read_write");

    const changed = asked.find((request) => request.method === "PATCH");
    expect(changed?.body).toEqual({
      name: null,
      permissions: { ...reading, tasks: "read_write" },
    });
  });

  it("revokes one, and says why the revoked one is still listed", async () => {
    anInstance({
      "GET /api/agents": [
        { body: [agent] },
        { body: [{ ...agent, revoked_at: "2026-09-14T12:00:00.000000Z" }] },
      ],
      "DELETE /api/agents/0199a0e0-0000-7000-8000-000000000001": [
        { body: { ...agent, revoked_at: "2026-09-14T12:00:00.000000Z" } },
      ],
    });

    render(<Agents />);

    await userEvent.click(await screen.findByRole("button", { name: "Revoke" }));

    expect(await screen.findByText(/Revoked\./)).toBeInTheDocument();
    expect(screen.getByText("the deploy agent")).toBeInTheDocument();
  });

  it("says what the instance refused with", async () => {
    anInstance({
      "GET /api/agents": [{ body: [] }],
      "POST /api/agents": [refused("conflict", 409, "Something is already called deploy.")],
    });

    render(<Agents />);

    await userEvent.type(await screen.findByLabelText("Name"), "deploy");
    await userEvent.click(screen.getByRole("button", { name: "Let it in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("already called deploy");
  });
});
