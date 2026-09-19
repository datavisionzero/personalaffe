import { cn } from "@/lib/utils";
import { lettersOf, type MarkColour, type MarkShape } from "./theMark";

/**
 * The instance's mark: the shape, in the instance's colour, with the letters
 * its title gives it.
 *
 * <b>It is drawn and never fetched.</b> A shape from a closed set of two and a
 * colour from a closed set of seven, both out of the token layer — there is no
 * image, nothing is uploaded and nothing is requested from anywhere. It is CSS
 * and not an inline SVG because the letters are the application's own font at
 * the application's own weight, which is a thing `<text>` in an SVG has to be
 * told and a `<span>` simply is. The one place the mark really is an SVG is the
 * favicon (`faviconOf`), which is a document of its own and carries no letters.
 *
 * <b>With no title it is exactly the mark the product has always drawn</b>: a
 * filled rounded square in `--brand`, at the same size and the same radius.
 * That is what makes an instance nobody has touched unchanged by this feature
 * rather than nearly unchanged.
 *
 * It is `aria-hidden` wherever the title is written out beside it, which is
 * everywhere it appears: a screen reader announcing "H A" before the word
 * "Haus" is reading the decoration twice.
 *
 * <b>It carries its own colour</b> rather than reading the document's, so that
 * the appearance screen can preview a colour before it is applied. The
 * attribute selects three values that already exist in the token layer
 * (`index.css`); no value from the API is ever written into a style string.
 */
export function Mark({
  colour,
  shape,
  title,
  className,
}: {
  colour: MarkColour;
  shape: MarkShape;
  title: string | null;
  className?: string;
}) {
  return (
    <span
      aria-hidden
      data-testid="mark"
      data-mark-colour={colour}
      className={cn(
        "bg-brand text-brand-foreground inline-flex shrink-0 items-center justify-center",
        "size-4.5 text-[0.5rem] leading-none font-semibold",
        shape === "circle" ? "rounded-full" : "rounded-sm",
        className,
      )}
    >
      {lettersOf(title)}
    </span>
  );
}
