import { createLogger, defineConfig } from "vite";

import react from "@vitejs/plugin-react";
import path from "node:path";

const backendTarget = process.env.ASPNETCORE_URL ?? "http://localhost:5108";
const wsBackendTarget = backendTarget.replace(/^http/, "ws");

// Hard-refreshes, idle WebSocket timeouts on the .NET host, and React Strict
// Mode's mount/unmount churn all yank the browser-side WebSocket without a
// clean close. http-proxy emits an `error` event for each of these and Vite
// always registers its own `error` listener AFTER any user-supplied
// `configure` callback — so adding a second listener can't stop Vite from
// logging. Filter at the logger layer instead: any `ws proxy …` or
// `ws proxy socket …` message whose stack mentions ECONNRESET / EPIPE /
// ECONNABORTED is the harmless disconnect noise and gets swallowed. Anything
// else still prints.
const QUIET_WS_PATTERNS = [
  /ws proxy error/,
  /ws proxy socket error/,
  /econnreset/i,
  /\bEPIPE\b/,
  /\bECONNABORTED\b/
];
const wsNoiseLogger = createLogger();
const upstreamError = wsNoiseLogger.error.bind(wsNoiseLogger);
wsNoiseLogger.error = (msg, opts) => {
  if (typeof msg === "string" && QUIET_WS_PATTERNS.some((p) => p.test(msg))) {
    return;
  }
  upstreamError(msg, opts);
};

export default defineConfig({
  base: "/",
  plugins: [react()],
  customLogger: wsNoiseLogger,
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "src")
    }
  },
  server: {
    port: 5173,
    strictPort: true,
    // public/drawio/ holds the vendored drawio webapp — ~2.8k files fetched
    // by `npm run fetch:drawio`. Letting Vite's file watcher track all of
    // them saturates macOS kqueue limits; once the watcher falls over, the
    // dev server stops responding promptly to API proxy requests and the
    // browser surfaces it as ERR_INSUFFICIENT_RESOURCES on /api/* calls.
    // These assets never change at runtime, so there's nothing to watch.
    watch: {
      ignored: ["**/public/drawio/**", "**/.drawio-staging/**"]
    },
    proxy: {
      "/api": { target: backendTarget, changeOrigin: false },
      "/account": { target: backendTarget, changeOrigin: false },
      "/dapr": { target: backendTarget, changeOrigin: false },
      "/bus-watcher": { target: backendTarget, changeOrigin: false },
      "/ws/bus-watcher": {
        target: wsBackendTarget,
        ws: true,
        changeOrigin: false
      },
      "/ws/agent-model-default": {
        target: wsBackendTarget,
        ws: true,
        changeOrigin: false
      },
      // DataOptions.PublicUrlPrefix — runtime data folder (page-template
      // thumbnails copied out of plugin zips, etc.). Without this proxy
      // entry, Vite's SPA fallback returns index.html for /files/* requests.
      "/files": { target: backendTarget, changeOrigin: false }
    }
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true
  }
});
