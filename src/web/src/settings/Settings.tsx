import { NavLink, Navigate, Route, Routes } from "react-router";

import { Agents } from "@/agents/Agents";
import { Security } from "@/security/Security";
import { Denied } from "@/shell/States";
import type { TheApplications } from "@/shell/useApplications";
import type { Me } from "@/session/useSession";
import { cn } from "@/lib/utils";
import { ApplicationSwitches } from "./ApplicationSwitches";
import { AppearanceSettings } from "./AppearanceSettings";
import { HomeSettings } from "./HomeSettings";

/**
 * Everything about the instance rather than about its content: which
 * applications it has, what it is called, how the owner gets in, and what the
 * owner has let in beside them.
 *
 * A route each rather than tabs on one, so that a link to the agent
 * tokens is a link to the agent tokens. The list of them is the navigation of
 * this area and it stays put while they are walked between
 * (`docs/mvp-plan.md`, PERSONAL-E4: area-owned navigation).
 *
 * <b>Security and agent access are the owner's alone</b>, which the API
 * enforces and this reflects: an agent that reached these addresses would see
 * a refusal where the owner sees a screen, so it is told instead.
 */
export function Settings({ me, applications }: { me: Me; applications: TheApplications }) {
  const areas = [
    { path: "/settings/applications", label: "Applications" },
    { path: "/settings/home", label: "Home page" },
    { path: "/settings/appearance", label: "Appearance" },
    { path: "/settings/security", label: "Security" },
    { path: "/settings/agents", label: "Agent access" },
  ];

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-5 md:p-8">
      <h1 className="text-xl font-semibold tracking-tight">Settings</h1>

      <nav aria-label="Settings" className="flex flex-wrap gap-1 border-b">
        {areas.map((area) => (
          <NavLink
            key={area.path}
            to={area.path}
            className={({ isActive }) =>
              cn(
                "rounded-t-md px-3 py-2 text-sm",
                isActive
                  ? "border-brand text-foreground border-b-2 font-medium"
                  : "text-muted-foreground hover:text-foreground",
              )
            }
          >
            {area.label}
          </NavLink>
        ))}
      </nav>

      <Routes>
        <Route index element={<Navigate to="/settings/applications" replace />} />
        <Route
          path="applications"
          element={<ApplicationSwitches me={me} applications={applications} />}
        />
        <Route path="home" element={<HomeSettings owner={me.kind === "owner"} />} />
        <Route path="appearance" element={<AppearanceSettings owner={me.kind === "owner"} />} />
        <Route
          path="security"
          element={me.kind === "owner" ? <Security /> : <Denied what="Security" />}
        />
        <Route
          path="agents"
          element={me.kind === "owner" ? <Agents /> : <Denied what="Agent access" />}
        />
        <Route path="*" element={<Navigate to="/settings/applications" replace />} />
      </Routes>
    </main>
  );
}
