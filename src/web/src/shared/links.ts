import type { UrlTransform } from "react-markdown";

/**
 * Links in a Markdown body are foreign links. `react-markdown`'s default admits
 * `irc`, `ircs` and `xmpp` beside the three below; personalaffe admits exactly
 * `http`, `https` and `mailto`, and a URL with any other scheme — or a relative
 * one — loses its `href` and stays the text it is (ADR 0001, adopted from
 * planaffe ADR 0017).
 *
 * <b>What is rendered here was not necessarily written by the owner.</b> An
 * agent with write access writes Markdown into this workspace, quoting things
 * nobody vetted, and `javascript:` is a scheme a link can carry.
 */
const admitted = new Set(["http:", "https:", "mailto:"]);

/**
 * Where a link leads inside this workspace, or nothing where it leads outside
 * it.
 *
 * <b>Nothing leads inside yet, and that is where Knowledge plugs in.</b>
 * PERSONAL-E7 gives a page an address that survives renaming and moving
 * (`docs/mvp-plan.md`), and a body naming another page is a scheme handled
 * here — `[the architecture](page:0199f0c4-…)` — so that it is followed rather
 * than opened: no new tab, no `noopener`, and the frame never remounted. Files
 * link the same way when PERSONAL-E6 gives them stable references.
 *
 * Until then every link in a body is somebody else's address, and is treated
 * as one.
 */
export function insidePath(href: string | undefined): string | undefined {
  void href;

  return undefined;
}

export const admitUrl: UrlTransform = (url) => {
  if (insidePath(url) !== undefined) {
    return url;
  }

  try {
    return admitted.has(new URL(url).protocol) ? url : undefined;
  } catch {
    // Not a URL at all, which a relative link is: it names nothing outside and
    // nothing inside, so it is text.
    return undefined;
  }
};
