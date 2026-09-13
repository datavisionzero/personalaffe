import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { App } from "@/shell/App";

/**
 * The screen reads what the instance answered. These stand an instance in front
 * of the generated client rather than in place of it: the client is the real
 * one, the contract's types are the real ones, and only `fetch` is ours — so a
 * route or a field that changed shape fails these tests by not compiling.
 */
describe("the foundation screen", () => {
  afterEach(() => vi.unstubAllGlobals());

  function answering(answer: () => Promise<Response>) {
    const fetch = vi.fn<typeof globalThis.fetch>(() => answer());
    vi.stubGlobal("fetch", fetch);
    return fetch;
  }

  function json(body: unknown, status = 200, contentType = "application/json") {
    return new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": contentType },
    });
  }

  it("says it is asking before anything has answered", () => {
    answering(() => new Promise<Response>(() => {}));

    render(<App />);

    expect(screen.getByRole("status")).toHaveTextContent("Asking the instance");
  });

  it("shows the version the instance actually answered", async () => {
    answering(() => Promise.resolve(json({ version: "1.4.2" })));

    render(<App />);

    expect(await screen.findByText(/1\.4\.2/)).toBeInTheDocument();
  });

  it("shows what the instance refused with, not a status code alone", async () => {
    answering(() =>
      Promise.resolve(
        json(
          {
            type: "/problems/not-found",
            title: "Nothing at that address",
            status: 404,
            detail: "This instance has no such endpoint.",
          },
          404,
          "application/problem+json",
        ),
      ),
    );

    render(<App />);

    expect(await screen.findByRole("alert")).toHaveTextContent("This instance has no such endpoint.");
  });

  it("tells an unreachable instance apart from a refused one", async () => {
    answering(() => Promise.reject(new TypeError("Failed to fetch")));

    render(<App />);

    expect(await screen.findByRole("alert")).toHaveTextContent("Nothing answered at this address");
  });

  it("asks the instance again when asked to, from the keyboard", async () => {
    let version = "1.4.2";
    const fetch = answering(() => Promise.resolve(json({ version })));

    render(<App />);
    expect(await screen.findByText(/1\.4\.2/)).toBeInTheDocument();

    version = "1.5.0";
    await userEvent.tab();
    expect(screen.getByRole("button", { name: "Ask again" })).toHaveFocus();
    await userEvent.keyboard("{Enter}");

    expect(await screen.findByText(/1\.5\.0/)).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("asks the instance it was served from, at the one prefix the API lives under", async () => {
    const fetch = answering(() => Promise.resolve(json({ version: "1.4.2" })));

    render(<App />);
    await screen.findByText(/1\.4\.2/);

    const asked = new URL((fetch.mock.calls[0][0] as Request).url);
    expect(asked.origin).toBe(window.location.origin);
    expect(asked.pathname).toBe("/api/version");
  });
});
