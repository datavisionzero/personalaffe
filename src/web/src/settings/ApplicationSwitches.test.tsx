import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Route, Routes } from "react-router";

import { anInstance, anAgent, renderAt, theApplications, theOwner } from "@/shared/anInstance";
import { Settings } from "./Settings";
import { useApplications } from "@/shell/useApplications";

/**
 * The screen as the frame mounts it: under `/settings/*`, so that its own
 * routes see the rest of the address and not the whole of it, and with the
 * frame's one question asked the way the frame asks it.
 */
function TheSettings({ me = theOwner }: { me?: typeof theOwner }) {
  const applications = useApplications();

  return (
    <Routes>
      <Route path="/settings/*" element={<Settings me={me} applications={applications} />} />
    </Routes>
  );
}

describe("the application switches", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("shows the four and which are on", async () => {
    anInstance({
      "GET /api/applications": theApplications({ tasks: { enabled: false } }),
    });

    renderAt("/settings/applications", <TheSettings />);

    const tasks = await rowFor("Tasks");
    expect(within(tasks).getByText("Off")).toBeInTheDocument();
    expect(within(await rowFor("Files")).getByText("On")).toBeInTheDocument();
  });

  it("switches one off, sending the version the screen read", async () => {
    const { asked } = anInstance({
      "GET /api/applications": [
        theApplications(),
        theApplications({ files: { enabled: false } }),
      ],
      "PUT /api/applications/files": {
        body: {
          application: "files",
          enabled: false,
          permission: "read_write",
          updated_at: "2026-09-14T16:00:00.000000Z",
        },
      },
    });

    renderAt("/settings/applications", <TheSettings />);

    await userEvent.click(await screen.findByRole("button", { name: "Switch off Files" }));

    expect(within(await rowFor("Files")).getByText("Off")).toBeInTheDocument();

    const write = asked.find((request) => request.method === "PUT");
    expect(write?.body).toEqual({ enabled: false });
    // The guard, from the read the screen did: a switch made from a stale
    // screen is refused rather than applied.
    expect(write?.headers.get("If-Match")).toBe('"2026-09-14T15:30:08.000000Z"');
  });

  it("says so when somebody switched it somewhere else first, and reads it again", async () => {
    anInstance({
      "GET /api/applications": [
        theApplications(),
        theApplications({ knowledge: { enabled: false } }),
      ],
      "PUT /api/applications/knowledge": {
        status: 412,
        body: { type: "/problems/stale", title: "stale", status: 412 },
      },
    });

    renderAt("/settings/applications", <TheSettings />);

    await userEvent.click(await screen.findByRole("button", { name: "Switch off Knowledge" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("changed somewhere else");
    expect(within(await rowFor("Knowledge")).getByText("Off")).toBeInTheDocument();
  });

  it("shows an agent the switches and no buttons", async () => {
    anInstance({ "GET /api/applications": theApplications() });

    renderAt("/settings/applications", <TheSettings me={anAgent({ knowledge: "read_write" })} />);

    expect(within(await rowFor("Knowledge")).getByText("On")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Switch/ })).toBeNull();
    expect(screen.getByText(/the owner's alone/)).toBeInTheDocument();
  });

  it("keeps security and agent access away from an agent", async () => {
    anInstance({ "GET /api/applications": theApplications() });

    renderAt("/settings/security", <TheSettings me={anAgent()} />);

    expect(await screen.findByText(/Security is not this credential's to see/)).toBeInTheDocument();
  });
});

async function rowFor(label: string) {
  const heading = await screen.findByText(label);

  return heading.closest("li")!;
}
