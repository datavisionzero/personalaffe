import { useState } from "react";
import { api, describe, guardedBy, versionOf } from "@/api/client";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import type { Bookmark } from "./useBookmarks";

type Undo = { row: Bookmark; queuedAt: string };
export function useBookmarkReading(changed: () => void) {
  const privacy = useBookmarkPrivacy();
  const [working, setWorking] = useState(false);
  const [undoRows, setUndoRows] = useState<Undo[]>([]);
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<string>();
  async function change(rows: Bookmark[], later: boolean) {
    setWorking(true); setError(undefined); setUndoRows([]);
    const failed: Bookmark[] = []; const undo: Undo[] = []; const errors: string[] = [];
    for (const row of rows) {
      try {
        const result = await api.PUT("/api/bookmarks/{id}/reading", { headers: privacy.headers,
          params: { path: { id: row.id }, ...guardedBy(versionOf(row.updated_at)) }, body: { read_later: later, queued_at: null } });
        if (result.data) { if (!later && row.read_later_at) undo.push({ row: result.data, queuedAt: row.read_later_at }); }
        else { failed.push(row); errors.push(`${row.title}: ${describe(result.error, result.response.status)}`); }
      } catch { failed.push(row); errors.push(`${row.title}: The instance did not answer.`); }
    }
    setUndoRows(undo); setMessage(`${rows.length - failed.length} ${later ? "saved for later" : "marked as read"}; ${failed.length} failed.`);
    setError(errors.join(" ")); setWorking(false); changed(); return failed;
  }
  async function undo() {
    setWorking(true); setError(undefined);
    const failed: Undo[] = []; const errors: string[] = [];
    for (const item of undoRows) {
      try {
        const result = await api.PUT("/api/bookmarks/{id}/reading", { headers: privacy.headers,
          params: { path: { id: item.row.id }, ...guardedBy(versionOf(item.row.updated_at)) }, body: { read_later: true, queued_at: item.queuedAt } });
        if (!result.data) { failed.push(item); errors.push(describe(result.error, result.response.status)); }
      } catch { failed.push(item); errors.push("The instance did not answer."); }
    }
    setMessage(`${undoRows.length - failed.length} restored to the reading list; ${failed.length} failed.`);
    setUndoRows(failed); setError(errors.join(" ")); setWorking(false); changed();
  }
  return { change, undo, canUndo: undoRows.length > 0, working, message, error };
}
