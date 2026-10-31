import { render, screen, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi, afterEach } from "vitest";
import { Link, MemoryRouter } from "react-router";
import { NavigationProvider } from "./NavigationProvider";
import { readNavigation, useDestination, useNavigation } from "./navigation";

function Visit({ path, title }: { path: string; title: string }) {
  useDestination(path, title);
  const navigation = useNavigation()!;
  return <>
    <button onClick={() => navigation.toggle({ path, title })}>Favorite</button>
    <button onClick={() => navigation.move(path, -1)}>Earlier</button>
    <button onClick={navigation.clearRecent}>Clear</button>
    <button onClick={() => navigation.forget(path)}>Deleted</button>
    <Link to={navigation.target("/knowledge")}>Resume Knowledge</Link>
    <ol aria-label="Favorites">{navigation.favorites.map(item => <li key={item.path}>{item.title}</li>)}</ol>
    <ol aria-label="Recent">{navigation.recent.map(item => <li key={item.path}>{item.title}</li>)}</ol>
    {!navigation.saved && <p>Storage unavailable</p>}
  </>;
}
const key = "personalaffe-navigation-v1:owner";
describe("personal navigation", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });
  it("rejects external, private and malformed saved destinations", () => {
    localStorage.setItem(key, JSON.stringify({ favorites: [
      { path: "https://example.com", title: "external" },
      { path: "/bookmarks?folder=private", title: "secret" },
      { path: "/knowledge/../../settings", title: "invalid" },
      { path: "/files/folder-1", title: "Folder" },
    ], recent: "invalid" }));
    expect(readNavigation(key)).toEqual({ favorites: [{ path: "/files/folder-1", title: "Folder" }], recent: [] });
    localStorage.setItem(key, "broken JSON");
    expect(readNavigation(key)).toEqual({ favorites: [], recent: [] });
  });
  it("resumes a loaded page, updates its title, persists favorites and forgets deleted targets", async () => {
    const view = (title: string) => <NavigationProvider account="owner"><Visit path="/knowledge/page-1" title={title} /></NavigationProvider>;
    const rendered = renderWithRouter(view("Project"));
    await userEvent.click(screen.getByRole("button", { name: "Favorite" }));
    expect(screen.getByRole("link", { name: "Resume Knowledge" })).toHaveAttribute("href", "/knowledge/page-1");
    rendered.rerender(<MemoryRouter>{view("Renamed project")}</MemoryRouter>);
    await waitFor(() => expect(readNavigation(key).favorites[0]?.title).toBe("Renamed project"));
    await userEvent.click(screen.getByRole("button", { name: "Deleted" }));
    expect(screen.getByRole("link", { name: "Resume Knowledge" })).toHaveAttribute("href", "/knowledge");
    expect(readNavigation(key).favorites).toHaveLength(0);
  });
  it("reorders favorites and clears history independently", async () => {
    localStorage.setItem(key, JSON.stringify({ favorites: [{ path: "/tasks", title: "Tasks" }, { path: "/knowledge", title: "Knowledge" }], recent: [] }));
    renderWithRouter(<NavigationProvider account="owner"><Visit path="/knowledge" title="Knowledge" /></NavigationProvider>);
    await userEvent.click(screen.getByRole("button", { name: "Earlier" }));
    expect(within(screen.getByRole("list", { name: "Favorites" })).getAllByRole("listitem").map(item => item.textContent)).toEqual(["Knowledge", "Tasks"]);
    await userEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(readNavigation(key).recent).toHaveLength(0);
    expect(readNavigation(key).favorites).toHaveLength(2);
  });
  it("keeps navigation usable when browser storage fails", async () => {
    vi.stubGlobal("localStorage", new Proxy(localStorage, {
      get(storage, key) {
        if (key === "setItem") return () => { throw new Error("full"); };
        const value = Reflect.get(storage, key, storage);
        return typeof value === "function" ? value.bind(storage) : value;
      },
    }));
    renderWithRouter(<NavigationProvider account="owner"><Visit path="/tasks" title="Tasks" /></NavigationProvider>);
    await userEvent.click(screen.getByRole("button", { name: "Favorite" }));
    expect(screen.getByText("Storage unavailable")).toBeInTheDocument();
    expect(within(screen.getByRole("list", { name: "Favorites" })).getByText("Tasks")).toBeInTheDocument();
  });
});

function renderWithRouter(element: React.ReactNode) { return render(<MemoryRouter>{element}</MemoryRouter>); }
