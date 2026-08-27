import { defineConfig } from "@playwright/test";

const port = Number.parseInt(process.env.BASIS_WEB_MEDIA_E2E_PORT ?? "4176", 10);

export default defineConfig({
  testDir: "./tests",
  timeout: 120_000,
  expect: { timeout: 90_000 },
  fullyParallel: false,
  workers: 1,
  use: {
    browserName: "chromium",
    headless: true,
    launchOptions: {
      args: [
        "--autoplay-policy=no-user-gesture-required",
        "--host-resolver-rules=MAP *.lvh.me 127.0.0.1",
      ],
    },
  },
  webServer: {
    command: "pnpm start",
    url: `http://app.lvh.me:${port}`,
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
