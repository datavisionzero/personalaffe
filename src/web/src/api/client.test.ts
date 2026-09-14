import { describe, expect, it } from "vitest";

import { expiresAt, guardedBy, outcomeOf, versionFrom, versionOf } from "./client";
import type { Problem } from "./client";

/**
 * The half of the guard that lives in the browser: keep the version a read
 * answered, send it on the write that follows, and tell the two 404s apart.
 */
describe("the guarded write", () => {
  it("spells a version the way the instance spells it", () => {
    expect(versionOf("2026-09-14T08:30:00.123456Z")).toBe('"2026-09-14T08:30:00.123456Z"');
  });

  it("reads the version a single-object read answered", () => {
    const answered = new Response(null, {
      headers: { ETag: '"2026-09-14T08:30:00.123456Z"' },
    });

    expect(versionFrom(answered)).toBe('"2026-09-14T08:30:00.123456Z"');
  });

  it("has no version where the read answered none", () => {
    expect(versionFrom(new Response(null))).toBeUndefined();
  });

  it("sends the version back in the header the instance reads", () => {
    expect(guardedBy(versionOf("2026-09-14T08:30:00.123456Z"))).toEqual({
      headers: { "If-Match": '"2026-09-14T08:30:00.123456Z"' },
    });
  });

  it("round trips: what a read answered is what the write sends", () => {
    const answered = new Response(null, { headers: { ETag: '"2026-09-14T08:30:00.123456Z"' } });
    const version = versionFrom(answered)!;

    expect(guardedBy(version).headers["If-Match"]).toBe(answered.headers.get("ETag"));
  });
});

describe("the outcomes a screen has to draw", () => {
  const problem = (type: string, extra: Record<string, unknown> = {}): Problem =>
    ({ type, status: 404, ...extra }) as Problem;

  it("tells the two 404s apart by their type and not their status", () => {
    expect(outcomeOf(problem("/problems/deleted"))).toBe("deleted");
    expect(outcomeOf(problem("/problems/not-found"))).toBe("other");
  });

  it("knows a write that was refused for holding an old version", () => {
    expect(outcomeOf(problem("/problems/stale"))).toBe("stale");
  });

  it("calls anything else what it is", () => {
    expect(outcomeOf(problem("/problems/forbidden"))).toBe("other");
    expect(outcomeOf(undefined)).toBe("other");
  });

  it("carries how long a deleted thing can still be brought back", () => {
    const gone = problem("/problems/deleted", { expires_at: "2026-10-12T19:02:11.881000Z" });

    expect(expiresAt(gone)).toBe("2026-10-12T19:02:11.881000Z");
    expect(expiresAt(problem("/problems/not-found"))).toBeUndefined();
  });
});
