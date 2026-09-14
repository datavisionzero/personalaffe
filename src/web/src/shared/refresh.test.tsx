import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { api } from "@/api/client";
import { anInstance } from "@/shared/anInstance";
import { refreshEvery, useAsk } from "./ask";
import { MarkdownField } from "./MarkdownField";

/**
 * A screen of the shape every content screen will be: something read from the
 * instance, refreshing by itself, with a field somebody is writing in.
 *
 * <b>This is the acceptance criterion the refresh is dangerous for.</b>
 * PERSONAL-E4 asks that changes made elsewhere appear without a reload
 * <em>and</em> that unsaved edits survive it, and those two pull in opposite
 * directions: the cheap way to show the new version is to put it on the screen,
 * over whatever the person at the keyboard had written.
 */
function AWritingScreen() {
  const [draft, setDraft] = useState<string>();

  const { asked } = useAsk(
    "/api/version",
    (signal) => api.GET("/api/version", { signal }),
    // What is being typed is the newest version of it. Nothing is asked while
    // it is unsaved, and what came back before it is not written over it.
    { hold: draft !== undefined },
  );

  const stored = asked.at === "known" ? asked.value.version : "";

  return (
    <div>
      <p data-testid="stored">{stored}</p>
      <MarkdownField label="The text" value={draft ?? stored} onChange={setDraft} />
      <button type="button" onClick={() => setDraft(undefined)}>
        Abandon it
      </button>
    </div>
  );
}

/**
 * The screen, drawn, with the instance's first answer on it.
 *
 * Two things have to have happened before a test can touch anything.
 * `MarkdownField` loads its editor as a chunk of its own — that is what keeps
 * the Scratchpad from downloading CodeMirror — and a lazy component resolves
 * over however many microtasks its import takes, which under fake timers is not
 * reliably one. And the first answer has to have arrived, or typing into the
 * field races the version landing in it.
 *
 * Waiting for both is the difference between a suite that passes and one that
 * passes most of the time.
 */
async function aScreenShowing(version: string) {
  render(<AWritingScreen />);

  const field = await screen.findByRole("textbox", { name: "The text" });

  await waitFor(() => expect(screen.getByTestId("stored")).toHaveTextContent(version));

  return field;
}

describe("a refresh under somebody's hands", () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    Object.defineProperty(document, "visibilityState", { value: "visible", configurable: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("brings what changed elsewhere onto a screen nobody is writing on", async () => {
    anInstance({ "GET /api/version": [{ body: { version: "one" } }, { body: { version: "two" } }] });

    await aScreenShowing("one");

    await act(async () => {
      vi.advanceTimersByTime(refreshEvery);
      await Promise.resolve();
    });

    expect(screen.getByTestId("stored")).toHaveTextContent("two");
    expect(screen.getByRole("textbox", { name: "The text" })).toHaveValue("two");
  });

  it("leaves what somebody is writing exactly where it was", async () => {
    const { asked } = anInstance({
      "GET /api/version": [{ body: { version: "one" } }, { body: { version: "two" } }],
    });

    const field = await aScreenShowing("one");

    await userEvent.clear(field);
    await userEvent.type(field, "what I was in the middle of");

    await act(async () => {
      vi.advanceTimersByTime(refreshEvery * 3);
      await Promise.resolve();
    });

    // Nothing was asked while it was held, and nothing was written over.
    expect(asked).toHaveLength(1);
    expect(field).toHaveValue("what I was in the middle of");
  });

  it("starts asking again the moment there is nothing unsaved", async () => {
    const { asked } = anInstance({
      "GET /api/version": [{ body: { version: "one" } }, { body: { version: "two" } }],
    });

    await userEvent.type(await aScreenShowing("one"), "!");
    await act(async () => {
      vi.advanceTimersByTime(refreshEvery);
      await Promise.resolve();
    });
    expect(asked).toHaveLength(1);

    await userEvent.click(screen.getByRole("button", { name: "Abandon it" }));
    await act(async () => {
      vi.advanceTimersByTime(refreshEvery);
      await Promise.resolve();
    });

    expect(asked).toHaveLength(2);
    expect(screen.getByTestId("stored")).toHaveTextContent("two");
  });
});
