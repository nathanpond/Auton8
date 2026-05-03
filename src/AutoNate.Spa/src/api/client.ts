import axios, { AxiosError } from "axios";

const LOGIN_PATH = "/";

export const api = axios.create({
  baseURL: "/",
  headers: {
    "Content-Type": "application/json"
  },
  withCredentials: true
});

api.interceptors.response.use(
  (response) => {
    // The .NET host has a SPA fallback (`MapFallbackToFile("{*path:nonfile}", "index.html")`)
    // that serves the React index.html for any unmatched route. If a request to /api
    // ever falls through to that, axios receives HTML and — because its default JSON
    // parser silently returns the raw string on parse failure — callers think they
    // got valid data and iterate over a string, producing garbage UI. Treat any
    // HTML response on /api as a hard error so the UI shows "failed to load" rather
    // than rendering character-by-character.
    const url = response.config?.url ?? "";
    if (url.startsWith("/api")) {
      const contentType = response.headers?.["content-type"];
      if (typeof contentType === "string" && contentType.toLowerCase().includes("text/html")) {
        return Promise.reject(
          new Error(`Unexpected HTML response from API endpoint ${url} (likely an unmatched route or stale bundle)`)
        );
      }
    }
    return response;
  },
  (error: AxiosError) => {
    const status = error.response?.status;
    const url = error.config?.url ?? "";

    // Auth probe and login endpoints return 401 as normal flow; don't redirect those.
    const isAuthProbe = url.includes("/api/auth/me");

    if (status === 401 && !isAuthProbe) {
      if (typeof window !== "undefined" && window.location.pathname !== LOGIN_PATH) {
        const returnUrl = window.location.pathname + window.location.search;
        window.location.href = `${LOGIN_PATH}?returnUrl=${encodeURIComponent(returnUrl)}`;
      }
    }

    return Promise.reject(error);
  }
);
