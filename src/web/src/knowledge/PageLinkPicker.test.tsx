import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it, vi } from "vitest";
import { renderAt } from "@/shared/anInstance";
import { PageLinkPicker } from "./PageLinkPicker";
it("filters titles and selects the stable page identity", async () => {
  const onPick = vi.fn();
  const page = { id: "0199f0c6-0000-7000-8000-00000000000a", title: "Rail travel" };
  renderAt("/", <PageLinkPicker pages={[page, { id: "other", title: "Cooking" }]} onPick={onPick} disabled={false} />);
  await userEvent.click(screen.getByRole("button", { name: "Link page" }));
  await userEvent.type(screen.getByRole("textbox", { name: "Find a page by title" }), "RAIL");
  expect(screen.queryByRole("button", { name: "Cooking" })).toBeNull();
  await userEvent.click(screen.getByRole("button", { name: "Rail travel" }));
  expect(onPick).toHaveBeenCalledWith(page);
});
