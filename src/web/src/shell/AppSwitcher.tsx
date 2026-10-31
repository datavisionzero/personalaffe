import { useState } from "react";
import { Link, useLocation } from "react-router";
import { Popover } from "@base-ui/react/popover";
import { ChevronDownIcon, SettingsIcon, Trash2Icon, SearchIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetTrigger, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { applicationAt, home } from "./applications";
import { Mark } from "./Mark";
import { useAppearance } from "./useAppearance";
import { useNavigation } from "./navigation";
import type { TheApplications } from "./useApplications";

export function AppSwitcher({ applications }: { applications: TheApplications }) {
  const { pathname } = useLocation();
  const mobile = useIsMobile();
  const [open, setOpen] = useState(false);
  const { appearance, name, known } = useAppearance();
  const navigation = useNavigation();
  const current = applicationAt(pathname) ?? (pathname.startsWith("/settings") ? { label: "Settings", icon: SettingsIcon }
    : pathname === "/trash" ? { label: "Trash", icon: Trash2Icon }
    : pathname === "/search" ? { label: "Search", icon: SearchIcon } : home);
  const trigger = <Button variant="ghost" className="min-h-11 min-w-0 gap-2 px-2" aria-label={`Switch application: ${current.label}`}>
    <current.icon aria-hidden className="size-4 shrink-0" /><span className="truncate">{current.label}</span><ChevronDownIcon aria-hidden className="size-4 shrink-0" />
  </Button>;
  const rows = [home, ...applications.offered, { label: "Trash", path: "/trash", icon: Trash2Icon }, { label: "Settings", path: "/settings", icon: SettingsIcon }];
  const content = <nav aria-label="The workspace" className="flex flex-col gap-1 p-2">
    {applications.asked.at === "asking" && <p className="p-2 text-sm text-muted-foreground" role="status">Loading applications…</p>}
    {rows.map(item => {
      const active = item.path === "/" ? pathname === "/" : pathname === item.path || pathname.startsWith(`${item.path}/`);
      return <Link key={item.path} to={applicationAt(item.path) ? navigation?.target(item.path) ?? item.path : item.path}
        aria-current={active ? "page" : undefined} onClick={() => setOpen(false)}
        className={`flex min-h-11 items-center gap-3 rounded-md px-3 text-sm hover:bg-accent focus-visible:outline-2 focus-visible:outline-ring ${active ? "bg-accent font-medium" : ""} ${item.path === "/trash" ? "mt-2 border-t" : ""}`}>
        <item.icon aria-hidden className="size-4" />{item.label}
      </Link>;
    })}
  </nav>;
  return <div className="flex min-w-0 items-center gap-2">
    {known && <Link to="/" aria-label={`${name}: Home`} data-slot="workspace-brand" className="flex min-h-11 shrink-0 items-center gap-2 px-1">
      <Mark colour={appearance.colour} shape={appearance.shape} title={appearance.title} />
      <span className="hidden max-w-40 truncate text-sm font-semibold lg:inline">{name}</span>
    </Link>}
    {mobile ? <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger render={trigger} />
      <SheetContent side="bottom" className="max-h-[85dvh] overflow-y-auto rounded-t-xl pb-[max(1rem,env(safe-area-inset-bottom))]">
        <SheetHeader><SheetTitle>{name}</SheetTitle></SheetHeader>{content}
      </SheetContent>
    </Sheet> : <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger render={trigger} />
      <Popover.Portal><Popover.Positioner sideOffset={8} align="start" className="z-50">
        <Popover.Popup className="w-64 max-h-[80dvh] overflow-auto rounded-xl border bg-popover text-popover-foreground shadow-lg">
          <Popover.Title className="px-5 pt-4 text-xs text-muted-foreground">Applications</Popover.Title>{content}
        </Popover.Popup>
      </Popover.Positioner></Popover.Portal>
    </Popover.Root>}
  </div>;
}
