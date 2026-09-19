/// <reference types="node" />
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";

import { faviconOf } from "./theMark";

// The stylesheet as it is written, read off the disk.
//
// `import "../index.css?raw"` is the obvious way and it answers the empty
// string: the Tailwind plugin claims every `.css` file and compiles it, and
// what this has to read is the source. The path is resolved from the working
// directory, which `vite.config.ts` fixes as this workspace, rather than from
// `import.meta.url`, which under the jsdom environment is not a file URL.
const css = readFileSync(join(process.cwd(), "src", "index.css"), "utf8");

/**
 * The seven colours an instance can wear, checked rather than asserted.
 *
 * <b>This reads `index.css`.</b> A table of values copied into a test proves
 * that somebody once copied them; what has to hold is that the values the
 * application actually ships are legible, so they are parsed out of the
 * stylesheet, converted to sRGB and measured here.
 *
 * <b>What "legible" means is violet.</b> `--brand` at oklch(0.58 0.13 265) on
 * the light background is 4.17:1 — under WCAG AA for small text, and the colour
 * this product has shipped with since PERSONAL-E4. Holding the six new ones to
 * 4.5 while the default sits below it would be a standard the product does not
 * meet; changing violet to meet it would repaint every existing instance, which
 * PERSONAL-72 explicitly must not do. So the rule is the honest one: <b>no
 * colour an owner can choose is less legible than the one they already have</b>,
 * in either theme, in any of the three places the colour is read — and all of
 * them clear 3:1, which is AA for user interface components and large text.
 */

type Oklch = [number, number, number];

/** The three values a `:root`-level or `[data-mark-colour]` block declares. */
type Brand = { brand: Oklch; foreground: Oklch; soft: Oklch };

function oklch(value: string): Oklch {
  const found = /oklch\(\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\)/.exec(value);

  if (found === null) {
    throw new Error(`Not a colour this test can read: ${value}`);
  }

  return [Number(found[1]), Number(found[2]), Number(found[3])];
}

/**
 * One declaration out of the first block a selector opens.
 *
 * Anchored to the start of a line, because `.dark` also appears inside the
 * `@custom-variant` at the top of the file and inside every dark block below —
 * and the first of those is not a block at all.
 */
function declared(selector: string, property: string): Oklch {
  const escaped = selector.replace(/[[\]().*+?^$|\\{}]/g, "\\$&");
  const block = new RegExp(`^${escaped}[^{]*\\{([^}]*)\\}`, "m").exec(css);

  if (block === null) {
    throw new Error(`index.css has no block for ${selector}.`);
  }

  const line = new RegExp(`${property}:\\s*([^;]+);`).exec(block[1]);

  if (line === null) {
    throw new Error(`${selector} declares no ${property}.`);
  }

  return oklch(line[1]);
}

function brandOf(selector: string): Brand {
  return {
    brand: declared(selector, "--brand"),
    foreground: declared(selector, "--brand-foreground"),
    soft: declared(selector, "--brand-soft"),
  };
}

/**
 * OKLab to sRGB, and then the relative luminance WCAG is written in terms of.
 * The matrices are the ones in CSS Color 4; nothing here is approximate except
 * the clamp, which is what a display does to a colour outside its gamut.
 */
function srgb([L, C, h]: Oklch): [number, number, number] {
  const radians = (h * Math.PI) / 180;
  const a = C * Math.cos(radians);
  const b = C * Math.sin(radians);

  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3;

  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ].map((linear) =>
    linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.pow(linear, 1 / 2.4) - 0.055,
  ) as [number, number, number];
}

