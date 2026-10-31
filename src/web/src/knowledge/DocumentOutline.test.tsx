import { useRef } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { DocumentOutline } from "./DocumentOutline";

function Document({ loaded = true, extra = false }: { loaded?: boolean; extra?: boolean }) {
  const root = useRef<HTMLDivElement>(null);
  const toolbar = useRef<HTMLDivElement>(null);
  return <>
    <DocumentOutline documentRoot={root} toolbar={toolbar} ready={loaded} />
    <div ref={toolbar}>Formatting</div>
    <div ref={root}><div className="ProseMirror">{loaded && <><h1 id="intro">Intro</h1><pre>{"# Not a heading"}</pre><h2 id="next">Next section</h2>{extra && <h3 id="more">More</h3>}</>}</div><h3 hidden>Editor menu heading</h3></div>
  </>;
}

afterEach(() => { window.history.replaceState(null, "", "/"); vi.restoreAllMocks(); });

it("resolves a legacy deep link after the document loads", async () => {
  const scroll = vi.spyOn(Element.prototype, "scrollIntoView");
  window.history.replaceState(null, "", "/#knowledge-heading-1");
  const view = render(<Document loaded={false} />);
  expect(scroll).not.toHaveBeenCalled();
  view.rerender(<Document />);
  await waitFor(() => expect(scroll).toHaveBeenCalled());
  expect(scroll.mock.contexts.at(-1)).toBe(screen.getByRole("heading", { name: "Next section" }));
});

it("updates the outline from rendered headings and navigates without editing", async () => {
  const scroll = vi.spyOn(Element.prototype, "scrollIntoView");
  const view = render(<Document />);
  await userEvent.click(await screen.findByText("On this page"));
  expect(screen.getAllByRole("link")).toHaveLength(2);
  view.rerender(<Document extra />);
  await screen.findByRole("link", { name: "More" });
  await userEvent.click(screen.getByRole("link", { name: "Next section" }));
  await waitFor(() => expect(scroll).toHaveBeenCalled());
  expect(window.location.hash).toBe("#knowledge-heading-1");
  expect(screen.getByText("More", { selector: "a" }).closest("details")).not.toHaveAttribute("open");
  expect(scroll.mock.contexts.at(-1)).toBe(screen.getByRole("heading", { name: "Next section" }));
});

it("also resolves named headings and subsequent hash changes", async () => {
  const scroll = vi.spyOn(Element.prototype, "scrollIntoView");
  window.history.replaceState(null, "", "/#next");
  render(<Document />);
  await waitFor(() => expect(scroll).toHaveBeenCalled());
  expect(scroll.mock.contexts.at(-1)).toBe(screen.getByRole("heading", { name: "Next section" }));
  act(() => { window.history.replaceState(null, "", "/#knowledge-heading-0"); window.dispatchEvent(new HashChangeEvent("hashchange")); });
  expect(scroll.mock.contexts.at(-1)).toBe(screen.getByRole("heading", { name: "Intro" }));
});
