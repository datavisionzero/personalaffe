import { CircleSlashIcon, LockIcon, PowerOffIcon, TriangleAlertIcon } from "lucide-react";
import type { ReactNode } from "react";
import { Link } from "react-router";

import { Button } from "@/components/ui/button";

/**
 * The five screens a content screen has to be able to draw before it can draw
 * anything, written once here so that the four applications do not each invent
 * them (`docs/mvp-plan.md`, PERSONAL-E4).
 *
 * None of them is a blank page and none of them is silent to a screen reader.
 * "Nothing here yet", "still loading", "not yours to see", "switched off" and
 * "something went wrong" are five different things to be told, and a screen
 * that collapses them into one spinner is a screen nobody can act on.
 */
export function Busy({ title = "Loading…" }: { title?: string }) {
  return (
    <Frame busy>
      <span aria-hidden className="bg-brand size-4.5 animate-pulse rounded-sm" />
      <p role="status" className="text-muted-foreground text-sm">
        {title}
      </p>
    </Frame>
  );
}

export function Empty({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <Frame>
      <p className="font-medium">{title}</p>
      {children !== undefined && (
        <div className="text-muted-foreground max-w-prose text-sm text-balance">{children}</div>
      )}
    </Frame>
  );
}

/** What this credential may not reach. The owner never sees it; an agent might. */
export function Denied({ what }: { what: string }) {
  return (
    <Frame>
      <LockIcon aria-hidden className="text-muted-foreground size-5" />
      <p className="font-medium">{what} is not this credential's to see.</p>
      <p className="text-muted-foreground max-w-prose text-sm text-balance">
        Agent access reaches the applications the owner gave it and nothing else. The owner changes
        that in Settings.
      </p>
    </Frame>
  );
}

/**
 * An application the owner has switched off. It says what is true — the content
 * is kept, and the retention deadlines are still running — because the fear the
 * switch raises is that switching it off threw something away.
 */
export function Disabled({ what }: { what: string }) {
  return (
    <Frame>
      <PowerOffIcon aria-hidden className="text-muted-foreground size-5" />
      <p className="font-medium">{what} is switched off.</p>
      <p className="text-muted-foreground max-w-prose text-sm text-balance">
        Everything in it is kept and comes back exactly as it was when you switch it on again.
        Retention keeps running while it is off: what was in the Trash still expires on the day it
        was going to.
      </p>
      <Button render={<Link to="/settings/applications" />} variant="outline">
        Switch it on
      </Button>
    </Frame>
  );
}

/** Something went wrong, with the way to try it again beside it. */
export function Failed({ why, again }: { why: string; again?: () => void }) {
  return (
    <Frame>
      <TriangleAlertIcon aria-hidden className="text-destructive size-5" />
      <p role="alert" className="max-w-prose text-balance">
        {why}
      </p>
      {again !== undefined && (
        <Button type="button" variant="outline" onClick={again}>
          Try again
        </Button>
      )}
    </Frame>
  );
}

/**
 * An application that has no screens yet: the epic that fills it has not
 * landed. It is a state of its own rather than an empty list, because "there is
 * nothing in your Scratchpad" and "this build has no Scratchpad" are different
 * facts and only one of them is the owner's to do something about.
 */
export function Awaited({ what, epic }: { what: string; epic: string }) {
  return (
    <Frame>
      <CircleSlashIcon aria-hidden className="text-muted-foreground size-5" />
      <p className="font-medium">{what} is not in this build yet.</p>
      <p className="text-muted-foreground max-w-prose text-sm text-balance">
        It arrives with {epic}. The navigation, the switch, the editor and the refresh around it are
        here; what goes inside is not. Nothing is stored in it, so nothing can be lost from it.
      </p>
      <Button render={<Link to="/knowledge" />} variant="outline">
        See the editor it will use
      </Button>
    </Frame>
  );
}

function Frame({ children, busy }: { children: ReactNode; busy?: boolean }) {
  return (
    <div
      aria-busy={busy}
      className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center"
    >
      {children}
    </div>
  );
}
