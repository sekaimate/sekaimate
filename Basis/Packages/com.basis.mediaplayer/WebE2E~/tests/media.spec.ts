import { expect, test, type Page } from "@playwright/test";

interface WebMediaDiagnostics {
  phase: string;
  mediaId: number;
  htmlVideoElement: boolean;
  sourceUrl: string;
  corsMode: string;
  crossOriginRequest: boolean;
  codecSupport: string;
  videoWidth: number;
  videoHeight: number;
  currentTime: number;
  paused: boolean;
  playbackStarted: boolean;
  playRequestCount: number;
  pauseRequestCount: number;
  pauseObserved: boolean;
  seekRequestCount: number;
  seekCompletedCount: number;
  lastSeekSeconds: number;
  textureUploadCount: number;
  audioContextCreated: boolean;
  mediaElementSourceCreated: boolean;
  gainConnected: boolean;
  destinationConnected: boolean;
  audioContextState: string;
  errorCode: number;
}

declare global {
  interface Window {
    __basisWebMediaE2E?: WebMediaDiagnostics;
    fixtureReady?: boolean;
  }
}

const port = Number.parseInt(process.env.BASIS_WEB_MEDIA_E2E_PORT ?? "4176", 10);
const appOrigin = `http://app.lvh.me:${port}`;
const mediaOrigin = `http://media.lvh.me:${port}`;
const mediaUrl = `${mediaOrigin}/__basis/media-fixture.webm`;

async function createBrowserMediaFixture(page: Page): Promise<void> {
  await page.goto(`${mediaOrigin}/__basis/media-fixture.html`);
  await expect.poll(() => page.evaluate(() => window.fixtureReady)).toBe(true);
}

test("BasisMediaPlayer plays cross-origin video through WebGL texture and WebAudio", async ({ page }) => {
  await createBrowserMediaFixture(page);

  const consoleErrors: string[] = [];
  page.on("console", message => {
    if (message.type() === "error") consoleErrors.push(message.text());
  });
  const mediaResponsePromise = page.waitForResponse(response =>
    response.url() === mediaUrl && (response.status() === 200 || response.status() === 206));
  const fixtureUrl = `${appOrigin}/?basisMediaE2E=1&basisMediaE2EUrl=${encodeURIComponent(mediaUrl)}`;
  await page.goto(fixtureUrl);

  const mediaResponse = await mediaResponsePromise;
  expect(mediaResponse.headers()["access-control-allow-origin"]).toBe(appOrigin);
  expect(mediaResponse.headers()["cross-origin-resource-policy"]).toBe("cross-origin");

  await expect.poll(() => page.evaluate(() => window.__basisWebMediaE2E?.textureUploadCount ?? 0)).toBeGreaterThan(2);
  await expect.poll(() => page.evaluate(() => window.__basisWebMediaE2E?.audioContextState)).toBe("running");
  await expect.poll(() => page.evaluate(() => window.__basisWebMediaE2E?.pauseRequestCount ?? 0)).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() => window.__basisWebMediaE2E?.seekRequestCount ?? 0)).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() => window.__basisWebMediaE2E?.seekCompletedCount ?? 0)).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() => window.__basisWebMediaE2E?.currentTime ?? 0)).toBeGreaterThan(0.5);
  const diagnostics = await page.evaluate(() => window.__basisWebMediaE2E);

  expect(diagnostics).toMatchObject({
    htmlVideoElement: true,
    sourceUrl: mediaUrl,
    corsMode: "anonymous",
    crossOriginRequest: true,
    codecSupport: expect.stringMatching(/maybe|probably/),
    videoWidth: 64,
    videoHeight: 64,
    playbackStarted: true,
    paused: false,
    audioContextCreated: true,
    mediaElementSourceCreated: true,
    gainConnected: true,
    destinationConnected: true,
    audioContextState: "running",
    errorCode: 0,
  });
  expect(diagnostics?.playRequestCount).toBeGreaterThan(0);
  expect(diagnostics?.pauseRequestCount).toBeGreaterThan(0);
  expect(diagnostics?.pauseObserved).toBe(true);
  expect(diagnostics?.seekRequestCount).toBeGreaterThan(0);
  expect(diagnostics?.seekCompletedCount).toBeGreaterThan(0);
  expect(diagnostics?.lastSeekSeconds).toBeCloseTo(0.25, 2);
  expect(diagnostics?.textureUploadCount).toBeGreaterThan(2);
  expect(diagnostics?.currentTime).toBeGreaterThan(0);
  expect(consoleErrors).toEqual([]);
});
