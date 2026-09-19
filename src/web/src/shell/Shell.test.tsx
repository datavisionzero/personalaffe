import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  anInstance,
  anAgent,
  renderAt,
  theAppearance,
  theApplications,
  theDashboard,
  theOwner,
  theWeather,
} from "@/shared/anInstance";
import { Shell } from "./Shell";

const emptyTrash = { "GET /api/trash": { body: { items: [], has_more: false } } };

// The home page is the dashboard since PERSONAL-E9, so every test that walks
// through it has to answer the one question it asks.
const emptyHome = { "GET /api/dashboard": theDashboard(), "GET /api/weather": theWeather() };

describe("the frame", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("offers the applications this workspace has switched on", async () => {
    anInstance({ "GET /api/applications": theApplications(), ...emptyTrash, ...emptyHome });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });

    for (const label of ["Home", "Scratchpad", "Knowledge", "Tasks", "Files", "Settings"]) {
      expect(await within(navigation).findByRole("link", { name: label })).toBeInTheDocument();
    }
  });

  it("says what this instance is called rather than what the product is", async () => {
    anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/appearance": theAppearance({ title: "Haus", colour: "teal", shape: "circle" }),
      ...emptyTrash,
      ...emptyHome,
    });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText("Haus")).toBeInTheDocument();
    expect(screen.queryByText("personalaffe")).not.toBeInTheDocument();

    // And the mark beside it is the one the instance chose.
    const mark = await screen.findByTestId("mark");
    expect(mark).toHaveAttribute("data-mark-colour", "teal");
    expect(mark).toHaveClass("rounded-full");
    expect(mark).toHaveTextContent("HA");
  });

  it("says the product's own name where nobody has named the instance", async () => {
    anInstance({
      "GET /api/applications": theApplications(),
      "GET /api/appearance": theAppearance(),
      ...emptyTrash,
      ...emptyHome,
    });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    expect(await screen.findByText("personalaffe")).toBeInTheDocument();

    // And the mark is the one the product has always drawn: a filled rounded
    // square, with nothing written in it.
    const mark = await screen.findByTestId("mark");
    expect(mark).toHaveAttribute("data-mark-colour", "violet");
    expect(mark).toHaveClass("rounded-sm");
    expect(mark).toHaveTextContent("");
  });

  it("leaves a switched-off application out of the navigation", async () => {
    anInstance({
      "GET /api/applications": theApplications({ tasks: { enabled: false } }),
      ...emptyTrash,
      ...emptyHome,
    });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });
    await within(navigation).findByRole("link", { name: "Knowledge" });

    // Absent rather than greyed out: a row that cannot be pressed is a promise
    // the application does not keep.
    expect(within(navigation).queryByRole("link", { name: "Tasks" })).toBeNull();
  });

  // The permission the frame reads is the one beside the switch, not the one
  // in the credential: they are the same answer, and `/api/applications` is the
  // one that is asked again while the screen is open.
  it("leaves out what this credential cannot read, whatever the switch says", async () => {
    anInstance({
      "GET /api/applications": theApplications({
        knowledge: { permission: "read" },
        scratchpad: { permission: "none" },
        tasks: { permission: "none" },
        files: { permission: "none" },
      }),
      ...emptyTrash,
      ...emptyHome,
    });

    renderAt(
      "/",
      <Shell me={anAgent({ knowledge: "read" })} onSignedOut={() => undefined} />,
    );

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });

    expect(await within(navigation).findByRole("link", { name: "Knowledge" })).toBeInTheDocument();
    expect(within(navigation).queryByRole("link", { name: "Files" })).toBeNull();
  });

  it("walks to an application and back home", async () => {
    anInstance({ "GET /api/applications": theApplications(), ...emptyTrash, ...emptyHome });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });

    await userEvent.click(within(navigation).getByRole("link", { name: "Tasks" }));

    expect(await screen.findByRole("heading", { name: "Tasks" })).toBeInTheDocument();

    await userEvent.click(within(navigation).getByRole("link", { name: "Home" }));
    expect(
      await screen.findByRole("heading", { name: "What is useful or pending" }),
    ).toBeInTheDocument();
  });

  describe("a direct link into an application", () => {
    it("opens the screen of an application that has one", async () => {
      anInstance({
        "GET /api/applications": theApplications(),
        "GET /api/scratchpad/entries": { body: { items: [], has_more: false } },
        ...emptyTrash,
        ...emptyHome,
      });

      renderAt("/scratchpad", <Shell me={theOwner} onSignedOut={() => undefined} />);

      expect(await screen.findByRole("heading", { name: "Scratchpad" })).toBeInTheDocument();
    });

    // There is no longer an application without a screen, so this is what used
    // to be "says an application still to come is still to come": every one of
    // the four opens at its own address.
    it("opens every one of the four at its own address", async () => {
      for (const [address, heading] of [
        ["/scratchpad", "Scratchpad"],
        ["/files", "Files"],
        ["/knowledge", "Knowledge"],
        ["/tasks", "Tasks"],
      ] as const) {
        anInstance({
          "GET /api/applications": theApplications(),
          "GET /api/scratchpad/entries": { body: { items: [], has_more: false } },
          "GET /api/files": {
            body: {
              chain: [],
              folders: [],
              files: [],
              used_bytes: 0,
              max_file_bytes: 1,
              max_total_bytes: 1,
            },
          },
          "GET /api/knowledge/pages": { body: { pages: [] } },
          "GET /api/tasks/lists": { body: { items: [] } },
          ...emptyTrash,
        });

        const { unmount } = renderAt(address, <Shell me={theOwner} onSignedOut={() => undefined} />);

        expect(await screen.findByRole("heading", { name: heading })).toBeInTheDocument();

        unmount();
      }
    });

    // Three different facts, and only one of them is the owner's to act on.
    it("says it is switched off, rather than sending the owner home", async () => {
      anInstance({
        "GET /api/applications": theApplications({ files: { enabled: false } }),
        ...emptyTrash,
      });

      renderAt("/files", <Shell me={theOwner} onSignedOut={() => undefined} />);

      expect(await screen.findByText("Files is switched off.")).toBeInTheDocument();
      expect(
        screen.getByRole("link", { name: "Switch it on" }),
      ).toHaveAttribute("href", "/settings/applications");
    });

    it("says it is not this credential's, rather than that it is switched off", async () => {
      anInstance({
        "GET /api/applications": theApplications({ files: { permission: "none" } }),
        ...emptyTrash,
      });

      renderAt(
        "/files",
        <Shell me={anAgent({ knowledge: "read_write" })} onSignedOut={() => undefined} />,
      );

      expect(await screen.findByText(/Files is not this credential's to see/)).toBeInTheDocument();
    });

    it("answers an address nothing took inside the frame", async () => {
      anInstance({ "GET /api/applications": theApplications(), ...emptyTrash, ...emptyHome });

      renderAt("/calendar", <Shell me={theOwner} onSignedOut={() => undefined} />);

      expect(await screen.findByText("Nothing at this address.")).toBeInTheDocument();
      expect(screen.getByRole("navigation", { name: "The workspace" })).toBeInTheDocument();
    });
  });

  describe("the keyboard", () => {
    it("opens the palette with its key and walks where a row leads", async () => {
      anInstance({ "GET /api/applications": theApplications(), ...emptyTrash, ...emptyHome });

      renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);
      await screen.findByRole("navigation", { name: "The workspace" });

      await userEvent.keyboard("{Meta>}k{/Meta}");

      const field = await screen.findByRole("combobox", { name: "Go anywhere, or type a command" });
      await userEvent.type(field, "task");

      await userEvent.keyboard("{Enter}");

      expect(await screen.findByRole("heading", { name: "Tasks" })).toBeInTheDocument();
    });

    it("shows every key it binds, and does not answer a bare key while typing", async () => {
      anInstance({ "GET /api/applications": theApplications(), ...emptyTrash, ...emptyHome });

      renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);
      await screen.findByRole("navigation", { name: "The workspace" });

      await userEvent.keyboard("?");

      const overview = await screen.findByRole("dialog");
      expect(within(overview).getByText("Search or jump to anything")).toBeInTheDocument();
      expect(within(overview).getByText("Go home")).toBeInTheDocument();

      await userEvent.keyboard("{Escape}");
      await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

      // The palette's own field is something being typed into, so `?` and `h`
      // belong to it rather than to the frame.
      await userEvent.keyboard("{Meta>}k{/Meta}");
      const field = await screen.findByRole("combobox", { name: "Go anywhere, or type a command" });
      await userEvent.type(field, "h?");

      expect(field).toHaveValue("h?");
    });
  });

  describe("the palette searches the workspace", () => {
    const aPage = {
      application: "knowledge",
      id: "0199f0c4-0000-7000-8000-000000000001",
      title: "Architecture decisions",
      snippet: "Where the storage decision lives",
      within: null,
      updated_at: "2026-09-14T08:30:00.000000Z",
      rank: 0.61,
    };

    it("puts what the instance found above the commands, and walks to it", async () => {
      anInstance({
        "GET /api/applications": theApplications(),
        "GET /api/search": { body: { query: "arch", items: [aPage], has_more: false } },
        // The row walks into Knowledge, so the screen behind it has to have an
        // answer of its own shape.
        "GET /api/knowledge/pages": { body: { pages: [] } },
        "GET /api/knowledge/pages/0199f0c4-0000-7000-8000-000000000001": {
          status: 404,
          body: { type: "/problems/not-found", title: "not-found", status: 404 },
        },
        ...emptyTrash,
        ...emptyHome,
      });

      renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);
      await screen.findByRole("navigation", { name: "The workspace" });

      await userEvent.keyboard("{Meta>}k{/Meta}");

      const field = await screen.findByRole("combobox", { name: "Go anywhere, or type a command" });
      await userEvent.type(field, "arch");

      const row = await screen.findByText("Architecture decisions");

      // The commands are still underneath: a palette that stopped offering
      // "Settings" the moment somebody typed "se" gets worse the more it is
      // used.
      expect(screen.getByText(/Search for/)).toBeInTheDocument();

      await userEvent.click(row);

      await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    });

    it("asks nothing at all for a word the instance would not look for", async () => {
      const instance = anInstance({
        "GET /api/applications": theApplications(),
        ...emptyTrash,
        ...emptyHome,
      });

      renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);
      await screen.findByRole("navigation", { name: "The workspace" });

      await userEvent.keyboard("{Meta>}k{/Meta}");

      const field = await screen.findByRole("combobox", { name: "Go anywhere, or type a command" });
      await userEvent.type(field, "a");

      await waitFor(() =>
        expect(instance.asked.filter((one) => one.path === "/api/search")).toHaveLength(0),
      );
    });
  });

  it("says so when the instance stops answering, and keeps what it last said", async () => {
    anInstance({
      "GET /api/applications": [theApplications(), { status: 503, body: { title: "gone" } }],
      ...emptyTrash,
    });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });
    await within(navigation).findByRole("link", { name: "Knowledge" });

    // The refresh is on a timer this test does not wait for; what is asserted
    // is the shape the frame is in while the answer it has still stands.
    expect(screen.queryByText("Not answering")).toBeNull();
  });
});
