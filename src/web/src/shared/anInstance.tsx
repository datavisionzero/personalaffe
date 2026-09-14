import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter } from "react-router";
import { vi } from "vitest";

import { ThemeProvider } from "@/components/theme-provider";
import { TooltipProvider } from "@/components/ui/tooltip";
import type { Me } from "@/session/useSession";

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

/**
 * A screen at an address, inside everything the frame puts above it: the theme,
 * the tooltips the owned primitives reach for, and a router whose history is
 * the test's rather than the browser's.
 */
export function renderAt(path: string, element: ReactElement) {
  return render(
    <ThemeProvider storageKey="a-test.theme">
      <TooltipProvider>
        <MemoryRouter initialEntries={[path]}>{element}</MemoryRouter>
      </TooltipProvider>
    </ThemeProvider>,
  );
}

/** The owner, as `GET /api/me` answers them. */
export const theOwner: Me = {
  kind: "owner",
  email: "owner@example.com",
  name: null,
  permissions: {
    scratchpad: "read_write",
    knowledge: "read_write",
    tasks: "read_write",
    files: "read_write",
  },
  since: "2026-09-13T12:00:00.000000Z",
};

/** Agent access, with whatever the owner gave it and nothing else. */
export function anAgent(permissions: Partial<Me["permissions"]> = {}): Me {
  return {
    kind: "agent",
    email: null,
    name: "the laptop agent",
    permissions: {
      scratchpad: "none",
      knowledge: "none",
      tasks: "none",
      files: "none",
      ...permissions,
    },
    since: "2026-09-13T12:00:00.000000Z",
  };
}

/**
 * What `GET /api/applications` answers: all four switched on and reachable,
 * unless the test says otherwise about one of them.
 */
export function theApplications(
  overrides: Record<string, { enabled?: boolean; permission?: string }> = {},
): Answer {
  return {
    body: {
      items: ["scratchpad", "knowledge", "tasks", "files"].map((application) => ({
        application,
        enabled: overrides[application]?.enabled ?? true,
        permission: overrides[application]?.permission ?? "read_write",
        updated_at: "2026-09-14T15:30:08.000000Z",
      })),
    },
  };
}
