import { createContext, useContext, useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { Link, useBlocker, useLocation } from "react-router";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { KnowledgeDrafts } from "./drafts";

const Context = createContext<KnowledgeDrafts | null>(null);

export function KnowledgeDraftProvider({ account, paused = false, children, manager: supplied }: { account: string; paused?: boolean; children: ReactNode; manager?: KnowledgeDrafts }) {
  const [manager] = useState(() => supplied ?? new KnowledgeDrafts(account));
  useEffect(() => {
    if (paused) { manager.stop(); return; }
    manager.start();
    const online = () => void manager.flush();
    window.addEventListener("online", online);
    return () => { manager.stop(); window.removeEventListener("online", online); };
  }, [manager, paused]);
  return <Context.Provider value={manager}>{children}<DraftNavigation /></Context.Provider>;
}

// Hooks are separate exports for the editor and the shell's persistent status.
// eslint-disable-next-line react-refresh/only-export-components
export function useKnowledgeDrafts() {
  const manager = useContext(Context);
  if (!manager) throw new Error("Knowledge drafts require an authenticated workspace");
  const pages = useSyncExternalStore(manager.subscribe, manager.snapshot);
  return { manager, pages };
}

function DraftNavigation() {
  const { manager, pages } = useKnowledgeDrafts();
  const location = useLocation();
  const unsafe = Object.values(pages).some(page => page.draft && !page.secured);
  const busy = Object.values(pages).some(page => page.draft && !page.secured && page.saving);
  const blocker = useBlocker(unsafe);
  useEffect(() => { void manager.flush(); }, [manager, location.key]);
  useEffect(() => {
    if (!unsafe) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [unsafe]);
  useEffect(() => {
    if (blocker.state === "blocked" && !unsafe) blocker.proceed();
  }, [blocker, unsafe]);
  return <Dialog open={blocker.state === "blocked" && unsafe} onOpenChange={open => { if (!open && blocker.state === "blocked") blocker.reset(); }}>
    <DialogContent>
      <DialogHeader><DialogTitle>Changes are not protected</DialogTitle></DialogHeader>
      <p>Some changes could not be saved on this device or the server. Stay here to retry, or explicitly discard those changes.</p>
      <DialogFooter>
        <Button variant="outline" onClick={() => { if (blocker.state === "blocked") blocker.reset(); }}>Stay here</Button>
        <Button onClick={() => void manager.flush()}>Retry saving</Button>
        <Button variant="destructive" disabled={busy} onClick={() => { manager.discardUnsafe(); if (blocker.state === "blocked") blocker.proceed(); }}>Discard changes and leave</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>;
}

export function KnowledgeSaveStatus() {
  const { manager, pages } = useKnowledgeDrafts();
  const location = useLocation();
  const pending = Object.entries(pages).filter(([id, page]) => page.draft && location.pathname.replace(/\/+$/, "") !== `/knowledge/${id}`);
  if (!pending.length) return null;
  return <aside aria-label="Pending knowledge changes" className="border-b px-3 py-2 text-sm">
    <ul className="flex flex-wrap gap-3">{pending.map(([id, page]) => <li key={id}>
      <Link className="underline" to={`/knowledge/${id}`}>{page.draft!.title || "Untitled page"}</Link>{": "}
      <span role="status">{page.conflict ? "Review conflicting changes" : !page.secured ? "Changes are not protected" : page.saving ? "Saving to server…" : page.error ? "Saved only on this device — server save failed" : !page.draft!.title.trim() ? "Add a title to save" : "Saved on this device — waiting to sync"}</span>
      {page.error && !page.conflict && <Button variant="ghost" size="sm" aria-label={`Retry saving ${page.draft!.title || "untitled page"}`} onClick={() => void manager.save(id)}>Retry saving</Button>}
    </li>)}</ul>
  </aside>;
}
