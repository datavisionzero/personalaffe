import { ArrowRightIcon, SearchIcon } from "lucide-react";
import { useId, useMemo, useState, type KeyboardEvent } from "react";
import { useNavigate } from "react-router";

import { useTheme } from "@/components/theme-provider";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils";
import { addressOf, useFindings, useSettled, worthAsking } from "@/search/useFindings";
import { home } from "./applications";
import { Keys } from "./ShortcutsDialog";
import { is } from "./shortcuts";
import type { TheApplications } from "./useApplications";

type Command = {
  id: string;
  label: string;
  hint?: string;
  group: string;
  run: () => void;
};

/**
 * The command palette — ⌘K, or Ctrl+K — over everywhere the workspace can go
 * and the few acts the frame itself has.
 *
 * <b>It offers what the sidebar offers and nothing more.</b> An application
 * that is switched off, or out of this credential's reach, is not a row here
 * either: a palette that could walk somewhere the navigation will not is a
 * second answer to the same question.
 *
 * <b>It searches the workspace as well as itself.</b> The field is where a
 * person types a word they half remember, so what the instance finds goes above
 * the commands: pages, tasks, Scratchpad text and file names, best match first,
 * the same list `/search` draws. The commands stay underneath, because a
 * palette that stopped offering "Settings" the moment somebody typed "se" would
 * be a palette that gets worse the more you use it.
 *
 * It asks a moment after the typing stops rather than on every keystroke, and
 * it asks nothing at all until there is a word the instance would look for.
 *
 * Owned rather than imported: a filtered list with a roving index inside a Base
 * UI dialog, which is what a palette is before it does more.
 */
type PaletteProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  applications: TheApplications;
  onShortcuts: () => void;
  onSignOut: () => void;
};

export function Palette({ open, ...rest }: PaletteProps) {
  return (
    <Dialog open={open} onOpenChange={rest.onOpenChange}>
      <DialogContent
        className="top-[20%] translate-y-0 gap-0 overflow-hidden p-0 sm:max-w-lg"
        showCloseButton={false}
      >
        <DialogHeader className="sr-only">
          <DialogTitle>Command palette</DialogTitle>
          <DialogDescription>Go anywhere in the workspace, or run a command.</DialogDescription>
        </DialogHeader>
        {open && <PaletteBody {...rest} />}
      </DialogContent>
    </Dialog>
  );
}

