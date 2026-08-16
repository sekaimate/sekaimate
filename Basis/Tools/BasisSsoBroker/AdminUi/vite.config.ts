import { defineConfig } from "vite-plus";
import react from "@vitejs/plugin-react";

const broker = process.env.BASIS_SSO_BROKER_URL ?? "https://localhost";

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
        target: broker,
        changeOrigin: true,
        secure: false,
      },
    },
  },
}));
