import { act, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { anInstance, renderAt } from "@/shared/anInstance";
import { Bookmarks } from "./Bookmarks";
import { BookmarkPrivacyProvider } from "./privacy";

const first = { id: "0199f0c4-0000-7000-8000-000000000001", title: "First link", url: "https://example.com/first", description: "", folder: null, private: false, favorite: true, favorite_position: 1, created_at: "2026-09-20T08:00:00.000000Z", updated_at: "2026-09-20T08:00:00.000000Z" };
const second = { ...first, id: "0199f0c4-0000-7000-8000-000000000002", title: "Second link", favorite_position: 2 };
const dashboard = { favorites: [first, second], frequent: [], recent: [], has_more_favorites: false };
function mount() { return renderAt("/bookmarks", <BookmarkPrivacyProvider><Bookmarks /></BookmarkPrivacyProvider>); }
function answers() { return { "GET /api/bookmarks/dashboard": { body: dashboard }, "GET /api/bookmarks/folders": { body: { items: [], next_offset: null } }, "GET /api/bookmarks": { body: { items: [], next_offset: null } } }; }

afterEach(() => vi.unstubAllGlobals());
describe("bookmarks dashboard", () => {
  it("opens actual links and reorders favorites using guarded keyboard-accessible buttons", async () => {
    const instance = anInstance({ ...answers(), [`PUT /api/bookmarks/${second.id}/favorite`]: { body: second }, [`POST /api/bookmarks/${first.id}/open`]: { body: {} } });
    mount();
    const user = userEvent.setup();
    const link = await screen.findByRole("link", { name: /First link/ });
    expect(link).toHaveAttribute("href", first.url);
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    await user.click(link);
    await waitFor(() => expect(instance.asked.some((request) => request.path.endsWith("/open"))).toBe(true));
    await user.click(screen.getByRole("button", { name: "Move Second link earlier" }));
    await waitFor(() => expect(instance.asked.some((request) => request.method === "PUT")).toBe(true));
    const write = instance.asked.find((request) => request.method === "PUT")!;
    expect(write.body).toEqual({ favorite: true, after: null });
    expect(write.headers.get("If-Match")).toContain(second.updated_at);
  });

  it("suggests an editable domain and preserves the form when saving fails", async () => {
    anInstance({ ...answers(), "POST /api/bookmarks": { status: 409, body: { title: "Conflict", status: 409 } } });
    mount(); const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "Add bookmark" }));
    const dialog = within(screen.getByRole("dialog"));
    await user.type(dialog.getByLabelText("URL"), "https://docs.example.com/guide");
    await user.tab();
    expect(dialog.getByLabelText("Title")).toHaveValue("docs.example.com");
    await user.click(dialog.getByRole("button", { name: "Add bookmark" }));
    await dialog.findByRole("alert");
    expect(dialog.getByLabelText("URL")).toHaveValue("https://docs.example.com/guide");
  });

  it("discards private results, open forms and late responses on mode changes; remount resets mode", async () => {
    const base = anInstance(answers()).fetch;
    let release: ((response: Response) => void) | undefined;
    let privateCalls = 0;
    vi.stubGlobal("fetch", vi.fn(async (request: Request) => {
      if (new URL(request.url).pathname === "/api/bookmarks/dashboard" && request.headers.get("Personalaffe-Private") === "true") {
        privateCalls++;
        if (privateCalls === 1) return Response.json({ ...dashboard, favorites: [{ ...first, title: "Secret link", private: true }] });
        return new Promise<Response>((resolve) => { release = resolve; });
      }
      return base(request);
    }));
    const view = mount(); const user = userEvent.setup();
    await screen.findByRole("link", { name: /First link/ });
    await user.click(screen.getByRole("button", { name: "Private mode off" }));
    await screen.findByRole("link", { name: /Secret link/ });
    await user.click(screen.getByRole("button", { name: "Add bookmark" }));
    await user.type(screen.getByLabelText("URL"), "https://secret.example.com");
    // Toggle through the provider control; the dialog itself is part of the discarded tree.
    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Private mode on" }));
    expect(screen.queryByText("Secret link")).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Private mode off" }));
    await waitFor(() => expect(release).toBeDefined());
    await user.click(screen.getByRole("button", { name: "Private mode on" }));
    await act(async () => release!(Response.json({ ...dashboard, favorites: [{ ...first, title: "Late secret", private: true }] })));
    expect(screen.queryByText("Late secret")).not.toBeInTheDocument();
    view.unmount(); mount();
    expect(screen.getByRole("button", { name: "Private mode off" })).toHaveAttribute("aria-pressed", "false");
  });
});
