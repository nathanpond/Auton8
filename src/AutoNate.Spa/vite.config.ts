import { createLogger } from "vite";
// vitest's defineConfig, not vite's: it is the same function with the `test`
// block added to the type. Importing vite's leaves `test` an unknown property
// and `npm run type-check` fails -- which is the right failure, since the block
// would then be silently ignored at runtime too.
import { defineConfig } from "vitest/config";

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
      "@": path.resolve(__dirname, "src"),
      // #107: the BPMN support manifest lives outside this project because both
      // the SPA and AutoNate.Web read it. Aliased rather than copied — a copy is
      // the drift this file exists to end.
      "@shared": path.resolve(__dirname, "../shared")
    }
  },
  server: {
    port: 5173,
    strictPort: true,
    fs: {
      // src/shared/ sits above this project root, so dev-server reads of it need
      // explicit permission; without this the manifest import 403s in `npm run dev`
      // while building fine, which is a confusing way to find out.
      allow: [path.resolve(__dirname), path.resolve(__dirname, "../shared")]
    },
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
  // #323. vitest reuses this config on purpose: the module graph, the `@/` and
  // `@shared/` aliases and the JSON imports resolve in tests exactly as they do
  // in the app. A second resolver is a second thing that can disagree with the
  // app, and "the test passed but the studio is broken" is the class of defect
  // this milestone exists to close.
  test: {
    include: ["src/**/*.test.{js,ts,tsx}"],
    environment: "node",
    // No globals: every test imports `describe`/`it`/`expect` explicitly, so a
    // file that runs outside vitest fails loudly instead of silently.
    globals: false
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true
  }
});
