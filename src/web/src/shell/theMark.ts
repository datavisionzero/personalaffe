import type { Schemas } from "@/api/client";

export type MarkColour = Schemas["MarkColour"];
export type MarkShape = Schemas["MarkShape"];

/** The product's own, and what an instance nobody has named wears. */
export const defaultColour: MarkColour = "violet";
export const defaultShape: MarkShape = "square";

/** What this instance is called when nobody has called it anything. */
export const productName = "personalaffe";

/**
 * Up to two letters taken from a title, for the middle of the mark.
 *
 * <b>Derived and never stored.</b> The first letter of each of the first two
 * words, or the first two letters of a single word, upper-cased — so there is
 * nothing to keep in step with a rename and nothing kept that a rename could
 * contradict.
 *
 * The title is spread rather than sliced, because a name beginning with an
 * emoji or with anything outside the basic plane is two code units and one
 * letter, and `"🏠x".slice(0, 2)` is half a character.
 */
export function lettersOf(title: string | null): string {
  const words = (title ?? "").trim().split(/\s+/).filter((word) => word !== "");

  if (words.length === 0) {
    return "";
  }

  const letters =
    words.length === 1 ? [...words[0]].slice(0, 2) : [[...words[0]][0], [...words[1]][0]];

  return letters.join("").toLocaleUpperCase();
}

/**
 * What each colour looks like in the tab strip, where no stylesheet of ours is
 * read.
 *
 * <b>The one place a colour is written twice</b>, and it is written twice
 * because a favicon is a document of its own: it is handed to the browser as a
 * `data:` URI and cannot reach `--brand`. Each value is the light theme's brand
 * for that colour rendered to sRGB, and `palette.test.ts` is what holds the two
 * together — it reads `index.css` and fails if one of these drifts from it.
 */
const inTheTabStrip: Record<MarkColour, string> = {
  violet: "#5477c7",
  blue: "#327cc2",
  teal: "#05868c",
  green: "#3c8841",
  amber: "#a86b02",
  red: "#bb5752",
  pink: "#af578c",
};

/**
 * The mark as a favicon: the shape and the colour, and no letters.
 *
 * <b>No letters, on purpose.</b> Two of them are not legible at 16px, and
 * leaving them out is what keeps this SVG built only out of a closed set of
 * shapes and a closed set of colours — <b>nothing the owner wrote is ever
 * assembled into markup or into a `data:` URI</b>, which is the whole reason a
 * title needs no escaping anywhere in this application.
 */
export function faviconOf(colour: MarkColour, shape: MarkShape): string {
  const fill = inTheTabStrip[colour];

  const body =
    shape === "circle"
      ? `<circle cx='16' cy='16' r='16' fill='${fill}'/>`
      : `<rect width='32' height='32' rx='7' fill='${fill}'/>`;

  const svg = `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'>${body}</svg>`;

  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}
