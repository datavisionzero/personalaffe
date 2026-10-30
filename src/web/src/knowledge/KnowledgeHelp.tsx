import { InfoIcon } from "lucide-react";
import type { ReactNode } from "react";

/** Native disclosure works with keyboard, pointer and touch without hover. */
export function KnowledgeHelp({ label, children }: { label: string; children: ReactNode }) {
  return <details className="text-sm text-muted-foreground">
    <summary aria-label={label} className="inline-flex cursor-pointer list-none items-center gap-1 rounded p-1 focus-visible:outline focus-visible:outline-2">
      <InfoIcon aria-hidden className="size-4" /><span className="sr-only">{label}</span>
    </summary>
    <div className="mt-1 rounded border p-3">{children}</div>
  </details>;
}
