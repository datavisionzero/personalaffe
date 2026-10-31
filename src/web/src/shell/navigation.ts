import { createContext, useContext, useEffect } from "react";
import { applicationAt, applications } from "./applications";

export type Destination = { path: string; title: string };
export type NavigationState = { favorites: Destination[]; recent: Destination[] };
export const emptyNavigation: NavigationState = { favorites: [], recent: [] };

// Only known internal destinations; never search terms or private bookmark folders.
export function safeDestination(value: unknown): value is Destination {
  if (!value || typeof value !== "object") return false;
  const { path, title } = value as Destination;
  if (typeof path !== "string" || typeof title !== "string" || !title.trim() || title.length > 160) return false;
  return applications.some(app => path === app.path ||
    (["knowledge", "tasks", "files"].includes(app.name) && new RegExp(`^${app.path}/[a-zA-Z0-9-]+$`).test(path)));
}
export function readNavigation(key: string): NavigationState {
  try {
    const saved = JSON.parse(localStorage.getItem(key) ?? "null") as NavigationState | null;
    const clean = (items: unknown, max: number) => Array.isArray(items)
      ? items.filter(safeDestination).filter((item, i, all) => all.findIndex(other => other.path === item.path) === i).slice(0, max) : [];
    return { favorites: clean(saved?.favorites, 40), recent: clean(saved?.recent, 12) };
  } catch { return emptyNavigation; }
}
export type Navigation = NavigationState & {
  remember: (destination: Destination) => void;
  forget: (path: string) => void;
  toggle: (destination: Destination) => void;
  move: (path: string, delta: number) => void;
  clearRecent: () => void;
  target: (path: string) => string;
  saved: boolean;
};
export const NavigationContext = createContext<Navigation | undefined>(undefined);
export function useNavigation() { return useContext(NavigationContext); }

/** Screens register only successfully loaded destinations, with their real titles. */
export function useDestination(path: string, title: string | undefined, missing = false) {
  const navigation = useNavigation();
  const remember = navigation?.remember;
  const forget = navigation?.forget;
  useEffect(() => {
    if (title) remember?.({ path, title: title.slice(0, 160) });
    else if (missing) forget?.(path);
  }, [path, title, missing, remember, forget]);
}
export function sameApplication(a: string, b: string) {
  return applicationAt(a)?.name === applicationAt(b)?.name;
}
