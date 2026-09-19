import { createContext, use } from "react";

import type { Schemas } from "@/api/client";
import { defaultColour, defaultShape, productName } from "./theMark";

export type Appearance = Schemas["AppearanceResponse"];

/**
 * What an instance that has not answered, or will not answer, is drawn as: the
 * product's own name and the product's own mark.
 */
export const theProducts: Appearance = {
  title: null,
  colour: defaultColour,
  shape: defaultShape,
  updated_at: "",
};

export type TheAppearance = {
  appearance: Appearance;
  /** The title, or the product name — what every screen actually writes out. */
  name: string;
  /**
   * Whether the instance has said yet.
   *
   * <b>What is drawn before it has is nothing, not the product name.</b> An
   * instance called "Haus" that said `personalaffe` for half a second on every
   * load would be worse than one that was never named, so the two places the
   * name appears hold their space until the answer arrives. Everything else on
   * the screen is drawn straight away: a sign-in form has no business waiting
   * on what the instance is called.
   */
  known: boolean;
  /** Read it again now, quietly, after a write. */
  again: () => void;
};

export const AppearanceContext = createContext<TheAppearance>({
  appearance: theProducts,
  name: productName,
  known: true,
  again: () => {},
});

/** What this instance is called and what its mark looks like. */
export function useAppearance(): TheAppearance {
  return use(AppearanceContext);
}
