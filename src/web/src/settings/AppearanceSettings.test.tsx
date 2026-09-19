import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  anAgent,
  anInstance,
  refused,
  renderAt,
  theAppearance,
  theApplications,
  theOwner,
} from "@/shared/anInstance";
import { Shell } from "@/shell/Shell";

function settings(overrides: Record<string, unknown> = {}) {
  return anInstance({
    "GET /api/applications": theApplications(),
    "GET /api/appearance": theAppearance(),
    ...overrides,
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
  delete document.documentElement.dataset.markColour;
});

describe("what this instance is called", () => {
  it("names it, with the version it read", async () => {
    const instance = settings({
      "PUT /api/appearance": theAppearance({
        title: "Haus",
        colour: "teal",
        shape: "circle",
        updated_at: "2026-09-19T14:00:00.000000Z",
      }),
    });

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const field = await screen.findByLabelText("Name");
    await userEvent.type(field, "Haus");
    await userEvent.selectOptions(screen.getByLabelText("Colour"), "teal");
    await userEvent.selectOptions(screen.getByLabelText("Mark"), "circle");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    const written = await waitFor(() => {
      const one = instance.asked.find((asked) => asked.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(written.path).toBe("/api/appearance");
    expect(written.body).toEqual({ title: "Haus", colour: "teal", shape: "circle" });

    // A write says which version it replaces (`docs/api.md`, The guarded
    // write), and this screen sends the one it drew.
    expect(written.headers.get("If-Match")).toBe('"2026-01-01T00:00:00.000000Z"');
  });

  it("sends nothing at all for a name that is only space", async () => {
    const instance = settings({
      "PUT /api/appearance": theAppearance(),
    });

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Name"), "   ");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    const written = await waitFor(() => {
      const one = instance.asked.find((asked) => asked.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(written.body).toMatchObject({ title: null });
  });

  it("says what a stale write means rather than repeating the code", async () => {
    settings({ "PUT /api/appearance": refused("stale", 412, "It changed.") });

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Name"), "Haus");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/renamed somewhere else/);
  });

  it("says the refusal the instance gave where it is not a stale one", async () => {
    settings({ "PUT /api/appearance": refused("validation", 400, "A title is at most 40 characters.") });

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Name"), "Haus");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("A title is at most 40 characters.");
  });

  it("says out loud that the name is public", async () => {
    settings();

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(
      await screen.findByText(/whoever can reach this instance can read the name you choose/i),
    ).toBeInTheDocument();
  });

  it("shows an agent what is set and no buttons", async () => {
    settings({ "GET /api/appearance": theAppearance({ title: "Haus", colour: "amber" }) });

    renderAt("/settings/appearance", <Shell me={anAgent()} onSignedOut={() => undefined} />);

    // The three of them, read off the list rather than off the page: the name
    // is in the sidebar and in the preview too, which is the point of it.
    const shown = (await screen.findAllByRole("definition")).map((one) => one.textContent);
    expect(shown).toEqual(["Haus", "Amber", "Rounded square"]);

    // Drawing a control that can only ever be refused is offering something
    // that is not on offer.
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();
  });

  it("previews the mark before it is saved, without applying it to the document", async () => {
    settings();

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.selectOptions(await screen.findByLabelText("Colour"), "green");

    // The preview is the real mark in the chosen colour…
    const marks = screen.getAllByTestId("mark");
    expect(marks.some((mark) => mark.dataset.markColour === "green")).toBe(true);

    // …and the instance is still wearing what it is wearing until it is saved.
    expect(document.documentElement.dataset.markColour).toBe("violet");
  });

  it("writes a name containing markup into the preview literally", async () => {
    settings();

    renderAt("/settings/appearance", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.type(await screen.findByLabelText("Name"), "<b>Haus</b>");

    const shown = await screen.findByText("<b>Haus</b>");

    expect(shown).toBeInTheDocument();
    expect(shown.querySelector("b")).toBeNull();
  });
});
