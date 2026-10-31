import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter, Outlet, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { KnowledgeDraftProvider, KnowledgeSaveStatus } from "./KnowledgeDraftProvider";
import { KnowledgeDrafts } from "./drafts";
import { anInstance, refused } from "@/shared/anInstance";

const base = { id: "page", title: "Pending page", markdown: "original", parent: null, pinned: false,
  created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" };
let manager: KnowledgeDrafts;
beforeEach(() => { localStorage.clear(); sessionStorage.clear(); manager = new KnowledgeDrafts("test"); });
afterEach(() => { manager.deactivate(); vi.restoreAllMocks(); });
function workspace() {
  const router = createMemoryRouter([{ element: <KnowledgeDraftProvider account="test" manager={manager}><KnowledgeSaveStatus /><Outlet /></KnowledgeDraftProvider>, children: [
    { path: "/one", element: <h1>First area</h1> }, { path: "/two", element: <h1>Second area</h1> },
  ] }], { initialEntries: ["/one", "/two"] });
  render(<RouterProvider router={router} />);
  return router;
}

describe("Workspace draft protection", () => {
  it("blocks browser back when storage fails, and requires an explicit discard", async () => {
    anInstance({ "PUT /api/knowledge/pages/page": refused("unavailable", 503) });
    const router = workspace();
    vi.spyOn(localStorage, "setItem").mockImplementation(() => { throw new Error("quota"); });
    act(() => manager.edit(base.id, { title: base.title, markdown: "unsaved", base }));
    await act(() => router.navigate(-1));
    expect(await screen.findByRole("dialog", { name: "Changes are not protected" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/two");
    await userEvent.click(screen.getByRole("button", { name: "Stay here" }));
    expect(manager.snapshot().page.draft?.markdown).toBe("unsaved");
    await act(() => router.navigate("/one"));
    await userEvent.click(screen.getByRole("button", { name: "Discard changes and leave" }));
    await screen.findByRole("heading", { name: "First area" });
    expect(manager.snapshot().page.draft).toBeUndefined();
  });

  it("continues navigation automatically after an unprotected draft saves successfully", async () => {
    anInstance({ "PUT /api/knowledge/pages/page": { body: { ...base, markdown: "unsaved" } } });
    const router = workspace();
    vi.spyOn(localStorage, "setItem").mockImplementation(() => { throw new Error("quota"); });
    act(() => manager.edit(base.id, { title: base.title, markdown: "unsaved", base }));
    await act(() => router.navigate("/one"));
    await userEvent.click(await screen.findByRole("button", { name: "Retry saving" }));
    await screen.findByRole("heading", { name: "First area" });
    expect(manager.snapshot().page.draft).toBeUndefined();
  });

  it("allows protected drafts to travel across areas and exposes offline failures globally", async () => {
    const { asked } = anInstance({ "PUT /api/knowledge/pages/page": refused("unavailable", 503) });
    const router = workspace();
    act(() => manager.edit(base.id, { title: base.title, markdown: "unsaved", base }));
    await act(() => router.navigate(-1));
    await screen.findByRole("heading", { name: "First area" });
    await screen.findByText("Saved only on this device — server save failed");
    expect(screen.getByRole("link", { name: "Pending page" })).toHaveAttribute("href", "/knowledge/page");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(asked.filter(r => r.method === "PUT")).toHaveLength(1);
    anInstance({ "PUT /api/knowledge/pages/page": { body: base } });
    act(() => window.dispatchEvent(new Event("online")));
    await waitFor(() => expect(manager.snapshot().page.draft).toBeUndefined());
  });
});
