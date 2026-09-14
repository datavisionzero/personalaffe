import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
// From vitest rather than vite, so that the test section below is typed too.
import { defineConfig } from "vitest/config";

/**
 * Where the instance is (`docs/codebase.md`). Development runs the two
 * toolchains side by side: Vite serves the application and forwards `/api` to
 * the instance, so that the application reaches the API at its own origin there
 * as well as in the image. One prefix, so a new endpoint is never a second
 * place to register it.
 */
const instance = "/api";

/** What `dotnet run --project src/Personalaffe.Api` listens on. */
const backend = "http://localhost:5000";

export default defineConfig({
  plugins: [react(), tailwindcss()],

  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "./src"),
    },
  },

  // A local `npm run build` lands where the server serves static files from, so
  // that one `dotnet run` gives the whole product. The image does the same in
  // two stages (deploy/Dockerfile).
  build: {
    outDir: "../Personalaffe.Api/wwwroot",
    emptyOutDir: true,
  },

  server: {
    port: 5173,
    proxy: { [instance]: backend },
  },

  test: {
    environment: "jsdom",
    setupFiles: ["./src/shared/setupTests.ts"],

    // The application's own tests and no others. `browser/` is Playwright's,
    // runs against a real instance in a real browser, and would be picked up by
    // the default glob and fail here for reasons that say nothing about it.
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
