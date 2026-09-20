import { useMemo, useState, type ReactNode } from "react";
import { useLocation, useNavigate } from "react-router";
import { LockKeyholeIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

import { Context, useBookmarkPrivacy, type Privacy } from "./useBookmarkPrivacy";

/** Memory belongs to this mounted session in this tab; never persisted or broadcast. */
export function BookmarkPrivacyProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const location = useLocation();
  const [state, setState] = useState({ enabled: false, epoch: 0 });
  const value = useMemo<Privacy>(() => ({
    ...state,
    headers: state.enabled ? { "Personalaffe-Private": "true" } : {} as Record<string, string>,
    toggle: () => {
      if (state.enabled && location.pathname.startsWith("/bookmarks")) void navigate(location.pathname, { replace: true });
      setState((current) => ({ enabled: !current.enabled, epoch: current.epoch + 1 }));
    },
  }), [state, navigate, location.pathname]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}


export function PrivateSwitch() {
  const privacy = useBookmarkPrivacy();
  return <Button variant={privacy.enabled ? "secondary" : "outline"} aria-pressed={privacy.enabled}
    onClick={privacy.toggle} title="Private bookmarks are visible only in this tab, until reload or sign-out.">
    <LockKeyholeIcon aria-hidden className="size-4" /> Private mode {privacy.enabled ? "on" : "off"}
  </Button>;
}
