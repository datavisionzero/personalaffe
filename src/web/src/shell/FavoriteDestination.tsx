import { useLocation } from "react-router";
import { StarIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useNavigation } from "./navigation";
import type { TheApplications } from "./useApplications";
import { applicationAt } from "./applications";

export function FavoriteDestination({ applications }: { applications: TheApplications }) {
  const { pathname, search } = useLocation();
  const navigation = useNavigation();
  const app = applicationAt(pathname);
  if (!navigation || !app || !applications.offered.some(item => item.name === app.name)) return null;
  const destination = pathname === app.path && !search ? { path: app.path, title: app.label }
    : navigation.recent.find(item => item.path === pathname);
  if (!destination || (app.name === "bookmarks" && (search || pathname !== app.path))) return null;
  const pinned = navigation.favorites.some(item => item.path === destination.path);
  return <Button variant="ghost" size="icon-sm" className="min-h-11 min-w-11 shrink-0" aria-label={`Favorite ${destination.title}`} aria-pressed={pinned} disabled={!pinned && navigation.favorites.length >= 40} title={pinned ? "Remove from home favorites" : navigation.favorites.length >= 40 ? "Remove a favorite on Home before adding another" : "Add to home favorites"} onClick={() => navigation.toggle(destination)}><StarIcon className={pinned ? "fill-current" : ""} /></Button>;
}
