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

export type Asked = {
  method: string;
  path: string;
  /** The query string, so that a test can say what travelled beside the path. */
  query: string;
  body: unknown;
  headers: Headers;
};

export function anInstance(answers: Record<string, Answer | Answer[]>) {
  const asked: Asked[] = [];

  const fetch = vi.fn<typeof globalThis.fetch>(async (input) => {
    const request = input as Request;
    const url = new URL(request.url);
    const key = `${request.method} ${url.pathname}`;

    // A body is JSON everywhere except an upload, whose body is the file
    // itself (`docs/api.md`, Files). What a test wants to see of one is what
    // arrived, so it is read as text and parsed where it parses. `request.body`
    // is not asked: the stream a `File` becomes is not one every runtime
    // exposes, and reading the clone is the same question without the
    // assumption.
    const body = await readBody(request.clone());
    asked.push({
      method: request.method,
      path: url.pathname,
      query: url.search,
      body,
      headers: request.headers,
    });

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

async function readBody(request: Request): Promise<unknown> {
  let text: string;

  try {
    text = await request.text();
  } catch {
    return undefined;
  }

  if (text === "") {
    return undefined;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return text;
  }
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

/**
 * What `GET /api/dashboard` answers: every tile shown and offered, and nothing
 * in any of them, unless the test says otherwise.
 *
 * Its shape is the contract's, so a section that changed from `null` to `[]` —
 * which is the one distinction the home page is built on — fails a test by not
 * compiling rather than by drawing something odd.
 */
export function theDashboard(
  overrides: {
    tiles?: Record<string, { shown?: boolean; offered?: boolean }>;
    tasks?: unknown[] | null;
    knowledge?: unknown[] | null;
    scratchpad?: unknown[] | null;
    files?: unknown[] | null;
  } = {},
): Answer {
  return {
    body: {
      tiles: ["tasks", "knowledge", "scratchpad", "files", "weather"].map((tile) => ({
        tile,
        shown: overrides.tiles?.[tile]?.shown ?? true,
        offered: overrides.tiles?.[tile]?.offered ?? true,
        updated_at: "2026-01-01T00:00:00.000000Z",
      })),
      tasks: overrides.tasks === undefined ? [] : overrides.tasks,
      knowledge: overrides.knowledge === undefined ? [] : overrides.knowledge,
      scratchpad: overrides.scratchpad === undefined ? [] : overrides.scratchpad,
      files: overrides.files === undefined ? [] : overrides.files,
    },
  };
}

/** What `GET /api/weather` answers when the owner has said where. */
export function theWeather(overrides: Record<string, unknown> = {}): Answer {
  return {
    body: {
      place: "Wuppertal",
      latitude: 51.2563,
      longitude: 7.1482,
      units: "metric",
      temperature_unit: "°C",
      wind_unit: "km/h",
      available: true,
      reading: {
        temperature: 16.1,
        feels_like: 16.5,
        high: 20.3,
        low: 14.2,
        wind: 2.2,
        code: 3,
        description: "Overcast",
        day: true,
        read_at: new Date().toISOString(),
      },
      attribution: "Weather data by a test",
      updated_at: "2026-09-15T18:02:11.000000Z",
      ...overrides,
    },
  };
}
