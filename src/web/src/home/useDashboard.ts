import { api, type Schemas } from "@/api/client";
import { useAsk, type Asked } from "@/shared/ask";

export type TheHome = Schemas["DashboardResponse"];
export type Tile = Schemas["TileResponse"];
export type TileName = Tile["tile"];

/**
 * What is useful or pending right now (`docs/api.md`, The dashboard).
 *
 * <b>One question for the whole page</b>, because the instance answers it as
 * one: the tiles and what is in the ones being drawn. Four questions for four
 * tiles would be four moments at which they could disagree about what this
 * workspace has, and four times the traffic on a screen that asks again every
 * fifteen seconds.
 *
 * The weather is deliberately not in it. It has its own address and its own
 * hook, because what is behind it is a server on the other side of the
 * internet and a home page must never wait on one.
 */
export function useDashboard(): {
  asked: Asked<TheHome>;
  again: () => void;
  unanswered: boolean;
} {
  const { asked, again, unanswered } = useAsk("/api/dashboard", (signal) =>
    api.GET("/api/dashboard", { signal }),
  );

  return { asked, again, unanswered };
}

/** Whether a tile is being drawn: the owner shows it and this caller is offered it. */
export function drawn(tiles: Tile[], name: TileName): boolean {
  return tiles.some((tile) => tile.tile === name && tile.shown && tile.offered);
}
