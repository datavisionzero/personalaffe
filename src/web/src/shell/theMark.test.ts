import { describe, expect, it } from "vitest";

import { lettersOf } from "./theMark";

describe("the letters a title gives a mark", () => {
  it("takes the first letter of each of the first two words", () => {
    expect(lettersOf("Haus und Hof")).toBe("HU");
  });

  it("takes the first two letters of a single word", () => {
    expect(lettersOf("Haus")).toBe("HA");
  });

  it("upper-cases what it took", () => {
    expect(lettersOf("haus")).toBe("HA");
    expect(lettersOf("das haus")).toBe("DH");
  });

  it("has nothing to draw for an instance nobody has named", () => {
    expect(lettersOf(null)).toBe("");
    expect(lettersOf("")).toBe("");
    expect(lettersOf("   ")).toBe("");
  });

  it("is not confused by the space around or between the words", () => {
    expect(lettersOf("  Haus   und   Hof  ")).toBe("HU");
  });

  it("counts a letter and not a code unit", () => {
    // "🏠x".slice(0, 2) is half a character followed by nothing legible.
    expect(lettersOf("🏠x")).toBe("🏠X");
    expect(lettersOf("🏠 Haus")).toBe("🏠H");
  });

  it("takes one letter from a title of one letter", () => {
    expect(lettersOf("H")).toBe("H");
  });

  it("reads a title as text and never as anything else", () => {
    // Whatever is in a title, what comes out is letters from it.
    expect(lettersOf("<script>alert(1)</script>")).toBe("<S");
  });
});
