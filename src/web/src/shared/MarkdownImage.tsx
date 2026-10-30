import { useState } from "react";

/** Explain attachments that have been deleted or are no longer readable. */
export function MarkdownImage({ src, alt }: { src?: string; alt?: string }) {
  const [failed, setFailed] = useState(false);
  if (failed || !src) return <span role="status" className="rounded border px-2 py-1 text-sm text-muted-foreground">Image unavailable: {alt || "untitled image"}. It may have been deleted or access may be restricted.</span>;
  return <img src={src} alt={alt ?? ""} loading="lazy" className="my-3 max-h-[70vh] max-w-full rounded object-contain" onError={() => setFailed(true)} />;
}
