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
  css: {
    preprocessorOptions: {
      scss: {
        loadPaths: [path.resolve(__dirname)],
        silenceDeprecations: ["legacy-js-api", "import", "global-builtin", "color-functions", "if-function"]
      }
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
      "/ws/bus-watcher": { target: wsBackendTarget, ws: true, changeOrigin: false }
    }
  },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: true
  }
});
