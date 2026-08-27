import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 180_000,
  expect: { timeout: 60_000 },
  use: {
    baseURL: process.env.BASIS_WEBGL_URL ?? 'http://localhost:4173',
    headless: true,
    trace: 'retain-on-failure',
  },
});
