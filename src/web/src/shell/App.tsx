import { api } from "@/api/client";
import { Agents } from "@/agents/Agents";
import { Security } from "@/security/Security";
import { Setup } from "@/session/Setup";
import { SignIn } from "@/session/SignIn";
import { useSession } from "@/session/useSession";
import { Button } from "@/shared/Form";
import { useInstance } from "@/shell/useInstance";

/**
 * The frame, and today the frame is the door and what is behind it.
 *
 * What is behind it is the owner's own settings and nothing else: the
 * workspace — the home page, the switcher, the four applications and the
 * Markdown editor — is PERSONAL-E4 and later. Their screens land in a folder
 * each beside `shell/`, and `shell/` grows the routes and the navigation that
 * reach them (`docs/codebase.md`).
 *
 * Nothing is drawn until the instance has said whether it has an owner and
 * whether this browser is signed in. Drawing a sign-in form at a fresh
 * installation would be offering a door with no lock and no key.
 */
export function App() {
  const { session, signedIn, signedOut } = useSession();

  async function signOut() {
    await api.DELETE("/api/session");
    signedOut();
  }

  if (session.state === "asking") {
    return (
      <Frame>
        <p className="text-muted" role="status">
          Asking the instance…
        </p>
      </Frame>
    );
  }

  if (session.state === "unreachable") {
    return (
      <Frame>
        <p role="alert" className="text-balance">
          Nothing answered at this address. The instance may not be running: {session.reason}
        </p>
      </Frame>
    );
  }

  if (session.state === "setup") {
    return (
      <Frame>
        <Setup onSignedIn={signedIn} />
      </Frame>
    );
  }

  if (session.state === "signed-out") {
    return (
      <Frame>
        <SignIn onSignedIn={signedIn} />
      </Frame>
    );
  }

  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col gap-10 px-5 py-12">
      <header className="flex flex-wrap items-baseline justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">personalaffe</h1>
          <p className="text-muted text-sm">{session.me.email}</p>
        </div>

        <Button type="button" onClick={() => void signOut()}>
          Sign out
        </Button>
      </header>

      <p className="text-muted text-sm text-balance">
        There is nothing in this workspace yet. The Scratchpad, the Knowledge base, Tasks and Files
        arrive in the epics after this one; what is here is who may reach them.
      </p>

      <Security />
      <Agents />
    </main>
  );
}

/** The screens somebody sees before they are in: centred, and nothing else on them. */
function Frame({ children }: { children: React.ReactNode }) {
  const { instance } = useInstance();

  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col justify-center gap-8 px-5 py-12">
      <header className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">personalaffe</h1>
        <p className="text-muted text-balance">A private workspace belonging to one person.</p>
      </header>

      {children}

      <footer className="text-muted text-sm">
        {instance.state === "answered" && (
          <span>
            This instance is version{" "}
            <span className="text-accent font-mono">{instance.version}</span>.
          </span>
        )}
      </footer>
    </main>
  );
}
