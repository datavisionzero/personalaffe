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
      // Moved to another list, so it lands at the end of that one.
      after: null,
    });
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
