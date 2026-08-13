import { expect, test, type BrowserContext, type Page } from '@playwright/test';

const DATA = 2;
const VOICE_CHANNEL = 3;
const FATAL_RUNTIME_ERROR = /Loading FSB failed|EncodingError|NullReferenceException|Object reference not set|is not approved and will be removed|An error occurred running the Unity content/i;

interface ObservedFrame {
  direction: 'sent' | 'received';
  kind: number;
  channel: number;
}

interface VoiceSnapshot {
  captureState: number;
  permissionGranted: boolean;
  capturePcmFrames: number;
  opusEncodedPackets: number;
  networkPacketsSent: number;
  networkPacketsReceived: number;
  opusDecodedFrames: number;
  playbackFramesPushed: number;
  playbackNonSilentFramesPushed: number;
  playbackPeak: number;
  muted: boolean;
  muteChanges: number;
  talkMode: number;
  talkModeChanges: number;
  remoteMuted: boolean;
  remoteMuteChanges: number;
  remoteTalkMode: number;
  remoteTalkModeChanges: number;
  localVisemeFrames: number;
  localVisemePeak: number;
  remoteVisemeFrames: number;
  remoteVisemePeak: number;
}

interface VoiceVerdict {
  passed: boolean;
  failures: string[];
  snapshot: VoiceSnapshot;
}

interface NetworkEvent {
  type: string;
  localPlayerId: number;
  remotePlayerCount: number;
  avatarStateReady: boolean;
}

function observeRuntimeErrors(page: Page, label: string): string[] {
  const errors: string[] = [];
  page.on('console', message => {
    if (message.type() !== 'error' || !FATAL_RUNTIME_ERROR.test(message.text())) return;
    const error = `${label} console: ${message.text()}`;
    errors.push(error);
    console.error(error);
  });
  page.on('pageerror', exception => {
    const error = `${label} page: ${exception.message}`;
    errors.push(error);
    console.error(error);
  });
  page.on('dialog', dialog => {
    if (!FATAL_RUNTIME_ERROR.test(dialog.message())) return;
    const error = `${label} dialog: ${dialog.message()}`;
    errors.push(error);
    console.error(error);
  });
  return errors;
}

function assertNoRuntimeErrors(...errorLists: string[][]): void {
  expect(errorLists.flat(), 'Unity runtime errors were rendered during E2E.').toEqual([]);
}

declare global {
  interface Window {
    BasisWebAudioDiagnostics?: {
      schemaVersion: number;
      reset(): void;
      snapshot(): VoiceSnapshot;
      verifySender(): VoiceVerdict;
      verifyReceiver(): VoiceVerdict;
    };
    basisNetworkE2EEvents?: NetworkEvent[];
    basisNetworkE2ESetMuted?(muted: boolean): void;
    basisNetworkE2ESetTalkMode?(talkMode: string): void;
  }
}

function requiredEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`${name} must point to an already-running test dependency.`);
  }
  return value;
}

function playerUrl(buildUrl: string, webSocketUri: string, userName: string, password: string): string {
  const url = new URL(buildUrl);
  url.searchParams.set('basisNetworkE2E', '1');
  url.searchParams.set('websocketUri', webSocketUri);
  url.searchParams.set('userName', userName);
  url.searchParams.set('password', password);
  return url.toString();
}

async function waitForEvent(page: Page, type: string): Promise<NetworkEvent> {
  await page.waitForFunction(eventType =>
    (window.basisNetworkE2EEvents ?? []).some(event => event.type === eventType), type);
  return page.evaluate(eventType => {
    const event = (window.basisNetworkE2EEvents ?? []).find(candidate => candidate.type === eventType);
    if (!event) throw new Error(`Missing network event: ${eventType}`);
    return event;
  }, type);
}

async function waitForAudioDiagnostics(page: Page): Promise<void> {
  await page.waitForFunction(() => window.BasisWebAudioDiagnostics?.schemaVersion === 1);
}

async function activateAudio(page: Page): Promise<void> {
  await page.evaluate(() => window.basisNetworkE2ESetMuted?.(false));
  await page.locator('#unity-canvas').click({ position: { x: 480, y: 300 } });
}

