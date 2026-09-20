import { api, type Schemas } from "@/api/client";
import { useAsk } from "@/shared/ask";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";

export type Bookmark = Schemas["BookmarkResponse"];
export type Folder = Schemas["BookmarkFolderResponse"];

export function useBookmarkDashboard(hold = false) {
  const privacy = useBookmarkPrivacy();
  return useAsk(`/api/bookmarks/dashboard:${privacy.epoch}`, (signal) =>
    api.GET("/api/bookmarks/dashboard", { headers: privacy.headers, signal }), { hold });
}

export function useBookmarkFolders(hold = false) {
  const privacy = useBookmarkPrivacy();
  return useAsk<Schemas["BookmarkFoldersResponse"]>(`/api/bookmarks/folders:${privacy.epoch}`, async (signal) => {
    const items: Folder[] = [];
    let offset = 0;
    for (;;) {
      const answer = await api.GET("/api/bookmarks/folders", {
        params: { query: { offset, limit: 500 } }, headers: privacy.headers, signal,
      });
      if (!answer.data) return answer;
      items.push(...answer.data.items);
      if (answer.data.next_offset === null) return { data: { items, next_offset: null }, response: answer.response };
      offset = answer.data.next_offset;
    }
  }, { hold });
}

export function useBookmarkList(query: string, folder: string, favorites = false, sort = "rank", offset = 0, hold = false, tags: string[] = [], readLater = false) {
  const privacy = useBookmarkPrivacy();
  return useAsk(`/api/bookmarks:${privacy.epoch}:${query}:${folder}:${favorites}:${sort}:${offset}:${JSON.stringify(tags)}:${readLater}`, (signal) =>
    api.GET("/api/bookmarks", {
      params: { query: { q: query || undefined, folder: folder && folder !== "unsorted" ? folder : undefined,
        unsorted: folder === "unsorted", favorites, sort, offset, limit: 100, tag: tags, read_later: readLater } },
      headers: privacy.headers, signal,
    }), { hold });
}

export function folderPath(folder: Folder, all: Folder[]): string {
  const parts = [folder.name];
  let parent = folder.parent;
  const seen = new Set([folder.id]);
  while (parent && !seen.has(parent)) {
    seen.add(parent);
    const row = all.find((candidate) => candidate.id === parent);
    if (!row) break;
    parts.unshift(row.name);
    parent = row.parent;
  }
  return parts.join(" / ");
}

export function domainOf(url: string): string {
  try { return new URL(url).hostname.replace(/^www\./, ""); } catch { return url; }
}
