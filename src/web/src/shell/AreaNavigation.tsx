import { useLocation } from "react-router";
import { useState, type ReactNode } from "react";
import { PanelLeftCloseIcon, PanelLeftOpenIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";

/** The app owns its contents; only presentation changes with the viewport. */
export function AreaNavigation({ label, children, hidden = false }: { label: string; children: ReactNode; hidden?: boolean }) {
  const mobile = useIsMobile();
  const location = useLocation();
  const [openAt, setOpenAt] = useState<string>();
  const open = openAt === location.key;
  const setOpen = (value: boolean) => setOpenAt(value ? location.key : undefined);
  const [collapsed, setCollapsed] = useState(false);
  if (mobile) return <Sheet open={open} onOpenChange={setOpen}>
    <SheetTrigger render={<Button variant="outline" className="min-h-11 self-start" />}><PanelLeftOpenIcon aria-hidden />{label}</SheetTrigger>
    <SheetContent side="left" className="w-[min(90vw,24rem)]! overflow-y-auto [&_a]:min-h-11 [&_button]:min-h-11 [&_button]:min-w-11 [&_input]:min-h-11">
      <SheetHeader><SheetTitle>{label}</SheetTitle></SheetHeader>
      <div className="px-4 pb-4" onClick={event => {
        if ((event.target as HTMLElement).closest('a[href^="/"]:not([download])')) setOpen(false);
      }}>{children}</div>
    </SheetContent>
  </Sheet>;
  if (hidden) return null;
  return <div className="shrink-0">
    <Button variant="ghost" size="sm" className="mb-2" aria-label={`${collapsed ? "Show" : "Hide"} ${label.toLowerCase()}`} aria-expanded={!collapsed} onClick={() => setCollapsed(!collapsed)}>
      {collapsed ? <PanelLeftOpenIcon aria-hidden /> : <PanelLeftCloseIcon aria-hidden />}{label}
    </Button>
    {!collapsed && children}
  </div>;
}
