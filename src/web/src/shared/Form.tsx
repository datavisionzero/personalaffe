import type { ReactNode } from "react";

/**
 * The two pieces of a form that are not one of the owned primitives: the label
 * with its hint, and the place a refusal is said out loud.
 *
 * Everything else a form is made of comes from `components/ui/`
 * (`docs/codebase.md`) — `Input`, `Button` — which is why this file is two
 * components and not a widget library. A `<select>` is the one control the
 * adopted set does not carry, so the class it wears is here, beside the two.
 */
export function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: ReactNode;
  children: ReactNode;
}) {
  // The hint is beside the label and not inside it. A label wrapping its input
  // is what associates the two without an id to keep in step — and everything
  // inside it becomes the field's name, so a sentence of explanation in there
  // is a sentence a screen reader says instead of "Email address".
  return (
    <div className="flex flex-col gap-1.5">
      <label className="flex flex-col gap-1.5 text-sm">
        <span className="font-medium">{label}</span>
        {children}
      </label>
      {hint !== undefined && (
        <span className="text-muted-foreground text-xs text-balance">{hint}</span>
      )}
    </div>
  );
}

/**
 * A native `<select>`, wearing what `Input` wears. The adopted set has a
 * `Picker`, and a closed set of three words on a settings screen does not need
 * a listbox in a popup to be usable with a keyboard.
 */
export const selectClass =
  "h-8 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-1 text-sm " +
  "transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 " +
  "focus-visible:ring-ring/50 disabled:opacity-50 dark:bg-input/30";

/** What went wrong, where a screen reader will be told about it. */
export function Refused({ children }: { children: ReactNode }) {
  return children === undefined || children === null || children === "" ? null : (
    <p role="alert" className="text-destructive text-sm text-balance">
      {children}
    </p>
  );
}
