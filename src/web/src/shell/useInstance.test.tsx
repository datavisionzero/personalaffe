import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { App } from "@/shell/App";
import { anInstance } from "@/shared/anInstance";

/**
 * The version line on the screens somebody sees before they are in. These stand
 * an instance in front of the generated client rather than in place of it, so a
 * route or a field that changed shape fails by not compiling.
 */
describe("what the instance says about itself", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("shows the version the instance actually answered", async () => {
    anInstance({
      "GET /api/version": { body: { version: "1.4.2" } },
      "GET /api/setup": { body: { required: true } },
    });

    render(<App />);

    expect(await screen.findByText("1.4.2")).toBeInTheDocument();
  });

  it("asks the instance it was served from, at the one prefix the API lives under", async () => {
    const { fetch } = anInstance({
      "GET /api/version": { body: { version: "1.4.2" } },
      "GET /api/setup": { body: { required: true } },
    });

    render(<App />);
    await screen.findByText("1.4.2");

    const asked = new URL((fetch.mock.calls[0][0] as Request).url);
    expect(asked.origin).toBe(window.location.origin);
    expect(asked.pathname.startsWith("/api/")).toBe(true);
  });

  it("says nothing about a version it could not get", async () => {
    anInstance({ "GET /api/setup": { body: { required: true } } });

    render(<App />);

    expect(await screen.findByRole("heading", { name: "Claim this workspace" })).toBeInTheDocument();
    expect(screen.queryByText(/This instance is version/)).not.toBeInTheDocument();
  });
});
