import { api, codeOf, describe, guardedBy, versionOf, type Problem, type Schemas } from "@/api/client";

export type Draft = { title: string; markdown: string; base: Schemas["PageResponse"] };
export type PendingPage = {
  draft?: Draft;
  saved?: Schemas["PageResponse"];
  secured: boolean;
  saving: boolean;
  error?: string;
  conflict?: Schemas["PageResponse"];
  storageKey?: string;
};
const prefix = "personalaffe-knowledge-draft:v2:";

/** Lives with the authenticated workspace, not with an editor or route. */
export class KnowledgeDrafts {
  private pages: Record<string, PendingPage> = {};
  private listeners = new Set<() => void>();
  private timers = new Map<string, ReturnType<typeof setTimeout>>();
  private requests = new Map<string, Promise<void>>();
  private recovered = new Map<string, PendingPage[]>();
  private running = false;
  private generation = 0;
  private readonly prefix: string;

  constructor(account: string) {
    this.prefix = prefix + encodeURIComponent(account) + ":";
    try {
      for (let index = 0; index < localStorage.length; index++) {
        const key = localStorage.key(index);
        if (!key) continue;
        if (!key.startsWith(this.prefix)) continue;
        const draft = JSON.parse(localStorage.getItem(key) ?? "null") as Draft | null;
        if (draft && typeof draft.title === "string" && typeof draft.markdown === "string" && typeof draft.base?.id === "string" && typeof draft.base.updated_at === "string") {
          const id = draft.base.id;
          const page = { draft, secured: true, saving: false, storageKey: key };
          if (!this.pages[id]) this.pages[id] = page;
          else this.recovered.set(id, [...(this.recovered.get(id) ?? []), page]);
        }
      }
    } catch { /* Edits still work when storage is unavailable. */ }
    // Upgrade the previous tab-only format while still in the same browser
    // session. Old versions cleared these keys on logout, so they belong to
    // the authenticated account opening this workspace.
    if (account) try {
      const legacy = "personalaffe-knowledge-draft:";
      for (const key of Object.keys(sessionStorage)) {
        if (!key.startsWith(legacy)) continue;
        const id = key.slice(legacy.length);
        const draft = JSON.parse(sessionStorage.getItem(key) ?? "null") as Draft | null;
        if (!draft || draft.base?.id !== id || typeof draft.markdown !== "string" || typeof draft.title !== "string" || typeof draft.base.updated_at !== "string") continue;
        const storageKey = this.prefix + id + ":recovery:" + crypto.randomUUID();
        const page: PendingPage = { draft, storageKey, secured: false, saving: false };
        try { localStorage.setItem(storageKey, JSON.stringify(draft)); page.secured = true; sessionStorage.removeItem(key); } catch { /* Keep the original until durably secured. */ }
        if (!this.pages[id]) this.pages[id] = page;
        else this.recovered.set(id, [...(this.recovered.get(id) ?? []), page]);
      }
    } catch { /* The legacy tab store may also be unavailable. */ }
  }

