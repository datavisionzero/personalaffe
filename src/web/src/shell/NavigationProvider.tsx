import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { NavigationContext, readNavigation, safeDestination, sameApplication, type Destination } from "./navigation";

export function NavigationProvider({ account, children }: { account: string; children: ReactNode }) {
  const key = `personalaffe-navigation-v1:${account}`;
  const [state, setState] = useState(() => readNavigation(key));
  const [saved, setSaved] = useState(true);
  useEffect(() => {
    // Report the outcome of synchronizing with browser storage, not derived UI state.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    try { localStorage.setItem(key, JSON.stringify(state)); setSaved(true); }
    catch { setSaved(false); }
  }, [key, state]);
  const remember = useCallback((destination: Destination) => {
    if (!safeDestination(destination)) return;
    setState(previous => {
      if (previous.recent[0]?.path === destination.path && previous.recent[0]?.title === destination.title) return previous;
      return {
        favorites: previous.favorites.map(item => item.path === destination.path ? destination : item),
        recent: [destination, ...previous.recent.filter(item => item.path !== destination.path)].slice(0, 12),
      };
    });
  }, []);
  const forget = useCallback((path: string) => setState(previous => {
    if (!previous.recent.some(item => item.path === path) && !previous.favorites.some(item => item.path === path)) return previous;
    return { favorites: previous.favorites.filter(item => item.path !== path), recent: previous.recent.filter(item => item.path !== path) };
  }), []);
  const value = useMemo(() => ({ ...state, saved, remember, forget,
    toggle: (destination: Destination) => {
      if (!safeDestination(destination)) return;
      setState(previous => ({ ...previous, favorites: previous.favorites.some(item => item.path === destination.path)
        ? previous.favorites.filter(item => item.path !== destination.path)
        : [...previous.favorites, destination].slice(0, 40) }));
    },
    move: (path: string, delta: number) => setState(previous => {
      const favorites = [...previous.favorites];
      const from = favorites.findIndex(item => item.path === path), to = from + delta;
      if (from < 0 || to < 0 || to >= favorites.length) return previous;
      [favorites[from], favorites[to]] = [favorites[to], favorites[from]];
      return { ...previous, favorites };
    }),
    clearRecent: () => setState(previous => ({ ...previous, recent: [] })),
    target: (path: string) => state.recent.find(item => sameApplication(item.path, path))?.path ?? path,
  }), [state, saved, remember, forget]);
  return <NavigationContext.Provider value={value}>{children}</NavigationContext.Provider>;
}
