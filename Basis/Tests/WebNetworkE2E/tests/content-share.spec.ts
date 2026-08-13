import { expect, test, type Page } from '@playwright/test';
import { type BeeFormat, verifyRenderedCapability } from './runtime-capability';

const DATA = 2;
const AVATAR_CHANGE_CHANNEL = 14;
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
  url.searchParams.set('basisBeeRuntimeE2E', '1');
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
): Promise<BasisNetworkE2EEvent> {
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
  return page.evaluate(criteria => {
    const event = (window.basisNetworkE2EEvents ?? []).find(candidate =>
      candidate.type === criteria.type
      && (criteria.sphereId === undefined || candidate.sphereId === criteria.sphereId)
      && (criteria.contentType === undefined || candidate.contentType === criteria.contentType)
      && (criteria.contentUrl === undefined || candidate.contentUrl === criteria.contentUrl));
    if (!event) throw new Error(`Missing network event: ${criteria.type}`);
    return event;
  }, { type, sphereId, contentType, contentUrl });
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
  const senderAuth = await waitForEvent(senderPage, 'authenticated');
  await receiverPage.goto(playerUrl(buildUrl, webSocketUri, `web-share-receiver-${runId}`, password));
  const receiverAuth = await waitForEvent(receiverPage, 'authenticated');
  await senderPage.locator('#unity-canvas').click({ position: { x: 480, y: 300 } });
  await receiverPage.locator('#unity-canvas').click({ position: { x: 480, y: 300 } });

  for (const share of shares) {
    await senderPage.evaluate(input => window.basisNetworkE2EShareContent?.(input), share);
    await waitForEvent(senderPage, 'content-created', share.sphereId, share.contentType, share.contentUrl);
    await waitForEvent(receiverPage, 'content-created', share.sphereId, share.contentType, share.contentUrl);
  }


  const avatarShare = shares.find(share => share.contentType === 'Avatar');
  if (!avatarShare) throw new Error('Avatar share is required.');
  await senderPage.evaluate(input => window.basisNetworkE2ESetAvatar?.(input), avatarShare);
  await waitForEvent(senderPage, 'avatar-load-complete', undefined, 'Avatar', avatarShare.contentUrl);
  await receiverPage.evaluate(input => window.basisNetworkE2ESetAvatar?.(input), avatarShare);
  await waitForEvent(receiverPage, 'avatar-load-complete', undefined, 'Avatar', avatarShare.contentUrl);
  await verifyRenderedCapability(senderPage, 'Avatar', 'RemoteAvatar', receiverAuth.localPlayerId);
  await verifyRenderedCapability(receiverPage, 'Avatar', 'RemoteAvatar', senderAuth.localPlayerId);
  await expect.poll(() => hasFrame(senderFrames, 'sent', AVATAR_CHANGE_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(senderFrames, 'received', AVATAR_CHANGE_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(receiverFrames, 'sent', AVATAR_CHANGE_CHANNEL)).toBe(true);
  await expect.poll(() => hasFrame(receiverFrames, 'received', AVATAR_CHANGE_CHANNEL)).toBe(true);
  await expect.poll(() => receiverBeeRequests.some(request =>
    request.url === avatarShare.contentUrl
    && (request.requestRange !== undefined
      || request.responseStatus === 206
      || request.contentRange !== undefined))).toBe(true);

  for (const share of beeShares.filter(candidate => candidate.contentType !== 'Avatar')) {
    const format = share.contentType as BeeFormat;
    await senderPage.evaluate(sphereId => window.basisNetworkE2ELoadContent?.(sphereId), share.sphereId);
    await waitForEvent(senderPage, 'content-load-complete', share.sphereId, share.contentType, share.contentUrl);
    await receiverPage.evaluate(sphereId => window.basisNetworkE2ELoadContent?.(sphereId), share.sphereId);
    await waitForEvent(receiverPage, 'content-load-complete', share.sphereId, share.contentType, share.contentUrl);
    await verifyRenderedCapability(senderPage, format, 'Content');
    await verifyRenderedCapability(receiverPage, format, 'Content');
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
