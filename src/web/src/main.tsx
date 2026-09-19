import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";

import "./index.css";
import { ThemeProvider } from "@/components/theme-provider";
import { App } from "@/shell/App";
import { AppearanceProvider } from "@/shell/AppearanceProvider";

/**
 * The appearance is above the router and above the door for the same reason the
 * theme is above both: it is on the document rather than on a screen, and the
 * screens before the door need it too. It is asked from outside the door, which
 * is what lets the browser tab and the sign-in screen say which instance this
 * is before anybody has signed in (`docs/api.md`, The appearance).
 *
 * The router is a browser one and its base is the root: every address outside
 * `/api` is this application's, and the instance serves `index.html` for all of
 * them (`docs/codebase.md`). So `/knowledge/architecture` typed into a fresh
 * tab reaches the same screen as walking there.
 *
 * The theme is above the router because it is on the document rather than on a
 * screen, and it is read from this browser's own storage — one person's
 * preference on one device, which is not a thing the instance should be asked
 * about.
 */
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ThemeProvider>
      <AppearanceProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </AppearanceProvider>
    </ThemeProvider>
  </StrictMode>,
);
