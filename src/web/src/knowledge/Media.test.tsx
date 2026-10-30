import { useState } from "react";
import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, it } from "vitest";
import { anInstance, renderAt } from "@/shared/anInstance";
import { MarkdownField } from "@/shared/MarkdownField";
import { Markdown } from "@/shared/Markdown";
const file = { id: "0199f0c6-0000-7000-8000-00000000000a", name: "photo.png", media_type: "image/png", size: 3, folder: null, created_at: "2026-01-01", updated_at: "2026-01-01" };
const listing = { folder: null, folders: [], files: [file], used_bytes: 0, max_file_bytes: 1000, max_total_bytes: 2000 };
function Writing() {
  const [value, setValue] = useState("hello ");
  return <MarkdownField label="Page" value={value} onChange={setValue} pages={[]} />;
}
it("inserts selected files as stable image references", async () => {
  anInstance({ "GET /api/files": { body: listing } });
  renderAt("/", <Writing />);
  await waitFor(() => expect(screen.getByRole("button", { name: "Insert file" })).toBeEnabled());
  await userEvent.click(screen.getByRole("button", { name: "Insert file" }));
  await userEvent.click(await screen.findByRole("button", { name: "photo.png" }));
  expect(screen.getByRole("textbox", { name: "Page" })).toHaveValue(`![photo.png](file:${file.id})hello `);
});
it("uploads pasted images and retains the surrounding text", async () => {
  const { asked } = anInstance({ "GET /api/files": { body: listing }, "POST /api/files/content": { body: file } });
  renderAt("/", <Writing />);
  const editor = await screen.findByRole("textbox", { name: "Page" });
  fireEvent.paste(editor, { clipboardData: { files: [new File(["png"], "photo.png", { type: "image/png" })] } });
  await screen.findByText(/Images uploaded to Files/);
  expect(editor).toHaveValue(`![photo.png](file:${file.id})hello `);
  expect(asked.find(r => r.method === "POST")?.headers.get("Content-Type")).toBe("image/png");
});
it("refuses oversized dropped images before sending bytes", async () => {
  const { asked } = anInstance({ "GET /api/files": { body: { ...listing, max_file_bytes: 1 } } });
  renderAt("/", <Writing />);
  const editor = await screen.findByRole("textbox", { name: "Page" });
  fireEvent.drop(editor, { dataTransfer: { files: [new File(["png"], "photo.png", { type: "image/png" })] } });
  expect(await screen.findByRole("alert")).toHaveTextContent("exceeds");
  expect(asked.some(r => r.method === "POST")).toBe(false);
  expect(editor).toHaveValue("hello ");
});
it("explains unavailable images", async () => {
  renderAt("/", <Markdown>{`![photo](file:${file.id})`}</Markdown>);
  const img = screen.getByRole("img", { name: "photo" });
  expect(img).toHaveAttribute("src", `/api/files/${file.id}/content`);
  fireEvent.error(img);
  await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("Image unavailable"));
});
