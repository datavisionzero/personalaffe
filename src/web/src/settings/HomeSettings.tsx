import { useState } from "react";

import { api, guardedBy, versionOf, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Busy, Failed } from "@/shell/States";
import { applicationNamed } from "@/shell/applications";
import { useAsk } from "@/shared/ask";
import { Field, Refused, selectClass } from "@/shared/Form";
import { refusal } from "@/session/useSession";
import type { TileName } from "@/home/useDashboard";

type Tile = Schemas["TileResponse"];
type Weather = Schemas["WeatherResponse"];
type Somewhere = Schemas["PlaceResponse"];

/** What each tile says it is, in the owner's words rather than the wire's. */
const described: Record<TileName, { label: string; hint: string }> = {
  bookmarks: { label: "Bookmarks", hint: "Favorites first, then frequently opened links." },
  tasks: { label: "Open tasks", hint: "What is open, soonest due first." },
  knowledge: { label: "Knowledge", hint: "The pages you wrote in most recently." },
  scratchpad: { label: "Scratchpad", hint: "What you put down last." },
  files: { label: "Files", hint: "What arrived on the disk most recently." },
  weather: { label: "Weather", hint: "Where you said, from outside this instance." },
};

/**
 * What is on the home page: the five tiles, and where the weather is for.
 *
 * <b>Both halves are the owner's alone</b>, which the API enforces and this
 * reflects: an agent is shown what is set and no buttons, because drawing a
 * control that can only ever be refused is offering something that is not on
 * offer. It is the same shape as the application switches beside it.
 *
 * <b>Hiding a tile is not switching an application off.</b> The page above says
 * so, because the two settings are one click apart and only one of them changes
 * what this workspace has.
 */
