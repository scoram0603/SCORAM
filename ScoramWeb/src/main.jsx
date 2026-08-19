import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Routes, Route } from "react-router-dom";
import "./index.css";
import App from "./App.jsx";
import AdminApp from "./admin/AdminApp.jsx";

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        {/* Deliberately not linked from anywhere in the student UI (nav, footer, etc.) --
            admins reach this by going straight to /admin/*. Each app owns its own nested
            <Routes> below, so this is the only place the two ever have to know about each other. */}
        <Route path="/admin/*" element={<AdminApp />} />
        <Route path="/*" element={<App />} />
      </Routes>
    </BrowserRouter>
  </StrictMode>
);
