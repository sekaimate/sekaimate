import { expect, test, type Page } from '@playwright/test';

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

async function waitForEvent(page: Page, type: string, minimumCount = 1): Promise<void> {
  await page.waitForFunction(({ expectedType, count }) =>
    (window.basisNetworkE2EEvents ?? []).filter(event => event.type === expectedType).length >= count,
  { expectedType: type, count: minimumCount });
}

async function latestEvent(page: Page, type: string): Promise<BasisNetworkE2EEvent> {
  return page.evaluate(expectedType => {
    const matches = (window.basisNetworkE2EEvents ?? []).filter(event => event.type === expectedType);
    const event = matches.at(-1);
    if (!event) {
      throw new Error(`Missing ${expectedType} event.`);
    }
    return event;
  }, type);
}

test('player list and individual player actions use the live two-client session', async ({ browser }) => {
  const buildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const runId = `${Date.now()}-${process.pid}`;
  const firstName = `player-ui-a-${runId}`;
  const secondName = `player-ui-b-${runId}`;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const firstPage = await firstContext.newPage();
  const secondPage = await secondContext.newPage();

  await firstPage.goto(playerUrl(buildUrl, webSocketUri, firstName, password));
  await waitForEvent(firstPage, 'authenticated');
  await secondPage.goto(playerUrl(buildUrl, webSocketUri, secondName, password));
  await waitForEvent(secondPage, 'authenticated');
  await firstPage.waitForFunction(() =>
    (window.basisNetworkE2EEvents ?? []).some(event => event.remotePlayerCount >= 1));
  await secondPage.waitForFunction(() =>
    (window.basisNetworkE2EEvents ?? []).some(event => event.remotePlayerCount >= 1));

  await firstPage.evaluate(() => window.basisNetworkE2EOpenPlayerList?.());
  await waitForEvent(firstPage, 'player-list-state');
  let listState = await latestEvent(firstPage, 'player-list-state');
  expect(listState.visibleLabels).toEqual(expect.arrayContaining([firstName, secondName]));

  await firstPage.evaluate(name => window.basisNetworkE2EPlayerSearch?.(name), secondName);
  await waitForEvent(firstPage, 'player-list-state', 2);
  listState = await latestEvent(firstPage, 'player-list-state');
  expect(listState.visibleLabels).toEqual([secondName]);

  await firstPage.evaluate(() => window.basisNetworkE2EPlayerSearch?.(''));
  await firstPage.evaluate(() => window.basisNetworkE2EPlayerSort?.('Name'));
  await waitForEvent(firstPage, 'player-list-state', 4);
  listState = await latestEvent(firstPage, 'player-list-state');
  expect(listState.visibleLabels).toEqual([...listState.visibleLabels].sort((a, b) =>
    a.localeCompare(b, undefined, { sensitivity: 'base' })));

  await firstPage.evaluate(name => window.basisNetworkE2EOpenPlayer?.(name), secondName);
  await waitForEvent(firstPage, 'individual-player-state');

  const action = async (localizationKey: string, expectedCount: number): Promise<BasisNetworkE2EEvent> => {
    await firstPage.evaluate(key => window.basisNetworkE2EPlayerUiAction?.(key), localizationKey);
    await waitForEvent(firstPage, 'individual-player-state', expectedCount);
    return latestEvent(firstPage, 'individual-player-state');
  };

  let state = await action('menu.individualPlayer.mute', 2);
  expect(state.volume).toBe(0);
  await firstPage.evaluate(() => window.basisNetworkE2EPlayerVolume?.(0.65));
  await waitForEvent(firstPage, 'individual-player-state', 3);
  state = await latestEvent(firstPage, 'individual-player-state');
  expect(state.volume).toBeCloseTo(0.65, 2);

  state = await action('menu.individualPlayer.pinButton', 4);
  expect(state.pinned).toBe(true);
  state = await action('menu.individualPlayer.highlight', 5);
  expect(state.highlighted).toBe(true);
  state = await action('menu.individualPlayer.hideAvatar', 6);
  expect(state.avatarVisible).toBe(false);
  state = await action('menu.individualPlayer.hideChat', 7);
  expect(state.chatVisible).toBe(false);

  await firstPage.evaluate(() =>
    window.basisNetworkE2EPlayerUiAction?.('menu.individualPlayer.blockButton'));
  await firstPage.evaluate(() => window.basisNetworkE2EConfirmDialogue?.(true));
  await waitForEvent(firstPage, 'individual-player-state', 8);
  state = await latestEvent(firstPage, 'individual-player-state');
  expect(state.blocked).toBe(true);

  await secondPage.evaluate(() => window.basisNetworkE2EOpenPlayerList?.());
  await secondPage.evaluate(name => window.basisNetworkE2EOpenPlayer?.(name), firstName);
  await secondPage.evaluate(() => window.basisNetworkE2EPlayerState?.());
  await waitForEvent(secondPage, 'individual-player-state');
  const reciprocalState = await latestEvent(secondPage, 'individual-player-state');
  expect(reciprocalState.temporarilyBlocked).toBe(true);

  expect(state.availableAdminActions).toEqual([]);

  await firstContext.close();
  await secondContext.close();
});