/** Mounted while the palette is open, so that its query starts empty every time. */
function PaletteBody({
  onOpenChange,
  applications,
  onShortcuts,
  onSignOut,
}: Omit<PaletteProps, "open">) {
  const navigate = useNavigate();
  const { setTheme } = useTheme();
  const [query, setQuery] = useState("");
  const [index, setIndex] = useState(0);
  const field = useId();

  const needle = query.trim();
  const settled = useSettled(needle);
  const { asked: findings } = useFindings(settled, 8);

  const found = useMemo(
    () => (findings.at === "known" ? findings.value.items : []),
    [findings],
  );

  const commands = useMemo<Command[]>(() => {
    const go = (to: string) => () => {
      onOpenChange(false);
      void navigate(to);
    };

    const close = (run: () => void) => () => {
      onOpenChange(false);
      run();
    };

    return [
      { id: "go:home", label: home.label, hint: "What is useful or pending.", group: "Go to", run: go(home.path) },
      ...applications.offered.map((application) => ({
        id: `go:${application.name}`,
        label: application.label,
        hint: application.hint,
        group: "Go to",
        run: go(application.path),
      })),
      { id: "go:trash", label: "Trash", hint: "What was deleted and is still recoverable.", group: "Go to", run: go("/trash") },
      { id: "go:settings", label: "Settings", hint: "Applications, security, agent access.", group: "Go to", run: go("/settings") },
      { id: "go:applications", label: "Applications", hint: "Switch one on or off.", group: "Settings", run: go("/settings/applications") },
      { id: "go:security", label: "Security", hint: "Password, second factor, signed-in browsers.", group: "Settings", run: go("/settings/security") },
      { id: "go:agents", label: "Agent access", hint: "The tokens you have handed out.", group: "Settings", run: go("/settings/agents") },
      { id: "theme:light", label: "Light theme", group: "Appearance", run: close(() => setTheme("light")) },
      { id: "theme:dark", label: "Dark theme", group: "Appearance", run: close(() => setTheme("dark")) },
      { id: "theme:system", label: "Follow the system", group: "Appearance", run: close(() => setTheme("system")) },
      {
        id: "shortcuts",
        label: "Keyboard shortcuts",
        hint: "Every key the workspace binds.",
        group: "Help",
        run: close(onShortcuts),
      },
      { id: "sign-out", label: "Sign out", group: "Account", run: close(onSignOut) },
    ];
  }, [applications.offered, navigate, onOpenChange, onShortcuts, onSignOut, setTheme]);

  const matching = useMemo(() => {
    const lowered = needle.toLowerCase();

    if (lowered === "") {
      return commands;
    }

    return commands.filter(
      (command) =>
        command.label.toLowerCase().includes(lowered) ||
        command.hint?.toLowerCase().includes(lowered) ||
        command.group.toLowerCase().includes(lowered),
    );
  }, [commands, needle]);

  // Findings first, commands after, as one list with one roving index — so that
  // the arrow keys walk everything the palette is offering rather than two
  // lists that each have a "first".
  const rows = useMemo<Command[]>(() => {
    if (!worthAsking(settled)) {
      return matching;
    }

    return [
      ...found.map((one) => ({
        id: `found:${one.application}:${one.id}`,
        label: one.title,
        hint: one.snippet ?? one.application,
        group: "In this workspace",
        run: () => {
          onOpenChange(false);
          void navigate(addressOf(one));
        },
      })),
      {
        id: "search:all",
        label: `Search for “${settled}”`,
        hint: "Everything that matches, on one screen.",
        group: "In this workspace",
        run: () => {
          onOpenChange(false);
          void navigate(`/search?q=${encodeURIComponent(settled)}`);
        },
      },
      ...matching,
    ];
  }, [found, matching, navigate, onOpenChange, settled]);

  const selected = rows[Math.min(index, Math.max(rows.length - 1, 0))];

  function onKeyDown(event: KeyboardEvent) {
    if (is("palette:next", event)) {
      event.preventDefault();
      setIndex((current) => Math.min(current + 1, rows.length - 1));
    } else if (is("palette:previous", event)) {
      event.preventDefault();
      setIndex((current) => Math.max(current - 1, 0));
    } else if (is("palette:run", event) && selected !== undefined) {
      event.preventDefault();
      selected.run();
    }
  }

  let lastGroup: string | undefined;

  return (
    <>
      <div className="flex items-center gap-2 border-b px-3">
        <SearchIcon className="text-muted-foreground size-4" />
        <input
          id={field}
          autoFocus
          role="combobox"
          aria-expanded
          aria-controls="palette-commands"
          aria-activedescendant={selected ? `palette-${selected.id}` : undefined}
          aria-label="Go anywhere, or type a command"
          placeholder="Go anywhere, or type a command"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setIndex(0);
          }}
          onKeyDown={onKeyDown}
          className="placeholder:text-muted-foreground h-11 flex-1 bg-transparent text-sm outline-hidden"
        />
        <Keys id="palette:close" />
      </div>

      <ul id="palette-commands" role="listbox" className="max-h-80 overflow-y-auto p-1">
        {rows.length === 0 && (
          <li className="text-muted-foreground px-3 py-6 text-center text-sm">Nothing matches.</li>
        )}
        {rows.map((command) => {
          const heading = command.group !== lastGroup ? command.group : undefined;
          lastGroup = command.group;

          return (
            <li key={command.id} role="presentation">
              {heading !== undefined && (
                <div className="text-muted-foreground px-2 pt-2 pb-1 text-[11px] font-medium tracking-wide uppercase">
                  {heading}
                </div>
              )}
              <div
                id={`palette-${command.id}`}
                role="option"
                aria-selected={command === selected}
                onMouseMove={() => setIndex(rows.indexOf(command))}
                onClick={command.run}
                className={cn(
                  "flex cursor-default items-center gap-3 rounded-md px-2 py-1.5 text-sm",
                  command === selected && "bg-accent text-accent-foreground",
                )}
              >
                <span className="flex-1 truncate">{command.label}</span>
                {command.hint !== undefined && (
                  <span className="text-muted-foreground truncate text-xs">{command.hint}</span>
                )}
                {command === selected && (
                  <ArrowRightIcon className="text-muted-foreground size-3.5" />
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </>
  );
}
