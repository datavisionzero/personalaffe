import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { Route, Routes } from "react-router";

import { Tasks } from "@/tasks/Tasks";
import { todayHere } from "@/tasks/today";
import { anInstance, refused, renderAt } from "@/shared/anInstance";

const einkauf = {
  id: "0199f0c7-0000-7000-8000-00000000000a",
  name: "Einkauf",
  open: 1,
  all: 2,
  created_at: "2026-09-14T08:00:00.000000Z",
  updated_at: "2026-09-14T08:00:00.000000Z",
};

const milch = {
  id: "0199f0c7-0000-7000-8000-00000000000b",
  list: einkauf.id,
  title: "Milch holen",
  description: "am Markt",
  due_on: null as string | null,
  completed: false,
  completed_at: null as string | null,
  after: null as string | null,
  created_at: "2026-09-14T08:10:00.000000Z",
  updated_at: "2026-09-14T08:10:00.000000Z",
};

const brot = {
  ...milch,
  id: "0199f0c7-0000-7000-8000-00000000000c",
  title: "Brot holen",
  description: "",
  completed: true,
  completed_at: "2026-09-14T09:00:00.000000Z",
  after: milch.id,
};

const kaese = {
  ...milch,
  id: "0199f0c7-0000-7000-8000-00000000000e",
  title: "Käse holen",
  description: "",
  after: brot.id,
};

/** One thing in the Trash, as `GET /api/trash` answers it. */
function theEntry(application: string, id: string, name: string, updated_at = "2026-09-14T10:00:00.000000Z") {
  return {
    application,
    id,
    name,
    where: null,
    deleted_at: updated_at,
    deleted_by: { kind: "owner", name: null },
    expires_at: "2026-10-14T10:00:00.000000Z",
    updated_at,
  };
}

function theLists(...items: unknown[]) {
  return { body: { items } };
}

function theTasks(...items: unknown[]) {
  return { body: { items } };
}

/** The screen at its real address: the route is `/tasks/*` in the frame. */
function tasksAt(path: string) {
  return renderAt(
    path,
    <Routes>
      <Route path="/tasks/*" element={<Tasks />} />
    </Routes>,
  );
}

