import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Scratchpad } from "@/scratchpad/Scratchpad";
import { anInstance, refused, renderAt } from "@/shared/anInstance";

const address = "/api/scratchpad/entries";

const note = {
  id: "0199f0c4-0000-7000-8000-000000000001",
  text: "the wifi password is hunter2\n\tand the guest one is not",
  pinned: false,
  created_at: "2026-09-14T08:30:00.123456Z",
  updated_at: "2026-09-14T08:30:00.123456Z",
  expires_at: "2026-09-21T08:30:00.123456Z",
};

const pinned = {
  ...note,
  id: "0199f0c4-0000-7000-8000-000000000002",
  text: "an ssh fingerprint worth keeping",
  pinned: true,
  expires_at: null,
};

function holding(...items: unknown[]) {
  return { body: { items, has_more: false } };
}

function withAClipboard() {
  const writeText = vi.fn(async () => undefined);

  vi.stubGlobal("navigator", { ...navigator, clipboard: { writeText } });

  return writeText;
}

describe("the Scratchpad", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("says nothing is in it rather than drawing a blank page", async () => {
    anInstance({ [`GET ${address}`]: holding() });

    renderAt("/scratchpad", <Scratchpad />);

    expect(await screen.findByText("Nothing is in your Scratchpad.")).toBeInTheDocument();
  });

  it("captures what was typed, and shows newlines as they were typed", async () => {
    const { asked } = anInstance({
      [`GET ${address}`]: [holding(), holding(note)],
      [`POST ${address}`]: [{ status: 201, body: note }],
    });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.type(await screen.findByLabelText("New entry"), "a line{Enter}another line");
    await userEvent.click(screen.getByRole("button", { name: "Put it down" }));

    const sent = asked.find((request) => request.method === "POST")?.body;
    expect(sent).toEqual({ text: "a line\nanother line", pinned: false });

    // Whitespace and newlines are what somebody will paste somewhere else, so
    // the row shows them rather than collapsing them.
    const shown = await screen.findByText(/the wifi password is hunter2/);
    expect(shown).toHaveClass("whitespace-pre-wrap");
  });

  it("captures with the modifier, because Enter is a newline in this box", async () => {
    const { asked } = anInstance({
      [`GET ${address}`]: [holding(), holding(note)],
      [`POST ${address}`]: [{ status: 201, body: note }],
    });

    renderAt("/scratchpad", <Scratchpad />);

    const box = await screen.findByLabelText("New entry");
    await userEvent.type(box, "a note");
    await userEvent.keyboard("{Meta>}{Enter}{/Meta}");

    await waitFor(() =>
      expect(asked.some((request) => request.method === "POST")).toBe(true),
    );
  });

  it("holds the refresh while the box has anything in it", async () => {
    const { asked } = anInstance({ [`GET ${address}`]: [holding(), holding(note)] });

    renderAt("/scratchpad", <Scratchpad />);

    await screen.findByText("Nothing is in your Scratchpad.");

    const reads = () => asked.filter((request) => request.method === "GET").length;
    const before = reads();

    await userEvent.type(await screen.findByLabelText("New entry"), "half a thought");

    // Nothing arrives underneath what somebody is typing: an answer replacing
    // the list would be one thing, an answer replacing the box would be the
    // half-written note.
    expect(reads()).toBe(before);
  });

  it("pins an entry by sending its text and its pin together, guarded", async () => {
    const { asked } = anInstance({
      [`GET ${address}`]: [holding(note), holding({ ...note, pinned: true, expires_at: null })],
      [`PUT ${address}/${note.id}`]: [{ body: { ...note, pinned: true, expires_at: null } }],
    });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.click(await screen.findByRole("button", { name: /^Pin “/ }));

    const write = asked.find((request) => request.method === "PUT");

    expect(write?.body).toEqual({ text: note.text, pinned: true });
    expect(write?.headers.get("If-Match")).toBe(`"${note.updated_at}"`);
  });

  it("says a pinned entry never expires", async () => {
    anInstance({ [`GET ${address}`]: holding(pinned) });

    renderAt("/scratchpad", <Scratchpad />);

    expect(await screen.findByText(/pinned, so it never expires/)).toBeInTheDocument();
  });

  it("asks before deleting, names the deletion as permanent, and cancelling changes nothing", async () => {
    const { asked } = anInstance({ [`GET ${address}`]: holding(note) });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.click(await screen.findByRole("button", { name: /^Delete “/ }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/does not go to the Trash/i)).toBeInTheDocument();

    await userEvent.click(within(dialog).getByRole("button", { name: "Keep it" }));

    expect(asked.some((request) => request.method === "DELETE")).toBe(false);
  });

  it("destroys the entry when the dialog is answered, guarded", async () => {
    const { asked } = anInstance({
      [`GET ${address}`]: [holding(note), holding()],
      [`DELETE ${address}/${note.id}`]: [{ status: 204 }],
    });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.click(await screen.findByRole("button", { name: /^Delete “/ }));
    await userEvent.click(
      within(await screen.findByRole("dialog")).getByRole("button", { name: "Delete for good" }),
    );

    const write = asked.find((request) => request.method === "DELETE");

    expect(write).toBeDefined();
    expect(write?.headers.get("If-Match")).toBe(`"${note.updated_at}"`);
  });

  it("puts the exact text on the clipboard", async () => {
    const writeText = withAClipboard();
    anInstance({ [`GET ${address}`]: holding(note) });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.click(await screen.findByRole("button", { name: /^Copy “/ }));

    expect(writeText).toHaveBeenCalledWith(note.text);
    expect(await screen.findByText(/Copied to this device's clipboard/)).toBeInTheDocument();
  });

  it("selects the text and says why where the clipboard is not allowed", async () => {
    // An instance reached over plain HTTP at a LAN address is not a secure
    // context, and a button that quietly did nothing there would be the worst
    // of the three answers (`docs/operations.md`).
    vi.stubGlobal("navigator", { ...navigator, clipboard: undefined });
    anInstance({ [`GET ${address}`]: holding(note) });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.click(await screen.findByRole("button", { name: /^Copy “/ }));

    expect(await screen.findByText(/only lets a page use the clipboard over HTTPS/)).toBeInTheDocument();
  });

  it("draws the switch and the permission as themselves, not as a failure", async () => {
    anInstance({ [`GET ${address}`]: refused("disabled", 409) });

    renderAt("/scratchpad", <Scratchpad />);

    expect(await screen.findByText("The Scratchpad is switched off.")).toBeInTheDocument();
  });

  it("says a credential that cannot read it cannot read it", async () => {
    anInstance({ [`GET ${address}`]: refused("forbidden", 403) });

    renderAt("/scratchpad", <Scratchpad />);

    expect(
      await screen.findByText("The Scratchpad is not this credential's to see."),
    ).toBeInTheDocument();
  });

  it("acts on a stale write rather than printing a version nobody held", async () => {
    anInstance({
      [`GET ${address}`]: [holding(note), holding(note)],
      [`PUT ${address}/${note.id}`]: [refused("stale", 412, "somebody got there first")],
    });

    renderAt("/scratchpad", <Scratchpad />);

    await userEvent.click(await screen.findByRole("button", { name: /^Pin “/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /changed while this list was open/,
    );
  });
});
