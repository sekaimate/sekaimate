import { expect, test, type Page } from '@playwright/test';

interface ServerEntryState {
  id: string;
  title: string;
  description: string;
  address: string;
  port: string;
  webSocketUri: string;
  serverInfoUri: string;
}

interface ServersUIState {
  ready: boolean;
  panelOpen: boolean;
  connected: boolean;
  username: string;
  autoConnect: boolean;
  hostControlsVisible: boolean;
  entries: ServerEntryState[];
}

interface ServersUIBridge {
  state?: ServersUIState;
  command(command: Record<string, string | boolean>): void;
}

declare global {
  interface Window {
    basisServersUIE2E?: ServersUIBridge;
  }
}

function requiredEnvironment(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`${name} must point to an already-running test dependency.`);
  }
  return value;
}

function buildUrl(url: string): string {
  const result = new URL(url);
  result.searchParams.set('basisServersUIE2E', '1');
  return result.toString();
}

async function command(page: Page, payload: Record<string, string | boolean>): Promise<void> {
  await page.waitForFunction(() => window.basisServersUIE2E?.state?.ready === true);
  await page.evaluate(value => window.basisServersUIE2E?.command(value), payload);
}

async function state(page: Page): Promise<ServersUIState> {
  return page.evaluate(() => {
    const current = window.basisServersUIE2E?.state;
    if (!current) {
      throw new Error('Servers UI state is unavailable.');
    }
    return current;
  });
}

test('Servers UI manages, probes, connects, and auto-connects to a real Basis Server', async ({ page }) => {
  const webBuildUrl = requiredEnvironment('BASIS_WEB_BUILD_URL');
  const webSocketUri = requiredEnvironment('BASIS_WEBSOCKET_URI');
  const serverInfoUri = requiredEnvironment('BASIS_SERVER_INFO_URI');
  const password = process.env.BASIS_SERVER_PASSWORD ?? '';
  const runId = `${Date.now()}-${process.pid}`;
  const username = `servers-ui-${runId}`;
  const address = `servers-ui-${runId}.invalid`;

  let socketCount = 0;
  page.on('websocket', socket => {
    if (socket.url() === webSocketUri) {
      socketCount += 1;
    }
  });

  await page.goto(buildUrl(webBuildUrl));
  await command(page, { type: 'set-username', value: username });
  await command(page, { type: 'set-auto-connect', boolValue: true });
  await command(page, { type: 'add-start' });
  await command(page, {
    type: 'editor-set',
    address,
    port: '4296',
    password,
    webSocketUri,
    serverInfoUri,
  });
  await command(page, { type: 'editor-save' });

  await expect.poll(async () => (await state(page)).entries.some(entry =>
    entry.address === address
    && entry.webSocketUri === webSocketUri
    && entry.serverInfoUri === serverInfoUri)).toBe(true);

  const saved = (await state(page)).entries.find(entry => entry.address === address);
  expect(saved).toBeDefined();
  await command(page, { type: 'refresh' });
  await expect.poll(async () => (await state(page)).entries.find(entry => entry.id === saved?.id)?.title ?? '')
    .toContain(' - ');
  await expect.poll(async () => (await state(page)).entries.find(entry => entry.id === saved?.id)?.description ?? '')
    .toContain('4296');

  await command(page, { type: 'edit', id: saved?.id ?? '' });
  await command(page, {
    type: 'editor-set',
    address,
    port: '4296',
    password,
    webSocketUri,
    serverInfoUri,
  });
  await command(page, { type: 'editor-save' });
  expect((await state(page)).hostControlsVisible).toBe(false);

  await command(page, { type: 'connect', id: saved?.id ?? '' });
  await expect.poll(() => socketCount).toBeGreaterThanOrEqual(1);
  await expect.poll(async () => (await state(page)).connected).toBe(true);

  await page.reload();
  await expect.poll(() => socketCount).toBeGreaterThanOrEqual(2);
  await expect.poll(async () => (await state(page)).connected).toBe(true);
  expect((await state(page)).username).toBe(username);
  expect((await state(page)).autoConnect).toBe(true);

  await command(page, { type: 'open' });
  await expect.poll(async () => (await state(page)).panelOpen).toBe(true);
  await expect.poll(async () => (await state(page)).entries.some(entry => entry.id === saved?.id)).toBe(true);
  await command(page, { type: 'edit', id: saved?.id ?? '' });
  await command(page, { type: 'remove-request' });
  await command(page, { type: 'remove-confirm' });
  await expect.poll(async () => (await state(page)).entries.some(entry => entry.id === saved?.id)).toBe(false);
});
