import {
  FileIcon,
  HomeIcon,
  ListTodoIcon,
  NotebookTextIcon,
  StickyNoteIcon,
  type LucideIcon,
} from "lucide-react";

import type { Schemas } from "@/api/client";

/** The word the contract spells an application (`docs/api.md`). */
export type ApplicationName = Schemas["ApplicationResponse"]["application"];

/**
 * One of the four focused areas of the workspace, as the navigation shows it.
 *
 * The order is `CONTEXT.md`'s and the epics': the Scratchpad is where something
 * is put down in seconds, Knowledge is what is kept, Tasks are what is owed,
 * Files are what is stored. Each is a route of its own, so a pasted link says
 * what it shows.
 */
export type Application = {
  name: ApplicationName;
  label: string;
  path: string;
  icon: LucideIcon;
  /** What it is for, in one sentence the empty state and the palette both use. */
  hint: string;
  /**
   * The epic that fills it, while it is still empty. The two applications that
   * have landed carry none: an entry with no `arrives` is one with a screen.
   */
  arrives?: string;
};

export const applications: Application[] = [
  {
    name: "scratchpad",
    label: "Scratchpad",
    path: "/scratchpad",
    icon: StickyNoteIcon,
    hint: "Text put down in seconds and read on another device.",
  },
  {
    name: "knowledge",
    label: "Knowledge",
    path: "/knowledge",
    icon: NotebookTextIcon,
    hint: "What is worth keeping, as Markdown pages in a hierarchy.",
    arrives: "PERSONAL-E7",
  },
  {
    name: "tasks",
    label: "Tasks",
    path: "/tasks",
    icon: ListTodoIcon,
    hint: "Personal commitments, in named lists you order yourself.",
    arrives: "PERSONAL-E8",
  },
  {
    name: "files",
    label: "Files",
    path: "/files",
    icon: FileIcon,
    hint: "Personal files, in folders, on this instance's own disk.",
  },
];

/** The home page, which is not an application and is where the frame starts. */
export const home = { label: "Home", path: "/", icon: HomeIcon } as const;

export function applicationAt(path: string): Application | undefined {
  return applications.find(
    (application) => path === application.path || path.startsWith(`${application.path}/`),
  );
}

export function applicationNamed(name: string): Application | undefined {
  return applications.find((application) => application.name === name);
}
