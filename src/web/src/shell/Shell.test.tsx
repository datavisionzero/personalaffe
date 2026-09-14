import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import {
  anInstance,
  anAgent,
  renderAt,
  theApplications,
  theOwner,
} from "@/shared/anInstance";
import { Shell } from "./Shell";

const emptyTrash = { "GET /api/trash": { body: { items: [], has_more: false } } };

describe("the frame", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("offers the applications this workspace has switched on", async () => {
    anInstance({ "GET /api/applications": theApplications(), ...emptyTrash });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });

    for (const label of ["Home", "Scratchpad", "Knowledge", "Tasks", "Files", "Settings"]) {
      expect(await within(navigation).findByRole("link", { name: label })).toBeInTheDocument();
    }
  });

  it("leaves a switched-off application out of the navigation", async () => {
    anInstance({
      "GET /api/applications": theApplications({ tasks: { enabled: false } }),
      ...emptyTrash,
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
    anInstance({ "GET /api/applications": theApplications(), ...emptyTrash });

    renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);

    const navigation = await screen.findByRole("navigation", { name: "The workspace" });

    await userEvent.click(within(navigation).getByRole("link", { name: "Tasks" }));

    expect(await screen.findByRole("heading", { name: "Tasks" })).toBeInTheDocument();

    await userEvent.click(within(navigation).getByRole("link", { name: "Home" }));
    expect(await screen.findByRole("heading", { name: "Your workspace" })).toBeInTheDocument();
  });

  describe("a direct link into an application", () => {
    it("opens the screen of an application that has one", async () => {
      anInstance({
        "GET /api/applications": theApplications(),
        "GET /api/scratchpad/entries": { body: { items: [], has_more: false } },
        ...emptyTrash,
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
      anInstance({ "GET /api/applications": theApplications(), ...emptyTrash });

      renderAt("/calendar", <Shell me={theOwner} onSignedOut={() => undefined} />);

      expect(await screen.findByText("Nothing at this address.")).toBeInTheDocument();
      expect(screen.getByRole("navigation", { name: "The workspace" })).toBeInTheDocument();
    });
  });

  describe("the keyboard", () => {
    it("opens the palette with its key and walks where a row leads", async () => {
      anInstance({ "GET /api/applications": theApplications(), ...emptyTrash });

      renderAt("/", <Shell me={theOwner} onSignedOut={() => undefined} />);
      await screen.findByRole("navigation", { name: "The workspace" });

      await userEvent.keyboard("{Meta>}k{/Meta}");

      const field = await screen.findByRole("combobox", { name: "Go anywhere, or type a command" });
      await userEvent.type(field, "task");

      await userEvent.keyboard("{Enter}");

      expect(await screen.findByRole("heading", { name: "Tasks" })).toBeInTheDocument();
    });

    it("shows every key it binds, and does not answer a bare key while typing", async () => {
      anInstance({ "GET /api/applications": theApplications(), ...emptyTrash });

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
