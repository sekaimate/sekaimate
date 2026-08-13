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

interface BeeRequestEvidence {
  url: string;
  requestRange: string | undefined;
  responseStatus: number;
  contentRange: string | undefined;
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
  contentUrl?: string,
): Promise<void> {
  await page.waitForFunction(criteria => (window.basisNetworkE2EEvents ?? []).some(event =>
    event.type === criteria.type
    && (criteria.sphereId === undefined || event.sphereId === criteria.sphereId)
    && (criteria.contentType === undefined || event.contentType === criteria.contentType)
    && (criteria.contentUrl === undefined || event.contentUrl === criteria.contentUrl)), {
    type,
    sphereId,
    contentType,
    contentUrl,
  });
}

function hasFrame(frames: ObservedFrame[], direction: ObservedFrame['direction'], channel: number): boolean {
  return frames.some(frame => frame.direction === direction && frame.kind === DATA && frame.channel === channel);
}

function observeBeeRequests(page: Page, beeUrls: string[]): BeeRequestEvidence[] {
  const evidence: BeeRequestEvidence[] = [];
  page.on('response', response => {
    if (!beeUrls.includes(response.url())) {
      return;
    }
    evidence.push({
      url: response.url(),
      requestRange: response.request().headers()['range'],
      responseStatus: response.status(),
      contentRange: response.headers()['content-range'],
    });
  });
  return evidence;
}

test('Avatar, Prop, World, and Server shares synchronize through Basis Server', async ({ browser }) => {
  const buildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const runId = `${Date.now()}-${process.pid}`;
  const shares: ContentShareInput[] = [
    ['Avatar', requiredEnvironment('BASIS_AVATAR_BEE_URL'), requiredEnvironment('BASIS_AVATAR_BEE_PASSWORD')],
    ['Prop', requiredEnvironment('BASIS_PROP_BEE_URL'), requiredEnvironment('BASIS_PROP_BEE_PASSWORD')],
    ['World', requiredEnvironment('BASIS_WORLD_BEE_URL'), requiredEnvironment('BASIS_WORLD_BEE_PASSWORD')],
    ['Server', requiredEnvironment('BASIS_SHARED_SERVER_CONNECTION'), ''],
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
  const beeShares = shares.filter(share => share.contentType !== 'Server');
  const receiverBeeRequests = observeBeeRequests(receiverPage, beeShares.map(share => share.contentUrl));

  await senderPage.goto(playerUrl(buildUrl, webSocketUri, `web-share-sender-${runId}`, password));
  await waitForEvent(senderPage, 'authenticated');
  await receiverPage.goto(playerUrl(buildUrl, webSocketUri, `web-share-receiver-${runId}`, password));
  await waitForEvent(receiverPage, 'authenticated');

  for (const share of shares) {
    await senderPage.evaluate(input => window.basisNetworkE2EShareContent?.(input), share);
    await waitForEvent(senderPage, 'content-created', share.sphereId, share.contentType, share.contentUrl);
    await waitForEvent(receiverPage, 'content-created', share.sphereId, share.contentType, share.contentUrl);
  }


  for (const share of beeShares) {
    await receiverPage.evaluate(sphereId => window.basisNetworkE2ELoadContent?.(sphereId), share.sphereId);
    await waitForEvent(receiverPage, 'content-load-complete', share.sphereId, share.contentType, share.contentUrl);
    await expect.poll(() => receiverBeeRequests.some(request =>
      request.url === share.contentUrl
      && (request.requestRange !== undefined
        || request.responseStatus === 206
        || request.contentRange !== undefined))).toBe(true);
  }

  await expect.poll(() => hasFrame(senderFrames, 'sent', CONTENT_SHARE_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(receiverFrames, 'received', CONTENT_SHARE_CHANNEL)).toBe(true);

  const lateContext = await browser.newContext();
  const latePage = await lateContext.newPage();
  await latePage.goto(playerUrl(buildUrl, webSocketUri, `web-share-late-${runId}`, password));
  await waitForEvent(latePage, 'authenticated');
  for (const share of shares) {
    await waitForEvent(latePage, 'content-created', share.sphereId, share.contentType, share.contentUrl);
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
