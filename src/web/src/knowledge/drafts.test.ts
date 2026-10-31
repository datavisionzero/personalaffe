import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { KnowledgeDrafts, type Draft } from "./drafts";
import { anInstance, refused } from "@/shared/anInstance";

const base = { id: "page", title: "Title", markdown: "original", parent: null, pinned: false,
  created_at: "2026-01-01T00:00:00Z", updated_at: "2026-01-01T00:00:00Z" };
const draft: Draft = { title: base.title, markdown: "new words", base };
const saved = { ...base, markdown: draft.markdown, updated_at: "2026-01-02T00:00:00Z" };
const stores: KnowledgeDrafts[] = [];
function store(account = "owner") { const manager = new KnowledgeDrafts(account); stores.push(manager); manager.start(); return manager; }

beforeEach(() => { localStorage.clear(); vi.useFakeTimers(); });
afterEach(() => { stores.splice(0).forEach(s => s.deactivate()); vi.useRealTimers(); vi.restoreAllMocks(); });

describe("Durable knowledge queue", () => {
  it("recovers after the tab is gone, including the original version, only for the same account", async () => {
    const first = store();
    first.edit(base.id, draft);
    first.deactivate();
    expect(store("other-account").snapshot()).toEqual({});
    const { asked } = anInstance({ "PUT /api/knowledge/pages/page": { body: saved } });
    const restored = store();
    expect(restored.snapshot().page.draft).toEqual(draft);
    await restored.flush();
    expect(asked[0].headers.get("If-Match")).toBe(`"${base.updated_at}"`);
    expect(store().snapshot()).toEqual({});
  });

  it("queues edits made during a request without losing them or sending overlapping writes", async () => {
    const manager = store();
    let acknowledge!: (response: Response) => void;
    const fetch = vi.fn<typeof globalThis.fetch>(() => new Promise(resolve => { acknowledge = resolve; }));
    vi.stubGlobal("fetch", fetch);
    manager.edit(base.id, draft);
    const pending = manager.save(base.id);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    manager.edit(base.id, { ...draft, markdown: "even newer" });
    void manager.save(base.id);
    expect(fetch).toHaveBeenCalledTimes(1);
    acknowledge(new Response(JSON.stringify(saved), { headers: { "Content-Type": "application/json" } }));
    await pending;
    expect(manager.snapshot().page.draft).toMatchObject({ markdown: "even newer", base: saved });
    const { asked } = anInstance({ "PUT /api/knowledge/pages/page": { body: { ...saved, markdown: "even newer" } } });
    await manager.flush();
    expect(asked[0].headers.get("If-Match")).toBe(`"${saved.updated_at}"`);
    expect(manager.snapshot().page.draft).toBeUndefined();
  });

  it("retains failed writes across logout and pauses conflicts until explicitly resolved", async () => {
    const { asked } = anInstance({
      "PUT /api/knowledge/pages/page": [refused("stale", 412), { body: saved }],
      "GET /api/knowledge/pages/page": { body: { ...saved, markdown: "someone else" } },
    });
    const manager = store();
    manager.edit(base.id, draft);
    await manager.flush();
    expect(manager.snapshot().page.conflict?.markdown).toBe("someone else");
    manager.edit(base.id, { ...draft, markdown: "combined" });
    await manager.flush();
    expect(asked.filter(r => r.method === "PUT")).toHaveLength(1);
    manager.resolve(base.id, true);
    await manager.flush();
    expect(asked.filter(r => r.method === "PUT")[1].body).toMatchObject({ markdown: "combined" });
  });

  it("keeps the in-memory draft if durable storage fails and still saves to the server", async () => {
    const manager = store();
    vi.spyOn(localStorage, "setItem").mockImplementation(() => { throw new Error("quota"); });
    manager.edit(base.id, draft);
    expect(manager.snapshot().page.secured).toBe(false);
    expect(manager.snapshot().page.draft).toEqual(draft);
    anInstance({ "PUT /api/knowledge/pages/page": { body: saved } });
    await manager.flush();
    expect(manager.snapshot().page.draft).toBeUndefined();
  });

  it("does not send new requests while locked and resumes the pending queue on unlock", async () => {
    const { asked } = anInstance({ "PUT /api/knowledge/pages/page": { body: saved } });
    const manager = store();
    manager.edit(base.id, draft);
    manager.stop();
    await vi.advanceTimersByTimeAsync(1000);
    await manager.flush();
    expect(asked).toHaveLength(0);
    manager.start();
    await manager.flush();
    expect(asked).toHaveLength(1);
  });

  it("preserves divergent drafts from two tabs as recoverable versions", async () => {
    const first = store();
    const second = store();
    first.edit(base.id, draft);
    second.edit(base.id, { ...draft, markdown: "other tab" });
    first.deactivate(); second.deactivate();
    const recovered = store();
    const { asked } = anInstance({ "PUT /api/knowledge/pages/page": [{ body: saved }, refused("stale", 412)],
      "GET /api/knowledge/pages/page": { body: saved } });
    await recovered.flush();
    await recovered.flush();
    expect(asked.filter(r => r.method === "PUT").map(r => (r.body as Draft).markdown).sort()).toEqual(["new words", "other tab"]);
    expect(recovered.snapshot().page.conflict).toEqual(saved);
  });
});
