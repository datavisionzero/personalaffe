import { screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  anInstance,
  renderAt,
  theApplications,
  theDashboard,
  theOwner,
  theWeather,
} from "@/shared/anInstance";
import { Shell } from "@/shell/Shell";

const aTask = {
  id: "0199f0c7-0000-7000-8000-000000000001",
  title: "Milch holen",
  list_id: "0199f0c6-0000-7000-8000-000000000001",
  list: "Einkauf",
  due_on: "2020-01-01",
  updated_at: "2026-09-14T08:30:00.000000Z",
};

const aPage = {
  id: "0199f0c4-0000-7000-8000-000000000001",
  title: "Architecture decisions",
  parent_id: null,
  updated_at: "2026-09-14T08:30:00.000000Z",
};

function home(dashboard = theDashboard(), weather = theWeather()) {
  return anInstance({
    "GET /api/applications": theApplications(),
    "GET /api/dashboard": dashboard,
    "GET /api/weather": weather,
  });
}

describe("the home page", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("draws a tile for every section the instance sent", async () => {
    home(theDashboard({ tasks: [aTask], knowledge: [aPage] }));

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const tasks = await screen.findByRole("region", { name: "Tasks" });

    expect(within(tasks).getByRole("link", { name: /Milch holen/ })).toHaveAttribute(
      "href",
      `/tasks/${aTask.list_id}`,
    );

    // The list is beside the task, because a task on the home page is out of
    // its list.
    expect(within(tasks).getByText("Einkauf")).toBeInTheDocument();

    const knowledge = await screen.findByRole("region", { name: "Knowledge" });
    expect(within(knowledge).getByRole("link", { name: /Architecture decisions/ })).toHaveAttribute(
      "href",
      `/knowledge/${aPage.id}`,
    );
  });

  it("tells a drawn tile that is empty from a tile that is not drawn", async () => {
    // `[]` against `null` — the instance's two different answers, and the two a
    // person has to be able to tell apart: "there is nothing in it" against "I
    // hid that".
    home(theDashboard({ tiles: { knowledge: { shown: false } }, knowledge: null }));

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const tasks = await screen.findByRole("region", { name: "Tasks" });
    expect(within(tasks).getByText("Nothing is open.")).toBeInTheDocument();

    expect(screen.queryByRole("region", { name: "Knowledge" })).toBeNull();
  });

  it("says so when every tile is hidden", async () => {
    home(
      theDashboard({
        tiles: {
          tasks: { shown: false },
          knowledge: { shown: false },
          scratchpad: { shown: false },
          files: { shown: false },
          weather: { shown: false },
        },
        tasks: null,
        knowledge: null,
        scratchpad: null,
        files: null,
      }),
    );

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText("Nothing is on this home page.")).toBeInTheDocument();
  });

  it("marks a task that is due", async () => {
    home(theDashboard({ tasks: [aTask] }));

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    // Two strings compared and never two dates, so the answer is the same
    // wherever the browser is standing.
    expect(await screen.findByText(/2020-01-01/)).toBeInTheDocument();
    expect(await screen.findByText(/due/)).toBeInTheDocument();
  });

  it("asks for the weather separately, so the tiles never wait on it", async () => {
    const instance = home(theDashboard({ tasks: [aTask] }));

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    await screen.findByRole("region", { name: "Tasks" });
    expect(await screen.findByText("Overcast")).toBeInTheDocument();

    // Two documents, two requests. One request for both would be a home page
    // that cannot finish drawing until a provider somewhere else has answered.
    expect(instance.asked.filter((one) => one.path === "/api/dashboard")).toHaveLength(1);
    expect(instance.asked.filter((one) => one.path === "/api/weather")).toHaveLength(1);
  });

  it("credits whoever said what the weather is", async () => {
    home();

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText(/Weather data by a test/)).toBeInTheDocument();
  });

  it("says what is true when there is nowhere to look at", async () => {
    home(theDashboard(), theWeather({ place: null, latitude: null, longitude: null, reading: null }));

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText(/Nowhere is set/)).toBeInTheDocument();
  });

  it("does not treat a provider that said nothing as a failure", async () => {
    home(theDashboard(), theWeather({ reading: null }));

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText(/The provider did not answer/)).toBeInTheDocument();

    // Not an alert, and nothing else on the page is affected.
    expect(screen.queryByRole("alert")).toBeNull();
    expect(await screen.findByRole("region", { name: "Tasks" })).toBeInTheDocument();
  });
});
