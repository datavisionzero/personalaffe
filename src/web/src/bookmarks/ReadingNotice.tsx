import { Button } from "@/components/ui/button";
import { Refused } from "@/shared/Form";
import type { useBookmarkReading } from "./useBookmarkReading";
export function ReadingNotice({ reading }: { reading: ReturnType<typeof useBookmarkReading> }) {
  return <><Refused>{reading.error}</Refused>{reading.message && <p role="status" className="text-sm">{reading.message} {reading.canUndo && <Button variant="outline" size="sm" disabled={reading.working} onClick={() => void reading.undo()}>Undo mark as read</Button>}</p>}</>;
}
