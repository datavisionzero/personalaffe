import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { anInstance, renderAt } from "@/shared/anInstance";
import { Bookmarks } from "./Bookmarks";
import { BookmarkPrivacyProvider } from "./privacy";

afterEach(() => vi.unstubAllGlobals());
it("does not start a private download from a response arriving after private mode is disabled", async () => {
  const base = anInstance({ "GET /api/bookmarks/folders": { body: { items: [], next_offset: null } }, "GET /api/bookmarks": { body: { items: [], next_offset: null } } }).fetch;
  let release: ((response: Response) => void) | undefined;
  vi.stubGlobal("fetch", vi.fn((request: Request) => new URL(request.url).pathname === "/api/bookmarks/export"
    ? new Promise<Response>((resolve) => { release = resolve; }) : base(request)));
  const create = vi.fn(() => "blob:private");
  const original = URL.createObjectURL;
  URL.createObjectURL = create;
  try {
    renderAt("/bookmarks/manage", <BookmarkPrivacyProvider><Bookmarks /></BookmarkPrivacyProvider>);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Private mode off" }));
    await user.click(screen.getByRole("button", { name: "Export HTML" }));
    const dialog = within(screen.getByRole("dialog"));
    await user.click(dialog.getByRole("checkbox", { name: /Include private/ }));
    await user.click(dialog.getByRole("button", { name: "Download HTML" }));
    await waitFor(() => expect(release).toBeDefined());
    // Exercise the provider transition even while the dialog has focus.
    fireEvent.click(screen.getByText("Private mode on"));
    await act(async () => release!(Response.json({ html: "Secret", bookmarks: 1, folders: 0, warning: "Unencrypted" })));
    expect(create).not.toHaveBeenCalled();
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  } finally { URL.createObjectURL = original; }
});
