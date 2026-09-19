import { SettingsIcon, Trash2Icon } from "lucide-react";
import { NavLink, useLocation } from "react-router";

import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";
import { Skeleton } from "@/components/ui/skeleton";
import { home } from "./applications";
import { Mark } from "./Mark";
import { useAppearance } from "./useAppearance";
import type { TheApplications } from "./useApplications";

/**
 * The left navigation: home, the applications this workspace has switched on,
 * and the settings behind them. On a phone the same component is the drawer the
 * header button opens — one application, not a reduced one.
 *
 * <b>What is not offered here is not reachable by clicking</b>, and that is the
 * whole of what the switch does to the navigation. An application that is
 * switched off, or that this credential may not read, is absent rather than
 * greyed out: a disabled row is a promise that something will happen if it is
 * pressed, and nothing will.
 */
export function AppSidebar({ applications }: { applications: TheApplications }) {
  const { setOpenMobile } = useSidebar();
  const { pathname } = useLocation();
  const { appearance, name, known } = useAppearance();

  function walked() {
    // The drawer closes behind whoever walked through it. On a desk there is
    // no drawer and this does nothing.
    setOpenMobile(false);
  }

  function active(path: string) {
    return path === "/" ? pathname === "/" : pathname === path || pathname.startsWith(`${path}/`);
  }

  return (
    <Sidebar collapsible="offcanvas">
      <SidebarHeader className="px-3 pt-3">
        <div className="flex h-5 items-center gap-2 px-1 text-sm font-semibold">
          {/* Its space is held rather than filled with the product name: an
              instance with a title must not say `personalaffe` first. */}
          {known ? (
            <>
              <Mark colour={appearance.colour} shape={appearance.shape} title={appearance.title} />
              <span className="truncate">{name}</span>
            </>
          ) : (
            <Skeleton className="h-4 w-28" />
          )}
        </div>
      </SidebarHeader>

      <SidebarContent>
        <nav aria-label="The workspace">
          <SidebarGroup>
            <SidebarGroupContent>
              <SidebarMenu>
                <SidebarMenuItem>
                  <SidebarMenuButton
                    isActive={active(home.path)}
                    render={<NavLink to={home.path} onClick={walked} />}
                  >
                    <home.icon />
                    <span>{home.label}</span>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>

          <SidebarGroup>
            <SidebarGroupLabel>Applications</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {applications.asked.at === "asking" && (
                  // Not the four names with a shrug: until the instance has
                  // said, which of them this workspace has is not known, and
                  // drawing all four would take one away a moment later.
                  <SidebarMenuItem>
                    <div className="flex flex-col gap-1.5 px-2 py-1.5" aria-busy>
                      <Skeleton className="h-4 w-24" />
                      <Skeleton className="h-4 w-20" />
                    </div>
                  </SidebarMenuItem>
                )}

                {applications.asked.at === "known" && applications.offered.length === 0 && (
                  <SidebarMenuItem>
                    <p className="text-muted-foreground px-2 py-1.5 text-xs text-balance">
                      Every application is switched off, or out of this credential's reach.
                    </p>
                  </SidebarMenuItem>
                )}

                {applications.offered.map((application) => (
                  <SidebarMenuItem key={application.name}>
                    <SidebarMenuButton
                      isActive={active(application.path)}
                      render={<NavLink to={application.path} onClick={walked} />}
                    >
                      <application.icon />
                      <span>{application.label}</span>
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                ))}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>

          <SidebarGroup>
            <SidebarGroupLabel>The instance</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                <SidebarMenuItem>
                  <SidebarMenuButton
                    isActive={active("/trash")}
                    render={<NavLink to="/trash" onClick={walked} />}
                  >
                    <Trash2Icon />
                    <span>Trash</span>
                  </SidebarMenuButton>
                </SidebarMenuItem>
                <SidebarMenuItem>
                  <SidebarMenuButton
                    isActive={active("/settings")}
                    render={<NavLink to="/settings" onClick={walked} />}
                  >
                    <SettingsIcon />
                    <span>Settings</span>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        </nav>
      </SidebarContent>

      <SidebarFooter className="px-3 pb-3">
        <p className="text-muted-foreground px-1 text-xs text-balance">
          One owner, one workspace. Nobody else has an account here.
        </p>
      </SidebarFooter>
    </Sidebar>
  );
}
