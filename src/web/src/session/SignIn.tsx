import { useState, type FormEvent } from "react";

import { api } from "@/api/client";
import { Button, Field, Refused, inputClass } from "@/shared/Form";
import { refusal, type Me } from "@/session/useSession";

/**
 * Signing in, in one step or two.
 *
 * With a second factor enrolled the instance answers the password alone with
 * `second-factor`, and the same request goes again with the code beside it. The
 * password stays in this component for that one extra request and is never put
 * anywhere else — not in storage, not in a URL, not in a state that outlives
 * the form.
 */
export function SignIn({ onSignedIn }: { onSignedIn: (me: Me) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [secondFactor, setSecondFactor] = useState("");
  const [wanted, setWanted] = useState(false);
  const [refused, setRefused] = useState<string>();
  const [working, setWorking] = useState(false);

  async function signIn(event: FormEvent) {
    event.preventDefault();
    setRefused(undefined);
    setWorking(true);

    try {
      const answer = await api.POST("/api/session", {
        body: {
          email,
          password,
          second_factor: secondFactor.trim() === "" ? null : secondFactor.trim(),
        },
      });

      if (answer.error) {
        const { message, code } = refusal(answer.error, answer.response.status);

        if (code === "second-factor") {
          // Not a refusal to show as one: the password was right, and what is
          // wanted next is a field that is not on the screen yet.
          setWanted(true);
          return;
        }

        setWanted(false);
        setSecondFactor("");
        setRefused(message);
        return;
      }

      const me = await api.GET("/api/me");

      if (me.data) {
        onSignedIn(me.data);
      }
    } finally {
      setWorking(false);
    }
  }

  return (
    <form onSubmit={signIn} className="flex flex-col gap-5" aria-labelledby="sign-in">
      <h2 id="sign-in" className="text-lg font-semibold">
        Sign in
      </h2>

      <Field label="Email address">
        <input
          className={inputClass}
          type="email"
          name="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />
      </Field>

      <Field label="Password">
        <input
          className={inputClass}
          type="password"
          name="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </Field>

      {wanted && (
        <Field
          label="Code from your authenticator"
          hint="Or one of your recovery codes, if the authenticator is gone."
        >
          <input
            className={inputClass}
            name="second_factor"
            autoComplete="one-time-code"
            inputMode="text"
            // The field appeared in answer to a submit, and it is the only
            // thing left to do on the screen.
            autoFocus
            required
            value={secondFactor}
            onChange={(event) => setSecondFactor(event.target.value)}
          />
        </Field>
      )}

      <Refused>{refused}</Refused>

      <div>
        <Button type="submit" kind="primary" disabled={working}>
          {working ? "Signing in…" : "Sign in"}
        </Button>
      </div>
    </form>
  );
}
