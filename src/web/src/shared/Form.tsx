import type { ReactNode } from "react";

/**
 * The three pieces every form on these screens is made of. They are here rather
 * than in `components/ui/` because they are three elements and a class list:
 * the owned primitives of the affe stack arrive with the shell that needs them
 * (`docs/codebase.md`), and inventing them early would be inventing them twice.
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
      {hint !== undefined && <span className="text-muted text-xs text-balance">{hint}</span>}
    </div>
  );
}

export const inputClass =
  "border-line focus-visible:outline-accent rounded-md border bg-transparent px-3 py-2 " +
  "text-sm focus-visible:outline-2 focus-visible:outline-offset-1";

export function Button({
  children,
  kind = "ordinary",
  ...rest
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { kind?: "primary" | "ordinary" | "quiet" }) {
  const look = {
    primary: "bg-accent text-paper border-accent",
    ordinary: "border-line hover:border-accent",
    quiet: "border-transparent text-muted hover:text-ink",
  }[kind];

  return (
    <button
      {...rest}
      className={
        `focus-visible:outline-accent rounded-md border px-3 py-1.5 text-sm ` +
        `focus-visible:outline-2 focus-visible:outline-offset-2 disabled:opacity-50 ${look}`
      }
    >
      {children}
    </button>
  );
}

/** What went wrong, where a screen reader will be told about it. */
export function Refused({ children }: { children: ReactNode }) {
  return children === undefined || children === null || children === "" ? null : (
    <p role="alert" className="text-sm text-balance">
      {children}
    </p>
  );
}
