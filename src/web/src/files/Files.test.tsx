import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";

import { Files } from "@/files/Files";
import { anInstance, refused, renderAt } from "@/shared/anInstance";

const reisen = {
  id: "0199f0c4-0000-7000-8000-00000000000a",
  name: "Reisen",
  parent: null,
  created_at: "2026-09-14T08:00:00.000000Z",
  updated_at: "2026-09-14T08:00:00.000000Z",
};

const plan = {
  id: "0199f0c4-0000-7000-8000-00000000000f",
  name: "plan.md",
  folder: null,
  size: 12,
  media_type: "text/markdown",
  created_at: "2026-09-14T08:10:00.000000Z",
  updated_at: "2026-09-14T08:10:00.000000Z",
};

const bahn = {
  ...plan,
  id: "0199f0c4-0000-7000-8000-00000000000b",
  name: "bahn.pdf",
  folder: reisen.id,
  size: 284119,
};

function holding(
  folders: unknown[] = [],
  files: unknown[] = [],
  chain: unknown[] = [],
  used = 0,
) {
  return {
    body: {
      chain,
      folders,
      files,
      used_bytes: used,
      max_file_bytes: 67108864,
      max_total_bytes: 5368709120,
    },
  };
}

/**
 * The screen at its real address. The route is `/files/*` in the frame, and the
 * folder is what is under it, so a test that rendered the component bare would
 * be a test in which no folder can ever be opened.
 */
function filesAt(path: string) {
  return renderAt(
    path,
    <Routes>
      <Route path="/files/*" element={<Files />} />
    </Routes>,
  );
}

afterEach(() => vi.restoreAllMocks());

