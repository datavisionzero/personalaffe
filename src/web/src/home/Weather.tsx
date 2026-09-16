import { CloudOffIcon } from "lucide-react";
import { Link } from "react-router";

import { api } from "@/api/client";
import { useAsk } from "@/shared/ask";

/**
 * The weather tile.
 *
 * <b>It asks on its own, and that is the whole design.</b> The instance keeps
 * the weather at an address of its own so that a provider somewhere else cannot
 * hold up the home page (`docs/api.md`, The weather); a tile that came down with
 * the rest of the dashboard would take that guarantee straight back. So this
 * component makes its own request, draws nothing until it has one, and the four
 * tiles beside it never wait for it.
 *
 * It asks far less often than everything else. A reading is held by the
 * instance for as long as the operator's freshness allows, so asking every
 * fifteen seconds would be fifteen-second traffic for a number that changes
 * four times an hour.
 *
 * <b>Nothing here is a failure.</b> No place, an instance told not to ask, a
 * provider that did not answer: three sentences, no alarm, and the tile stays
 * where it is.
 */
export function Weather() {
  const { asked } = useAsk("/api/weather", (signal) => api.GET("/api/weather", { signal }), {
    every: 60_000,
  });

  if (asked.at !== "known") {
    return (
      <Tile>
        <p className="text-muted-foreground text-sm">
          {asked.at === "failed" ? "The weather is not answering." : "Asking…"}
        </p>
      </Tile>
    );
  }

  const weather = asked.value;

  if (!weather.available) {
    return (
      <Tile>
        <Quiet>This instance does not ask anybody about the weather.</Quiet>
      </Tile>
    );
  }

  if (weather.place === null || weather.place === "") {
    return (
      <Tile>
        <Quiet>
          Nowhere is set.{" "}
          <Link className="text-brand underline-offset-4 hover:underline" to="/settings/home">
            Say where in Settings.
          </Link>
        </Quiet>
      </Tile>
    );
  }

  if (weather.reading === null) {
    return (
      <Tile>
        <p className="font-medium">{weather.place}</p>
        <Quiet>The provider did not answer. This is all there is to say about it.</Quiet>
      </Tile>
    );
  }

  const reading = weather.reading;

  return (
    <Tile>
      <div className="flex items-baseline gap-3">
        <span className="text-3xl font-semibold tabular-nums">
          {reading.temperature}
          {weather.temperature_unit}
        </span>
        <span className="text-sm">{reading.description}</span>
      </div>

      <p className="text-muted-foreground text-sm">
        {weather.place}
        {reading.low !== null && reading.high !== null && (
          <>
            {" · "}
            {reading.low}
            {weather.temperature_unit} to {reading.high}
            {weather.temperature_unit}
          </>
        )}
        {" · "}
        {reading.wind} {weather.wind_unit}
      </p>

      {/* What a free provider is paid in, beside the number it produced. */}
      <p className="text-muted-foreground text-[11px]">
        {weather.attribution} · read {when(reading.read_at)}
      </p>
    </Tile>
  );
}

function Tile({ children }: { children: React.ReactNode }) {
  return <div className="flex flex-col gap-1.5">{children}</div>;
}

function Quiet({ children }: { children: React.ReactNode }) {
  return (
    <p className="text-muted-foreground flex items-start gap-2 text-sm text-balance">
      <CloudOffIcon aria-hidden className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  );
}

/**
 * How long ago the instance asked. It is what makes freshness plain without a
 * timestamp nobody reads — and it is the instance's own moment, so a browser
 * with a wrong clock says something odd rather than something wrong about the
 * weather.
 */
function when(readAt: string): string {
  const minutes = Math.max(0, Math.round((Date.now() - Date.parse(readAt)) / 60_000));

  if (minutes < 1) {
    return "just now";
  }

  return minutes < 60
    ? `${minutes} min ago`
    : `${Math.round(minutes / 60)} h ago`;
}