async function snapshot(page: Page): Promise<VoiceSnapshot> {
  return page.evaluate(() => {
    if (!window.BasisWebAudioDiagnostics) throw new Error('Web audio diagnostics are unavailable.');
    return window.BasisWebAudioDiagnostics.snapshot();
  });
}

async function reset(page: Page): Promise<void> {
  await page.evaluate(() => window.BasisWebAudioDiagnostics?.reset());
}

async function grantMicrophone(context: BrowserContext, buildUrl: string): Promise<void> {
  await context.grantPermissions(['microphone'], { origin: new URL(buildUrl).origin });
}

function observeFrames(page: Page, expectedWebSocketUri: string): ObservedFrame[] {
  const frames: ObservedFrame[] = [];
  page.on('websocket', socket => {
    if (socket.url() !== expectedWebSocketUri) return;
    socket.on('framesent', event => recordFrame(frames, 'sent', event.payload));
    socket.on('framereceived', event => recordFrame(frames, 'received', event.payload));
  });
  return frames;
}

function recordFrame(
  frames: ObservedFrame[],
  direction: ObservedFrame['direction'],
  payload: string | Buffer,
): void {
  const bytes = typeof payload === 'string' ? Buffer.from(payload) : payload;
  if (bytes.length < 3) return;
  frames.push({ direction, kind: bytes[0], channel: bytes[1] });
}

function hasVoiceFrame(frames: ObservedFrame[], direction: ObservedFrame['direction']): boolean {
  return frames.some(frame => frame.direction === direction
    && frame.kind === DATA
    && frame.channel === VOICE_CHANNEL);
}

test('denied getUserMedia permission reports the production capture failure', async ({ browser }) => {
  const buildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const context = await browser.newContext();
  const page = await context.newPage();
  const runtimeErrors = observeRuntimeErrors(page, 'permission-denied');
  const client = await context.newCDPSession(page);
  await client.send('Browser.setPermission', {
    permission: { name: 'audioCapture' },
    setting: 'denied',
    origin: new URL(buildUrl).origin,
  });

  await page.goto(playerUrl(buildUrl, webSocketUri, `web-audio-denied-${Date.now()}`, password));
  await waitForEvent(page, 'authenticated');
  await waitForAudioDiagnostics(page);
  await reset(page);
  await activateAudio(page);

  await expect.poll(() => snapshot(page).then(value => value.captureState)).toBe(4);
  await expect.poll(() => snapshot(page).then(value => value.permissionGranted)).toBe(false);
  await expect.poll(() => snapshot(page).then(value => value.muted)).toBe(true);
  assertNoRuntimeErrors(runtimeErrors);
  await context.close();
});

