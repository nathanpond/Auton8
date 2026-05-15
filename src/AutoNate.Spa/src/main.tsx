import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ModalsProvider } from "@mantine/modals";
import { Notifications } from "@mantine/notifications";

import Router from "./router";
import { SiteAppearanceProvider } from "./providers/SiteAppearanceProvider";
import { MantineRoot } from "./providers/MantineRoot";

import "@fortawesome/fontawesome-free/css/all.css";
import "@mantine/core/styles.layer.css";
import "@mantine/dropzone/styles.layer.css";
import "@mantine/notifications/styles.layer.css";
import "@mantine/tiptap/styles.layer.css";
import "@excalidraw/excalidraw/index.css";
import "mantine-datatable/styles.layer.css";
import "./index.css";
import "./widgets.css"; // ManageUsers identity/avatar/status, .row-archived, .notification-unread

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
        <MantineRoot>
          <ModalsProvider>
            <BrowserRouter>
              <Notifications />
              <Router />
            </BrowserRouter>
          </ModalsProvider>
        </MantineRoot>
      </SiteAppearanceProvider>
    </QueryClientProvider>
  </React.StrictMode>
);