export function HomeSettings({ owner }: { owner: boolean }) {
  const tiles = useAsk("/api/dashboard", (signal) => api.GET("/api/dashboard", { signal }));
  const weather = useAsk("/api/weather", (signal) => api.GET("/api/weather", { signal }), {
    every: false,
  });

  const [working, setWorking] = useState<string>();
  const [refused, setRefused] = useState<string>();

  async function show(tile: Tile) {
    setWorking(tile.tile);
    setRefused(undefined);

    try {
      const answer = await api.PUT("/api/dashboard/tiles/{tile}", {
        params: { path: { tile: tile.tile }, ...guardedBy(versionOf(tile.updated_at)) },
        body: { shown: !tile.shown },
      });

      if (answer.error) {
        const { message, code } = refusal(answer.error, answer.response.status);

        setRefused(
          code === "stale"
            ? "This home page changed somewhere else while the screen was open. It is being read again."
            : message,
        );
      }
    } finally {
      // Either way: what is on the screen afterwards is what the instance says.
      tiles.again();
      setWorking(undefined);
    }
  }

  if (tiles.asked.at === "asking") {
    return <Busy title="Asking what is on the home page…" />;
  }

  if (tiles.asked.at === "failed") {
    return <Failed why={tiles.asked.why} again={tiles.again} />;
  }

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-4">
        <p className="text-muted-foreground max-w-prose text-sm text-balance">
          The home page answers one question: what is useful or pending right now. Every tile can be
          hidden, and hiding one changes nothing but this screen — the application stays switched
          on, its own screen stays where it is, and the search still finds what is in it.
        </p>

        <Refused>{refused}</Refused>

        <ul className="flex flex-col gap-3">
          {tiles.asked.value.tiles.map((tile) => (
            <li
              key={tile.tile}
              className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-4"
            >
              <div className="flex flex-col gap-1">
                <div className="flex items-center gap-2 font-medium">
                  <Icon tile={tile.tile} />
                  {described[tile.tile].label}
                </div>
                <p className="text-muted-foreground text-sm text-balance">
                  {described[tile.tile].hint}
                </p>
                {!tile.offered && (
                  <p className="text-muted-foreground text-xs text-balance">
                    Not offered right now: the application behind it is switched off, or this
                    credential does not reach it. What you set here is kept either way.
                  </p>
                )}
              </div>

              <div className="flex items-center gap-3">
                <span className={tile.shown ? "text-brand text-sm" : "text-muted-foreground text-sm"}>
                  {tile.shown ? "Shown" : "Hidden"}
                </span>

                {owner && (
                  <Button
                    type="button"
                    variant="outline"
                    disabled={working !== undefined}
                    aria-label={`${tile.shown ? "Hide" : "Show"} ${described[tile.tile].label}`}
                    onClick={() => void show(tile)}
                  >
                    {working === tile.tile ? "…" : tile.shown ? "Hide" : "Show"}
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      </div>

      {weather.asked.at === "known" && (
        <WhereTheWeatherIs
          owner={owner}
          weather={weather.asked.value}
          again={() => {
            weather.again();
            tiles.again();
          }}
        />
      )}

      {!owner && (
        <p className="text-muted-foreground text-xs text-balance">
          What is on the home page is the owner's alone. Agent access acts on the owner's behalf in
          the applications it was given and nowhere else.
        </p>
      )}
    </section>
  );
}

function Icon({ tile }: { tile: TileName }) {
  const application = applicationNamed(tile);

  return application === undefined ? null : <application.icon aria-hidden className="size-4" />;
}

/**
 * Where the weather is for.
 *
 * <b>A name is typed and a point is stored.</b> The geocoder is asked once,
 * here, so that a tile never depends on a second service; what is kept is two
 * coordinates and the label to write above them
 * (`docs/api.md`, The weather).
 */
function WhereTheWeatherIs({
  owner,
  weather,
  again,
}: {
  owner: boolean;
  weather: Weather;
  again: () => void;
}) {
  const [typed, setTyped] = useState("");
  const [found, setFound] = useState<Somewhere[]>();
  const [looking, setLooking] = useState(false);
  const [refused, setRefused] = useState<string>();

  async function look() {
    if (typed.trim() === "") {
      return;
    }

    setLooking(true);
    setRefused(undefined);

    const answer = await api.GET("/api/weather/places", { params: { query: { q: typed } } });

    setLooking(false);

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
      return;
    }

    setFound(answer.data.items);
  }

  async function set(place: Somewhere | null, units: Weather["units"] = weather.units) {
    setRefused(undefined);

    const answer = await api.PUT("/api/weather/place", {
      params: { ...guardedBy(versionOf(weather.updated_at)) },
      body:
        place === null
          ? { name: null, latitude: null, longitude: null, units }
          : {
              name: [place.name, place.country].filter(Boolean).join(", "),
              latitude: place.latitude,
              longitude: place.longitude,
              units,
            },
    });

    if (answer.error) {
      setRefused(refusal(answer.error, answer.response.status).message);
    } else {
      setFound(undefined);
      setTyped("");
    }

    again();
  }

  return (
    <div className="flex flex-col gap-4 border-t pt-6">
      <div className="flex flex-col gap-1">
        <h2 className="font-medium">Where the weather is for</h2>
        <p className="text-muted-foreground max-w-prose text-sm text-balance">
          {weather.available
            ? "A name is looked up once, here. What is kept is the point and the label — the tile never asks a second service."
            : "This instance has been configured not to ask anybody about the weather, so the tile will say so whatever is set here."}
        </p>
      </div>

      <Refused>{refused}</Refused>

      <p className="text-sm">
        <span className="text-muted-foreground">Set to: </span>
        {weather.place === null || weather.place === "" ? "nowhere" : weather.place}
      </p>

      {owner && (
        <>
          <Field
            label="Units"
            hint="What the temperature and the wind are written in."
          >
            <select
              name="weather-units"
              className={selectClass}
              value={weather.units}
              onChange={(event) =>
                void set(
                  weather.latitude === null || weather.longitude === null || weather.place === null
                    ? null
                    : {
                        name: weather.place,
                        region: null,
                        country: null,
                        latitude: weather.latitude,
                        longitude: weather.longitude,
                      },
                  event.target.value as Weather["units"],
                )
              }
            >
              <option value="metric">Celsius and km/h</option>
              <option value="imperial">Fahrenheit and mph</option>
            </select>
          </Field>

          <form
            className="flex flex-wrap items-end gap-2"
            onSubmit={(event) => {
              event.preventDefault();
              void look();
            }}
          >
            <div className="min-w-48 flex-1">
              <Field label="Look a place up">
                <Input
                  name="weather-place"
                  type="search"
                  placeholder="Wuppertal"
                  value={typed}
                  onChange={(event) => setTyped(event.target.value)}
                />
              </Field>
            </div>
            <Button type="submit" variant="outline" disabled={looking}>
              {looking ? "…" : "Look it up"}
            </Button>
            {weather.place !== null && weather.place !== "" && (
              <Button type="button" variant="ghost" onClick={() => void set(null)}>
                Clear it
              </Button>
            )}
          </form>

          {found !== undefined && found.length === 0 && (
            <p className="text-muted-foreground text-sm">Nothing came back for that.</p>
          )}

          {found !== undefined && found.length > 0 && (
            <ul className="flex flex-col gap-1">
              {found.map((place) => (
                <li key={`${place.latitude},${place.longitude}`}>
                  <button
                    type="button"
                    className="hover:bg-accent w-full rounded-md px-2 py-1.5 text-left text-sm"
                    onClick={() => void set(place)}
                  >
                    {place.name}
                    <span className="text-muted-foreground">
                      {[place.region, place.country].filter(Boolean).join(", ") !== "" &&
                        ` · ${[place.region, place.country].filter(Boolean).join(", ")}`}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  );
}
