import { useInstance } from "@/shell/useInstance";

/**
 * The frame, and today the frame is the whole screen.
 *
 * What it draws is the one thing the foundation can honestly show: that this
 * browser, this application and this instance are talking to each other over
 * the API both clients use. It reads the answer rather than claiming it — a
 * screen that says "connected" without having asked is worth nothing on the
 * day it is wrong.
 *
 * The application this becomes — the home page, the switcher, the four
 * applications and the Markdown editor — is PERSONAL-E4 and later. Their
 * screens land in a folder each beside `shell/`, and `shell/` grows the routes
 * and the navigation that reach them (`docs/codebase.md`).
 */
export function App() {
  const { instance, ask } = useInstance();

  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col justify-center gap-8 px-5 py-12">
      <header className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">personalaffe</h1>
        <p className="text-muted text-balance">
          A private workspace belonging to one person. This is the foundation: there is no owner, no
          sign-in and nothing stored yet.
        </p>
      </header>

      <section aria-labelledby="instance" className="border-line flex flex-col gap-3 rounded-lg border p-5">
        <h2 id="instance" className="text-sm font-medium tracking-wide uppercase">
          The instance
        </h2>

        {instance.state === "asking" && (
          <p className="text-muted" role="status">
            Asking the instance…
          </p>
        )}

        {instance.state === "answered" && (
          <p role="status">
            It answered, and it is version{" "}
            <span className="text-accent font-mono font-medium">{instance.version}</span>.
          </p>
        )}

        {instance.state === "refused" && (
          <p role="alert" className="text-balance">
            The instance refused: {instance.reason}
          </p>
        )}

        {instance.state === "unreachable" && (
          <p role="alert" className="text-balance">
            Nothing answered at this address. The instance may not be running: {instance.reason}
          </p>
        )}

        {instance.state !== "asking" && (
          <div>
            <button
              type="button"
              onClick={ask}
              className="border-line hover:border-accent focus-visible:outline-accent rounded-md border px-3 py-1.5 text-sm focus-visible:outline-2 focus-visible:outline-offset-2"
            >
              Ask again
            </button>
          </div>
        )}
      </section>

      <footer className="text-muted text-sm text-balance">
        Both this page and the <code className="font-mono">pea</code> command reach the same HTTP API,
        and the contract they are generated from is <code className="font-mono">docs/api/openapi.json</code>.
      </footer>
    </main>
  );
}
