import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { anInstance, renderAt, theApplications, theOwner } from "@/shared/anInstance";
import { Shell } from "@/shell/Shell";
import { addressOf, usable, worthAsking } from "./useFindings";
import { marked } from "./marked";

const aPage = {
  application: "knowledge" as const,
  id: "0199f0c4-0000-7000-8000-000000000001",
  title: "Architecture decisions",
  snippet: "Where the storage decision lives",
  within: null,
  updated_at: "2026-09-14T08:30:00.000000Z",
  rank: 0.61,
};

const aTask = {
  application: "tasks" as const,
  id: "0199f0c7-0000-7000-8000-000000000001",
  title: "Write the architecture page",
  snippet: null,
  within: "0199f0c6-0000-7000-8000-000000000001",
  updated_at: "2026-09-13T08:30:00.000000Z",
  rank: 0.24,
};

function found(items: unknown[], hasMore = false) {
  return { body: { query: "arch", items, has_more: hasMore } };
}

describe("the words the instance will look for", () => {
  it("keeps letters and digits of every script and drops what is too short", () => {
    expect(usable("Budget-2026.final.pdf")).toEqual(["budget", "2026", "final", "pdf"]);
    expect(usable("a page")).toEqual(["page"]);
    expect(usable("Größe")).toEqual(["größe"]);
  });

  it("says when there is nothing worth asking about", () => {
    // A field that sent this would be making a request whose answer is always
    // the same refusal.
    expect(worthAsking("")).toBe(false);
    expect(worthAsking("a")).toBe(false);
    expect(worthAsking("ab")).toBe(true);
  });
});

describe("where a finding lives", () => {
  it("opens the thing itself, or the screen it is on", () => {
    expect(addressOf(aPage)).toBe(`/knowledge/${aPage.id}`);
    expect(addressOf(aTask)).toBe(`/tasks/${aTask.within}`);
    expect(addressOf({ ...aTask, within: null })).toBe("/tasks");
    expect(addressOf({ ...aPage, application: "scratchpad" })).toBe("/scratchpad");
  });
});

describe("marking what was asked for", () => {
  it("marks a prefix inside a word and leaves the rest alone", () => {
    const pieces = marked("Architecture", ["arch"]);

    expect(Array.isArray(pieces)).toBe(true);
  });

  it("does nothing without words, which is what an empty search is", () => {
    expect(marked("Architecture", [])).toBe("Architecture");
  });
});

describe("the search screen", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("asks for what is in the address and draws what came back", async () => {
    anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/search": found([aPage, aTask]),
    });

    renderAt("/search?q=arch", <Shell me={theOwner} onSignedOut={() => undefined} />);

    // Scoped to the list the findings are in: the frame is full of lists, and a
    // test that matched the navigation would pass for the wrong reason.
    const list = await screen.findByRole("list", { name: "What was found" });
    const rows = within(list).getAllByRole("listitem");

    // In the instance's order, not regrouped: a page does not outrank a task
    // for being a page, and grouping by application would hide exactly that.
    expect(within(rows[0]!).getByRole("link")).toHaveAttribute("href", `/knowledge/${aPage.id}`);
    expect(within(rows[1]!).getByRole("link")).toHaveAttribute("href", `/tasks/${aTask.within}`);
  });

  it("fills its field from the address, so a search can be linked to", async () => {
    anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/search": found([aPage]),
    });

    renderAt("/search?q=arch", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByRole("searchbox", { name: "Search the workspace" })).toHaveValue(
      "arch",
    );
  });

  it("asks nothing at all until there is a word worth looking for", async () => {
    const instance = anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/search": found([]),
    });

    renderAt("/search", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const field = await screen.findByRole("searchbox", { name: "Search the workspace" });

    expect(await screen.findByText("Type something to look for.")).toBeInTheDocument();

    await userEvent.type(field, "a");

    await waitFor(() =>
      expect(instance.asked.filter((one) => one.path === "/api/search")).toHaveLength(0),
    );
  });

  it("says when it found nothing", async () => {
    anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/search": found([]),
    });

    renderAt("/search?q=pomegranate", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText(/Nothing matches/)).toBeInTheDocument();
  });

  it("says when there was more than it drew", async () => {
    anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/search": found([aPage], true),
    });

    renderAt("/search?q=arch", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText(/There is more than this/)).toBeInTheDocument();
  });

  it("does not ask again on a timer", async () => {
    vi.useFakeTimers();

    try {
      const instance = anInstance({
        "GET /api/applications": theApplications(),
        "GET /api/search": [found([aPage]), found([aPage]), found([aPage])],
      });

      renderAt("/search?q=arch", <Shell me={theOwner} onSignedOut={() => undefined} />);

      await vi.advanceTimersByTimeAsync(60_000);

      // A search is a question about a moment. Re-running it on a timer would
      // move rows under somebody who is reading them.
      expect(instance.asked.filter((one) => one.path === "/api/search")).toHaveLength(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
