import { ChevronRightIcon } from "lucide-react";
import { useEffect, useRef, useState, type RefObject } from "react";

type Heading = { text: string; level: number };

/** Observe the rendered document, so code blocks never become outline entries. */
export function DocumentOutline({ documentRoot, toolbar, ready }: {
  documentRoot: RefObject<HTMLDivElement | null>;
  toolbar: RefObject<HTMLDivElement | null>;
  ready: boolean;
}) {
  const [headings, setHeadings] = useState<Heading[]>([]);
  const elements = useRef<HTMLElement[]>([]);
  const details = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    if (!ready) return;
    const root = documentRoot.current;
    const formatting = toolbar.current;
    if (!root || !formatting) return;
    let initialHashPending = true;
    let previous = "";
    let frame = 0;
    const scrollToHash = () => {
      let hash: string;
      try { hash = decodeURIComponent(window.location.hash.slice(1)); } catch { return false; }
      if (!hash) return false;
      const legacy = /^knowledge-heading-(\d+)$/.exec(hash);
      const target = legacy ? elements.current[Number(legacy[1])] : elements.current.find(element => element.id === hash);
      target?.scrollIntoView({ block: "start" });
      return !!target;
    };
    const measureToolbar = () => root.style.setProperty("--knowledge-toolbar-offset", `${formatting.getBoundingClientRect().height + 16}px`);
    const sync = () => {
      elements.current = Array.from(root.querySelectorAll<HTMLElement>(".ProseMirror :is(h1,h2,h3,h4,h5,h6)"));
      const next = elements.current.map(element => ({ text: element.textContent || "Untitled section", level: Number(element.tagName[1]) }));
      const signature = JSON.stringify(next);
      if (signature !== previous) { previous = signature; setHeadings(next); }
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => { if (initialHashPending && scrollToHash()) initialHashPending = false; });
    };
    measureToolbar();
    sync();
    const mutations = new MutationObserver(sync);
    mutations.observe(root, { childList: true, subtree: true, characterData: true });
    const sizes = new ResizeObserver(measureToolbar);
    sizes.observe(formatting);
    const onHashChange = () => { initialHashPending = !scrollToHash(); };
    window.addEventListener("hashchange", onHashChange);
    return () => { cancelAnimationFrame(frame); mutations.disconnect(); sizes.disconnect(); window.removeEventListener("hashchange", onHashChange); };
  }, [ready, documentRoot, toolbar]);

  if (!headings.length) return null;
  const baseLevel = Math.min(...headings.map(heading => heading.level));
  return <details ref={details} className="group mb-2 rounded-lg border px-3">
    <summary className="flex min-h-11 cursor-pointer items-center gap-2 text-sm font-medium"><ChevronRightIcon aria-hidden className="size-4 shrink-0 transition-transform group-open:rotate-90" />On this page <span className="text-muted-foreground">({headings.length})</span></summary>
    <nav aria-label="On this page" className="max-h-64 overflow-y-auto pb-2">
      {headings.map((heading, index) => <a key={index} href={`#knowledge-heading-${index}`}
        className="flex min-h-11 items-center rounded px-2 py-2 text-sm text-brand wrap-anywhere hover:bg-muted md:min-h-8"
        style={{ paddingInlineStart: `${0.5 + (heading.level - baseLevel) * 0.75}rem` }}
        onClick={event => {
          if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
          event.preventDefault();
          if (details.current) details.current.open = false;
          window.history.replaceState(window.history.state, "", `#knowledge-heading-${index}`);
          requestAnimationFrame(() => elements.current[index]?.scrollIntoView({ block: "start" }));
        }}>{heading.text}</a>)}
    </nav>
  </details>;
}
