import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  timeout: 180_000,
  use: {
    headless: true,
    viewport: { width: 1280, height: 720 },
  },
});