function luminance(colour: Oklch): number {
  const [r, g, b] = srgb(colour)
    .map((channel) => Math.min(1, Math.max(0, channel)))
    .map((channel) =>
      channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4,
    );

  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(one: Oklch, other: Oklch): number {
  const a = luminance(one);
  const b = luminance(other);

  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

const colours = ["violet", "blue", "teal", "green", "amber", "red", "pink"] as const;

const light = {
  background: declared(":root", "--background"),
  foreground: declared(":root", "--foreground"),
};

const dark = {
  background: declared(".dark", "--background"),
  foreground: declared(".dark", "--foreground"),
};

/** The three readings that decide whether a colour is usable at all. */
function readings(brand: Brand, theme: { background: Oklch; foreground: Oklch }) {
  return {
    // The brand as text and as a border on the page: the version string, the
    // active settings tab, a link.
    brandOnBackground: contrast(brand.brand, theme.background),
    // Text on the brand: the letters in the mark, a filled button.
    foregroundOnBrand: contrast(brand.foreground, brand.brand),
    // Ordinary text on the soft background behind it.
    foregroundOnSoft: contrast(theme.foreground, brand.soft),
  };
}

const theDefault = {
  light: readings(brandOf('[data-mark-colour="violet"]'), light),
  dark: readings(brandOf('.dark[data-mark-colour="violet"]'), dark),
};

describe("the colours an instance can wear", () => {
  it("has a violet that is character for character the product's own", () => {
    // If this fails, every existing instance has just been repainted.
    expect(brandOf('[data-mark-colour="violet"]')).toEqual(brandOf(":root"));
    expect(brandOf('.dark[data-mark-colour="violet"]')).toEqual(brandOf(".dark"));
  });

  it.each(colours)("declares %s in both themes", (colour) => {
    expect(() => brandOf(`[data-mark-colour="${colour}"]`)).not.toThrow();
    expect(() => brandOf(`.dark[data-mark-colour="${colour}"]`)).not.toThrow();
  });

  it.each(colours)("%s is no less legible than the colour it replaces", (colour) => {
    const measured = {
      light: readings(brandOf(`[data-mark-colour="${colour}"]`), light),
      dark: readings(brandOf(`.dark[data-mark-colour="${colour}"]`), dark),
    };

    for (const theme of ["light", "dark"] as const) {
      for (const reading of ["brandOnBackground", "foregroundOnBrand", "foregroundOnSoft"] as const) {
        // A hair of tolerance, because the values are rounded to three decimal
        // places in the stylesheet and the floor is computed from one of them.
        expect(
          measured[theme][reading],
          `${colour} ${theme} ${reading}`,
        ).toBeGreaterThanOrEqual(theDefault[theme][reading] - 0.01);

        // And whatever the default is, nothing drops below AA for a user
        // interface component.
        expect(measured[theme][reading], `${colour} ${theme} ${reading}`).toBeGreaterThanOrEqual(3);
      }
    }
  });

  it.each(colours)("%s draws a favicon out of the same colour it paints with", (colour) => {
    const href = faviconOf(colour, "square");
    const hex = /%23([0-9a-f]{6})/.exec(href);

    expect(hex, href).not.toBeNull();

    const n = parseInt(hex![1], 16);
    const swatch = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => v / 255);
    const painted = srgb(brandOf(`[data-mark-colour="${colour}"]`).brand);

    // The favicon cannot reach `--brand`, so the hex is written out by hand in
    // `theMark.ts`. This is what keeps the two from drifting: each channel is
    // the stylesheet's light brand, to within the rounding a hex costs.
    for (let channel = 0; channel < 3; channel += 1) {
      expect(
        Math.abs(swatch[channel] - painted[channel]),
        `${colour} channel ${channel}`,
      ).toBeLessThan(0.01);
    }
  });

  it("draws the two shapes and nothing else", () => {
    expect(faviconOf("violet", "square")).toContain(encodeURIComponent("<rect"));
    expect(faviconOf("violet", "circle")).toContain(encodeURIComponent("<circle"));

    // No text element anywhere: nothing the owner wrote is assembled into a
    // `data:` URI.
    expect(faviconOf("violet", "circle")).not.toContain(encodeURIComponent("<text"));
  });
});
