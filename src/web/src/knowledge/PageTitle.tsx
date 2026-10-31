import { useLayoutEffect, useRef } from "react";

/** A title remains fully readable at any width, including while editing it. */
export function PageTitle({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const field = useRef<HTMLTextAreaElement>(null);
  useLayoutEffect(() => {
    const element = field.current;
    if (!element) return;
    const resize = () => {
      element.style.height = "auto";
      element.style.height = `${element.scrollHeight + element.offsetHeight - element.clientHeight}px`;
    };
    resize();
    let width = element.clientWidth;
    let frame = 0;
    const observer = new ResizeObserver(() => {
      if (element.clientWidth === width) return;
      width = element.clientWidth;
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(resize);
    });
    observer.observe(element);
    return () => { cancelAnimationFrame(frame); observer.disconnect(); };
  }, [value]);
  return <textarea ref={field} name="title" aria-label="Title" rows={1} maxLength={200}
    value={value} onChange={event => onChange(event.target.value.replace(/[\r\n]+/g, " "))}
    onKeyDown={event => { if (event.key === "Enter" && !event.nativeEvent.isComposing) event.preventDefault(); }}
    className="block min-h-14 w-full min-w-0 resize-none overflow-hidden rounded-lg border border-transparent bg-transparent px-0 py-2 text-3xl font-semibold leading-tight wrap-anywhere outline-none focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/50" />;
}
