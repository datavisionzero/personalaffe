import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { admitUrl, insidePath } from "./links";
import { Markdown } from "./Markdown";

describe("the Markdown pipeline", () => {
  it("renders GitHub-flavoured Markdown to components", () => {
    render(<Markdown>{"| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n- [ ] open\n\n~~gone~~"}</Markdown>);

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getAllByRole("checkbox")).toHaveLength(2);
    expect(screen.getByText("gone").tagName).toBe("DEL");
  });

  // The one departure from CommonMark. Somebody types into the editor and
  // presses Enter; nothing that reaches here is hard-wrapped, so a newline is
  // only ever meant.
  it("makes a line break of a single newline", () => {
    const { container } = render(<Markdown>{"Zeile eins\nZeile zwei\n\nAbsatz zwei"}</Markdown>);

    expect(container.querySelectorAll("br")).toHaveLength(1);
    expect(container.querySelectorAll("p")).toHaveLength(2);
  });

  it("never interprets HTML", () => {
    const { container } = render(
      <Markdown>{'before <img src="x" onerror="alert(1)"> <script>alert(1)</script> after'}</Markdown>,
    );

    expect(container.querySelector("img")).toBeNull();
    expect(container.querySelector("script")).toBeNull();
    expect(container.textContent).toContain("before");
    expect(container.textContent).toContain("after");
  });

  it("opens links as foreign links and admits three schemes", () => {
    render(<Markdown>{"[ok](https://example.org) [mail](mailto:a@example.org) [no](javascript:alert(1)) [rel](docs/api.md)"}</Markdown>);

    const ok = screen.getByRole("link", { name: "ok" });
    expect(ok).toHaveAttribute("href", "https://example.org");
    expect(ok).toHaveAttribute("rel", "noopener noreferrer");
    expect(ok).toHaveAttribute("target", "_blank");
    expect(screen.getByRole("link", { name: "mail" })).toHaveAttribute("href", "mailto:a@example.org");

    expect(screen.queryByRole("link", { name: "no" })).toBeNull();
    expect(screen.queryByRole("link", { name: "rel" })).toBeNull();
    expect(screen.getByText("no")).toBeInTheDocument();
  });

  it("refuses what the library would have admitted", () => {
    expect(admitUrl("irc://irc.example.org/#x", "href", { type: "element", tagName: "a", properties: {}, children: [] })).toBeUndefined();
    expect(admitUrl("xmpp:a@b", "href", { type: "element", tagName: "a", properties: {}, children: [] })).toBeUndefined();
    expect(admitUrl("http://example.org", "href", { type: "element", tagName: "a", properties: {}, children: [] })).toBe("http://example.org");
  });

  // A file has an address since PERSONAL-E6 and a page gets one in PERSONAL-E7,
  // which is what `insidePath` is the seam for. A scheme with nothing behind it
  // yet, and a `file:` that is not an id, are both somebody else's address and
  // stay text.
  it("treats a scheme it does not know as text", () => {
    render(<Markdown>{"[a](page:architecture) [b](file:invoice.pdf)"}</Markdown>);

    for (const name of ["a", "b"]) {
      expect(screen.queryByRole("link", { name })).toBeNull();
      expect(screen.getByText(name)).toBeInTheDocument();
    }

    expect(insidePath("page:architecture")).toBeUndefined();
  });

  it("marks fenced code apart from inline code", () => {
    const { container } = render(<Markdown>{"say `pea trash list`\n\n```sh\npea applications\n```"}</Markdown>);

    expect(container.querySelector("pre code")).toHaveTextContent("pea applications");
    expect(container.querySelectorAll("code")).toHaveLength(2);
  });

  // Nothing tokenizes code here, so the word the fence named is what says what
  // the block is.
  it("names the language of a fenced block and highlights nothing", () => {
    const { container } = render(<Markdown>{"```csharp\nvar x = 1;\n```"}</Markdown>);

    expect(screen.getByText("csharp")).toBeInTheDocument();
    expect(container.querySelectorAll("pre code span")).toHaveLength(0);
  });

  it("says nothing above a fence that named nothing", () => {
    const { container } = render(<Markdown>{"```\nplain\n```"}</Markdown>);

    expect(container.querySelector("pre")).toHaveTextContent("plain");
    expect(container.querySelector("pre")?.previousElementSibling).toBeNull();
  });

  it("turns a file: link into the download address the id will always have", () => {
    const id = "0199f0c4-1234-7abc-8def-0123456789ab";

    render(<Markdown>{`[the report](file:${id})`}</Markdown>);

    // The reference is the id and not the name, so renaming the file or moving
    // it into another folder leaves this link working (`docs/mvp-plan.md`,
    // PERSONAL-E6).
    expect(screen.getByRole("link", { name: "the report" })).toHaveAttribute(
      "href",
      `/api/files/${id}/content`,
    );
  });

  it("leaves a file: link that is not an id as text", () => {
    render(<Markdown>{"[not one](file:../../etc/passwd)"}</Markdown>);

    expect(screen.queryByRole("link")).toBeNull();
    expect(screen.getByText("not one")).toBeInTheDocument();
  });
});
