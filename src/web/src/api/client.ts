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

  // Reached through `globalThis` when a request is made rather than captured
  // when this module loads, so that a test can stand an instance in front of
  // the generated client rather than in place of it.
  fetch: (request) => globalThis.fetch(request),
});

export type Schemas = components["schemas"];
export type Problem = Schemas["ProblemDetails"];

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
