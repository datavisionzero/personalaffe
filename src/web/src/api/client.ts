import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

/**
 * The one way this application reaches the instance.
 *
 * The types are generated from `docs/api/openapi.json` before every build,
 * typecheck and test, which is what makes that document load-bearing: a route
 * that changed shape stops compiling here rather than failing on a screen.
 * Nothing hand-writes a URL or a body.
 *
 * The instance it reaches is the one that served this page. In development Vite
 * forwards what belongs to the instance, so that stays true there too — and it
 * is why nothing here reads an address out of the environment. An address in
 * the bundle would be a second place to get wrong, and a build that is specific
 * to one installation.
 */
export const api = createClient<paths>({
  baseUrl: window.location.origin,
  credentials: "same-origin",

  // A write from a browser proves it came from this application, with a header
  // no cross-site form can set (`docs/api.md`, The door). The other half of the
  // proof is `Origin`, which the browser sets on every request that is not a
  // GET and which no script can forge. Sent on reads too: it costs a header and
  // saves every call site from remembering which of them writes.
  headers: { "X-Personalaffe-CSRF": "1" },

  // Reached through `globalThis` when a request is made rather than captured
  // when this module loads, so that a test can stand an instance in front of
  // the generated client rather than in place of it.
  fetch: (request) => globalThis.fetch(request),
});

export type Schemas = components["schemas"];
export type Problem = Schemas["ProblemDetails"];

/**
 * The version a write says it is replacing: the object's `updated_at`, as the
 * instance spells it in `ETag` (`docs/api.md`, The guarded write).
 *
 * A read of one object answers the header and a list carries `updated_at` on
 * each item; either way, what a write sends back is the same value. This is the
 * one place in the application that knows the spelling, so that a screen never
 * assembles one out of a `Date`.
 */
export type Version = string & { readonly version: unique symbol };

/** The version an object at this `updated_at` is at, ready to be sent back. */
export function versionOf(updatedAt: string): Version {
  return `"${updatedAt}"` as Version;
}

/** The version a single-object read answered, where it answered one. */
export function versionFrom(response: Response): Version | undefined {
  const tag = response.headers.get("ETag");

  return tag === null ? undefined : (tag as Version);
}

/**
 * What a guarded write carries, as the generated client takes it: `If-Match` is
 * a header parameter in the contract, so it goes in `params.header` beside the
 * path. Every write that replaces something takes one, and the generated types
 * make forgetting it a compile error on the screen rather than a `412` in front
 * of the owner.
 */
export function guardedBy(version: Version): { header: { "If-Match": string } } {
  return { header: { "If-Match": version } };
}

/**
 * The two outcomes every screen in the workspace has to be able to draw, told
 * apart by the code rather than by the status: `deleted` and `not-found` are
 * both 404, and one of them means the owner can have the thing back.
 */
export type Outcome = "stale" | "deleted" | "other";

export function outcomeOf(problem: Problem | undefined): Outcome {
  const code = codeOf(problem);

  return code === "stale" || code === "deleted" ? code : "other";
}

/** When the thing at that address stops being recoverable, where it says so. */
export function expiresAt(problem: Problem | undefined): string | undefined {
  const said = (problem as { expires_at?: unknown } | undefined)?.expires_at;

  return typeof said === "string" ? said : undefined;
}

/**
 * The code a client switches on: the last segment of a refusal's relative
 * `type` (`docs/api.md`, Errors). `/problems/not-found` is `not-found`.
 */
export function codeOf(problem: Problem | undefined): string | undefined {
  return problem?.type?.split("/").pop();
}

/**
 * What went wrong, in a sentence: the problem document if the instance answered
 * one, and otherwise the status alone — because the thing that answered may not
 * have been the instance at all.
 */
export function describe(problem: Problem | undefined, status: number): string {
  return problem?.detail ?? problem?.title ?? `The instance answered ${status}.`;
}