describe("Tasks", () => {
  it("says there are no lists rather than drawing a blank page", async () => {
    anInstance({ "GET /api/tasks/lists": theLists() });

    tasksAt("/tasks");

    expect(await screen.findByText(/No lists yet/)).toBeInTheDocument();
  });

  it("lists the lists with how much is open in each", async () => {
    anInstance({ "GET /api/tasks/lists": theLists(einkauf) });

    tasksAt("/tasks");

    const nav = within(await screen.findByRole("navigation", { name: "The lists" }));
    const link = nav.getByRole("link", { name: /Einkauf/ });

    expect(link).toHaveAttribute("href", `/tasks/${einkauf.id}`);
    expect(link).toHaveTextContent("1");
  });

  it("puts open tasks above completed ones", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch, brot),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    expect(await screen.findByText("Milch holen")).toBeInTheDocument();

    // Completed ones are struck through and below, not hidden: hiding them
    // would make ticking a box feel like deleting.
    expect(screen.getByRole("heading", { name: "Done" })).toBeInTheDocument();
    expect(screen.getByText("Brot holen")).toHaveClass("line-through");
  });

  it("captures with one field and Enter, and keeps what was typed if it is refused", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(),
      [`POST /api/tasks/lists/${einkauf.id}/tasks`]: [
        refused("validation", 400, "A task is something, and something has a name."),
        { status: 201, body: milch },
      ],
    });

    tasksAt(`/tasks/${einkauf.id}`);

    const box = await screen.findByLabelText("What has to be done");

    await userEvent.type(box, "Milch holen{Enter}");

    // A box emptied optimistically is a box that loses what somebody typed.
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(box).toHaveValue("Milch holen");

    await userEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(box).toHaveValue(""));

    const captured = asked.filter((one) => one.method === "POST");

    expect(captured).toHaveLength(2);
    expect(captured[1]?.body).toEqual({ title: "Milch holen", description: "", due_on: null });
  });

  it("completes and reopens with the box, in one write that carries the whole task", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`PUT /api/tasks/${milch.id}`]: { body: { ...milch, completed: true } },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("checkbox", { name: "Complete Milch holen" }));

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(write.body).toEqual({
      list: einkauf.id,
      title: "Milch holen",
      description: "am Markt",
      due_on: null,
      completed: true,
      after: null,
    });

    expect(write.headers.get("If-Match")).toBe(`"${milch.updated_at}"`);
  });

  it("moves a task by a step, sending the neighbour it lands behind", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(
        milch,
        { ...brot, completed: false, completed_at: null },
      ),
      [`PUT /api/tasks/${brot.id}`]: { body: brot },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Move Brot holen up" }));

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    // The top of the list is nothing, not a number.
    expect((write.body as { after: unknown }).after).toBeNull();
  });

  it("moves a step among the open ones, past a completed one nobody can see between them", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      // Milch, then Brot which is done, then Käse: on the screen Käse is
      // directly under Milch.
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch, brot, kaese),
      [`PUT /api/tasks/${kaese.id}`]: { body: kaese },
      [`PUT /api/tasks/${milch.id}`]: { body: milch },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Move Käse holen up" }));

    const up = await waitFor(() => {
      const one = asked.find((request) => request.path === `/api/tasks/${kaese.id}`);
      expect(one).toBeDefined();
      return one!;
    });

    // Above Milch, which is the top. Behind Milch would be above Brot only,
    // and nothing on the screen would have moved.
    expect((up.body as { after: unknown }).after).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Move Milch holen down" }));

    const down = await waitFor(() => {
      const one = asked.find((request) => request.path === `/api/tasks/${milch.id}`);
      expect(one).toBeDefined();
      return one!;
    });

    expect((down.body as { after: unknown }).after).toBe(kaese.id);
  });

  it("knows the first and the last of each group, whatever sits around them in the whole order", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(
        { ...brot, after: null },
        { ...milch, after: brot.id },
        { ...kaese, after: milch.id },
      ),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    // The first open one, with a completed one above it in the order.
    expect(await screen.findByRole("button", { name: "Move Milch holen up" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Move Milch holen down" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Move Käse holen down" })).toBeDisabled();

    // The only completed one, with open ones below it in the order.
    expect(screen.getByRole("button", { name: "Move Brot holen up" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Move Brot holen down" })).toBeDisabled();
  });

  it("leaves the rows standing while something else on the screen changes", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    const edit = await screen.findByRole("button", { name: "Edit Milch holen" });

    await userEvent.type(screen.getByLabelText("What has to be done"), "x");

    // The same element, and not one that looks the same: a row taken down and
    // put up again is a row that drops the focus of whoever was on it.
    expect(edit.isConnected).toBe(true);
    expect(screen.getByRole("button", { name: "Edit Milch holen" })).toBe(edit);
  });

  it("keeps the keyboard on the task it moved and the box it ticked", async () => {
    const moved = { ...kaese, after: null };
    const ticked = { ...moved, completed: true, completed_at: "2026-09-14T09:00:00.000000Z" };

    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: [
        theTasks(milch, { ...kaese, after: milch.id }),
        theTasks(moved, { ...milch, after: kaese.id }),
        theTasks(ticked, { ...milch, after: kaese.id }),
      ],
      [`PUT /api/tasks/${kaese.id}`]: [{ body: moved }, { body: ticked }],
    });

    tasksAt(`/tasks/${einkauf.id}`);

    (await screen.findByRole("button", { name: "Move Käse holen up" })).focus();
    await userEvent.keyboard("{Enter}");

    // It is at the top now, so up is not there to stand on: the other way is.
    await waitFor(() => expect(screen.getByRole("button", { name: "Move Käse holen up" })).toBeDisabled());
    await waitFor(() => expect(screen.getByRole("button", { name: "Move Käse holen down" })).toHaveFocus());

    screen.getByRole("checkbox", { name: "Complete Käse holen" }).focus();
    await userEvent.keyboard(" ");

    // The row went to the other group, and the box it has there is a new one.
    await waitFor(() => expect(screen.getByRole("checkbox", { name: "Reopen Käse holen" })).toHaveFocus());
  });

  it("cannot move the first one up or the last one down", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(
        milch,
        { ...brot, completed: false, completed_at: null },
      ),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    expect(await screen.findByRole("button", { name: "Move Milch holen up" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Move Brot holen down" })).toBeDisabled();
  });

  it("reads a due date as the day it is, whatever the browser's timezone", async () => {
    // Midnight UTC on the fourteenth is the thirteenth for anybody west of it.
    // Nothing on this screen makes a `Date` out of a day, which is why the
    // fourteenth is the fourteenth.
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks({ ...milch, due_on: "2026-09-14" }),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    expect(await screen.findByText(/2026-09-14/)).toBeInTheDocument();
  });

  it("marks a date that has passed, and today", async () => {
    const today = todayHere();
    const yesterday = todayHere(new Date(Date.now() - 24 * 60 * 60 * 1000));

    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(
        { ...milch, due_on: yesterday },
        { ...brot, completed: false, completed_at: null, due_on: today },
      ),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    expect(await screen.findByText(new RegExp(`overdue ${yesterday}`))).toBeInTheDocument();
    expect(screen.getByText(new RegExp(`today ${today}`))).toBeInTheDocument();
  });

  it("says today is today in the browser's own calendar and not in UTC", () => {
    // Late in the evening west of UTC, the UTC date is already tomorrow. What
    // the owner means by "today" is what their own calendar says.
    const evening = new Date(2026, 8, 14, 23, 30);

    expect(todayHere(evening)).toBe("2026-09-14");
  });

  it("edits the rest of a task in a dialog, and moves it between lists", async () => {
    const arbeit = { ...einkauf, id: "0199f0c7-0000-7000-8000-00000000000d", name: "Arbeit" };

    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf, arbeit),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`GET /api/tasks/lists/${arbeit.id}/tasks`]: theTasks(
        { ...brot, list: arbeit.id, after: null },
        { ...kaese, list: arbeit.id, after: brot.id },
      ),
      [`PUT /api/tasks/${milch.id}`]: { body: milch },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Edit Milch holen" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.type(dialog.getByLabelText("Due"), "2026-09-20");
    // By role: `Field` puts the label's text in a span beside the control as
    // well, so "List" matches twice by text and once by role.
    await userEvent.selectOptions(dialog.getByRole("combobox", { name: "List" }), arbeit.id);
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(write.body).toMatchObject({
      due_on: "2026-09-20",
      list: arbeit.id,
      // Moved to another list, so it lands at the end of that one: behind
      // whatever is last there. `null` would be the top (`docs/api.md`, Tasks).
      after: kaese.id,
    });
  });

  it("puts a task moved to an empty list at the top of it, which is also its end", async () => {
    const arbeit = { ...einkauf, id: "0199f0c7-0000-7000-8000-00000000000d", name: "Arbeit" };

    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf, arbeit),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`GET /api/tasks/lists/${arbeit.id}/tasks`]: theTasks(),
      [`PUT /api/tasks/${milch.id}`]: { body: milch },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Edit Milch holen" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.selectOptions(dialog.getByRole("combobox", { name: "List" }), arbeit.id);
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    const write = await waitFor(() => {
      const one = asked.find((request) => request.method === "PUT");
      expect(one).toBeDefined();
      return one!;
    });

    expect(write.body).toMatchObject({ list: arbeit.id, after: null });
  });

  it("reads the task again after a stale edit and keeps the dialog for a deliberate retry", async () => {
    const newer = { ...milch, title: "Hafermilch holen", updated_at: "2026-09-14T09:30:00.000000Z" };

    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: [theTasks(milch), theTasks(newer), theTasks(newer)],
      [`PUT /api/tasks/${milch.id}`]: [refused("stale", 412), { body: newer }],
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Edit Milch holen" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.type(dialog.getByLabelText("Due"), "2026-09-20");
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    expect(await dialog.findByRole("alert")).toHaveTextContent("changed somewhere else");

    // What is being read again is the tasks, and not only the lists beside
    // them: the row underneath the dialog is the newer one.
    await screen.findByText("Hafermilch holen");

    // What was typed is still there, and saving again is a decision made
    // against the version that is now on the screen.
    expect(dialog.getByLabelText("Due")).toHaveValue("2026-09-20");
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    expect(asked.filter((request) => request.method === "PUT").map((request) => request.headers.get("If-Match")))
      .toEqual([`"${milch.updated_at}"`, `"${newer.updated_at}"`]);
  });

  it("shows that a task has notes, and the notes where they are asked for", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(
        { ...milch, description: "am **Markt**, nicht im Supermarkt" },
        { ...kaese, description: "" },
      ),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    const show = await screen.findByRole("button", { name: "Show the notes on Milch holen" });

    // Only where there is something to read.
    expect(screen.queryByRole("button", { name: /notes on Käse holen/ })).not.toBeInTheDocument();
    expect(screen.queryByText("Markt")).not.toBeInTheDocument();
    expect(show).toHaveAttribute("aria-expanded", "false");

    await userEvent.click(show);

    // Drawn as what it is, which is Markdown.
    expect(screen.getByText("Markt").tagName).toBe("STRONG");

    await userEvent.click(screen.getByRole("button", { name: "Hide the notes on Milch holen" }));

    expect(screen.queryByText("Markt")).not.toBeInTheDocument();
  });

  it("marks the task an address points at and opens its notes", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(
        { ...milch, description: "am Markt" },
        { ...kaese, description: "vom Stand" },
      ),
    });

    tasksAt(`/tasks/${einkauf.id}?task=${kaese.id}`);

    expect((await screen.findByText("Käse holen")).closest("li")).toHaveAttribute("aria-current", "true");
    expect(screen.getByText("Milch holen").closest("li")).not.toHaveAttribute("aria-current");

    expect(screen.getByText("vom Stand")).toBeInTheDocument();
    expect(screen.queryByText("am Markt")).not.toBeInTheDocument();
  });

  it("captures with the day it is due, when one is given", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(),
      [`POST /api/tasks/lists/${einkauf.id}/tasks`]: { status: 201, body: milch },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    const day = await screen.findByLabelText("When it is due");

    await userEvent.type(day, "2026-09-20");
    await userEvent.type(screen.getByLabelText("What has to be done"), "Milch holen{Enter}");

    await waitFor(() => expect(day).toHaveValue(""));

    expect(asked.find((one) => one.method === "POST")?.body).toEqual({
      title: "Milch holen",
      description: "",
      due_on: "2026-09-20",
    });
  });

  it("says where a deleted task went, and puts it back with the version the Trash holds", async () => {
    const inTheTrash = "2026-09-14T10:00:00.000000Z";

    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: [theTasks(milch), theTasks(), theTasks(milch)],
      [`DELETE /api/tasks/${milch.id}`]: { status: 204 },
      "GET /api/trash": {
        body: {
          items: [
            theEntry("knowledge", milch.id, "something else with the same id"),
            theEntry("tasks", milch.id, milch.title, inTheTrash),
          ],
          has_more: false,
        },
      },
      [`POST /api/trash/tasks/${milch.id}/restore`]: {
        body: { application: "tasks", id: milch.id, name: milch.title, where: "Einkauf", moved_to_the_root: false },
      },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete Milch holen" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Milch holen is in the Trash.");

    await userEvent.click(screen.getByRole("button", { name: "Put it back" }));

    expect(await screen.findByRole("checkbox", { name: "Complete Milch holen" })).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    const restore = asked.find((request) => request.path.endsWith("/restore"));

    // Deleting moved the version on, so the one this screen held is not the
    // one a restore replaces.
    expect(restore?.headers.get("If-Match")).toBe(`"${inTheTrash}"`);
  });

  it("says so when what was deleted is no longer in the Trash", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: [theTasks(milch), theTasks()],
      [`DELETE /api/tasks/${milch.id}`]: { status: 204 },
      "GET /api/trash": { body: { items: [], has_more: false } },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete Milch holen" }));
    await userEvent.click(await screen.findByRole("button", { name: "Put it back" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("no longer in the Trash");
    expect(screen.queryByRole("button", { name: "Put it back" })).not.toBeInTheDocument();
    expect(asked.some((request) => request.method === "POST")).toBe(false);
  });

  it("deletes everything completed in one go, and only that", async () => {
    const quark = { ...brot, id: "0199f0c7-0000-7000-8000-00000000000f", title: "Quark holen", after: brot.id };

    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: [theTasks(milch, brot, quark), theTasks(milch)],
      [`DELETE /api/tasks/${brot.id}`]: { status: 204 },
      [`DELETE /api/tasks/${quark.id}`]: { status: 204 },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete completed" }));

    expect(await screen.findByRole("status")).toHaveTextContent("2 completed tasks are in the Trash.");
    expect(screen.getByRole("button", { name: "Put them back" })).toBeInTheDocument();

    expect(asked.filter((request) => request.method === "DELETE").map((request) => request.path)).toEqual([
      `/api/tasks/${brot.id}`,
      `/api/tasks/${quark.id}`,
    ]);

    expect(screen.getByText("Milch holen")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("heading", { name: "Done" })).not.toBeInTheDocument());
  });

  it("offers what went before a refusal half-way through deleting the completed ones", async () => {
    const quark = { ...brot, id: "0199f0c7-0000-7000-8000-00000000000f", title: "Quark holen", after: brot.id };

    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: [theTasks(milch, brot, quark), theTasks(milch, quark)],
      [`DELETE /api/tasks/${brot.id}`]: { status: 204 },
      [`DELETE /api/tasks/${quark.id}`]: refused("stale", 412),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete completed" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("changed somewhere else");
    expect(screen.getByRole("status")).toHaveTextContent("1 completed task is in the Trash.");
    expect(screen.getByText("Quark holen")).toBeInTheDocument();
  });

  it("makes a list and says when the name is taken, in the instance's own sentence", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      "POST /api/tasks/lists": refused("conflict", 409, "A list is already called `Einkauf`."),
    });

    tasksAt("/tasks");

    await userEvent.click(await screen.findByRole("button", { name: "New list" }));

    const dialog = within(screen.getByRole("dialog"));

    await userEvent.type(dialog.getByLabelText("Name"), "Einkauf");
    await userEvent.click(dialog.getByRole("button", { name: "Make it" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("already called");
  });

  it("puts the open list in the Trash with everything on it, and says where it went", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`DELETE /api/tasks/lists/${einkauf.id}`]: { status: 204 },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete list" }));

    expect(await screen.findByRole("status")).toHaveTextContent(
      "Einkauf and everything on it are in the Trash.",
    );

    const discard = asked.find((request) => request.method === "DELETE");

    expect(discard?.headers.get("If-Match")).toBe(`"${einkauf.updated_at}"`);
  });

  it("puts a deleted list back from where it was deleted, and walks to it", async () => {
    const inTheTrash = "2026-09-14T10:00:00.000000Z";

    const { asked } = anInstance({
      "GET /api/tasks/lists": [theLists(einkauf), theLists(), theLists(einkauf)],
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`DELETE /api/tasks/lists/${einkauf.id}`]: { status: 204 },
      "GET /api/trash": {
        body: { items: [theEntry("tasks", einkauf.id, einkauf.name, inTheTrash)], has_more: false },
      },
      [`POST /api/trash/tasks/${einkauf.id}/restore`]: {
        body: { application: "tasks", id: einkauf.id, name: einkauf.name, where: null, moved_to_the_root: false },
      },
    });

    tasksAt(`/tasks/${einkauf.id}`);

    await userEvent.click(await screen.findByRole("button", { name: "Delete list" }));
    await userEvent.click(await screen.findByRole("button", { name: "Put it back" }));

    expect(await screen.findByRole("heading", { name: "Einkauf" })).toBeInTheDocument();
    expect(await screen.findByText("Milch holen")).toBeInTheDocument();

    expect(asked.find((request) => request.path.endsWith("/restore"))?.headers.get("If-Match"))
      .toBe(`"${inTheTrash}"`);
  });

  it("renames a list with a guarded write and updates its heading and navigation", async () => {
    const renamed = { ...einkauf, name: "Wochenmarkt" };
    const { asked } = anInstance({
      "GET /api/tasks/lists": [theLists(einkauf), theLists(renamed)],
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`PUT /api/tasks/lists/${einkauf.id}`]: { body: renamed },
    });
    tasksAt(`/tasks/${einkauf.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Rename list" }));
    const dialog = within(screen.getByRole("dialog", { name: "Rename list" }));
    expect(dialog.getByLabelText("Name")).toHaveValue("Einkauf");
    await userEvent.clear(dialog.getByLabelText("Name"));
    expect(dialog.getByRole("button", { name: "Save" })).toBeDisabled();
    await userEvent.type(dialog.getByLabelText("Name"), "  Wochenmarkt  {Enter}");
    expect(await screen.findByRole("heading", { name: "Wochenmarkt" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Wochenmarkt/ })).toHaveAttribute("href", `/tasks/${einkauf.id}`);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    const write = asked.find((request) => request.method === "PUT");
    expect(write?.body).toEqual({ name: "Wochenmarkt" });
    expect(write?.headers.get("If-Match")).toBe(`"${einkauf.updated_at}"`);
    expect(screen.getByText("Milch holen")).toBeInTheDocument();
  });

  it("keeps the typed name when a rename conflicts and cancels without writing again", async () => {
    const { asked } = anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`PUT /api/tasks/lists/${einkauf.id}`]: refused("conflict", 409, "A list is already called Arbeit."),
    });
    tasksAt(`/tasks/${einkauf.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Rename list" }));
    const dialog = within(screen.getByRole("dialog"));
    await userEvent.clear(dialog.getByLabelText("Name"));
    await userEvent.type(dialog.getByLabelText("Name"), "Arbeit{Enter}");
    expect(await dialog.findByRole("alert")).toHaveTextContent("already called Arbeit");
    expect(dialog.getByLabelText("Name")).toHaveValue("Arbeit");
    await userEvent.click(dialog.getByRole("button", { name: "Cancel" }));
    await userEvent.click(screen.getByRole("button", { name: "Rename list" }));
    expect(within(screen.getByRole("dialog")).getByLabelText("Name")).toHaveValue("Einkauf");
    expect(asked.filter((request) => request.method === "PUT")).toHaveLength(1);
  });

  it("refreshes after a stale rename and preserves the draft for a deliberate retry", async () => {
    const newer = { ...einkauf, name: "Extern", updated_at: "2026-09-14T09:00:00.000000Z" };
    const { asked } = anInstance({
      "GET /api/tasks/lists": [theLists(einkauf), theLists(newer), theLists({ ...newer, name: "Markt" })],
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`PUT /api/tasks/lists/${einkauf.id}`]: [refused("stale", 412), { body: { ...newer, name: "Markt" } }],
    });
    tasksAt(`/tasks/${einkauf.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Rename list" }));
    const dialog = within(screen.getByRole("dialog"));
    await userEvent.clear(dialog.getByLabelText("Name"));
    await userEvent.type(dialog.getByLabelText("Name"), "Markt{Enter}");
    expect(await dialog.findByRole("alert")).toHaveTextContent("changed somewhere else");
    await screen.findByRole("heading", { name: "Extern", hidden: true });
    expect(dialog.getByLabelText("Name")).toHaveValue("Markt");
    await userEvent.click(dialog.getByRole("button", { name: "Save" }));
    await screen.findByRole("heading", { name: "Markt" });
    expect(asked.filter((request) => request.method === "PUT").map((request) => request.headers.get("If-Match")))
      .toEqual([`"${einkauf.updated_at}"`, `"${newer.updated_at}"`]);
  });

  it("keeps a list open when deleting it is refused", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(einkauf),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: theTasks(milch),
      [`DELETE /api/tasks/lists/${einkauf.id}`]: refused("stale", 412),
    });
    tasksAt(`/tasks/${einkauf.id}`);
    await userEvent.click(await screen.findByRole("button", { name: "Delete list" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("changed somewhere else");
    expect(screen.getByRole("heading", { name: "Einkauf" })).toBeInTheDocument();
    expect(screen.getByText("Milch holen")).toBeInTheDocument();
  });

  it("draws the denied state rather than a failure when access was revoked", async () => {
    anInstance({
      "GET /api/tasks/lists": refused("forbidden", 403, "This access does not reach Tasks."),
    });

    tasksAt("/tasks");

    expect(await screen.findByText("Tasks is not this credential's to see.")).toBeInTheDocument();
  });

  it("draws the disabled state rather than a failure when the switch is thrown", async () => {
    anInstance({
      "GET /api/tasks/lists": refused("disabled", 409, "Tasks is switched off in this workspace."),
    });

    tasksAt("/tasks");

    expect(await screen.findByText("Tasks is switched off.")).toBeInTheDocument();
  });

  it("says a list that is gone is gone, with the way to try again", async () => {
    anInstance({
      "GET /api/tasks/lists": theLists(),
      [`GET /api/tasks/lists/${einkauf.id}/tasks`]: refused(
        "deleted",
        404,
        "The list `Einkauf` was deleted by the owner and is in the Trash.",
      ),
    });

    tasksAt(`/tasks/${einkauf.id}`);

    expect(await screen.findByRole("alert")).toHaveTextContent("Trash");
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});
