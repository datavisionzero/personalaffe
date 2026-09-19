import { useState } from "react";

import { api, guardedBy, versionOf } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { refusal } from "@/session/useSession";
import { Field, Refused, selectClass } from "@/shared/Form";
import { Failed } from "@/shell/States";
import { Mark } from "@/shell/Mark";
import { productName, type MarkColour, type MarkShape } from "@/shell/theMark";
import { useAppearance, type Appearance } from "@/shell/useAppearance";

/** The seven, in the order the contract lists them, in the owner's words. */
const colours: { value: MarkColour; label: string }[] = [
  { value: "violet", label: "Violet" },
  { value: "blue", label: "Blue" },
  { value: "teal", label: "Teal" },
  { value: "green", label: "Green" },
  { value: "amber", label: "Amber" },
  { value: "red", label: "Red" },
  { value: "pink", label: "Pink" },
];

const shapes: { value: MarkShape; label: string }[] = [
  { value: "square", label: "Rounded square" },
  { value: "circle", label: "Circle" },
];

/** What the field will take, and what the instance will refuse beyond. */
const titleMaxLength = 40;

/**
 * What this instance is called and what its mark looks like.
 *
 * <b>It is the owner's alone</b>, which the API enforces and this reflects: an
 * agent is shown what is set and no buttons, because drawing a control that can
 * only ever be refused is offering something that is not on offer. It is the
 * same shape as the two screens beside it.
 *
 * <b>The screen says out loud that the name is public.</b> What is chosen here
 * is readable by whoever can reach this instance, because the sign-in screen
 * and the browser tab are drawn before anybody has signed in — and somebody
 * choosing a name is entitled to know that before they choose it rather than
 * after.
 */
export function AppearanceSettings({ owner }: { owner: boolean }) {
  const { appearance, name, again } = useAppearance();

  if (appearance.updated_at === "") {
    // The provider draws nothing until the instance has answered once, so an
    // appearance with no version is the instance having refused rather than the
    // screen having arrived early.
    return <Failed why="This instance did not say what it is called." again={again} />;
  }

  if (!owner) {
    return <WhatIsSet appearance={appearance} name={name} />;
  }

  // Keyed on the version, so that an appearance that changed somewhere else
  // arrives as a fresh form rather than as an effect reaching into fields
  // somebody may be typing in. A version that did not change is the same key
  // and leaves a half-typed name exactly where it was.
  return <TheChoices key={appearance.updated_at} appearance={appearance} again={again} />;
}

/** What an agent is shown: what is set, and no buttons. */
function WhatIsSet({ appearance, name }: { appearance: Appearance; name: string }) {
  return (
    <section className="flex flex-col gap-6">
      <Explanation />

      <Preview title={appearance.title} colour={appearance.colour} shape={appearance.shape} />

      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
        <dt className="text-muted-foreground">Name</dt>
        <dd>{name}</dd>
        <dt className="text-muted-foreground">Colour</dt>
        <dd>{colours.find((one) => one.value === appearance.colour)?.label}</dd>
        <dt className="text-muted-foreground">Mark</dt>
        <dd>{shapes.find((one) => one.value === appearance.shape)?.label}</dd>
      </dl>

      <p className="text-muted-foreground text-xs text-balance">
        What this instance is called is the owner's alone. Agent access acts on the owner's behalf
        in the applications it was given and nowhere else.
      </p>
    </section>
  );
}

