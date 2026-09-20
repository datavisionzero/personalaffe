import type { ReactNode } from "react";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { api } from "@/api/client";

export function BookmarkLink({ id, url, privateContext, className, children }: {
  id: string; url: string; privateContext?: boolean; className?: string; children: ReactNode;
}) {
  const privacy = useBookmarkPrivacy();
  const privateEnabled = privateContext ?? privacy.enabled;
  let safe = false;
  try { safe = ["https:", "http:"].includes(new URL(url).protocol); } catch { /* No navigable address. */ }
  if (!safe) return <span className={className}>{children}</span>;

  function opened() {
    void api.POST("/api/bookmarks/{id}/open", {
      params: { path: { id }, header: { "Personalaffe-Private": privateEnabled ? "true" : undefined } },
      body: { event_id: crypto.randomUUID() }, keepalive: true,
    }).catch(() => { /* A statistic must never prevent following a link. */ });
  }

  return <a href={url} target="_blank" rel="noopener noreferrer" className={className}
    onClick={() => opened()} onAuxClick={(event) => { if (event.button === 1) opened(); }}>
    {children}
  </a>;
}
