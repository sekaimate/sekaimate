import { expect, test, type Page } from '@playwright/test';

const HELLO = 1;
const DATA = 2;
const ACCEPT = 5;
const AUTH_IDENTITY_CHANNEL = 0;
const METADATA_CHANNEL = 1;
const AVATAR_CHANNEL_MIN = 6;
const AVATAR_CHANNEL_MAX = 13;
const CREATE_REMOTE_PLAYER_CHANNEL = 16;
const CREATE_EXISTING_REMOTE_PLAYER_CHANNEL = 17;
const CHAT_CHANNEL = 18;
const LARGE_AVATAR_CHANNEL_MIN = 41;
const LARGE_AVATAR_CHANNEL_MAX = 48;

interface ObservedFrame {
  direction: 'sent' | 'received';
  kind: number;
  channel: number;
  deliveryMethod: number;
  payloadLength: number;
}

interface NetworkTrace {
  connections: number;
  closedConnections: number;
  frames: ObservedFrame[];
}

interface EventCriteria {
  type: string;
  minimumCount?: number;
  message?: string;
  minimumRemotePlayerCount?: number;
  requireAvatarState?: boolean;
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

function observeNetwork(page: Page, expectedWebSocketUri: string): NetworkTrace {
  const trace: NetworkTrace = { connections: 0, closedConnections: 0, frames: [] };
  page.on('websocket', socket => {
    if (socket.url() !== expectedWebSocketUri) {
      return;
    }
    trace.connections += 1;
    socket.on('framesent', event => recordFrame(trace, 'sent', event.payload));
    socket.on('framereceived', event => recordFrame(trace, 'received', event.payload));
    socket.on('close', () => {
      trace.closedConnections += 1;
    });
  });
  return trace;
}

function recordFrame(trace: NetworkTrace, direction: ObservedFrame['direction'], payload: string | Buffer): void {
  const bytes = typeof payload === 'string' ? Buffer.from(payload) : payload;
  if (bytes.length < 3) {
    return;
  }
  trace.frames.push({
    direction,
    kind: bytes[0],
    channel: bytes[1],
    deliveryMethod: bytes[2],
    payloadLength: bytes.length - 3,
  });
}

async function waitForHarnessEvent(page: Page, criteria: EventCriteria): Promise<void> {
  await page.waitForFunction(value => {
    const events = window.basisNetworkE2EEvents ?? [];
    const matches = events.filter(event =>
      event.type === value.type
      && (value.message === undefined || event.message === value.message)
      && (value.minimumRemotePlayerCount === undefined
        || event.remotePlayerCount >= value.minimumRemotePlayerCount)
      && (!value.requireAvatarState || event.avatarStateReady));
    return matches.length >= (value.minimumCount ?? 1);
  }, criteria);
}

function hasFrame(
  trace: NetworkTrace,
  direction: ObservedFrame['direction'],
  kind: number,
  channel?: number,
): boolean {
  return trace.frames.some(frame =>
    frame.direction === direction
    && frame.kind === kind
    && (channel === undefined || frame.channel === channel));
}

function frameCount(
  trace: NetworkTrace,
  direction: ObservedFrame['direction'],
  kind: number,
  channel?: number,
): number {
  return trace.frames.filter(frame =>
    frame.direction === direction
    && frame.kind === kind
    && (channel === undefined || frame.channel === channel)).length;
}

function hasAvatarFrame(trace: NetworkTrace, direction: ObservedFrame['direction']): boolean {
  return trace.frames.some(frame => frame.direction === direction
    && frame.kind === DATA
    && ((frame.channel >= AVATAR_CHANNEL_MIN && frame.channel <= AVATAR_CHANNEL_MAX)
      || (frame.channel >= LARGE_AVATAR_CHANNEL_MIN && frame.channel <= LARGE_AVATAR_CHANNEL_MAX)));
}

test('real WebGL players authenticate, synchronize, chat, and reconnect through Basis Server', async ({ browser }) => {
  const buildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const runId = `${Date.now()}-${process.pid}`;

  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const firstPage = await firstContext.newPage();
  const secondPage = await secondContext.newPage();
  const firstTrace = observeNetwork(firstPage, webSocketUri);
  const secondTrace = observeNetwork(secondPage, webSocketUri);

  await firstPage.goto(playerUrl(buildUrl, webSocketUri, `web-e2e-a-${runId}`, password));
  await waitForHarnessEvent(firstPage, { type: 'authenticated', requireAvatarState: true });
  await secondPage.goto(playerUrl(buildUrl, webSocketUri, `web-e2e-b-${runId}`, password));
  await waitForHarnessEvent(secondPage, { type: 'authenticated', requireAvatarState: true });

  await expect.poll(() => hasFrame(firstTrace, 'sent', HELLO)).toBe(true);
  await expect.poll(() => hasFrame(firstTrace, 'received', ACCEPT)).toBe(true);
  await expect.poll(() => hasFrame(firstTrace, 'received', DATA, AUTH_IDENTITY_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(firstTrace, 'sent', DATA, AUTH_IDENTITY_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(firstTrace, 'received', DATA, METADATA_CHANNEL)).toBe(true);

  await waitForHarnessEvent(firstPage, {
    type: 'remote-state',
    minimumRemotePlayerCount: 1,
    requireAvatarState: true,
  });
  await waitForHarnessEvent(secondPage, {
    type: 'remote-state',
    minimumRemotePlayerCount: 1,
    requireAvatarState: true,
  });
  await expect.poll(() =>
    hasFrame(firstTrace, 'received', DATA, CREATE_REMOTE_PLAYER_CHANNEL)
    || hasFrame(firstTrace, 'received', DATA, CREATE_EXISTING_REMOTE_PLAYER_CHANNEL)).toBe(true);
  await expect.poll(() => hasAvatarFrame(firstTrace, 'sent')).toBe(true);
  await expect.poll(() => hasAvatarFrame(secondTrace, 'received')).toBe(true);

  const initialChat = 'basis hello from first client';
  await firstPage.evaluate(message => window.basisNetworkE2ESendChat?.(message), initialChat);
  await waitForHarnessEvent(secondPage, { type: 'chat-received', message: initialChat });
  await expect.poll(() => hasFrame(firstTrace, 'sent', DATA, CHAT_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(secondTrace, 'received', DATA, CHAT_CHANNEL)).toBe(true);

  await secondPage.evaluate(() => window.basisNetworkE2EReconnect?.());
  await waitForHarnessEvent(secondPage, { type: 'transport-accepted', minimumCount: 2 });
  await waitForHarnessEvent(secondPage, {
    type: 'authenticated',
    minimumCount: 2,
    requireAvatarState: true,
  });
  await expect.poll(() => secondTrace.closedConnections).toBeGreaterThanOrEqual(1);
  await expect.poll(() => secondTrace.connections).toBeGreaterThanOrEqual(2);
  await expect.poll(() => frameCount(secondTrace, 'sent', HELLO)).toBeGreaterThanOrEqual(2);
  await expect.poll(() => frameCount(secondTrace, 'received', ACCEPT)).toBeGreaterThanOrEqual(2);
  await expect.poll(() => frameCount(secondTrace, 'received', DATA, AUTH_IDENTITY_CHANNEL)).toBeGreaterThanOrEqual(2);
  await expect.poll(() => frameCount(secondTrace, 'sent', DATA, AUTH_IDENTITY_CHANNEL)).toBeGreaterThanOrEqual(2);
  await expect.poll(() => frameCount(secondTrace, 'received', DATA, METADATA_CHANNEL)).toBeGreaterThanOrEqual(2);

  const reconnectedChat = 'basis hello after reconnect';
  await firstPage.evaluate(message => window.basisNetworkE2ESendChat?.(message), reconnectedChat);
  await waitForHarnessEvent(secondPage, { type: 'chat-received', message: reconnectedChat });

  await firstContext.close();
  await secondContext.close();
});
