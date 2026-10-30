import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Route, Routes } from "react-router";

import { readDraft } from "./drafts";
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
    <Routes>
      <Route path="/knowledge/*" element={<Knowledge />} />
    </Routes>,
  );
}

describe("Knowledge", () => {
  beforeEach(() => sessionStorage.clear());
  it("says nothing is written rather than drawing a blank page", async () => {
    anInstance({ "GET /api/knowledge/pages": aTree() });

    knowledgeAt("/knowledge");

    expect(await screen.findByText(/Nothing is written yet/)).toBeInTheDocument();
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

  it("opens the page a pasted address names, with its Markdown in the field", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    expect(await screen.findByRole("navigation", { name: "On this page" })).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: "The page" })).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: "Edit page" }));
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

    await userEvent.click(await screen.findByRole("button", { name: "Edit page" }));
    const field = await screen.findByRole("textbox", { name: "The page" });

    await userEvent.clear(field);
    await userEvent.type(field, "etwas anderes");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

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

    await userEvent.click(await screen.findByRole("button", { name: "Edit page" }));
    const field = await screen.findByRole("textbox", { name: "The page" });

    await userEvent.type(field, "noch ein Satz");

    const before = asked.filter((one) => one.path === `/api/knowledge/pages/${bahn.id}`).length;

    // What is in the field is the newest version of it, and an answer landing
    // on top is what would take it away.
    expect(screen.getByText(/Draft saved in this tab/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(
        asked.filter((one) => one.path === `/api/knowledge/pages/${bahn.id}` && one.method === "GET")
          .length,
      ).toBeGreaterThan(before - 1),
    );

    expect(screen.getByText("Saved.")).toBeInTheDocument();
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

    await userEvent.click(await screen.findByRole("button", { name: "Edit page" }));
    const field = await screen.findByRole("textbox", { name: "The page" });

    await userEvent.clear(field);
    await userEvent.type(field, "meine Fassung");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));

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

    await userEvent.click(await screen.findByRole("button", { name: "New page under this one" }));

    // Inside the dialog: the page that is open has a Title field of its own,
    // and a modal one hides it rather than replacing it.
    const dialog = within(screen.getByRole("dialog"));

    await userEvent.type(dialog.getByLabelText("Title"), "Fahrplan");
    await userEvent.click(dialog.getByRole("button", { name: "Write it" }));

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

    await userEvent.click(await screen.findByRole("button", { name: "New page" }));
    await userEvent.type(screen.getByLabelText("Title"), "Reisen");
    await userEvent.click(screen.getByRole("button", { name: "Write it" }));

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

    await userEvent.click(await screen.findByRole("button", { name: "History" }));

    const dialog = within(await screen.findByRole("dialog"));

    await userEvent.click(await dialog.findByRole("button", { name: /Die Bahn/ }));

    expect(await dialog.findByText("was es einmal sagte")).toBeInTheDocument();

    // The fact that makes it safe, said on the screen where it is done.
    expect(
      dialog.getByText(/Putting a version back keeps what the page says now/),
    ).toBeInTheDocument();

    await userEvent.click(dialog.getByRole("button", { name: "Put this one back" }));

    const recovered = await waitFor(() => {
      const one = asked.find((request) => request.method === "POST");
      expect(one).toBeDefined();
      return one!;
    });

    // Guarded by the page's version and not the revision's.
    expect(recovered.headers.get("If-Match")).toBe(`"${thePage.updated_at}"`);
    expect(await screen.findByText(/version of its own/)).toBeInTheDocument();
  });

  it("says a page that has never been changed has nothing behind it", async () => {
    anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`GET /api/knowledge/pages/${bahn.id}/revisions`]: { body: { items: [] } },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "History" }));

    expect(await screen.findByText(/never been changed/)).toBeInTheDocument();
  });

  it("deletes a page and goes back to the tree", async () => {
    const { asked } = anInstance({
      "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`DELETE /api/knowledge/pages/${bahn.id}`]: { status: 204 },
    });

    knowledgeAt(`/knowledge/${bahn.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete Bahn" }));

    await waitFor(() => expect(asked.some((one) => one.method === "DELETE")).toBe(true));

    expect(await screen.findByRole("heading", { name: "Your knowledge" })).toBeInTheDocument();
  });

  it("offers the export as a link the browser fetches with the session it has", async () => {
    anInstance({ "GET /api/knowledge/pages": aTree(reisen) });

    knowledgeAt("/knowledge");

    expect(await screen.findByRole("link", { name: "Export all of it" })).toHaveAttribute(
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
    await userEvent.click(await screen.findByRole("button", { name: "Edit page" }));
    await userEvent.type(await screen.findByRole("textbox", { name: "The page" }), "Saved draft");
    expect(readDraft(bahn.id)?.markdown).toContain("Saved draft");
    view.unmount();
    knowledgeAt(`/knowledge/${bahn.id}`);
    expect(await screen.findByRole("textbox", { name: "The page" })).toHaveValue(thePage.markdown + "Saved draft");
    await userEvent.click(screen.getByRole("button", { name: "Save" }));
    await screen.findByText("Saved.");
    expect(readDraft(bahn.id)).toBeUndefined();
    expect(asked.find(r => r.method === "PUT")?.headers.get("If-Match")).toBe(`"${thePage.updated_at}"`);
  });

  it("confirms leaving unsaved work and keeps the draft when leaving", async () => {
    anInstance({ "GET /api/knowledge/pages": aTree(reisen, bahn),
      [`GET /api/knowledge/pages/${bahn.id}`]: { body: thePage },
      [`GET /api/knowledge/pages/${reisen.id}`]: { body: { ...reisen, markdown: "" } } });
    knowledgeAt(`/knowledge/${bahn.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Edit page" }));
    await userEvent.type(await screen.findByRole("textbox", { name: "The page" }), "draft");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const tree = within(screen.getByRole("navigation", { name: "The pages" }));
    await userEvent.click(tree.getByRole("link", { name: "Reisen" }));
    expect(screen.getByRole("textbox", { name: "The page" })).toBeInTheDocument();
    confirm.mockReturnValue(true);
    await userEvent.click(tree.getByRole("link", { name: "Reisen" }));
    await screen.findByRole("heading", { name: "Reisen" });
    expect(readDraft(bahn.id)?.markdown).toContain("draft");
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
    await userEvent.click(await screen.findByRole("button", { name: "Move page" }));
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

});
