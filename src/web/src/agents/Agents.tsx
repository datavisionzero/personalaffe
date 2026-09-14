import { useCallback, useEffect, useState, type FormEvent } from "react";

import { api, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused, selectClass } from "@/shared/Form";
import { refusal } from "@/session/useSession";

type Agent = Schemas["AgentResponse"];
type Granted = Schemas["Permissions"];
type Permission = Schemas["Permission"];

/** The four applications, in the order CONTEXT.md lists them. */
const applications = ["scratchpad", "knowledge", "tasks", "files"] as const;

const nothing: Granted = { scratchpad: "none", knowledge: "none", tasks: "none", files: "none" };

/**
 * Agent access: what the owner has let in, and what each of them reaches.
 *
 * The token is shown once, here, because that is the only time it exists
 * anywhere: what the instance keeps is a digest. The screen says so rather than
 * leaving somebody to find out.
 */
export function Agents() {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [issued, setIssued] = useState<{ name: string; token: string }>();
  const [refused, setRefused] = useState<string>();

  // Nothing is written on the way in, so that the effect below starts the
  // request without a render of its own; an answer that arrives after the
  // screen is gone is dropped.
  const read = useCallback(() => {
    let abandoned = false;

    void (async () => {
      const answer = await api.GET("/api/agents");

      if (!abandoned && answer.data) {
        setAgents(answer.data);
      }
    })();

    return () => {
      abandoned = true;
    };
  }, []);

  useEffect(() => read(), [read]);

  async function grant(name: string, permissions: Granted) {
    setRefused(undefined);

    const answer = await api.POST("/api/agents", { body: { name, permissions } });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return false;
    }

    setIssued({ name: answer.data.agent.name, token: answer.data.token });
    read();
    return true;
  }

  async function change(id: string, permissions: Granted) {
    setRefused(undefined);

    const answer = await api.PATCH("/api/agents/{id}", {
      params: { path: { id } },
      body: { name: null, permissions },
    });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    read();
  }

  async function reissue(id: string) {
    setRefused(undefined);

    const answer = await api.POST("/api/agents/{id}/token", { params: { path: { id } } });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    setIssued({ name: answer.data.agent.name, token: answer.data.token });
    read();
  }

  async function revoke(id: string) {
    setRefused(undefined);

    const answer = await api.DELETE("/api/agents/{id}", { params: { path: { id } } });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    read();
  }

  return (
    <section aria-labelledby="agents" className="flex flex-col gap-6">
      <div className="flex flex-col gap-2">
        <h2 id="agents" className="text-lg font-semibold">
          Agent access
        </h2>
        <p className="text-muted-foreground text-sm text-balance">
          A named key to part of this workspace, revocable on its own. It is not a second account:
          an agent cannot issue credentials, change how you sign in, or reset the instance.
        </p>
      </div>

      <Refused>{refused}</Refused>

      {issued && <Token issued={issued} onDone={() => setIssued(undefined)} />}

      <Grant onGrant={grant} />

      <ul className="flex flex-col gap-3">
        {agents.map((agent) => (
          <Row
            key={agent.id}
            agent={agent}
            onChange={(permissions) => change(agent.id, permissions)}
            onReissue={() => reissue(agent.id)}
            onRevoke={() => revoke(agent.id)}
          />
        ))}
      </ul>
    </section>
  );
}

function Token({
  issued,
  onDone,
}: {
  issued: { name: string; token: string };
  onDone: () => void;
}) {
  return (
    <div className="border-accent flex flex-col gap-3 rounded-lg border p-5" role="status">
      <h3 className="font-medium">The token for {issued.name}</h3>
      <p className="text-muted-foreground text-sm text-balance">
        This is the only time it is shown. What this instance keeps is a digest of it, so a token
        that is lost is reissued rather than recovered.
      </p>
      <code className="border-border bg-muted rounded-md border px-3 py-2 font-mono text-sm break-all">
        {issued.token}
      </code>
      <div>
        <Button type="button" variant="outline" onClick={onDone}>
          I have it
        </Button>
      </div>
    </div>
  );
}

function Grant({ onGrant }: { onGrant: (name: string, permissions: Granted) => Promise<boolean> }) {
  const [name, setName] = useState("");
  const [permissions, setPermissions] = useState<Granted>(nothing);

  async function grant(event: FormEvent) {
    event.preventDefault();

    if (await onGrant(name, permissions)) {
      setName("");
      setPermissions(nothing);
    }
  }

  return (
    <form onSubmit={grant} className="border-border flex flex-col gap-4 rounded-lg border p-5">
      <h3 className="text-sm font-medium">Let an agent in</h3>

      <Field label="Name" hint="What this key is for, so that a list of them answers that question.">
        <Input
          name="name"
          required
          maxLength={100}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
      </Field>

      <Permissions permissions={permissions} onChange={setPermissions} />

      <div>
        <Button type="submit">
          Let it in
        </Button>
      </div>
    </form>
  );
}

function Row({
  agent,
  onChange,
  onReissue,
  onRevoke,
}: {
  agent: Agent;
  onChange: (permissions: Granted) => Promise<void>;
  onReissue: () => Promise<void>;
  onRevoke: () => Promise<void>;
}) {
  return (
    <li className="border-border flex flex-col gap-4 rounded-lg border p-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="font-medium">{agent.name}</h3>
        <code className="text-muted-foreground font-mono text-xs">{agent.token_prefix}…</code>
      </div>

      {agent.revoked_at ? (
        <p className="text-muted-foreground text-sm">
          Revoked. It is kept here because it is what acted, wherever it acted.
        </p>
      ) : (
        <>
          <Permissions permissions={agent.permissions} onChange={(next) => void onChange(next)} />

          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={() => void onReissue()}>
              New token
            </Button>
            <Button type="button" variant="outline" onClick={() => void onRevoke()}>
              Revoke
            </Button>
          </div>
        </>
      )}
    </li>
  );
}

function Permissions({
  permissions,
  onChange,
}: {
  permissions: Granted;
  onChange: (permissions: Granted) => void;
}) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {applications.map((application) => (
        <Field key={application} label={application}>
          <select
            name={application}
            className={selectClass}
            aria-label={application}
            value={permissions[application]}
            onChange={(event) =>
              onChange({ ...permissions, [application]: event.target.value as Permission })
            }
          >
            <option value="none">no access</option>
            <option value="read">read</option>
            <option value="read_write">read and write</option>
          </select>
        </Field>
      ))}
    </div>
  );
}
