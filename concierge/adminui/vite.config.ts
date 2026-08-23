import { defineConfig } from "vite-plus";
import react from "@vitejs/plugin-react";

const concierge = process.env.CONCIERGE_URL ?? "http://127.0.0.1:5080";

export default defineConfig(({ command }) => ({
  base: command === "build" ? "/admin/" : "/",
  plugins: [react()],
  build: {
    rollupOptions: {
      input: {
        admin: "index.html",
        join: "join.html",
      },
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: concierge,
        changeOrigin: true,
        secure: false,
      },
      "/health": {
        target: concierge,
        changeOrigin: true,
        secure: false,
      },
      "/join": {
        target: concierge,
        changeOrigin: true,
        secure: false,
      },
    },
  },
}));
