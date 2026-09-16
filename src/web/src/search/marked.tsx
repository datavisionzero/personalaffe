import type { ReactNode } from "react";

/**
 * The words that were asked for, in bold.
 *
 * <b>The instance sends text and never markup</b> (`docs/api.md`, The search),
 * which is what keeps a snippet out of the business of being safe to render.
 * The marking is done here, over the words this application worked out for
 * itself, so nothing that came back over the wire is ever treated as anything
 * but text — a `<mark>` around a piece of a string is not the same thing as
 * trusting the string.
 *
 * The words are prefixes, so a match is a piece of a word: asking for `arch`
 * marks the first four letters of "Architecture" and leaves the rest alone.
 */
export function marked(text: string, words: string[]): ReactNode {
  if (words.length === 0) {
    return text;
  }

  // Escaped, because a needle keeps only letters and digits everywhere else but
  // this is the one place it becomes a pattern, and a pattern built out of
  // somebody's own text is exactly where that stops being obvious.
  const pattern = new RegExp(
    `(${words.map((word) => word.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|")})`,
    "giu",
  );

  return text
    .split(pattern)
    .map((piece, index) =>
      words.includes(piece.toLowerCase()) ? (
        <mark key={index} className="bg-brand/20 text-foreground rounded-sm px-0.5">
          {piece}
        </mark>
      ) : (
        piece
      ),
    );
}
