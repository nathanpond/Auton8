import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";
import type { ProxyOptions } from "vite";

const backendTarget = process.env.ASPNETCORE_URL ?? "http://localhost:5108";
const wsBackendTarget = backendTarget.replace(/^http/, "ws");

// Hard-refreshes yank the browser-side WebSocket without a clean close, and
// the http-proxy that Vite uses logs the resulting TCP reset as a noisy
// "ws proxy socket error: ECONNRESET" stack. The connection IS torn down,
// nothing's broken; the next page load reconnects. Swallow that one error
// code so the dev console stays readable. Any other proxy error still bubbles.
type ProxyConfigureCallback = NonNullable<ProxyOptions["configure"]>;
const silenceWsResetNoise: ProxyConfigureCallback = (proxy) => {
  const isReset = (err: unknown) =>
    typeof err === "object" &&
    err !== null &&
    "code" in err &&
    (err as { code?: string }).code === "ECONNRESET";
  proxy.on("error", (err) => {
    if (isReset(err)) return;
    console.error(err);
  });
  proxy.on("econnreset", () => {
    // http-proxy emits this dedicated event when the upstream socket
    // resets — silently consume so the default handler doesn't print.
  });
};

export default defineConfig({
  base: "/",
  plugins: [react()],
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
        changeOrigin: false,
        configure: silenceWsResetNoise
      },
      "/ws/agent-model-default": {
        target: wsBackendTarget,
        ws: true,
        changeOrigin: false,
        configure: silenceWsResetNoise
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
