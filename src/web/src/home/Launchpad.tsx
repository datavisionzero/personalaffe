import { useState } from "react";
import { Link } from "react-router";
import { ArrowLeftIcon, ArrowRightIcon, StarIcon, XIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { applicationAt } from "@/shell/applications";
import { useNavigation, type Destination } from "@/shell/navigation";
import type { TheApplications } from "@/shell/useApplications";

export function Launchpad({ applications }: { applications: TheApplications }) {
  const navigation = useNavigation();
  const [editing, setEditing] = useState(false);
  if (!navigation) return null;
  const offered = (item: Destination) => applications.offered.some(app => app.name === applicationAt(item.path)?.name);
  const favorites = navigation.favorites.filter(offered);
  const recent = navigation.recent.filter(offered).slice(0, 6);
  function moveFavorite(item: Destination, delta: number) {
    if (!navigation) return;
    const neighbor = favorites[favorites.findIndex(candidate => candidate.path === item.path) + delta];
    if (neighbor) navigation.move(item.path, navigation.favorites.findIndex(candidate => candidate.path === neighbor.path) - navigation.favorites.findIndex(candidate => candidate.path === item.path));
  }
  return <div className="flex flex-col gap-6">
    <section aria-labelledby="home-favorites">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 id="home-favorites" className="font-semibold">Favorites</h2>
        {favorites.length > 0 && <Button variant="ghost" size="sm" aria-pressed={editing} onClick={() => setEditing(!editing)}>{editing ? "Done" : "Arrange favorites"}</Button>}
      </div>
      {navigation.favorites.length >= 40 && <p className="mb-2 text-sm text-muted-foreground">All 40 favorite slots are used. Remove a favorite before adding another.</p>}
      {favorites.length === 0 ? <p className="text-sm text-muted-foreground">Star an application below, or use the star in the header to keep a page, list or folder here.</p>
        : <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{favorites.map((item, index) => <li key={item.path} className="min-w-0 rounded-lg border p-2">
          <DestinationLink item={item} />
          {editing && <div className="mt-1 flex gap-1">
            <Button variant="ghost" className="min-h-11 min-w-11" size="icon-sm" aria-label={`Move ${item.title} earlier`} disabled={index === 0} onClick={() => moveFavorite(item, -1)}><ArrowLeftIcon /></Button>
            <Button variant="ghost" className="min-h-11 min-w-11" size="icon-sm" aria-label={`Move ${item.title} later`} disabled={index === favorites.length - 1} onClick={() => moveFavorite(item, 1)}><ArrowRightIcon /></Button>
            <Button variant="ghost" className="ml-auto min-h-11 min-w-11" size="icon-sm" aria-label={`Remove ${item.title} from favorites`} onClick={() => navigation.toggle(item)}><XIcon /></Button>
          </div>}
        </li>)}</ul>}
    </section>
    <section aria-labelledby="home-apps">
      <h2 id="home-apps" className="mb-2 font-semibold">Applications</h2>
      <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{applications.offered.map(app => <li key={app.name} className="flex min-w-0 items-center rounded-lg border p-2">
        <Link to={navigation.target(app.path)} className="flex min-h-11 min-w-0 flex-1 items-center gap-3 rounded-md px-2 text-sm hover:bg-accent"><app.icon aria-hidden className="size-5 shrink-0" /><span className="truncate">{app.label}</span></Link>
        <Button variant="ghost" size="icon-sm" className="min-h-11 min-w-11" aria-label={`Favorite ${app.label}`} disabled={navigation.favorites.length >= 40 && !navigation.favorites.some(item => item.path === app.path)} aria-pressed={navigation.favorites.some(item => item.path === app.path)} onClick={() => navigation.toggle({ path: app.path, title: app.label })}><StarIcon className={navigation.favorites.some(item => item.path === app.path) ? "fill-current" : ""} /></Button>
      </li>)}</ul>
    </section>
    {recent.length > 0 && <section aria-labelledby="home-recent">
      <div className="mb-2 flex items-center justify-between gap-2"><h2 id="home-recent" className="font-semibold">Recently visited</h2><Button variant="ghost" size="sm" onClick={navigation.clearRecent}>Clear history</Button></div>
      <ul className="grid gap-1 sm:grid-cols-2 xl:grid-cols-3">{recent.map(item => <li key={item.path} className="min-w-0"><DestinationLink item={item} /></li>)}</ul>
    </section>}
    <p className="text-xs text-muted-foreground">Favorites and recent visits are saved in this browser. Private bookmark destinations are never saved.{!navigation.saved && <span role="status"> Browser storage is unavailable; changes will last only until you leave this session.</span>}</p>
  </div>;
}
function DestinationLink({ item }: { item: Destination }) {
  const app = applicationAt(item.path)!;
  return <Link to={item.path} className="flex min-h-11 min-w-0 items-center gap-3 rounded-md p-2 text-sm hover:bg-accent">
    <app.icon aria-hidden className="size-4 shrink-0" /><span className="min-w-0"><span className="block truncate">{item.title}</span>{item.path !== app.path && <span className="block text-xs text-muted-foreground">{app.label}</span>}</span>
  </Link>;
}
