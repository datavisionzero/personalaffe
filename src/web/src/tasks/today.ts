/**
 * Today, in the browser's own calendar, spelled the way the instance spells a
 * day (`docs/api.md`, Tasks).
 *
 * <b>It is a string and never a `Date`.</b> A due date is a calendar day and
 * not a moment; `new Date("2026-09-14")` is midnight UTC, which is the
 * thirteenth for everybody west of it. Comparing two of these strings is the
 * whole of "is this overdue", and it is right in every timezone because neither
 * side of the comparison ever became a time.
 */
export function todayHere(when: Date = new Date()): string {
  const month = `${when.getMonth() + 1}`.padStart(2, "0");
  const day = `${when.getDate()}`.padStart(2, "0");

  return `${when.getFullYear()}-${month}-${day}`;
}
