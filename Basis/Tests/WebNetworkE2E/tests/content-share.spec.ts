import { expect, test, type Page } from '@playwright/test';

const DATA = 2;
const CONTENT_SHARE_CHANNEL = 29;
const CONTENT_SHARE_CLEANUP_CHANNEL = 30;

type ContentShareType = 'Avatar' | 'Prop' | 'World' | 'Server';

interface ObservedFrame {
  direction: 'sent' | 'received';
  kind: number;
  channel: number;
}

interface ContentShareInput {
  sphereId: string;
  contentUrl: string;
  unlockPassword: string;
  contentType: ContentShareType;
  positionX: number;
  positionY: number;
  positionZ: number;
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

function observeFrames(page: Page, expectedWebSocketUri: string): ObservedFrame[] {
  const frames: ObservedFrame[] = [];
  page.on('websocket', socket => {
    if (socket.url() !== expectedWebSocketUri) {
      return;
    }
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
  if (bytes.length < 3) {
    return;
  }
  frames.push({ direction, kind: bytes[0], channel: bytes[1] });
}

async function waitForEvent(
  page: Page,
  type: string,
  sphereId?: string,
  contentType?: ContentShareType,
): Promise<void> {
  await page.waitForFunction(criteria => (window.basisNetworkE2EEvents ?? []).some(event =>
    event.type === criteria.type
    && (criteria.sphereId === undefined || event.sphereId === criteria.sphereId)
    && (criteria.contentType === undefined || event.contentType === criteria.contentType)), {
    type,
    sphereId,
    contentType,
  });
}

function hasFrame(frames: ObservedFrame[], direction: ObservedFrame['direction'], channel: number): boolean {
  return frames.some(frame => frame.direction === direction && frame.kind === DATA && frame.channel === channel);
}

test('Avatar, Prop, World, and Server shares synchronize through Basis Server', async ({ browser }) => {
  const buildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const runId = `${Date.now()}-${process.pid}`;
  const shares: ContentShareInput[] = [
    ['Avatar', 'https://example.test/avatar.BEE', 'avatar-pass'],
    ['Prop', 'https://example.test/prop.BEE', 'prop-pass'],
    ['World', 'https://example.test/world.BEE', 'world-pass'],
    ['Server', 'basis.example.test:4296#server-pass', ''],
  ].map(([contentType, contentUrl, unlockPassword], index) => ({
    sphereId: `web-content-${runId}-${contentType.toLowerCase()}`,
    contentUrl,
    unlockPassword,
    contentType: contentType as ContentShareType,
    positionX: index * 1.5,
    positionY: 1,
    positionZ: 2,
  }));

  const senderContext = await browser.newContext();
  const receiverContext = await browser.newContext();
  const senderPage = await senderContext.newPage();
  const receiverPage = await receiverContext.newPage();
  const senderFrames = observeFrames(senderPage, webSocketUri);
  const receiverFrames = observeFrames(receiverPage, webSocketUri);

  await senderPage.goto(playerUrl(buildUrl, webSocketUri, `web-share-sender-${runId}`, password));
  await waitForEvent(senderPage, 'authenticated');
  await receiverPage.goto(playerUrl(buildUrl, webSocketUri, `web-share-receiver-${runId}`, password));
  await waitForEvent(receiverPage, 'authenticated');

  for (const share of shares) {
    await senderPage.evaluate(input => window.basisNetworkE2EShareContent?.(input), share);
    await waitForEvent(senderPage, 'content-created', share.sphereId, share.contentType);
    await waitForEvent(receiverPage, 'content-created', share.sphereId, share.contentType);
  }

  await expect.poll(() => hasFrame(senderFrames, 'sent', CONTENT_SHARE_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(receiverFrames, 'received', CONTENT_SHARE_CHANNEL)).toBe(true);

  const lateContext = await browser.newContext();
  const latePage = await lateContext.newPage();
  await latePage.goto(playerUrl(buildUrl, webSocketUri, `web-share-late-${runId}`, password));
  await waitForEvent(latePage, 'authenticated');
  for (const share of shares) {
    await waitForEvent(latePage, 'content-created', share.sphereId, share.contentType);
  }

  const explicitlyRemoved = shares[0];
  await senderPage.evaluate(sphereId => window.basisNetworkE2ERemoveContent?.(sphereId), explicitlyRemoved.sphereId);
  await waitForEvent(senderPage, 'content-removed', explicitlyRemoved.sphereId);
  await waitForEvent(receiverPage, 'content-removed', explicitlyRemoved.sphereId);
  await waitForEvent(latePage, 'content-removed', explicitlyRemoved.sphereId);
  await expect.poll(() => hasFrame(senderFrames, 'sent', CONTENT_SHARE_CLEANUP_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(receiverFrames, 'received', CONTENT_SHARE_CLEANUP_CHANNEL)).toBe(true);

  await senderContext.close();
  for (const share of shares.slice(1)) {
    await waitForEvent(receiverPage, 'content-removed', share.sphereId);
    await waitForEvent(latePage, 'content-removed', share.sphereId);
  }

  await receiverContext.close();
  await lateContext.close();
});
