import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  anAgent,
  anInstance,
  refused,
  renderAt,
  theApplications,
  theDashboard,
  theOwner,
  theWeather,
} from "@/shared/anInstance";
import { Shell } from "@/shell/Shell";

function settings(overrides: Record<string, unknown> = {}) {
  return anInstance({
    "GET /api/applications": theApplications(),
    "GET /api/dashboard": theDashboard(),
    "GET /api/weather": theWeather(),
    ...overrides,
  });
}

describe("what is on the home page", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("hides a tile with the version it read", async () => {
    const instance = settings({
      "PUT /api/dashboard/tiles/{tile}": { body: {} },
      "PUT /api/dashboard/tiles/files": {
        body: {
          tile: "files",
          shown: false,
          offered: true,
          updated_at: "2026-09-16T09:00:00.000000Z",
        },
      },
    });

    renderAt("/settings/home", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.click(await screen.findByRole("button", { name: "Hide Files" }));

    const written = await waitFor(() =>
      instance.asked.find((one) => one.method === "PUT" && one.path.startsWith("/api/dashboard")),
    );

    expect(written?.path).toBe("/api/dashboard/tiles/files");
    expect(written?.body).toEqual({ shown: false });

    // A write says which version it replaces (`docs/api.md`, The guarded
    // write), and this screen sends the one it drew.
    expect(written?.headers.get("If-Match")).toBe('"2026-01-01T00:00:00.000000Z"');
  });

  it("says what a stale write means rather than repeating the code", async () => {
    settings({
      "PUT /api/dashboard/tiles/files": refused("stale", 412, "It changed."),
    });

    renderAt("/settings/home", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.click(await screen.findByRole("button", { name: "Hide Files" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/changed somewhere else/);
  });

  it("shows an agent what is set and no buttons", async () => {
    settings();

    renderAt(
      "/settings/home",
      <Shell me={anAgent({ knowledge: "read" })} onSignedOut={() => undefined} />,
    );

    expect(await screen.findByText("Open tasks")).toBeInTheDocument();

    // Drawing a control that can only ever be refused is offering something
    // that is not on offer.
    expect(screen.queryByRole("button", { name: /^Hide / })).toBeNull();
    expect(screen.getByText(/the owner's alone/)).toBeInTheDocument();
  });

  it("says a tile is kept even while nothing is offering it", async () => {
    settings({
      "GET /api/dashboard": theDashboard({ tiles: { files: { offered: false } } }),
    });

    renderAt("/settings/home", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText(/What you set here is kept either way/)).toBeInTheDocument();
  });
});

describe("where the weather is for", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("looks a name up and stores the point behind it", async () => {
    const instance = settings({
      "GET /api/weather/places": {
        body: {
          items: [
            {
              name: "Wuppertal",
              region: "North Rhine-Westphalia",
              country: "Germany",
              latitude: 51.2563,
              longitude: 7.1482,
            },
          ],
        },
      },
      "PUT /api/weather/place": theWeather(),
    });

    renderAt("/settings/home", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.type(
      await screen.findByRole("searchbox", { name: /Look a place up/ }),
      "Wuppertal",
    );
    await userEvent.click(screen.getByRole("button", { name: "Look it up" }));

    await userEvent.click(await screen.findByRole("button", { name: /Wuppertal/ }));

    const written = await waitFor(() =>
      instance.asked.find((one) => one.method === "PUT" && one.path === "/api/weather/place"),
    );

    // The geocoder is asked once, here. What is kept is the two numbers, so a
    // tile never depends on a second service.
    expect(written?.body).toEqual({
      name: "Wuppertal, Germany",
      latitude: 51.2563,
      longitude: 7.1482,
      units: "metric",
    });
  });

  it("clears it with everything empty", async () => {
    const instance = settings({ "PUT /api/weather/place": theWeather({ place: null }) });

    renderAt("/settings/home", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await userEvent.click(await screen.findByRole("button", { name: "Clear it" }));

    const written = await waitFor(() =>
      instance.asked.find((one) => one.method === "PUT" && one.path === "/api/weather/place"),
    );

    expect(written?.body).toEqual({
      name: null,
      latitude: null,
      longitude: null,
      units: "metric",
    });
  });

  it("says so when this instance does not ask anybody", async () => {
    settings({ "GET /api/weather": theWeather({ available: false, reading: null }) });

    renderAt("/settings/home", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(
      await screen.findByText(/configured not to ask anybody about the weather/),
    ).toBeInTheDocument();
  });
});

describe("the settings navigation", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("has the home page beside the applications", async () => {
    settings();

    renderAt("/settings/applications", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const areas = await screen.findByRole("navigation", { name: "Settings" });

    expect(within(areas).getByRole("link", { name: "Home page" })).toHaveAttribute(
      "href",
      "/settings/home",
    );
  });
});
