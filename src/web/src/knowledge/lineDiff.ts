export type DiffLine = { kind: "same" | "added" | "removed"; text: string };

/** Bounded LCS: large replacements use a block diff instead of quadratic memory. */
export function lineDiff(before: string, after: string): DiffLine[] {
  if (before === after) return before.split("\n").map(text => ({ kind: "same", text }));
  const a = before === "" ? [] : before.split("\n");
  const b = after === "" ? [] : after.split("\n");
  const prefix: DiffLine[] = [];
  let start = 0;
  while (start < a.length && start < b.length && a[start] === b[start]) {
    prefix.push({ kind: "same", text: a[start]! }); start++;
  }
  let endA = a.length, endB = b.length;
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) { endA--; endB--; }
  const old = a.slice(start, endA), next = b.slice(start, endB);
  const suffix = a.slice(endA).map(text => ({ kind: "same" as const, text }));
  if ((old.length + 1) * (next.length + 1) > 1_000_000) return [...prefix,
    ...old.map(text => ({ kind: "removed" as const, text })),
    ...next.map(text => ({ kind: "added" as const, text })), ...suffix];
  const width = next.length + 1;
  const lengths = new Uint32Array((old.length + 1) * width);
  for (let i = old.length - 1; i >= 0; i--) for (let j = next.length - 1; j >= 0; j--) {
    lengths[i * width + j] = old[i] === next[j] ? 1 + lengths[(i + 1) * width + j + 1]!
      : Math.max(lengths[(i + 1) * width + j]!, lengths[i * width + j + 1]!);
  }
  let i = 0, j = 0;
  while (i < old.length || j < next.length) {
    if (i < old.length && j < next.length && old[i] === next[j]) {
      prefix.push({ kind: "same", text: old[i++]! }); j++;
    } else if (i < old.length && (j === next.length || lengths[(i + 1) * width + j]! >= lengths[i * width + j + 1]!)) {
      prefix.push({ kind: "removed", text: old[i++]! });
    } else prefix.push({ kind: "added", text: next[j++]! });
  }
  return [...prefix, ...suffix];
}