describe("Files", () => {

  it("opens download beside the row and restores focus when the menu closes", async () => {
    anInstance({ "GET /api/files": holding([], [plan]) });
    filesAt("/files");
    const trigger = await screen.findByRole("button", { name: "Actions for plan.md" });
    await userEvent.click(trigger);
    expect(screen.getByRole("menuitem", { name: "Download plan.md" })).toHaveAttribute("href", `/api/files/${plan.id}/content`);
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  });

  it("moves a file without renaming it", async () => {
    const { asked } = anInstance({ "GET /api/files": holding([reisen], [plan]), [`PUT /api/files/${plan.id}`]: { body: plan } });
    filesAt("/files");
    await userEvent.click(await screen.findByRole("button", { name: "Actions for plan.md" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Move to folder" }));
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.queryByLabelText("Name")).not.toBeInTheDocument();
    await userEvent.selectOptions(dialog.getByLabelText("Where"), reisen.id);
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(asked.find(request => request.method === "PUT")?.body).toEqual({ name: plan.name, folder: reisen.id }));
  });

  it("sorts files naturally and keeps folders first", async () => {
    anInstance({ "GET /api/files": holding([reisen], [plan, { ...bahn, name: "2.pdf" }]) });
    filesAt("/files");
    const table = await screen.findByRole("table");
    await screen.findByRole("button", { name: "plan.md" });
    expect(within(table).getAllByRole("row").slice(1).map(row => row.textContent)).toEqual([
      expect.stringContaining("Reisen"), expect.stringContaining("2.pdf"), expect.stringContaining("plan.md"),
    ]);
    await userEvent.click(screen.getByRole("button", { name: "Name" }));
    expect(within(table).getAllByRole("row")[1]).toHaveTextContent("Reisen");
    expect(within(table).getAllByRole("row")[2]).toHaveTextContent("plan.md");
  });

  it("opens a mobile action sheet and edits in the same dialog", async () => {
    const match = window.matchMedia;
    vi.spyOn(window, "matchMedia").mockImplementation(query => ({ ...match(query), matches: true }));
    anInstance({ "GET /api/files": holding([], [plan]) });
    filesAt("/files");
    const trigger = await screen.findByRole("button", { name: "Actions for plan.md" });
    await userEvent.click(trigger);
    const dialog = within(screen.getByRole("dialog", { name: "plan.md" }));
    expect(dialog.getByRole("link", { name: "Download plan.md" })).toHaveAttribute("download", plan.name);
    await userEvent.click(dialog.getByRole("button", { name: "Rename" }));
    expect(screen.getAllByRole("dialog")).toHaveLength(1);
    expect(dialog.getByLabelText("Name")).toHaveValue(plan.name);
    await userEvent.click(dialog.getByRole("button", { name: "Back to file actions" }));
    expect(dialog.getByRole("link", { name: "Download plan.md" })).toBeInTheDocument();
    await userEvent.keyboard("{Escape}");
    await waitFor(() => expect(trigger).toHaveFocus());
  });


  it("switches actions with selection and clears them when walking to another folder", async () => {
    anInstance({ "GET /api/files": [holding([reisen], [plan, bahn]), holding([], [bahn], [reisen])] });
    filesAt("/files");
    await userEvent.click(await screen.findByRole("button", { name: "plan.md" }));
    expect(screen.getByRole("link", { name: "Download plan.md" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "bahn.pdf" }));
    expect(screen.queryByRole("link", { name: "Download plan.md" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Download bahn.pdf" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Reisen" }));
    await screen.findByText("bahn.pdf");
    expect(screen.queryByRole("region", { name: "Selected item actions" })).not.toBeInTheDocument();
  });

  it("uses refreshed metadata for the selected item's guarded write", async () => {
    const updated = { ...plan, name: "new-plan.md", updated_at: "2026-10-30T12:00:00Z" };
    const { asked } = anInstance({
      "GET /api/files": [holding([], [plan]), holding([], [updated])],
      [`PUT /api/files/${plan.id}`]: { body: updated },
      [`DELETE /api/files/${plan.id}`]: { status: 204 },
    });
    filesAt("/files");
    await userEvent.click(await screen.findByRole("button", { name: "plan.md" }));
    await userEvent.click(screen.getByRole("button", { name: "Rename plan.md" }));
    const dialog = within(screen.getByRole("dialog"));
    await userEvent.clear(dialog.getByLabelText("Name"));
    await userEvent.type(dialog.getByLabelText("Name"), updated.name);
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));
    await userEvent.click(await screen.findByRole("button", { name: "Delete new-plan.md" }));
    await waitFor(() => expect(asked.find(request => request.method === "DELETE")?.headers.get("If-Match")).toBe(`"${updated.updated_at}"`));
  });

  it("says the folder is empty rather than drawing a blank page", async () => {
    anInstance({ "GET /api/files": holding() });

    filesAt("/files");

    expect(await screen.findByText("Nothing in this folder.")).toBeInTheDocument();
  });

  it("lists folders before files and offers a download for the selected file", async () => {
    anInstance({ "GET /api/files": holding([reisen], [plan], [], 12) });

    filesAt("/files");

    expect(await screen.findByRole("button", { name: "Reisen" })).toBeInTheDocument();
    expect(screen.getByText("plan.md")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "plan.md" }));

    // The download is a plain link to the instance's own address, so the
    // browser fetches it with the session it already has and nothing on this
    // page holds a file the instance can store.
    expect(screen.getByRole("link", { name: "Download plan.md" })).toHaveAttribute(
      "href",
      `/api/files/${plan.id}/content`,
    );
  });

  it("says how much room is left, because this is the screen somebody uploads from", async () => {
    anInstance({ "GET /api/files": holding([], [plan], [], 1024 * 1024) });

    filesAt("/files");

    expect(await screen.findByText(/1.0 MiB of 5.0 GiB used/)).toBeInTheDocument();
    expect(screen.getByText(/at most 64 MiB per file/)).toBeInTheDocument();
  });

  it("opens a folder at its own address, with a breadcrumb back", async () => {
    anInstance({
      "GET /api/files": [holding([reisen]), holding([], [bahn], [reisen], 284119)],
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "Reisen" }));

    // The address is the folder, so a link into one opens that one.
    expect(await screen.findByText("bahn.pdf")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Where you are" })).toHaveTextContent("Reisen");
  });

  it("opens the folder a pasted address names, without walking to it", async () => {
    const { asked } = anInstance({
      "GET /api/files": holding([], [bahn], [reisen], 284119),
    });

    filesAt(`/files/${reisen.id}`);

    expect(await screen.findByText("bahn.pdf")).toBeInTheDocument();
    expect(asked[0]?.path).toBe("/api/files");
  });

  it("sends an upload to the content address, with its name and its media type", async () => {
    const { asked } = anInstance({
      "GET /api/files": [holding(), holding([], [plan])],
      "POST /api/files/content": { status: 201, body: plan },
    });

    filesAt("/files");

    await screen.findByText("Nothing in this folder.");

    await userEvent.upload(
      screen.getByLabelText("Files to upload"),
      new File(["# the plan"], "plan.md", { type: "text/markdown" }),
    );

    await waitFor(() => expect(screen.getByText("plan.md")).toBeInTheDocument());

    const upload = asked.find((one) => one.path === "/api/files/content");

    // The name and the type are what this test can see. jsdom's `File` is not
    // a body its `Request` knows how to carry, so the bytes themselves cannot
    // be asserted here — that is `browser/files.spec.ts`, in a real browser,
    // where a real file goes up and comes back down.
    expect(upload).toBeDefined();
    expect(upload?.headers.get("Content-Type")).toBe("text/markdown");
  });

  it("names the folder it is uploading into", async () => {
    const { asked } = anInstance({
      "GET /api/files": holding([], [], [reisen]),
      "POST /api/files/content": { status: 201, body: bahn },
    });

    filesAt(`/files/${reisen.id}`);

    await screen.findByText("Nothing in this folder.");

    await userEvent.upload(
      screen.getByLabelText("Files to upload"),
      new File(["x"], "bahn.pdf", { type: "application/pdf" }),
    );

    await waitFor(() =>
      expect(asked.some((one) => one.path === "/api/files/content")).toBe(true),
    );

    // The wire carries the folder's id, because the folder is what the address
    // of this screen is.
    expect(asked.find((one) => one.path === "/api/files/content")?.query).toContain(
      `folder=${reisen.id}`,
    );
  });

  it("says which upload was refused and keeps the others", async () => {
    anInstance({
      "GET /api/files": holding(),
      "POST /api/files/content": [
        refused("too-large", 413, "This file is larger than the 64 MiB one file may be."),
        { status: 201, body: plan },
      ],
    });

    filesAt("/files");

    await screen.findByText("Nothing in this folder.");

    await userEvent.upload(screen.getByLabelText("Files to upload"), [
      new File(["x"], "big.bin"),
      new File(["y"], "small.bin"),
    ]);

    // One file that is too large must not take the rest of a drop down with it.
    expect(await screen.findByRole("alert")).toHaveTextContent("64 MiB");
  });

  it("makes a folder from the dialog and reads the listing again", async () => {
    const { asked } = anInstance({
      "GET /api/files": [holding(), holding([reisen])],
      "POST /api/files/folders": { status: 201, body: reisen },
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "New folder" }));
    await userEvent.type(screen.getByLabelText("Name"), "Reisen");
    await userEvent.click(screen.getByRole("button", { name: "Make it" }));

    await waitFor(() =>
      expect(asked.some((one) => one.path === "/api/files/folders")).toBe(true),
    );

    expect(await screen.findByRole("button", { name: "Reisen" })).toBeInTheDocument();
  });

  it("renames with a guarded write while preserving the current folder", async () => {
    const { asked } = anInstance({
      "GET /api/files": holding([reisen], [plan]),
      [`PUT /api/files/${plan.id}`]: { body: plan },
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "Actions for plan.md" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Rename" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.clear(dialog.getByLabelText("Name"));
    await userEvent.type(dialog.getByLabelText("Name"), "der Plan.md");
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(write.body).toEqual({ name: "der Plan.md", folder: null });

    // Every write says which version it replaces (`docs/api.md`).
    expect(write.headers.get("If-Match")).toBe(`"${plan.updated_at}"`);
  });

  it("says a stale write is being read again rather than printing a version nobody held", async () => {
    anInstance({
      "GET /api/files": holding([], [plan]),
      [`PUT /api/files/${plan.id}`]: refused("stale", 412, "The file has changed since it was read."),
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "Actions for plan.md" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Rename" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.clear(dialog.getByLabelText("Name"));
    await userEvent.type(dialog.getByLabelText("Name"), "der Plan.md");
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("being read again");
  });

  it("says a taken name is taken, in the instance's own sentence", async () => {
    anInstance({
      "GET /api/files": holding([], [plan]),
      [`PUT /api/files/${plan.id}`]: refused(
        "conflict",
        409,
        "Something in that folder is already called `der Plan.md`.",
      ),
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "Actions for plan.md" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Rename" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.clear(dialog.getByLabelText("Name"));
    await userEvent.type(dialog.getByLabelText("Name"), "der Plan.md");
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("already called");
  });

  it("deletes without asking, and says where it went", async () => {
    const { asked } = anInstance({
      "GET /api/files": [holding([], [plan]), holding()],
      [`DELETE /api/files/${plan.id}`]: { status: 204 },
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "Actions for plan.md" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Delete plan.md" }));

    // The opposite of the Scratchpad's dialog: nothing was destroyed, so the
    // screen says what is true instead of asking whether to make it true.
    expect(await screen.findByText(/plan.md is in the Trash/)).toBeInTheDocument();

    const write = asked.find((one) => one.method === "DELETE");
    expect(write?.headers.get("If-Match")).toBe(`"${plan.updated_at}"`);
  });

  it("says a folder took everything in it", async () => {
    anInstance({
      "GET /api/files": [holding([reisen]), holding()],
      [`DELETE /api/files/folders/${reisen.id}`]: { status: 204 },
    });

    filesAt("/files");

    await userEvent.click(await screen.findByRole("button", { name: "Actions for Reisen" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Delete Reisen" }));

    expect(await screen.findByText(/everything in it are in the Trash/)).toBeInTheDocument();
  });

  it("draws the denied state rather than a failure when access was revoked", async () => {
    anInstance({
      "GET /api/files": refused("forbidden", 403, "This access does not reach Files."),
    });

    filesAt("/files");

    expect(await screen.findByText("Files is not this credential's to see.")).toBeInTheDocument();
  });

  it("draws the disabled state rather than a failure when the switch is thrown", async () => {
    anInstance({
      "GET /api/files": refused("disabled", 409, "Files is switched off in this workspace."),
    });

    filesAt("/files");

    expect(await screen.findByText("Files is switched off.")).toBeInTheDocument();
  });

  it("says a folder that is gone is gone, with the way to try again", async () => {
    anInstance({
      "GET /api/files": refused("not-found", 404, "Nothing with that id is a folder in Files."),
    });

    filesAt(`/files/${reisen.id}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("folder");
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});
