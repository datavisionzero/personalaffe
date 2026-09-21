import { useEffect, useRef, useState, type FormEvent } from "react";

import { api, confirmedUnlocked } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused } from "@/shared/Form";
import { refusal } from "./useSession";

export function InactivityLock({
  reason,
  onUnlocked,
  onRetry,
  onSignOut,
}: {
  reason?: string;
  onUnlocked: () => void;
  onRetry: () => void;
  onSignOut: () => Promise<void>;
}) {
  const [proof, setProof] = useState<"pin" | "password">("pin");
  const [value, setValue] = useState("");
  const [message, setMessage] = useState(reason);
  const [working, setWorking] = useState(false);
  const screenRef = useRef<HTMLElement | null>(null);

  // Base UI portals are body siblings of #root. Seal those too, including one
  // created by a response that arrives after the lock screen appeared.
  useEffect(() => {
    document.documentElement.dataset.sessionLocked = "true";
    let root = screenRef.current as HTMLElement | null;
    while (root?.parentElement && root.parentElement !== document.body) root = root.parentElement;
    const sealed = new Map<HTMLElement, { inert: boolean; ariaHidden: string | null; hidden: boolean }>();
    const seal = (node: Node) => {
      if (!(node instanceof HTMLElement) || node === root || node.parentElement !== document.body) return;
      sealed.set(node, { inert: node.inert, ariaHidden: node.getAttribute("aria-hidden"), hidden: node.hidden });
      node.inert = true;
      node.hidden = true;
      node.setAttribute("aria-hidden", "true");
    };
    for (const child of Array.from(document.body.children)) seal(child);
    const observer = new MutationObserver((records) => {
      for (const record of records) for (const node of Array.from(record.addedNodes)) seal(node);
    });
    observer.observe(document.body, { childList: true });
    return () => {
      observer.disconnect();
      delete document.documentElement.dataset.sessionLocked;
      for (const [node, before] of sealed) {
        node.inert = before.inert;
        node.hidden = before.hidden;
        if (before.ariaHidden === null) node.removeAttribute("aria-hidden");
        else node.setAttribute("aria-hidden", before.ariaHidden);
      }
    };
  }, []);

  async function unlock(event: FormEvent) {
    event.preventDefault();
    setMessage(undefined);
    setWorking(true);
    try {
      const answer = await api.POST("/api/session/lock/unlock", {
        body: proof === "pin" ? { pin: value, password: null } : { pin: null, password: value },
      });
      if (!answer.error) {
        setValue("");
        confirmedUnlocked();
        onUnlocked();
        return;
      }

      const said = refusal(answer.error, answer.response.status);
      const retry = answer.response.headers.get("Retry-After");
      setMessage(retry ? `${said.message} Try again in ${retry} seconds.` : said.message);
      setValue("");
    } catch {
      setMessage("The instance could not confirm the unlock. The workspace remains locked.");
    } finally {
      setWorking(false);
    }
  }

  return (
    <main ref={screenRef} className="mx-auto flex min-h-dvh max-w-md flex-col justify-center gap-6 px-5 py-12">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">Workspace locked</h1>
        <p className="text-muted-foreground text-sm text-balance">
          Your signed-in browser was inactive. Unlocking does not turn this protection off.
        </p>
      </div>

      <form className="flex flex-col gap-4" onSubmit={unlock}>
        {proof === "pin" ? (
          <Field label="PIN">
            <Input
              type="password"
              name="pin"
              inputMode="numeric"
              pattern="[0-9]{4,6}"
              minLength={4}
              maxLength={6}
              autoComplete="off"
              autoFocus
              required
              value={value}
              onChange={(event) => setValue(event.target.value)}
            />
          </Field>
        ) : (
          <Field label="Current password">
            <Input
              type="password"
              name="password"
              autoComplete="current-password"
              autoFocus
              required
              value={value}
              onChange={(event) => setValue(event.target.value)}
            />
          </Field>
        )}

        <Refused>{message}</Refused>
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={working}>{working ? "Unlocking…" : "Unlock"}</Button>
          <Button
            type="button"
            variant="ghost"
            onClick={() => {
              setProof((current) => current === "pin" ? "password" : "pin");
              setValue("");
              setMessage(undefined);
            }}
          >
            {proof === "pin" ? "Use my password" : "Use my PIN"}
          </Button>
        </div>
      </form>

      {reason && (
        <Button type="button" variant="outline" onClick={onRetry}>Check the connection again</Button>
      )}
      <Button type="button" variant="link" onClick={() => void onSignOut()}>Sign out</Button>
    </main>
  );
}
