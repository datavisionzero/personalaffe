import { api } from "@/api/client";
import { Setup } from "@/session/Setup";
import { SignIn } from "@/session/SignIn";
import { useSession } from "@/session/useSession";
import { Shell } from "@/shell/Shell";
import { useInstance } from "@/shell/useInstance";

/**
 * The frame, and the frame is the door and what is behind it.
 *
 * Nothing is drawn until the instance has said whether it has an owner and
 * whether this browser is signed in. Drawing a sign-in form at a fresh
 * installation would be offering a door with no lock and no key.
 *
 * Behind the door is `Shell`, which is the workspace: the sidebar, the
 * applications, the palette and the routes. The door itself is three centred
 * screens with no navigation on them — there is nowhere to go from a screen
 * somebody has not come through yet.
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
        <p className="text-muted-foreground" role="status">
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

  return <Shell me={session.me} onSignedOut={() => void signOut()} />;
}

/** The screens somebody sees before they are in: centred, and nothing else on them. */
function Frame({ children }: { children: React.ReactNode }) {
  const { instance } = useInstance();

  return (
    <main className="mx-auto flex min-h-dvh max-w-md flex-col justify-center gap-8 px-5 py-12">
      <header className="flex flex-col gap-2">
        <h1 className="text-3xl font-semibold tracking-tight">personalaffe</h1>
        <p className="text-muted-foreground text-balance">
          A private workspace belonging to one person.
        </p>
      </header>

      {children}

      <footer className="text-muted-foreground text-sm">
        {instance.state === "answered" && (
          <span>
            This instance is version{" "}
            <span className="text-brand font-mono">{instance.version}</span>.
          </span>
        )}
      </footer>
    </main>
  );
}
