import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { anInstance, renderAt, refused } from "@/shared/anInstance";
import { Bookmarks } from "./Bookmarks";
import { BookmarkPrivacyProvider } from "./privacy";
const row = { id: "0199f0c4-0000-7000-8000-000000000001", title: "First link", url: "https://example.com", description: "", folder: null, private: false, favorite: false, favorite_position: null, created_at: "2026-09-20T08:00:00.000000Z", updated_at: "2026-09-20T08:00:00.000000Z" };
const other = { ...row, id: "0199f0c4-0000-7000-8000-000000000002", title: "Second link" };
function mount(path = "/bookmarks/manage") { return renderAt(path, <BookmarkPrivacyProvider><Bookmarks /></BookmarkPrivacyProvider>); }
function answers(items = [row, other]) { return { "GET /api/bookmarks/folders": { body: { items: [], next_offset: null } }, "GET /api/bookmarks": { body: { items, next_offset: null } } }; }
afterEach(() => vi.unstubAllGlobals());
it("reports partial bulk failure and restores the successful deletion using its current Trash version", async () => {
  const deletedVersion = "2026-09-20T09:00:00.000000Z";
  const instance = anInstance({ ...answers(), [`DELETE /api/bookmarks/${row.id}`]: {}, [`DELETE /api/bookmarks/${other.id}`]: refused("stale", 412), "GET /api/trash": { body: { items: [{ id: row.id, application: "bookmarks", updated_at: deletedVersion }], has_more: false } }, [`POST /api/trash/bookmarks/${row.id}/restore`]: {} });
  mount(); const user = userEvent.setup();
  await user.click(await screen.findByLabelText("Select this page"));
  await user.click(screen.getByRole("button", { name: "Delete selected" }));
  await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Confirm" }));
  await screen.findByText(/1 moved to Trash; 1 failed/);
  expect(screen.getByLabelText("Select Second link")).toBeChecked();
  expect(screen.getByLabelText("Select First link")).not.toBeChecked();
  await user.click(screen.getByRole("button", { name: "Undo deletion" }));
  await waitFor(() => expect(instance.asked.find((request) => request.path.endsWith("/restore"))?.headers.get("If-Match")).toContain(deletedVersion));
});
it("requires an explicit visibility confirmation for a private bookmark moved to root", async () => {
  const secret = { ...row, private: true };
  const instance = anInstance({ ...answers([secret]), [`PUT /api/bookmarks/${row.id}`]: { body: row } });
  mount(); const user = userEvent.setup();
  await user.click(await screen.findByRole("button", { name: "Edit First link" }));
  const dialog = within(screen.getByRole("dialog"));
  const save = dialog.getByRole("button", { name: "Save changes" });
  expect(save).toBeDisabled();
  await user.click(dialog.getByRole("checkbox", { name: /outside private mode/ }));
  await user.click(save);
  await waitFor(() => expect(instance.asked.some((request) => request.method === "PUT")).toBe(true));
});
it("keeps edits on a stale direct-link version instead of overwriting another writer", async () => {
  const instance = anInstance({ ...answers(), [`GET /api/bookmarks/${row.id}`]: { body: row }, [`PUT /api/bookmarks/${row.id}`]: refused("stale", 412) });
  mount(`/bookmarks/manage?selected=${row.id}`); const user = userEvent.setup();
  const title = await screen.findByLabelText("Title");
  await user.clear(title); await user.type(title, "My unsaved edit");
  await user.click(screen.getByRole("button", { name: "Save changes" }));
  await screen.findByRole("alert");
  expect(title).toHaveValue("My unsaved edit");
  expect(instance.asked.find((request) => request.method === "PUT")?.headers.get("If-Match")).toContain(row.updated_at);
});
