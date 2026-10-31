import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";

import { KnowledgeDrafts } from "./drafts";
import { KnowledgeDraftProvider } from "./KnowledgeDraftProvider";
let manager: KnowledgeDrafts;
const readDraft = (id: string) => manager.snapshot()[id]?.draft;
import { Knowledge } from "@/knowledge/Knowledge";
import { anInstance, refused, renderAt } from "@/shared/anInstance";

const reisen = {
  id: "0199f0c6-0000-7000-8000-00000000000a",
  title: "Reisen",
  parent: null,
  created_at: "2026-09-14T08:00:00.000000Z",
  updated_at: "2026-09-14T08:00:00.000000Z",
};

const bahn = {
  id: "0199f0c6-0000-7000-8000-00000000000b",
  title: "Bahn",
  parent: reisen.id,
  created_at: "2026-09-14T08:10:00.000000Z",
  updated_at: "2026-09-14T08:10:00.000000Z",
};

const thePage = { ...bahn, markdown: "# Bahn\n\nDas Wichtigste zuerst.\n" };

const aRevision = {
  id: "0199f0c6-0000-7000-8000-0000000000f1",
  title: "Die Bahn",
  at: "2026-09-14T08:05:00.000000Z",
  by: { kind: "owner", name: null },
};

function aTree(...pages: unknown[]) {
  return { body: { pages } };
}

/**
 * The screen at its real address. The route is `/knowledge/*` in the frame, and
 * the page is what is under it, so a test that rendered the component bare
 * would be a test in which no page can ever be opened.
 */
function knowledgeAt(path: string) {
  return renderAt(
    path,
    <KnowledgeDraftProvider account="test" manager={manager}><Routes>
      <Route path="/knowledge/*" element={<Knowledge />} />
    </Routes></KnowledgeDraftProvider>,
  );
}

