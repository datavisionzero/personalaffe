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
 * The scheme a Markdown body names a stored file with: `file:` and the file's
 * id, which is made at its first upload and never changes.
 *
 * `[the report](file:0199f0c4-1234-7abc-8def-0123456789ab)` is a link to
 * `/api/files/0199…/content`, which the browser fetches with the session it
 * already has. Renaming the file or moving it into another folder does not
 * break it — that is what the reference being the id is for
 * (`docs/mvp-plan.md`, PERSONAL-E6) — and there is no second attachment store
 * for Knowledge to write into when it arrives in PERSONAL-E7.
 */
const filed = /^file:([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i;

/** Where a `file:` link points, or nothing where it is not one. */
export function fileHref(href: string | undefined): string | undefined {
  const named = filed.exec(href ?? "");

  return named === null ? undefined : `/api/files/${named[1]}/content`;
}

/**
 * Where a link leads inside this workspace, or nothing where it leads outside
 * it.
 *
 * <b>Knowledge is where the rest of this plugs in.</b> PERSONAL-E7 gives a page
 * an address that survives renaming and moving (`docs/mvp-plan.md`), and a body
 * naming another page is a scheme handled here — `[the architecture](page:0199…)`
 * — so that it is followed rather than opened: no new tab, no `noopener`, and
 * the frame never remounted.
 *
 * A `file:` link is deliberately not one of these. It is a download and not a
 * route: following it inside the application would mean the router being asked
 * for an address only the instance can answer.
 */
export function insidePath(href: string | undefined): string | undefined {
  void href;

  return undefined;
}

export const admitUrl: UrlTransform = (url) => {
  if (insidePath(url) !== undefined) {
    return url;
  }

  // A stored file, named by the id that does not change. It becomes the
  // instance's own download address, which is behind the same door as
  // everything else — a body that names a file cannot reach one the reader
  // could not have reached anyway.
  const file = fileHref(url);

  if (file !== undefined) {
    return file;
  }

  try {
    return admitted.has(new URL(url).protocol) ? url : undefined;
  } catch {
    // Not a URL at all, which a relative link is: it names nothing outside and
    // nothing inside, so it is text.
    return undefined;
  }
};
