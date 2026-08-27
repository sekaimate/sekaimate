import { defineConfig } from '@playwright/test';

const useRealMicrophone = process.env.BASIS_REAL_MICROPHONE === '1';

export default defineConfig({
  testDir: './tests',
  timeout: 300_000,
  expect: { timeout: 60_000 },
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    browserName: 'chromium',
    channel: useRealMicrophone ? 'chrome' : undefined,
    headless: !useRealMicrophone,
    trace: 'retain-on-failure',
    launchOptions: useRealMicrophone ? undefined : {
      args: [
        '--use-fake-device-for-media-stream',
        '--use-fake-ui-for-media-stream',
      ],
    },
  },
});