function TheChoices({ appearance, again }: { appearance: Appearance; again: () => void }) {
  const [title, setTitle] = useState(appearance.title ?? "");
  const [colour, setColour] = useState<MarkColour>(appearance.colour);
  const [shape, setShape] = useState<MarkShape>(appearance.shape);
  const [working, setWorking] = useState(false);
  const [refused, setRefused] = useState<string>();

  async function save() {
    setWorking(true);
    setRefused(undefined);

    try {
      const answer = await api.PUT("/api/appearance", {
        params: { ...guardedBy(versionOf(appearance.updated_at)) },
        body: { title: title.trim() === "" ? null : title, colour, shape },
      });

      if (answer.error) {
        const { message, code } = refusal(answer.error, answer.response.status);

        setRefused(
          code === "stale"
            ? "This instance was renamed somewhere else while the screen was open. It is being read again."
            : message,
        );
      }
    } finally {
      // Either way: what is on the screen afterwards is what the instance says.
      again();
      setWorking(false);
    }
  }

  const unchanged =
    (appearance.title ?? "") === title &&
    appearance.colour === colour &&
    appearance.shape === shape;

  return (
    <section className="flex flex-col gap-6">
      <Explanation />

      <Refused>{refused}</Refused>

      <Preview title={title} colour={colour} shape={shape} />

      <form
        className="flex flex-col gap-4"
        onSubmit={(event) => {
          event.preventDefault();
          void save();
        }}
      >
        <Field
          label="Name"
          hint={
            <>
              At most {titleMaxLength} characters, on one line. Plain text: it is written out as you
              typed it and is never read as Markdown. Leave it empty to go back to {productName}.{" "}
              <strong className="font-medium">
                Whoever can reach this instance can read the name you choose
              </strong>{" "}
              — it is on the sign-in screen, which is drawn before anybody has signed in. Who you
              are is not.
            </>
          }
        >
          <Input
            name="appearance-title"
            value={title}
            maxLength={titleMaxLength}
            placeholder={productName}
            onChange={(event) => setTitle(event.target.value)}
          />
        </Field>

        <Field label="Colour" hint="Seven, each one checked against the light and the dark theme.">
          <select
            name="appearance-colour"
            className={selectClass}
            value={colour}
            onChange={(event) => setColour(event.target.value as MarkColour)}
          >
            {colours.map((one) => (
              <option key={one.value} value={one.value}>
                {one.label}
              </option>
            ))}
          </select>
        </Field>

        <Field
          label="Mark"
          hint="Drawn here, from two shapes. Nothing is uploaded and nothing is fetched."
        >
          <select
            name="appearance-shape"
            className={selectClass}
            value={shape}
            onChange={(event) => setShape(event.target.value as MarkShape)}
          >
            {shapes.map((one) => (
              <option key={one.value} value={one.value}>
                {one.label}
              </option>
            ))}
          </select>
        </Field>

        <div className="flex flex-wrap items-center gap-2">
          <Button type="submit" disabled={working || unchanged}>
            {working ? "…" : "Save"}
          </Button>
          {!unchanged && (
            <Button
              type="button"
              variant="ghost"
              disabled={working}
              onClick={() => {
                setTitle(appearance.title ?? "");
                setColour(appearance.colour);
                setShape(appearance.shape);
                setRefused(undefined);
              }}
            >
              Put it back
            </Button>
          )}
        </div>
      </form>
    </section>
  );
}

function Explanation() {
  return (
    <p className="text-muted-foreground max-w-prose text-sm text-balance">
      A name and a mark of your own, so that one of these is told from another at a glance — in the
      sidebar, in the browser tab and at the sign-in screen. It names this installation and not the
      software.
    </p>
  );
}

/**
 * The mark as it will be drawn, using the component that draws it and the
 * tokens it draws with. A mock-up that could disagree with the thing it
 * previews would be worse than no preview at all.
 */
function Preview({
  title,
  colour,
  shape,
}: {
  title: string | null;
  colour: MarkColour;
  shape: MarkShape;
}) {
  const named = (title ?? "").trim() !== "";

  return (
    <div className="flex items-center gap-3 rounded-lg border p-4">
      <Mark colour={colour} shape={shape} title={named ? title : null} className="size-9 text-base" />
      <div className="flex min-w-0 flex-col">
        <span className="truncate font-medium">{named ? title : productName}</span>
        <span className="text-muted-foreground text-xs">
          {named
            ? "What the sidebar and the tab will say."
            : "No name of its own, so it says the product's."}
        </span>
      </div>
    </div>
  );
}
