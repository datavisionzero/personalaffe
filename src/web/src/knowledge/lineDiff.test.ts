import { expect, it } from "vitest";
import { lineDiff } from "./lineDiff";
it("shows insertions and deletions between unchanged lines", () => {
  expect(lineDiff("start\nold\nend", "start\nnew\nend")).toEqual([
    { kind: "same", text: "start" }, { kind: "removed", text: "old" },
    { kind: "added", text: "new" }, { kind: "same", text: "end" },
  ]);
});
it("preserves both originals for large replacements and repeated lines", () => {
  for (const [before, after] of [["a\na\nb", "a\nb\nb"], ["", "hello"], ["bye", ""], ["old\n".repeat(1100), "new\n".repeat(1100)]]) {
    const diff = lineDiff(before!, after!);
    expect(diff.filter(l => l.kind !== "added").map(l => l.text).join("\n")).toBe(before);
    expect(diff.filter(l => l.kind !== "removed").map(l => l.text).join("\n")).toBe(after);
  }
});