describe("Knowledge", () => {
  beforeEach(() => { localStorage.clear(); sessionStorage.clear(); manager = new KnowledgeDrafts("test"); });
  it("says nothing is written rather than drawing a blank page", async () => {
    anInstance({ "GET /api/knowledge/pages": aTree() });

    knowledgeAt("/knowledge");

    expect(await screen.findByText(/No pages yet/)).toBeInTheDocument();
  });

  it("draws the tree from the flat list, with each page under the one it names", async () => {
    anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn) });

    knowledgeAt("/knowledge");

    const tree = within(await screen.findByRole("navigation", { name: "The pages" }));

    expect(tree.getByRole("link", { name: "Reisen" })).toHaveAttribute(
      "href",
      `/knowledge/${reisen.id}`,
    );

    // The instance answers the pages flat, each saying which one it is under;
    // the hierarchy is drawn here, in one pass.
    const under = tree.getByRole("link", { name: "Bahn" });

    expect(under.closest("li")?.parentElement?.closest("li")).toContainElement(
      tree.getByRole("link", { name: "Reisen" }),
    );
  });

  it("keeps branches collapsed across page navigation and offers an empty-page editor", async () => {
    const other = { ...reisen, id: "other", title: "Other" };
    const nested = { ...bahn, id: "nested", parent: other.id, title: "Nested" };
    anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn, other, nested),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`GET /api/knowledge/pages/${other.id}`]: { body: { ...other, markdown: "" } } });
    knowledgeAt("/knowledge");
    await userEvent.click(await screen.findByRole("button", { name: "Collapse Reisen" }));
    await userEvent.click(screen.getByRole("button", { name: "Collapse Other" }));
    await userEvent.click(within(screen.getByRole("navigation", { name: "The pages" })).getByRole("link", { name: "Other" }));
    await screen.findByDisplayValue("Other");
    expect(screen.getByRole("button", { name: "Expand Reisen" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Expand Other" })).toBeInTheDocument();
    expect(await screen.findByRole("textbox", { name: "The page" })).toBeInTheDocument();
  });

  it("offers a top-level page even while reading a nested page", async () => {
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      "POST /api/knowledge/pages": { body: { ...reisen, markdown: "" } },
      [`GET /api/knowledge/pages/${reisen.id}`]: { body: { ...reisen, markdown: "" } } });
    knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Create page" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "New page" }));
    const dialog = within(screen.getByRole("dialog"));
    await userEvent.type(dialog.getByRole("textbox", { name: "Title" }), "New root");
    await userEvent.click(dialog.getByRole("button", { name: "Create page" }));
    await waitFor(() => expect(asked.find(r => r.method === "POST")?.body).toEqual({ title: "New root", parent: null, markdown: "" }));
  });

  it("opens the page a pasted address names, with its Markdown in the field", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    expect(screen.queryByRole("button", { name: "Edit page" })).toBeNull();
    expect(screen.queryByLabelText("The page, preview")).toBeNull();
    expect(await screen.findByRole("textbox", { name: "The page" })).toHaveValue(thePage.markdown);
    expect(screen.getByLabelText("Title")).toHaveValue("Bahn");
  });

  it("carries the title, the place and the body in one guarded write", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: { body: thePage },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    const field = await screen.findByRole("textbox", { name: "The page" });

    await userEvent.clear(field);
    await userEvent.type(field, "etwas anderes");
    await waitFor(() => expect(asked.some(r => r.method === "PUT")).toBe(true), { timeout: 2000 });

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(write.body).toEqual({
      title: "Bahn",
      parent: reisen.id,
      markdown: "etwas anderes",
    });

    // Every write says which version it replaces (`docs/api.md`).
    expect(write.headers.get("If-Match")).toBe(`"${thePage.updated_at}"`);
  });

  it("holds the refresh while there is unsaved work, and lets go when it is saved", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: { body: thePage },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    const field = await screen.findByRole("textbox", { name: "The page" });

    await userEvent.type(field, "noch ein Satz");

    expect(screen.getByText(/Draft saved on this device/)).toBeInTheDocument();
    await waitFor(() => expect(asked.some(r => r.method === "PUT")).toBe(true), { timeout: 2000 });

    expect(await screen.findByText("Saved.")).toBeInTheDocument();
  });

  it("keeps what somebody wrote when the write is refused", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: refused(
        "stale",
        412,
        "The page has changed since it was read.",
      ),
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    const field = await screen.findByRole("textbox", { name: "The page" });

    await userEvent.clear(field);
    await userEvent.type(field, "meine Fassung");
    await screen.findByRole("alert", {}, { timeout: 2000 });

    // A draft dropped optimistically is a draft that loses what somebody wrote.
    expect(await screen.findByRole("alert")).toHaveTextContent("Your draft is preserved");
    expect(screen.getByRole("textbox", { name: "The page" })).toHaveValue("meine Fassung");
  });

  it("writes a new page under the one that is open", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      "POST /api/knowledge/pages": { status: 201, body: { ...thePage, id: reisen.id } },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Create page" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "New subpage" }));

    // Inside the dialog: the page that is open has a Title field of its own,
    // and a modal one hides it rather than replacing it.
    const dialog = within(screen.getByRole("dialog"));

    await userEvent.type(dialog.getByLabelText("Title"), "Fahrplan");
    await userEvent.click(dialog.getByRole("button", { name: "Create page" }));

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "POST");
      expect(one).toBeDefined();
      return one!;
    });

    expect(write.body).toEqual({ title: "Fahrplan", parent: bahn.id, markdown: "" });
  });

  it("says a title already taken is taken, in the instance's own sentence", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen),
      "POST /api/knowledge/pages": refused(
        "conflict",
        409,
        "A page there is already called `Reisen`.",
      ),
    });

    knowledgeAt("/knowledge");

    await userEvent.click(await screen.findByRole("button", { name: "Create page" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "New page" }));
    await userEvent.type(screen.getByLabelText("Title"), "Reisen");
    await userEvent.click(screen.getByRole("button", { name: "Create page" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("already called");
  });

  it("shows what a page used to say and says what putting one back does", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`GET /api/knowledge/pages/${bahn.id}/revisions`]: { body: { items: [aRevision] } },
      [`GET /api/knowledge/pages/${bahn.id}/revisions/${aRevision.id}`]: {
        body: { ...aRevision, markdown: "was es einmal sagte" },
      },
      [`POST /api/knowledge/pages/${bahn.id}/revisions/${aRevision.id}`]: { body: thePage },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Page actions" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Version history" }));

    const dialog = within(await screen.findByRole("dialog"));

    await userEvent.click(await dialog.findByRole("button", { name: /Die Bahn/ }));

    expect((await dialog.findAllByText("was es einmal sagte")).length).toBeGreaterThan(0);
    expect(dialog.getByRole("region", { name: "Changes from selected version to current page" })).toHaveTextContent("Das Wichtigste zuerst.");

    // The fact that makes it safe, said on the screen where it is done.
    await userEvent.click(dialog.getByLabelText("Restoring versions help"));
    expect(dialog.getByText(/Restoring a version keeps the current content/)).toBeVisible();

    await userEvent.click(dialog.getByRole("button", { name: "Restore version" }));

    const recovered = await waitFor(() => {
      const one = asked.find((request) => request.method === "POST");
      expect(one).toBeDefined();
      return one!;
    });

    // Guarded by the page's version and not the revision's.
    expect(recovered.headers.get("If-Match")).toBe(`"${thePage.updated_at}"`);
    expect(await screen.findByText(/previous content remains/)).toBeInTheDocument();
  });

  it("says a page that has never been changed has nothing behind it", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`GET /api/knowledge/pages/${bahn.id}/revisions`]: { body: { items: [] } },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Page actions" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Version history" }));

    expect(await screen.findByText(/No previous versions/)).toBeInTheDocument();
  });

  it("deletes a page and goes back to the tree", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`DELETE /api/knowledge/pages/${bahn.id}`]: { status: 204 },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Page actions" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Delete Bahn" }));

    await waitFor(() => expect(asked.some((one) => one.method === "DELETE")).toBe(true));

    expect(await screen.findByRole("heading", { name: "Your knowledge" })).toBeInTheDocument();
  });

  it("offers the export as a link the browser fetches with the session it has", async () => {
    anInstance({ "GET /api/knowledge/pages": aTree(reisen) });

    knowledgeAt("/knowledge");

    expect(await screen.findByRole("link", { name: "Export all pages" })).toHaveAttribute(
      "href",
      "/api/knowledge/export",
    );
  });

  it("draws the denied state rather than a failure when access was revoked", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: refused(
        "forbidden",
        403,
        "This access does not reach Knowledge.",
      ),
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    expect(await screen.findByText("Knowledge is not this credential's to see.")).toBeInTheDocument();
  });

  it("draws the disabled state rather than a failure when the switch is thrown", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: refused(
        "disabled",
        409,
        "Knowledge is switched off in this workspace.",
      ),
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    expect(await screen.findByText("Knowledge is switched off.")).toBeInTheDocument();
  });

  it("says a page that is gone is gone, with the way to try again", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen),
      [`GET /api/knowledge/pages/${bahn.id}`]: refused(
        "deleted",
        404,
        "The page `Bahn` was deleted by the owner and is in the Trash.",
      ),
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Trash");
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
  it("restores a draft after remount and guards its original version", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: { body: thePage },
    });
    const view = knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.type(await screen.findByRole("textbox", { name: "The page" }), "Saved draft");
    expect(readDraft(bahn.id)?.markdown).toContain("Saved draft");
    view.unmount();
    knowledgeAt(`/knowledge/${bahn.id}`);
    expect(await screen.findByRole("textbox", { name: "The page" })).toHaveValue(thePage.markdown + "Saved draft");
    await waitFor(() => expect(asked.some(r => r.method === "PUT")).toBe(true), { timeout: 2000 });
    await screen.findByText("Saved.");
    expect(readDraft(bahn.id)).toBeUndefined();
    expect(asked.find(r => r.method === "PUT")?.headers.get("If-Match")).toBe(`"${thePage.updated_at}"`);
  });

  it("leaves protected drafts without confirmation and saves after navigation", async () => {
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: { body: { ...thePage, markdown: "changed" } },
      [`GET /api/knowledge/pages/${reisen.id}`]: { body: { ...reisen, markdown: "" } } });
    knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.type(await screen.findByRole("textbox", { name: "The page" }), "draft");
    const confirm = vi.spyOn(window, "confirm");
    await userEvent.click(within(screen.getByRole("navigation", { name: "The pages" })).getByRole("link", { name: "Reisen" }));
    await screen.findByDisplayValue("Reisen");
    await waitFor(() => expect(readDraft(bahn.id)).toBeUndefined());
    expect(asked.find(r => r.method === "PUT")?.body).toMatchObject({ markdown: thePage.markdown + "draft" });
    expect(confirm).not.toHaveBeenCalled();
    confirm.mockRestore();
  });

  it("collapses branches and moves a page with a guarded current body", async () => {
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: { body: { ...thePage, parent: null } } });
    knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Collapse Reisen" }));
    expect(within(screen.getByRole("navigation", { name: "The pages" })).queryByRole("link", { name: "Bahn" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Expand Reisen" }));
    await userEvent.click(await screen.findByRole("button", { name: "Page actions" }));
    await userEvent.click(await screen.findByRole("menuitem", { name: "Move page" }));
    expect(screen.queryByRole("option", { name: "Reisen / Bahn" })).toBeNull();
    await userEvent.selectOptions(screen.getByLabelText("Destination"), "");
    await userEvent.click(screen.getByRole("button", { name: /^Move$/ }));
    await screen.findByText("Page moved.");
    expect(asked.find(r => r.method === "PUT")?.body).toEqual({ title: "Bahn", markdown: thePage.markdown, parent: null });
  });

  it("searches content with snippets and sends the selected subtree", async () => {
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      "GET /api/search": { body: { query: "station", has_more: false, items: [{ application: "knowledge", id: bahn.id, title: "Bahn", snippet: "The station entrance", rank: 1, updated_at: bahn.updated_at }] } } });
    knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.type(screen.getByRole("searchbox", { name: "Search Knowledge" }), "station");
    expect(await screen.findByRole("list", { name: "Knowledge search results" })).toHaveTextContent("The station entrance");
    await userEvent.click(screen.getByRole("checkbox", { name: "Only this page and its subpages" }));
    await waitFor(() => expect(asked.some(r => new URLSearchParams(r.query).get("within") === bahn.id)).toBe(true));
    expect(new URLSearchParams(asked.filter(r => r.path === "/api/search").at(-1)!.query).get("application")).toBe("knowledge");
    const navigation = within(screen.getByRole("navigation", { name: "The pages" }));
    expect(navigation.queryByRole("link", { name: "Reisen" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Back to page tree" }));
    expect(navigation.getByRole("link", { name: "Reisen" })).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Knowledge search results" })).toBeNull();
  });

  it("keeps pin metadata after autosaving, even when pinning leaves the document version unchanged", async () => {
    const saved = { ...thePage, markdown: "Updated", pinned: false, updated_at: "2026-09-14T10:00:00.000000Z" };
    anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`PUT /api/knowledge/pages/${bahn.id}`]: { body: saved },
      [`PUT /api/knowledge/pages/${bahn.id}/pin`]: { body: { ...saved, pinned: true } } });
    const view = knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.type(await screen.findByRole("textbox", { name: "The page" }), "Updated");
    await screen.findByText("Saved.", {}, { timeout: 2000 });
    await userEvent.click(screen.getByRole("button", { name: "Pin page" }));
    await screen.findByRole("button", { name: "Unpin page" });
    view.unmount();
    knowledgeAt(`/knowledge/${bahn.id}`);
    await screen.findByRole("button", { name: "Unpin page" });
  });

  it("shows persistent pins and recently edited pages on the start page", async () => {
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree({ ...reisen, pinned: true }, bahn),
      [`PUT /api/knowledge/pages/${reisen.id}/pin`]: { body: { ...reisen, markdown: "", pinned: false } } });
    knowledgeAt("/knowledge");
    const pinned = within(await screen.findByRole("region", { name: "Pinned pages" }));
    await pinned.findByRole("link", { name: "Reisen" });
    const recent = within(screen.getByRole("region", { name: "Recently edited pages" }));
    expect(recent.getAllByRole("link")[0]).toHaveTextContent("Bahn");
    await userEvent.click(pinned.getByRole("button", { name: "Unpin Reisen" }));
    await waitFor(() => expect(asked.some(r => r.method === "PUT" && (r.body as { pinned: boolean }).pinned === false)).toBe(true));
  });

  it("does not write a page merely because it was opened", async () => {
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage } });
    knowledgeAt(`/knowledge/${bahn.id}`);
    await screen.findByRole("textbox", { name: "The page" });
    await new Promise(resolve => setTimeout(resolve, 1100));
    expect(asked.some(request => request.method === "PUT")).toBe(false);
  });

  it("pauses autosaving a conflict until the user reviews it, then uses the new version", async () => {
    const newer = { ...thePage, markdown: "From elsewhere", updated_at: "2026-09-14T09:00:00.000000Z" };
    const { asked } = anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: [{ body: thePage }, { body: newer }],
      [`PUT /api/knowledge/pages/${bahn.id}`]: [refused("stale", 412), { body: { ...newer, markdown: "Mine" } }] });
    knowledgeAt(`/knowledge/${bahn.id}`);
    const field = await screen.findByRole("textbox", { name: "The page" });
    await userEvent.clear(field);
    await userEvent.type(field, "Mine");
    await screen.findByRole("region", { name: "Resolve changes" }, { timeout: 2000 });
    await userEvent.type(field, " updated");
    await new Promise(resolve => setTimeout(resolve, 1100));
    expect(asked.filter(request => request.method === "PUT")).toHaveLength(1);
    await userEvent.click(screen.getByRole("button", { name: "I reviewed the changes — keep my draft" }));
    await screen.findByText("Saved.", {}, { timeout: 2000 });
    const writes = asked.filter(request => request.method === "PUT");
    expect(writes).toHaveLength(2);
    expect(writes[1].headers.get("If-Match")).toBe(`"${newer.updated_at}"`);
    expect(writes[1].body).toMatchObject({ markdown: "Mine updated" });
  });

});
