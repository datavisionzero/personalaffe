import { useState, type FormEvent } from "react";

import { api } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused } from "@/shared/Form";
import { refusal, type Me } from "@/session/useSession";

/**
 * The one-time setup: an instance that belongs to nobody acquires the one owner
 * it will ever have.
 *
 * It signs in immediately afterwards with what was just typed, because the
 * alternative is a form that congratulates somebody and then asks them for the
 * same two fields again.
 */
export function Setup({ onSignedIn }: { onSignedIn: (me: Me) => void }) {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [refused, setRefused] = useState<string>();
  const [working, setWorking] = useState(false);

  async function claim(event: FormEvent) {
    event.preventDefault();
    setRefused(undefined);
    setWorking(true);

    try {
      const claimed = await api.POST("/api/setup", { body: { email, password } });

      if (claimed.error) {
        setRefused(refusal(claimed.error, claimed.response.status).message);
        return;
      }

      const signedIn = await api.POST("/api/session", {
        body: { email, password, second_factor: null },
      });

      if (signedIn.error) {
        setRefused(refusal(signedIn.error, signedIn.response.status).message);
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
    <form onSubmit={claim} className="flex flex-col gap-5" aria-labelledby="setup">
      <div className="flex flex-col gap-2">
        <h2 id="setup" className="text-lg font-semibold">
          Claim this workspace
        </h2>
        <p className="text-muted-foreground text-sm text-balance">
          It belongs to nobody yet. This works exactly once: there is one owner, and there is no
          second account.
        </p>
      </div>

      <Field
        label="Email address"
        hint="The name you sign in with. personalaffe sends no mail and needs no mail server."
      >
        <Input
          type="email"
          name="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />
      </Field>

      <Field label="Password" hint="At least 12 characters. There is no other rule.">
        <Input
          type="password"
          name="password"
          autoComplete="new-password"
          minLength={12}
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </Field>

      <Refused>{refused}</Refused>

      <div>
        <Button type="submit" disabled={working}>
          {working ? "Claiming…" : "Claim it"}
        </Button>
      </div>
    </form>
  );
}
