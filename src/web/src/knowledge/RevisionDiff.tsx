import { useMemo } from "react";
import { lineDiff } from "./lineDiff";

export function RevisionDiff({ before, after }: { before: string; after: string }) {
  const lines = useMemo(() => lineDiff(before, after), [before, after]);
  return <section aria-label="Changes from selected version to current page" className="space-y-2">
    <p className="text-xs">− Removed from selected version · + Added in current version</p>
    {before === after && <p>No changes.</p>}
    <pre className="max-h-80 overflow-auto rounded border text-xs whitespace-pre-wrap break-words">{lines.map((line, index) => <span key={index} className={`block px-2 ${line.kind === "added" ? "bg-green-100 text-green-950 dark:bg-green-950 dark:text-green-100" : line.kind === "removed" ? "bg-red-100 text-red-950 dark:bg-red-950 dark:text-red-100" : ""}`}>
      <span aria-label={line.kind}>{line.kind === "added" ? "+ " : line.kind === "removed" ? "− " : "  "}</span>{line.text}{"\n"}
    </span>)}</pre>
  </section>;
}
