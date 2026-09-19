import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { anInstance, refused, theAppearance } from "@/shared/anInstance";
import { AppearanceProvider } from "./AppearanceProvider";
import { useAppearance } from "./useAppearance";
import { faviconOf } from "./theMark";

/** What every screen actually reads out of the provider. */
function Says() {
  const { name, known, appearance } = useAppearance();

  return (
    <p>
      {known ? `known: ${name} / ${appearance.colour} / ${appearance.shape}` : "not said yet"}
    </p>
  );
}

function icon() {
  return document.querySelector('link[rel="icon"]')?.getAttribute("href") ?? null;
}

/** What `index.html` ships, which is what an untouched instance must keep. */
const shipped =
  "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Crect width='32' height='32' rx='7' fill='%235b6ee1'/%3E%3C/svg%3E";

function asTheDocumentShips() {
  document.title = "personalaffe";
  document.head.querySelector('link[rel="icon"]')?.remove();

  const link = document.createElement("link");
  link.rel = "icon";
  link.setAttribute("href", shipped);
  document.head.append(link);
}

describe("what this instance is called", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    document.head.querySelector('link[rel="icon"]')?.remove();
    delete document.documentElement.dataset.markColour;
  });

  it("says nothing at all until the instance has said", async () => {
    anInstance({ "GET /api/appearance": theAppearance({ title: "Haus" }) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    // Not `personalaffe` first and `Haus` a moment later: an instance with a
    // name must not flash the product's.
    expect(screen.getByText("not said yet")).toBeInTheDocument();

    expect(await screen.findByText(/known: Haus/)).toBeInTheDocument();
  });

  it("puts the title in the tab", async () => {
    asTheDocumentShips();
    anInstance({ "GET /api/appearance": theAppearance({ title: "Haus" }) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    await waitFor(() => expect(document.title).toBe("Haus"));
  });

  it("writes the title into the tab as it was typed and never as markup", async () => {
    asTheDocumentShips();
    anInstance({ "GET /api/appearance": theAppearance({ title: "<b>Haus</b> **x**" }) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    await waitFor(() => expect(document.title).toBe("<b>Haus</b> **x**"));
  });

  it("puts the colour on the document as an attribute and never as a style", async () => {
    anInstance({ "GET /api/appearance": theAppearance({ colour: "teal" }) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    await waitFor(() => expect(document.documentElement.dataset.markColour).toBe("teal"));

    // The three values are the token layer's; nothing was written inline.
    expect(document.documentElement.getAttribute("style")).toBeNull();
  });

  it("leaves the icon the document shipped with exactly where it is", async () => {
    asTheDocumentShips();
    anInstance({ "GET /api/appearance": theAppearance({ title: "Haus" }) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    await screen.findByText(/known: Haus/);

    // A title alone changes no icon — the favicon carries no letters — and the
    // default colour and shape are the ones already in `index.html`. Replacing
    // it with a generated one that is meant to look the same is how you find
    // out that it does not.
    expect(icon()).toBe(shipped);
  });

  it("draws the icon itself once the mark is the owner's own", async () => {
    asTheDocumentShips();
    anInstance({ "GET /api/appearance": theAppearance({ colour: "red", shape: "circle" }) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    await waitFor(() => expect(icon()).toBe(faviconOf("red", "circle")));
  });

  it("wears the product's own name when the instance will not say", async () => {
    asTheDocumentShips();
    anInstance({ "GET /api/appearance": refused("not-found", 404) });

    render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    // A screen still draws. What the instance is called is not a thing worth
    // holding a sign-in form for.
    expect(await screen.findByText("known: personalaffe / violet / square")).toBeInTheDocument();
    expect(document.title).toBe("personalaffe");
    expect(icon()).toBe(shipped);
  });

  it("gives the document back what it found when it goes away", async () => {
    asTheDocumentShips();
    anInstance({ "GET /api/appearance": theAppearance({ title: "Haus", colour: "pink" }) });

    const { unmount } = render(
      <AppearanceProvider>
        <Says />
      </AppearanceProvider>,
    );

    await waitFor(() => expect(document.title).toBe("Haus"));

    unmount();

    expect(document.title).toBe("personalaffe");
    expect(icon()).toBe(shipped);
    expect(document.documentElement.dataset.markColour).toBeUndefined();
  });
});
