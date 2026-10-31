import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import "./index.css";
import App from "./App.jsx";
import ScrollManager from "./components/routing/ScrollManager";
import RouteSuspense from "./components/routing/RouteSuspense";
import { lazyWithRetry } from "./components/routing/lazyWithRetry";

// The whole admin panel (every admin page, Recharts, bulk-import tooling) is its own chunk, fetched
// only when someone actually opens /admin/* -- students and landing-page visitors never download it.
// (KaTeX's stylesheet is imported by MathText itself, so it also loads only with math-heavy pages.)
const AdminApp = lazyWithRetry(() => import("./admin/AdminApp.jsx"));

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <BrowserRouter>
      {/* New routes open at the top; "/#section" anchors scroll to their section. */}
      <ScrollManager />
      <Routes>
        {/* Deliberately not linked from anywhere in the student UI (nav, footer, etc.) --
            admins reach this by going straight to /admin/*. Each app owns its own nested
            <Routes> below, so this is the only place the two ever have to know about each other. */}
        <Route
          path="/admin/*"
          element={
            <RouteSuspense>
              <AdminApp />
            </RouteSuspense>
          }
        />
        <Route path="/*" element={<App />} />
      </Routes>
    </BrowserRouter>
  </StrictMode>
);
