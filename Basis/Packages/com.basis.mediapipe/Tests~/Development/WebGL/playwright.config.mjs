import { defineConfig } from "@playwright/test";

const baseURL = process.env.BASIS_MEDIAPIPE_E2E_BASE_URL;
if (!baseURL) {
  throw new Error("BASIS_MEDIAPIPE_E2E_BASE_URL must point to the prepared Development E2E directory.");
}

export default defineConfig({
  testDir: ".",
  testMatch: "mediapipe-worker.spec.mjs",
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL,
    headless: true,
  },
});
