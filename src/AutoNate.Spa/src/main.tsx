import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReactNotifications } from "react-notifications-component";

import Router from "./router";
import { SiteAppearanceProvider } from "./providers/SiteAppearanceProvider";

import "bootstrap";
import "@fortawesome/fontawesome-free/css/all.css";
import "bootstrap-icons/font/bootstrap-icons.css";
import "react-notifications-component/dist/theme.css";
import "react-perfect-scrollbar/dist/css/styles.css";
import "./index.css";
import "./scss/react.scss";
import "./scss/app.css"; // AutoNate custom overlay (page-head, quick-link-card, dashboard-stat, etc.)

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
      staleTime: 30_000
    }
  }
});

const container = document.getElementById("root");
if (!container) {
  throw new Error("Root container #root not found in index.html");
}

createRoot(container).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <SiteAppearanceProvider>
        <BrowserRouter>
          <ReactNotifications />
          <Router />
        </BrowserRouter>
      </SiteAppearanceProvider>
    </QueryClientProvider>
  </React.StrictMode>
);
