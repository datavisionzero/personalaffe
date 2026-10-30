import { useEffect, useRef, useState } from "react";
import { Markdown } from "@/shared/Markdown";

/** Derive the outline from rendered Markdown, so fenced code never becomes a heading. */
export function ReadingPage({ markdown }: { markdown: string }) {
  const body = useRef<HTMLDivElement>(null);
  const [headings, setHeadings] = useState<{ id: string; text: string; level: number }[]>([]);
  useEffect(() => {
    const items = Array.from(body.current?.querySelectorAll("h1,h2,h3,h4,h5,h6") ?? []);
    setHeadings(items.map((heading, index) => {
      const id = `knowledge-heading-${index}`;
      heading.id = id;
      return { id, text: heading.textContent ?? "", level: Number(heading.tagName[1]) };
    }));
  }, [markdown]);
  return <>
    {headings.length > 0 && <nav aria-label="On this page" className="rounded-lg border p-3">
      <p className="mb-2 font-medium">On this page</p>
      {headings.map(heading => <a key={heading.id} href={`#${heading.id}`} className="block text-sm text-brand hover:underline" style={{ paddingLeft: `${Math.max(0, heading.level - 2)}rem` }}>{heading.text}</a>)}
    </nav>}
    <div ref={body} className="max-w-prose py-4"><Markdown>{markdown || "_This page is empty._"}</Markdown></div>
  </>;
}
