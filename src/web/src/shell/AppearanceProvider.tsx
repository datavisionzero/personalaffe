import { useEffect, useRef } from "react";

import { api } from "@/api/client";
import { useAsk } from "@/shared/ask";
import { defaultColour, defaultShape, faviconOf, productName } from "./theMark";
import { AppearanceContext, theProducts, type Appearance } from "./useAppearance";

/**
 * What this instance is called and what its mark looks like, asked once for the
 * whole application.
 *
 * <b>Nothing waits on it except the name itself.</b> An instance with a title
 * that flashed `personalaffe` for half a second on every load would be worse
 * than one that never had a title — but a sign-in form that waited on what the
 * instance is called would be worse still. So this draws its children at once
 * and hands down `known`, and the two places the name appears hold their space
 * until the instance has said. Where it will not say, the product's own name
 * and mark are what is drawn.
 *
 * <b>It is outside the door and it is asked from outside it</b>, which is why
 * this sits above the session rather than inside the workspace: the sign-in
 * screen and the browser tab are exactly where the answer is worth having.
 *
 * A change made on another device arrives here the way it arrives everywhere
 * else — {@link useAsk} asks again on a timer, quietly (PERSONAL-31) — so
 * renaming an instance on a phone renames the tab open on the desk. What is
 * handed down as `again` is the quiet read and not the loud one: the loud one
 * puts the answer back to "asking", and everything in the application is below
 * this.
 */
export function AppearanceProvider({ children }: { children: React.ReactNode }) {
  const { asked, refresh } = useAsk("/api/appearance", (signal) =>
    api.GET("/api/appearance", { signal }),
  );

  const appearance = asked.at === "known" ? asked.value : theProducts;

  // Once it has answered it never goes back to "asking": the address never
  // changes and nothing below calls the loud read, so this settles once and
  // stays settled.
  const settled = asked.at !== "asking";

  useTheDocumentWears(appearance, settled);

  return (
    <AppearanceContext
      value={{
        appearance,
        name: appearance.title ?? productName,
        known: settled,
        again: refresh,
      }}
    >
      {children}
    </AppearanceContext>
  );
}

/**
 * The three things about an instance's appearance that are the document's
 * rather than a component's: the colour every token is read through, the name
 * in the tab, and the icon beside it.
 */
function useTheDocumentWears(appearance: Appearance, settled: boolean) {
  // What the document already had, so that what this put there can be taken
  // back off. A test mounting and unmounting the application must not leave a
  // title behind for the next one.
  const before = useRef<{ title: string; icon: string | null }>(undefined);

  useEffect(() => {
    if (!settled) {
      return;
    }

    before.current ??= { title: document.title, icon: iconHref() };

    // The colour is an attribute and never a style string. No value from the
    // API is written into CSS: the seven words each name three values that
    // already exist in the token layer, and a word that is not one of them
    // simply selects nothing and leaves the product's own colour standing.
    document.documentElement.dataset.markColour = appearance.colour;

    document.title = appearance.title ?? productName;

    // The icon is replaced only where the mark actually differs from the one
    // the document shipped with. An instance nobody has given a colour or a
    // shape keeps the icon in `index.html`, byte for byte — swapping it for a
    // generated one that is meant to look the same is a way to find out that
    // it does not.
    const own = appearance.colour !== defaultColour || appearance.shape !== defaultShape;

    if (own) {
      setIconHref(faviconOf(appearance.colour, appearance.shape));
    } else if (before.current.icon !== null) {
      setIconHref(before.current.icon);
    }
  }, [appearance.colour, appearance.shape, appearance.title, settled]);

  useEffect(
    () => () => {
      const had = before.current;

      if (had === undefined) {
        return;
      }

      document.title = had.title;
      delete document.documentElement.dataset.markColour;

      if (had.icon !== null) {
        setIconHref(had.icon);
      }
    },
    [],
  );
}

function icon(): HTMLLinkElement | null {
  return document.querySelector<HTMLLinkElement>('link[rel="icon"]');
}

function iconHref(): string | null {
  return icon()?.getAttribute("href") ?? null;
}

function setIconHref(href: string) {
  const link =
    icon() ??
    document.head.appendChild(Object.assign(document.createElement("link"), { rel: "icon" }));

  link.setAttribute("href", href);
}