test('two WebGL clients run capture, Opus, network, playback, talk modes, mute, and lip sync', async ({ browser }) => {
  const buildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const runId = `${Date.now()}-${process.pid}`;
  const senderContext = await browser.newContext();
  const receiverContext = await browser.newContext();
  await grantMicrophone(senderContext, buildUrl);
  await grantMicrophone(receiverContext, buildUrl);
  const sender = await senderContext.newPage();
  const receiver = await receiverContext.newPage();
  const senderRuntimeErrors = observeRuntimeErrors(sender, 'sender');
  const receiverRuntimeErrors = observeRuntimeErrors(receiver, 'receiver');
  const senderFrames = observeFrames(sender, webSocketUri);
  const receiverFrames = observeFrames(receiver, webSocketUri);

  await sender.goto(playerUrl(buildUrl, webSocketUri, `web-audio-sender-${runId}`, password));
  await receiver.goto(playerUrl(buildUrl, webSocketUri, `web-audio-receiver-${runId}`, password));
  const senderAuth = await waitForEvent(sender, 'authenticated');
  const receiverAuth = await waitForEvent(receiver, 'authenticated');
  await expect.poll(() => sender.evaluate(() => (window.basisNetworkE2EEvents ?? []).some(event =>
    event.type === 'remote-state' && event.remotePlayerCount >= 1 && event.avatarStateReady))).toBe(true);
  await expect.poll(() => receiver.evaluate(() => (window.basisNetworkE2EEvents ?? []).some(event =>
    event.type === 'remote-state' && event.remotePlayerCount >= 1 && event.avatarStateReady))).toBe(true);
  await waitForAudioDiagnostics(sender);
  await waitForAudioDiagnostics(receiver);
  await reset(sender);
  await reset(receiver);
  await activateAudio(sender);
  await activateAudio(receiver);
  assertNoRuntimeErrors(senderRuntimeErrors, receiverRuntimeErrors);

  await expect.poll(() => sender.evaluate(() => window.BasisWebAudioDiagnostics?.verifySender())).toMatchObject({
    passed: true,
    failures: [],
  });
  await expect.poll(() => sender.evaluate(() => window.BasisWebAudioDiagnostics?.verifyReceiver())).toMatchObject({
    passed: true,
    failures: [],
  });
  await expect.poll(() => receiver.evaluate(() => window.BasisWebAudioDiagnostics?.verifySender())).toMatchObject({
    passed: true,
    failures: [],
  });
  await expect.poll(() => receiver.evaluate(() => window.BasisWebAudioDiagnostics?.verifyReceiver())).toMatchObject({
    passed: true,
    failures: [],
  });
  await expect.poll(() => hasVoiceFrame(senderFrames, 'sent')).toBe(true);
  await expect.poll(() => hasVoiceFrame(senderFrames, 'received')).toBe(true);
  await expect.poll(() => hasVoiceFrame(receiverFrames, 'sent')).toBe(true);
  await expect.poll(() => hasVoiceFrame(receiverFrames, 'received')).toBe(true);
  await expect.poll(() => snapshot(sender).then(value => value.localVisemeFrames)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(sender).then(value => value.localVisemePeak)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(sender).then(value => value.remoteVisemeFrames)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(sender).then(value => value.remoteVisemePeak)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(receiver).then(value => value.localVisemeFrames)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(receiver).then(value => value.localVisemePeak)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(receiver).then(value => value.remoteVisemeFrames)).toBeGreaterThan(0);
  await expect.poll(() => snapshot(receiver).then(value => value.remoteVisemePeak)).toBeGreaterThan(0);

  await sender.evaluate(targetPlayerId =>
    window.basisNetworkE2ESetTalkMode?.(`ThisPerson:${targetPlayerId}`), receiverAuth.localPlayerId);
  await expect.poll(() => snapshot(sender).then(value => value.talkMode)).toBe(3);
  await expect.poll(() => snapshot(receiver).then(value => value.remoteTalkMode)).toBe(3);
  await sender.evaluate(() => window.basisNetworkE2ESetTalkMode?.('Normal'));
  await expect.poll(() => snapshot(sender).then(value => value.talkMode)).toBe(0);
  await expect.poll(() => snapshot(receiver).then(value => value.remoteTalkMode)).toBe(0);

  await sender.evaluate(() => window.basisNetworkE2ESetMuted?.(true));
  await expect.poll(() => snapshot(sender).then(value => value.muted)).toBe(true);
  await expect.poll(() => snapshot(receiver).then(value => value.remoteMuted)).toBe(true);
  await pageStablePacketCount(sender);

  const packetsBeforeUnmute = (await snapshot(sender)).networkPacketsSent;
  await sender.evaluate(() => window.basisNetworkE2ESetMuted?.(false));
  await sender.locator('#unity-canvas').click({ position: { x: 480, y: 300 } });
  await expect.poll(() => snapshot(sender).then(value => value.muted)).toBe(false);
  await expect.poll(() => snapshot(receiver).then(value => value.remoteMuted)).toBe(false);
  await expect.poll(() => snapshot(sender).then(value => value.networkPacketsSent)).toBeGreaterThan(packetsBeforeUnmute);

  expect(senderAuth.localPlayerId).not.toBe(receiverAuth.localPlayerId);
  assertNoRuntimeErrors(senderRuntimeErrors, receiverRuntimeErrors);
  await senderContext.close();
  await receiverContext.close();
});

async function pageStablePacketCount(page: Page): Promise<void> {
  await page.waitForTimeout(500);
  const before = (await snapshot(page)).networkPacketsSent;
  await page.waitForTimeout(1_000);
  expect((await snapshot(page)).networkPacketsSent).toBe(before);
}
