import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { api } from "@/api/client";
import { anInstance, refused } from "@/shared/anInstance";
import { refreshEvery, useAsk } from "./ask";

/** A screen with one question on it, and nothing else. */
function AScreen({
  at = "/api/version",
  hold = false,
  onState,
}: {
  at?: string;
  hold?: boolean;
  onState?: (state: string) => void;
}) {
  const { asked, again, unanswered } = useAsk(
    at,
    (signal) => api.GET("/api/version", { signal }),
    { hold },
  );

  onState?.(asked.at);

  return (
    <div>
      <p data-testid="state">{asked.at}</p>
      <p data-testid="value">{asked.at === "known" ? asked.value.version : ""}</p>
      <p data-testid="why">{asked.at === "failed" ? asked.why : ""}</p>
      <p data-testid="unanswered">{unanswered ? "yes" : "no"}</p>
      <button type="button" onClick={again}>
        Again
      </button>
    </div>
  );
}

function visibility(state: "visible" | "hidden") {
  Object.defineProperty(document, "visibilityState", { value: state, configurable: true });
  document.dispatchEvent(new Event("visibilitychange"));
}

/** One turn of the timer, and everything it set going. */
async function tick(by = refreshEvery) {
  await act(async () => {
    vi.advanceTimersByTime(by);
    await Promise.resolve();
  });
}

describe("asking the instance again by itself", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    visibility("visible");
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("asks once and then keeps asking", async () => {
    const { asked } = anInstance({ "GET /api/version": { body: { version: "1.0.0" } } });

    render(<AScreen />);

    await act(async () => undefined);
    expect(screen.getByTestId("value")).toHaveTextContent("1.0.0");
    expect(asked).toHaveLength(1);

    await tick();
    expect(asked).toHaveLength(2);

    await tick();
    expect(asked).toHaveLength(3);
  });

  it("brings a change made somewhere else onto the screen without a reload", async () => {
    anInstance({
      "GET /api/version": [{ body: { version: "1.0.0" } }, { body: { version: "1.1.0" } }],
    });

    render(<AScreen />);

    await act(async () => undefined);
    expect(screen.getByTestId("value")).toHaveTextContent("1.0.0");

    await tick();
    expect(screen.getByTestId("value")).toHaveTextContent("1.1.0");
  });

  it("never blanks the screen while it is asking again, and does when asked on purpose", async () => {
    anInstance({
      "GET /api/version": [
        { body: { version: "1.0.0" } },
        { body: { version: "1.1.0" } },
        { body: { version: "1.2.0" } },
      ],
    });

    const seen: string[] = [];
    render(<AScreen onState={(state) => seen.push(state)} />);
    await act(async () => undefined);

    await tick();
    await tick();

    // Asked twice more, and the screen was never empty in between: a spinner
    // every fifteen seconds is how a refreshing screen becomes unreadable.
    expect(screen.getByTestId("value")).toHaveTextContent("1.2.0");
    expect(seen.filter((state) => state === "asking")).toHaveLength(1);

    // Asking on purpose does say so: what it replaces may have been an answer,
    // and leaving it standing would show something nobody is claiming.
    await act(async () => {
      screen.getByRole("button", { name: "Again" }).click();
    });

    expect(seen.filter((state) => state === "asking")).toHaveLength(2);
  });

  it("keeps the last answer when a refresh fails, and says it is unanswered", async () => {
    anInstance({
      "GET /api/version": [
        { body: { version: "1.0.0" } },
        refused("internal", 500, "Something went wrong."),
      ],
    });

    render(<AScreen />);
    await act(async () => undefined);

    await tick();

    expect(screen.getByTestId("state")).toHaveTextContent("known");
    expect(screen.getByTestId("value")).toHaveTextContent("1.0.0");
    expect(screen.getByTestId("unanswered")).toHaveTextContent("yes");
  });

  it("says what went wrong where there was no answer to keep", async () => {
    anInstance({ "GET /api/version": refused("internal", 500, "Something went wrong.") });

    render(<AScreen />);
    await act(async () => undefined);

    expect(screen.getByTestId("state")).toHaveTextContent("failed");
    expect(screen.getByTestId("why")).toHaveTextContent("Something went wrong.");
  });

  it("asks nothing while the tab is hidden, and once the moment it is looked at", async () => {
    const { asked } = anInstance({ "GET /api/version": { body: { version: "1.0.0" } } });

    render(<AScreen />);
    await act(async () => undefined);
    expect(asked).toHaveLength(1);

    visibility("hidden");
    await tick();
    await tick();
    expect(asked).toHaveLength(1);

    await act(async () => {
      visibility("visible");
      await Promise.resolve();
    });

    expect(asked).toHaveLength(2);
  });

  it("asks nothing at all while the screen is holding unsaved work", async () => {
    const { asked } = anInstance({ "GET /api/version": { body: { version: "1.0.0" } } });

    render(<AScreen hold />);
    await act(async () => undefined);
    expect(asked).toHaveLength(1);

    await tick();
    await tick();

    // What somebody is typing is the newest version of it; an answer arriving
    // underneath is what would take it away.
    expect(asked).toHaveLength(1);
  });

  it("forgets the answer to the old address when the screen walks on", async () => {
    anInstance({ "GET /api/version": { body: { version: "1.0.0" } } });

    const { rerender } = render(<AScreen at="/api/version?a" />);
    await act(async () => undefined);
    expect(screen.getByTestId("value")).toHaveTextContent("1.0.0");

    rerender(<AScreen at="/api/version?b" />);

    // Not the previous address's answer under the new one, even for a moment.
    expect(screen.getByTestId("state")).toHaveTextContent("asking");
  });
});
