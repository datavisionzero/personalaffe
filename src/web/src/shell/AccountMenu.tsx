import {
  CheckIcon,
  KeyboardIcon,
  LogOutIcon,
  MonitorIcon,
  MoonIcon,
  SettingsIcon,
  SunIcon,
} from "lucide-react";
import { useNavigate } from "react-router";

import { useTheme } from "@/components/theme-provider";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import type { Me } from "@/session/useSession";
import { Keys } from "./ShortcutsDialog";

/**
 * Top right, where every reader of a web application looks for it: who is
 * signed in, the theme, the keys, settings, sign out.
 *
 * The overview of the keys is here rather than in the sidebar because the
 * sidebar carries the workspace and the keys belong to the whole application —
 * and because a list of shortcuts reachable only by a shortcut helps nobody who
 * has not found one yet.
 */
export function AccountMenu({
  me,
  onShortcuts,
  onSignOut,
}: {
  me: Me;
  onShortcuts: () => void;
  onSignOut: () => void;
}) {
  const { theme, setTheme } = useTheme();
  const navigate = useNavigate();

  // The owner signs in with an address; an agent has a name instead. One of the
  // two is always there (`docs/api.md`, `GET /api/me`).
  const called = me.email ?? me.name ?? "this credential";

  const themes = [
    { id: "light", label: "Light", icon: SunIcon },
    { id: "dark", label: "Dark", icon: MoonIcon },
    { id: "system", label: "System", icon: MonitorIcon },
  ] as const;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={<Button variant="ghost" size="icon-sm" aria-label={`Account: ${called}`} />}
      >
        <span
          aria-hidden
          className="bg-secondary flex size-6 items-center justify-center rounded-full font-mono text-[11px] font-medium uppercase"
        >
          {called.slice(0, 2)}
        </span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-52">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="font-normal">
            <div className="font-medium">{called}</div>
            <div className="text-muted-foreground text-xs">
              {me.kind === "owner" ? "the owner" : "agent access"}
            </div>
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          {themes.map((candidate) => (
            <DropdownMenuItem key={candidate.id} onClick={() => setTheme(candidate.id)}>
              <candidate.icon />
              {candidate.label}
              {theme === candidate.id && <CheckIcon className="ml-auto size-3.5" />}
            </DropdownMenuItem>
          ))}
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={onShortcuts}>
          <KeyboardIcon />
          Keyboard shortcuts
          <Keys id="global:shortcuts" className="ml-auto" />
        </DropdownMenuItem>
        <DropdownMenuItem onClick={() => void navigate("/settings")}>
          <SettingsIcon />
          Settings
        </DropdownMenuItem>
        <DropdownMenuItem onClick={onSignOut}>
          <LogOutIcon />
          Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
