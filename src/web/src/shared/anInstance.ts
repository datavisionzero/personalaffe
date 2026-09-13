import { vi } from "vitest";

/**
 * An instance standing in front of the generated client rather than in place of
 * it: the client is the real one, the contract's types are the real ones, and
 * only `fetch` is ours — so a route or a field that changed shape fails these
 * tests by not compiling.
 */
export type Answer = { status?: number; body?: unknown; contentType?: string };

export type Asked = { method: string; path: string; body: unknown; headers: Headers };

export function anInstance(answers: Record<string, Answer | Answer[]>) {
  const asked: Asked[] = [];

  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
    const request = input as Request;
    const url = new URL(request.url);
    const key = `${request.method} ${url.pathname}`;

    const body = request.body === null ? undefined : await request.clone().json();
    asked.push({ method: request.method, path: url.pathname, body, headers: request.headers });

    const answer = answers[key];
    const next = Array.isArray(answer) ? (answer.shift() ?? { status: 404 }) : answer;

    if (next === undefined) {
      return new Response(
        JSON.stringify({ type: "/problems/not-found", title: "Nothing at that address", status: 404 }),
        { status: 404, headers: { "Content-Type": "application/problem+json" } },
      );
    }

    const status = next.status ?? 200;

    return new Response(next.body === undefined ? null : JSON.stringify(next.body), {
      status: next.body === undefined ? 204 : status,
      headers: {
        "Content-Type":
          next.contentType ?? (status >= 400 ? "application/problem+json" : "application/json"),
      },
    });
  });

  vi.stubGlobal("fetch", fetch);

  return { asked, fetch };
}

/** A refusal as the instance writes one (`docs/api.md`, Errors). */
export function refused(code: string, status: number, detail?: string): Answer {
  return {
    status,
    body: { type: `/problems/${code}`, title: code, status, detail },
  };
}
