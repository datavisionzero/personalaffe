import { CommandIcon, WifiOffIcon } from "lucide-react";
import { lazy, Suspense, useEffect, useState, type ReactElement } from "react";
import { Link, Route, Routes, useNavigate } from "react-router";

import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { SidebarInset, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";
import { Home } from "@/home/Home";
import { Scratchpad } from "@/scratchpad/Scratchpad";
import type { Me } from "@/session/useSession";
import { Settings } from "@/settings/Settings";
import { Trash } from "@/trash/Trash";
import { AccountMenu } from "./AccountMenu";
import { applications, type Application } from "./applications";
import { AppSidebar } from "./AppSidebar";
import { Awaited, Busy, Denied, Disabled, Empty } from "./States";
import { Keys, ShortcutsDialog } from "./ShortcutsDialog";
import { is, overlaid, typing } from "./shortcuts";
import { useApplications, type TheApplications } from "./useApplications";

// The palette is the one thing in the frame that nobody has asked for yet when
// the frame is drawn. It arrives when it is first opened.
const Palette = lazy(() => import("./Palette").then((module) => ({ default: module.Palette })));

// The Markdown pipeline weighs more than the frame does, and CodeMirror behind
// it weighs more again. Both arrive with the first screen that writes, never
// with the frame (ADR 0001).
const Editing = lazy(() =>
  import("@/editor/Editing").then((module) => ({ default: module.Editing })),
);

/**
 * The frame every screen sits in: drawn before any content arrives, and never
 * remounted by navigation.
 *
 * <b>One person's workspace, so the frame stands in the whole of it</b> — there
 * is nothing to scope it to. What it holds is the sidebar, the header, the
 * palette, the overview of the keys, and the routes.
 *
 * The applications are asked for once, here, and handed to everything that
 * needs them. A sidebar, a palette and a route each asking on their own would
 * be three requests and three moments at which they could disagree about what
 * this workspace has.
 */
export function Shell({ me, onSignedOut }: { me: Me; onSignedOut: () => void }) {
  const navigate = useNavigate();
  const applicationsAsked = useApplications();
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  // The keys the frame itself owns, read from `shortcuts.ts` so that this
  // handler and the overview it feeds cannot come apart. `?` and `h` are bare
  // keys on purpose: ⌘P is the browser's print, and bare keys are the alphabet
  // the screens already use rather than a fight with the browser for a
  // modifier.
  useEffect(() => {
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (is("global:palette", event)) {
        event.preventDefault();
        // One dialog at a time: the palette arrives over whatever the overview
        // was explaining, not behind it.
        setShortcutsOpen(false);
        setPaletteOpen((open) => !open);
        return;
      }

      // Not while something is being typed, and not while a menu or a dialog
      // has the focus — those close with Escape, as they always did.
      if (typing(event) || overlaid(event)) {
        return;
      }

      if (is("global:shortcuts", event)) {
        event.preventDefault();
        setShortcutsOpen(true);
      } else if (is("global:home", event)) {
        event.preventDefault();
        void navigate("/");
      }
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [navigate]);

  return (
    <SidebarProvider>
      <AppSidebar applications={applicationsAsked} />
      <SidebarInset>
        <header className="flex h-12 shrink-0 items-center gap-2 border-b px-3">
          <SidebarTrigger className="md:hidden" />
          <Separator orientation="vertical" className="mr-1 h-4! md:hidden" />
          <div className="flex-1" />

          {/* The one thing the frame says about the connection. A workspace
              that has stopped hearing from its instance is still readable, and
              what is on it is what was true a moment ago rather than now. */}
          {applicationsAsked.unanswered && (
            <span
              role="status"
              className="text-muted-foreground flex items-center gap-1.5 text-xs"
            >
              <WifiOffIcon aria-hidden className="size-3.5" />
              <span className="hidden sm:inline">Not answering</span>
            </span>
          )}

          <Button
            variant="outline"
            size="sm"
            className="text-muted-foreground hidden gap-2 sm:flex"
            onClick={() => setPaletteOpen(true)}
          >
            <CommandIcon className="size-3.5" />
            <span className="text-xs">Go anywhere…</span>
            <Keys id="global:palette" />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            className="sm:hidden"
            aria-label="Command palette"
            onClick={() => setPaletteOpen(true)}
          >
            <CommandIcon />
          </Button>

          <AccountMenu
            me={me}
            onShortcuts={() => setShortcutsOpen(true)}
            onSignOut={onSignedOut}
          />
        </header>

        <Routes>
          <Route path="/" element={<Home me={me} applications={applicationsAsked} />} />
          <Route path="/trash" element={<Trash />} />
          {/* The shared Markdown field, with nothing behind it yet
              (`editor/Editing.tsx`). It goes when Knowledge arrives. */}
          <Route
            path="/editor"
            element={
              <Suspense fallback={<Busy title="Loading the editor…" />}>
                <Editing />
              </Suspense>
            }
          />
          <Route
            path="/settings/*"
            element={<Settings me={me} applications={applicationsAsked} />}
          />

          {/* One route per application, and one screen behind all four of them
              until the epics that fill them land. The address is real from
              today, so a link into an application keeps working when there is
              something at the end of it. */}
          {applications.map((application) => (
            <Route
              key={application.name}
              path={`${application.path}/*`}
              element={
                <TheApplication application={application} applications={applicationsAsked} />
              }
            />
          ))}

          {/* A typed or stale address is answered inside the frame rather than
              redirected away: landing somewhere else silently hides the typo,
              and a blank page tells nobody anything. */}
          <Route
            path="*"
            element={
              <Empty title="Nothing at this address.">
                <Link className="text-brand underline-offset-4 hover:underline" to="/">
                  Go home
                </Link>
              </Empty>
            }
          />
        </Routes>
      </SidebarInset>

      <Suspense fallback={null}>
        <Palette
          open={paletteOpen}
          onOpenChange={setPaletteOpen}
          applications={applicationsAsked}
          onShortcuts={() => setShortcutsOpen(true)}
          onSignOut={onSignedOut}
        />
      </Suspense>
      <ShortcutsDialog open={shortcutsOpen} onOpenChange={setShortcutsOpen} />
    </SidebarProvider>
  );
}

/**
 * The screen an application has, for the ones that have one. The three still to
 * come are drawn by `Awaited` until their epic lands.
 */
const screens: Partial<Record<Application["name"], () => ReactElement>> = {
  scratchpad: () => <Scratchpad />,
};

/**
 * What is at an application's address.
 *
 * <b>The three answers a direct link has to be able to give</b>, and they are
 * different answers: this credential may not reach it, the owner has switched
 * it off, or it is switched on and this build has nothing in it yet. Sending
 * all three home would be the same screen for a permission problem, a setting
 * and an unfinished epic.
 *
 * The first two are settled here, from the answer the frame already has, so
 * that a screen an agent cannot read is never asked for. A screen still answers
 * them for itself: the switch can be thrown while somebody is looking at it.
 */
function TheApplication({
  application,
  applications: asked,
}: {
  application: Application;
  applications: TheApplications;
}) {
  if (asked.asked.at === "asking") {
    return <Busy title={`Opening ${application.label}…`} />;
  }

  const state = asked.stateOf(application.name);

  if (state === undefined || state.permission === "none") {
    return <Denied what={application.label} />;
  }

  if (!state.enabled) {
    return <Disabled what={application.label} />;
  }

  return (
    screens[application.name]?.() ?? <Awaited what={application.label} epic={application.arrives} />
  );
}
