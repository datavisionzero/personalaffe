import type { Schemas } from "@/api/client";
export type Draft = { title: string; markdown: string; base: Schemas["PageResponse"] };
const prefix = "personalaffe-knowledge-draft:";
export function readDraft(id: string): Draft | undefined {
  try {
    const value = JSON.parse(sessionStorage.getItem(prefix + id) ?? "null");
    if (value && typeof value.title === "string" && typeof value.markdown === "string" && value.base?.id === id && typeof value.base.updated_at === "string") return value;
  } catch { /* Storage can be unavailable. The editor remains usable. */ }
}
export function storeDraft(id: string, draft: Draft | undefined): boolean {
  try {
    if (draft) sessionStorage.setItem(prefix + id, JSON.stringify(draft));
    else sessionStorage.removeItem(prefix + id);
    return true;
  } catch { return false; }
}
export function clearKnowledgeDrafts() {
  try {
    for (const key of Object.keys(sessionStorage)) if (key.startsWith(prefix)) sessionStorage.removeItem(key);
  } catch { /* Nothing persisted if storage is unavailable. */ }
}
