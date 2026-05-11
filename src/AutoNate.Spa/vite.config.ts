import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

const backendTarget = process.env.ASPNETCORE_URL ?? "http://localhost:5108";
const wsBackendTarget = backendTarget.replace(/^http/, "ws");

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
    proxy: {
      "/api": { target: backendTarget, changeOrigin: false },
      "/account": { target: backendTarget, changeOrigin: false },
      "/dapr": { target: backendTarget, changeOrigin: false },
      "/bus-watcher": { target: backendTarget, changeOrigin: false },
      "/ws/bus-watcher": { target: wsBackendTarget, ws: true, changeOrigin: false },
      "/ws/agent-model-default": { target: wsBackendTarget, ws: true, changeOrigin: false },
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
