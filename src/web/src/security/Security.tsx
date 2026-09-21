import { useCallback, useEffect, useState, type FormEvent } from "react";

import { api, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused } from "@/shared/Form";
import { refusal } from "@/session/useSession";

type SecurityState = Schemas["SecurityResponse"];
type SignedInBrowser = Schemas["SessionResponse"];

/**
 * How the owner gets in, and the three ways of changing it.
 *
 * Every change here asks for the password again, however recently the browser
 * signed in: a screen somebody walked away from is otherwise enough to take the
 * instance over. The form asks for it each time rather than remembering it,
 * which is the same rule one level up.
 */
export function Security() {
  const [state, setState] = useState<SecurityState>();
  const [sessions, setSessions] = useState<SignedInBrowser[]>([]);
  const [refused, setRefused] = useState<string>();

  // Shown once, and then gone from the screen as well as from the instance.
  const [codes, setCodes] = useState<string[]>();
  const [offer, setOffer] = useState<Schemas["SecondFactorOfferResponse"]>();

  // The request, and nothing else: no state is written on the way in, so that
  // the effect below starts it without a render of its own, and an answer that
  // arrives after the screen is gone is dropped rather than written into a
  // state nobody is reading.
  const read = useCallback(() => {
    let abandoned = false;

    void (async () => {
      const [security, signedIn] = await Promise.all([
        api.GET("/api/security"),
        api.GET("/api/sessions"),
      ]);

      if (abandoned) {
        return;
      }

      if (security.data) {
        setState(security.data);
      }
      if (signedIn.data) {
        setSessions(signedIn.data);
      }
    })();

    return () => {
      abandoned = true;
    };
  }, []);

  useEffect(() => read(), [read]);

  async function begin(password: string) {
    setRefused(undefined);

    const answer = await api.POST("/api/security/second-factor", { body: { password } });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    // Offered, and nothing is in force until a code made from it comes back.
    setOffer(answer.data);
  }

  async function confirm(code: string) {
    setRefused(undefined);

    const answer = await api.POST("/api/security/second-factor/confirm", { body: { code } });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    setOffer(undefined);
    setCodes(answer.data.recovery_codes);
    read();
  }

  async function ask(address: string, password: string, then: (codes?: string[]) => void) {
    setRefused(undefined);

    const answer = await api.POST(address as "/api/security/second-factor/off", {
      body: { password },
    });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    then((answer.data as { recovery_codes?: string[] } | undefined)?.recovery_codes);
    read();
  }

  async function revoke(id: string) {
    await api.DELETE("/api/sessions/{id}", { params: { path: { id } } });
    read();
  }

  return (
    <section aria-labelledby="security" className="flex flex-col gap-6">
      <h2 id="security" className="text-lg font-semibold">
        How you sign in
      </h2>

      <Refused>{refused}</Refused>

      {state?.recovered_at && (
        <p className="border-border rounded-md border px-3 py-2 text-sm text-balance">
          This instance was recovered from its server on{" "}
          {new Date(state.recovered_at).toLocaleString()}. If that was not you, whoever has the
          machine has this workspace.
        </p>
      )}

      {codes && <RecoveryCodes codes={codes} onDone={() => setCodes(undefined)} />}

      {state && !state.second_factor_enabled && !offer && (
        <PasswordForm
          title="Add a second factor"
          explanation="A code from an authenticator app, after your password. Nothing changes until you confirm it."
          submit="Begin"
          onSubmit={begin}
        />
      )}

      {offer && <Enrolment offer={offer} onConfirm={confirm} />}

      {state?.second_factor_enabled && (
        <div className="border-border flex flex-col gap-5 rounded-lg border p-5">
          <p className="text-sm">
            A second factor is on, and {state.recovery_codes_remaining} recovery{" "}
            {state.recovery_codes_remaining === 1 ? "code is" : "codes are"} left.
          </p>

          <PasswordForm
            title="New recovery codes"
            explanation="The codes you have now stop working."
            submit="Replace them"
            onSubmit={(password) =>
              ask("/api/security/recovery-codes", password, (issued) => setCodes(issued))
            }
          />

          <PasswordForm
            title="Turn the second factor off"
            explanation="The recovery codes go with it, and every other browser is signed out."
            submit="Turn it off"
            onSubmit={(password) => ask("/api/security/second-factor/off", password, () => {})}
          />
        </div>
      )}

      {state && (
        <InactivityLockSettings state={state} onRefused={setRefused} onChanged={read} />
      )}

      <ChangePassword onRefused={setRefused} onChanged={read} />

      <div className="flex flex-col gap-3">
        <h3 className="text-sm font-medium tracking-wide uppercase">Where you are signed in</h3>

        <ul className="flex flex-col gap-2">
          {sessions.map((session) => (
            <li
              key={session.id}
              className="border-border flex flex-wrap items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm"
            >
              <span className="text-balance">
                {session.description ?? "A browser that did not say what it is"}
                {session.current && <span className="text-brand"> — this one</span>}
              </span>
              <Button type="button" variant="outline" onClick={() => void revoke(session.id)}>
                {session.current ? "Sign out here" : "Sign it out"}
              </Button>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function InactivityLockSettings({
  state,
  onRefused,
  onChanged,
}: {
  state: SecurityState;
  onRefused: (message?: string) => void;
  onChanged: () => void;
}) {
  const [pin, setPin] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [minutes, setMinutes] = useState(String(state.inactivity_minutes));
  const [passwords, setPasswords] = useState<Record<string, string>>({});
  const [done, setDone] = useState(false);

  async function change(event: FormEvent, action: "enable" | "duration" | "pin" | "disable") {
    event.preventDefault();
    onRefused(undefined);
    setDone(false);

    if ((action === "enable" || action === "pin") && pin !== confirmation) {
      onRefused("The PIN and its confirmation do not match.");
      return;
    }

    const answer = await api.PUT("/api/security/inactivity-lock", {
      body: {
        enabled: action !== "disable",
        pin: action === "enable" || action === "pin" ? pin : null,
        inactivity_minutes: action === "disable" ? null : Number(minutes),
        current_password: passwords[action] ?? "",
      },
    });

    setPasswords((current) => ({ ...current, [action]: "" }));
    setPin("");
    setConfirmation("");
    if (answer.error) {
      onRefused(refusal(answer.error, answer.response.status).message);
      return;
    }
    setDone(true);
    onChanged();
  }

  const passwordField = (action: "enable" | "duration" | "pin" | "disable") => (
    <Field label="Your current password">
      <Input
        type="password"
        name="current_password"
        autoComplete="current-password"
        required
        value={passwords[action] ?? ""}
        onChange={(event) => setPasswords((current) => ({ ...current, [action]: event.target.value }))}
      />
    </Field>
  );
  const minutesField = (
    <Field label="Lock after this many inactive minutes">
      <Input
        type="number"
        name="inactivity_minutes"
        min={1}
        max={1440}
        required
        value={minutes}
        onChange={(event) => setMinutes(event.target.value)}
      />
    </Field>
  );
  const pinFields = (
    <>
      <Field label={state.inactivity_lock_enabled ? "New PIN" : "PIN"}>
        <Input
          type="password"
          name="pin"
          inputMode="numeric"
          pattern="[0-9]{4,6}"
          minLength={4}
          maxLength={6}
          autoComplete="off"
          required
          value={pin}
          onChange={(event) => setPin(event.target.value)}
        />
      </Field>
      <Field label="Confirm PIN">
        <Input
          type="password"
          name="pin_confirmation"
          inputMode="numeric"
          pattern="[0-9]{4,6}"
          minLength={4}
          maxLength={6}
          autoComplete="off"
          required
          value={confirmation}
          onChange={(event) => setConfirmation(event.target.value)}
        />
      </Field>
    </>
  );

  return (
    <div className="border-border flex flex-col gap-5 rounded-lg border p-5">
      <div className="flex flex-col gap-1">
        <h3 className="text-sm font-medium">Lock after inactivity</h3>
        <p className="text-muted-foreground text-sm text-balance">
          {state.inactivity_lock_enabled
            ? `On. This browser locks after ${state.inactivity_minutes} inactive minutes.`
            : "Off. A short PIN can protect a browser you walk away from; your password always remains a fallback."}
        </p>
        {done && <p role="status" className="text-sm">Changed.</p>}
      </div>

      {!state.inactivity_lock_enabled ? (
        <form className="flex flex-col gap-3" onSubmit={(event) => void change(event, "enable")}>
          {pinFields}
          {minutesField}
          {passwordField("enable")}
          <div><Button type="submit" variant="outline">Enable inactivity lock</Button></div>
        </form>
      ) : (
        <>
          <form className="flex flex-col gap-3" onSubmit={(event) => void change(event, "duration")}>
            <h4 className="text-sm font-medium">Change the timeout</h4>
            {minutesField}
            {passwordField("duration")}
            <div><Button type="submit" variant="outline">Save timeout</Button></div>
          </form>
          <form className="flex flex-col gap-3" onSubmit={(event) => void change(event, "pin")}>
            <h4 className="text-sm font-medium">Replace the PIN</h4>
            {pinFields}
            {passwordField("pin")}
            <div><Button type="submit" variant="outline">Replace PIN</Button></div>
          </form>
          <form className="flex flex-col gap-3" onSubmit={(event) => void change(event, "disable")}>
            <h4 className="text-sm font-medium">Turn the inactivity lock off</h4>
            {passwordField("disable")}
            <div><Button type="submit" variant="outline">Turn it off</Button></div>
          </form>
        </>
      )}
    </div>
  );
}

/** The secret, and the code that turns it into the second factor. */
function Enrolment({
  offer,
  onConfirm,
}: {
  offer: Schemas["SecondFactorOfferResponse"];
  onConfirm: (code: string) => Promise<void>;
}) {
  const [code, setCode] = useState("");

  return (
    <form
      className="border-border flex flex-col gap-4 rounded-lg border p-5"
      onSubmit={(event: FormEvent) => {
        event.preventDefault();
        void onConfirm(code);
      }}
    >
      <p className="text-sm text-balance">
        Put this into your authenticator, then type the code it shows. Until you do, nothing has
        changed.
      </p>

      <code className="border-border bg-muted rounded-md border px-3 py-2 font-mono text-sm break-all">
        {offer.secret}
      </code>

      <a className="text-brand text-xs break-all underline" href={offer.uri}>
        {offer.uri}
      </a>

      <Field label="The code it shows">
        <Input
          name="code"
          autoComplete="one-time-code"
          required
          value={code}
          onChange={(event) => setCode(event.target.value)}
        />
      </Field>

      <div>
        <Button type="submit">
          Turn it on
        </Button>
      </div>
    </form>
  );
}

/** Shown once, and the screen says so. */
function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  return (
    <div className="border-accent flex flex-col gap-3 rounded-lg border p-5" role="status">
      <h3 className="font-medium">Your recovery codes</h3>
      <p className="text-muted-foreground text-sm text-balance">
        Each works once, and this is the only time they are shown. Keep them somewhere that is not
        the phone with the authenticator on it.
      </p>
      <ul className="grid grid-cols-2 gap-1 font-mono text-sm">
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ul>
      <div>
        <Button type="button" variant="outline" onClick={onDone}>
          I have them
        </Button>
      </div>
    </div>
  );
}

/** A change that asks for the password, which is all of them. */
function PasswordForm({
  title,
  explanation,
  submit,
  onSubmit,
}: {
  title: string;
  explanation: string;
  submit: string;
  onSubmit: (password: string) => Promise<void> | void;
}) {
  const [password, setPassword] = useState("");

  return (
    <form
      className="flex flex-col gap-3"
      onSubmit={(event: FormEvent) => {
        event.preventDefault();
        void onSubmit(password);
        setPassword("");
      }}
    >
      <h3 className="text-sm font-medium">{title}</h3>
      <p className="text-muted-foreground text-sm text-balance">{explanation}</p>

      <Field label={`Your password, to ${submit.toLowerCase()}`}>
        <Input
          type="password"
          name="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </Field>

      <div>
        <Button type="submit" variant="outline">{submit}</Button>
      </div>
    </form>
  );
}

function ChangePassword({
  onRefused,
  onChanged,
}: {
  onRefused: (message?: string) => void;
  onChanged: () => void;
}) {
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [done, setDone] = useState(false);

  async function change(event: FormEvent) {
    event.preventDefault();
    onRefused(undefined);
    setDone(false);

    const answer = await api.POST("/api/security/password", {
      body: { current_password: current, password: next },
    });

    if (answer.error) {
      onRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    setCurrent("");
    setNext("");
    setDone(true);
    onChanged();
  }

  return (
    <form onSubmit={change} className="flex flex-col gap-3">
      <h3 className="text-sm font-medium">Change your password</h3>
      <p className="text-muted-foreground text-sm text-balance">Every other browser is signed out.</p>

      <Field label="Your password now">
        <Input
          type="password"
          name="current_password"
          autoComplete="current-password"
          required
          value={current}
          onChange={(event) => setCurrent(event.target.value)}
        />
      </Field>

      <Field label="Your new password" hint="At least 12 characters.">
        <Input
          type="password"
          name="password"
          autoComplete="new-password"
          minLength={12}
          required
          value={next}
          onChange={(event) => setNext(event.target.value)}
        />
      </Field>

      {done && <p role="status" className="text-sm">Changed.</p>}

      <div>
        <Button type="submit" variant="outline">Change it</Button>
      </div>
    </form>
  );
}