  subscribe = (listener: () => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  snapshot = () => this.pages;
  private publish(id: string, page: PendingPage) {
    this.pages = { ...this.pages, [id]: page };
    this.listeners.forEach(listener => listener());
  }
  private persist(id: string, draft: Draft | undefined, previous?: Draft): boolean {
    try {
      const key = this.pages[id]?.storageKey ?? this.prefix + id;
      const existing = localStorage.getItem(key);
      // Two tabs may edit the same page. Preserve the other tab's unsent
      // contents before replacing its durable copy; recovery presents each
      // version using its original server version, so conflicts stay explicit.
      if (draft && existing && existing !== JSON.stringify(previous) && existing !== JSON.stringify(draft)) {
        localStorage.setItem(this.prefix + id + ":recovery:" + crypto.randomUUID(), existing);
      }
      // A late acknowledgement must never remove another tab's newer draft.
      if (draft) localStorage.setItem(key, JSON.stringify(draft));
      else if (!previous || localStorage.getItem(key) === JSON.stringify(previous)) localStorage.removeItem(key);
      return true;
    } catch { return false; }
  }
  edit(id: string, draft: Draft | undefined) {
    const previous = this.pages[id];
    const secured = this.persist(id, draft, previous?.draft);
    this.publish(id, { ...previous, draft, secured, saving: previous?.saving ?? false,
      conflict: draft ? previous?.conflict : undefined,
      error: draft && previous?.conflict ? previous.error : undefined });
    this.schedule(id);
  }
  private schedule(id: string, delay = 900) {
    clearTimeout(this.timers.get(id));
    this.timers.delete(id);
    const page = this.pages[id];
    if (!this.running || !page?.draft?.title.trim() || page.saving || page.conflict || page.error) return;
    this.timers.set(id, setTimeout(() => { this.timers.delete(id); void this.save(id); }, delay));
  }
  start() {
    this.running = true;
    for (const [id, page] of Object.entries(this.pages)) {
      if (page.error && !page.conflict) this.publish(id, { ...page, error: undefined });
      this.schedule(id);
    }
  }
  stop() {
    this.running = false;
    this.timers.forEach(clearTimeout);
    this.timers.clear();
  }
  /** Drop only memory at logout; durable drafts belong to this account on return. */
  deactivate() {
    this.stop(); this.generation++;
    this.requests.clear();
    for (const [id, page] of Object.entries(this.pages)) if (page.saving) this.publish(id, { ...page, saving: false });
  }
  async flush() {
    await Promise.all(Object.keys(this.pages).map(id => this.save(id)));
  }
  save(id: string): Promise<void> {
    const request = this.requests.get(id);
    if (request) return request;
    clearTimeout(this.timers.get(id));
    this.timers.delete(id);
    const page = this.pages[id];
    if (!this.running || !page?.draft?.title.trim() || page.conflict) return Promise.resolve();
    const pending = page.draft;
    const generation = this.generation;
    const secured = this.persist(id, pending, pending);
    this.publish(id, { ...page, secured, saving: true, error: undefined });
    const operation = (async () => {
      try {
        const answer = await api.PUT("/api/knowledge/pages/{id}", {
          params: { path: { id }, ...guardedBy(versionOf(pending.base.updated_at)) },
          body: { title: pending.title, parent: pending.base.parent, markdown: pending.markdown },
        });
        if (generation !== this.generation) return;
        if (!answer.data) {
          const stale = codeOf(answer.error as Problem) === "stale";
          // Keep the error even if fetching the comparison fails. A retry must
          // still use the original version, never silently rebase a conflict.
          this.publish(id, { ...this.pages[id], saving: false, error: stale
            ? "This page changed somewhere else. Your draft is preserved. Compare the versions before saving again."
            : describe(answer.error as Problem, answer.response.status) });
          if (stale) {
            const latest = await api.GET("/api/knowledge/pages/{id}", { params: { path: { id } } });
            if (generation === this.generation && latest.data) this.publish(id, { ...this.pages[id], conflict: latest.data });
          }
          return;
        }
        const current = this.pages[id];
        const draft = current.draft && current.draft !== pending ? { ...current.draft, base: answer.data } : undefined;
        const secured = this.persist(id, draft, current.draft);
        this.publish(id, { ...current, draft, saved: answer.data, secured, saving: false, error: undefined, conflict: undefined });
        if (!draft) this.recoverNext(id);
      } catch {
        if (generation === this.generation) this.publish(id, { ...this.pages[id], saving: false,
          error: "Could not save. Your draft is still here. Try again when connected." });
      } finally {
        if (generation === this.generation) { this.requests.delete(id); this.schedule(id); }
      }
    })();
    this.requests.set(id, operation);
    return operation;
  }
  resolve(id: string, keepDraft: boolean) {
    const page = this.pages[id];
    if (!page?.conflict || !page.draft || page.saving) return;
    const draft = keepDraft ? { ...page.draft, base: page.conflict } : undefined;
    const secured = this.persist(id, draft, page.draft);
    this.publish(id, { ...page, draft, saved: page.conflict, secured, saving: false, error: undefined, conflict: undefined });
    if (!draft) this.recoverNext(id);
    this.schedule(id);
  }
  private recoverNext(id: string) {
    const next = this.recovered.get(id)?.shift();
    if (next) this.publish(id, { ...next, saved: this.pages[id]?.saved });
  }
  discardUnsafe() {
    for (const [id, page] of Object.entries(this.pages)) if (page.draft && !page.secured && !page.saving) this.edit(id, undefined);
  }
}
