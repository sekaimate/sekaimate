import { expect, test, type Page } from '@playwright/test';

type ContentType = 'Avatar' | 'Prop' | 'World';

interface BeeFixture {
  password: string;
  type: ContentType;
  url: string;
}

const fixtures: BeeFixture[] = [
  fixture('Avatar', 'BASIS_AVATAR_BEE_URL', 'BASIS_AVATAR_BEE_PASSWORD'),
  fixture('Prop', 'BASIS_PROP_BEE_URL', 'BASIS_PROP_BEE_PASSWORD'),
  fixture('World', 'BASIS_WORLD_BEE_URL', 'BASIS_WORLD_BEE_PASSWORD'),
];
let requestId = 0;

function requiredEnvironment(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required for the real BEE browser test.`);
  return value;
}

function fixture(type: ContentType, urlName: string, passwordName: string): BeeFixture {
  return { type, url: process.env[urlName] ?? '', password: process.env[passwordName] ?? '' };
}

async function command(page: Page, action: string, target?: string, value?: string): Promise<void> {
  requestId += 1;
  const currentRequestId = requestId;
  await page.evaluate(({ action, requestId: browserRequestId, target, value }) => {
    if (!window.basisLibraryE2E) throw new Error('Library E2E API is unavailable.');
    window.basisLibraryE2E.command({ action, requestId: browserRequestId, target, value });
  }, { action, requestId: currentRequestId, target, value });
  await expect.poll(() => page.evaluate(() => window.basisLibraryE2E?.snapshot?.lastRequestId)).toBe(currentRequestId);
  const error = await page.evaluate(() => window.basisLibraryE2E?.snapshot?.lastError ?? 'Missing command result.');
  if (error) throw new Error(`${action} failed: ${error}`);
}

async function snapshot(page: Page): Promise<BasisLibraryE2ESnapshot> {
  return page.evaluate(() => {
    const value = window.basisLibraryE2E?.snapshot;
    if (!value) throw new Error('Library E2E snapshot is unavailable.');
    return value;
  });
}

async function waitForSnapshot(page: Page, predicate: (value: BasisLibraryE2ESnapshot) => boolean): Promise<void> {
  await expect.poll(async () => predicate(await snapshot(page))).toBe(true);
}

async function openLibrary(page: Page): Promise<void> {
  await command(page, 'open');
  await waitForSnapshot(page, value => value.buttons.length > 0);
}

async function addBeeThroughDialog(page: Page, bee: BeeFixture): Promise<void> {
  await command(page, 'click-title-key', 'library.addNewContent');
  await command(page, 'set-text-key', 'library.beeFileUrl', bee.url);
  await command(page, 'set-password-key', 'library.beeFilePassword', bee.password);
  await command(page, 'click-title-key', 'library.dialog.add.addButton');
  await waitForSnapshot(page, value => value.keys.some(key => key.url === bee.url && key.mode === bee.type));
}

async function ensureBeeFixtures(page: Page): Promise<void> {
  for (const bee of fixtures) {
    const current = await snapshot(page);
    if (!current.keys.some(key => key.url === bee.url)) await addBeeThroughDialog(page, bee);
  }
}

async function searchFixture(page: Page, bee: BeeFixture): Promise<void> {
  const key = (await snapshot(page)).keys.find(candidate => candidate.url === bee.url);
  if (!key?.title) throw new Error(`Metadata title is unavailable for ${bee.url}.`);
  await command(page, 'search', undefined, key.title);
  await waitForSnapshot(page, value => value.search === key.title);
}

test.beforeEach(async ({ page, baseURL }) => {
  const url = new URL(baseURL ?? 'http://localhost:4173');
  url.searchParams.set('basisLibraryE2E', '1');
  if (process.env.BASIS_WEBSOCKET_URI && process.env.BASIS_NETWORK_USER) {
    url.searchParams.set('basisNetworkE2E', '1');
    url.searchParams.set('websocketUri', process.env.BASIS_WEBSOCKET_URI);
    url.searchParams.set('userName', process.env.BASIS_NETWORK_USER);
    url.searchParams.set('password', process.env.BASIS_NETWORK_PASSWORD ?? '');
  }
  await page.goto(url.toString());
  await page.waitForFunction(() => window.basisLibraryE2E?.snapshot?.ready === true);
  await openLibrary(page);
  await ensureBeeFixtures(page);
});

test.beforeAll(() => {
  requiredEnvironment('BASIS_AVATAR_BEE_URL');
  requiredEnvironment('BASIS_AVATAR_BEE_PASSWORD');
  requiredEnvironment('BASIS_PROP_BEE_URL');
  requiredEnvironment('BASIS_PROP_BEE_PASSWORD');
  requiredEnvironment('BASIS_WORLD_BEE_URL');
  requiredEnvironment('BASIS_WORLD_BEE_PASSWORD');
});

test('adds real Avatar, Prop, and World BEE files through the visible library dialog', async ({ page }) => {
  for (const bee of fixtures) {
    await command(page, 'select-tab', bee.type);
    await waitForSnapshot(page, value => value.currentPage === bee.type);
    await searchFixture(page, bee);
    await command(page, 'sort', undefined, 'DateNewestToOldest');
    await waitForSnapshot(page, value => value.dropdowns.some(dropdown => dropdown.value === 'DateNewestToOldest'));
    await command(page, 'click-first-card');
    await waitForSnapshot(page, value => value.buttons.some(button => button.title.length > 0 && button.title !== bee.url));
    if (bee.type === 'Prop') {
      await command(page, 'click-title-key', 'library.pin');
      await waitForSnapshot(page, value => value.keys.some(key => key.url === bee.url && key.pinned));
      await command(page, 'click-title-key', 'library.pinned');
    }
  }
});

test('loads Avatar, Prop, and World from their real detail overlays', async ({ page }) => {
  for (const bee of fixtures) {
    await command(page, 'select-tab', bee.type);
    await searchFixture(page, bee);
    await command(page, 'click-first-card');
    if (bee.type !== 'Avatar') {
      await command(page, 'set-dropdown-key', 'library.networkType', 'Local');
      await command(page, 'toggle-key', 'library.ephemeralMode');
    }
    await command(page, 'click-title-key', 'library.load');
    await waitForSnapshot(page, value => value.instances.some(instance => instance.url === bee.url));
    if (bee.type === 'Prop') {
      await command(page, 'select-tab', 'Prop');
      await searchFixture(page, bee);
      await command(page, 'click-first-card');
      await command(page, 'click-title-key', 'library.despawn');
      await waitForSnapshot(page, value => !value.instances.some(instance => instance.url === bee.url));
    }
  }
});

test('operates instantiated placement, teleport, persistence, static, and despawn controls', async ({ page }) => {
  const prop = fixtures.find(fixtureValue => fixtureValue.type === 'Prop');
  if (!prop) throw new Error('Prop fixture is missing.');
  await command(page, 'select-tab', 'Prop');
  await searchFixture(page, prop);
  await command(page, 'click-first-card');
  const networkEnabled = Boolean(process.env.BASIS_WEBSOCKET_URI && process.env.BASIS_NETWORK_USER);
  if (networkEnabled) {
    await waitForSnapshot(page, value => value.dropdowns.some(dropdown => dropdown.title.length > 0 && dropdown.entries.length >= 3));
    await command(page, 'set-dropdown-key', 'library.networkType', 'Networked');
  } else {
    await command(page, 'set-dropdown-key', 'library.networkType', 'Local');
  }
  await command(page, 'click-title-key', 'library.load');
  await waitForSnapshot(page, value => value.instances.some(instance => instance.url === prop.url));
  await command(page, 'select-tab', 'Instantiated');
  await waitForSnapshot(page, value => value.currentPage === 'Instantiated' && value.instances.some(instance => instance.url === prop.url));
  await command(page, 'click-tooltip-key', 'library.instantiated.select.tooltip');
  await waitForSnapshot(page, value => value.instances.some(instance => instance.url === prop.url && instance.selected));
  await openLibrary(page);
  await command(page, 'select-tab', 'Instantiated');
  await command(page, 'click-tooltip-key', 'library.instantiated.teleport.tooltip');

  const propInstance = (await snapshot(page)).instances.find(instance => instance.url === prop.url);
  if (propInstance?.networked) {
    await command(page, 'click-tooltip-key', 'library.instantiated.static.tooltip');
    await waitForSnapshot(page, value => value.instances.some(instance => instance.url === prop.url && instance.static));
  }

  await command(page, 'filter', undefined, 'PersistentOnly');
  await waitForSnapshot(page, value => value.dropdowns.some(dropdown => dropdown.value === 'PersistentOnly'));
  await command(page, 'click-tooltip-key', 'library.instantiated.remove.tooltip');
  await command(page, 'click-title-key', 'ui.yes');
  await waitForSnapshot(page, value => !value.instances.some(instance => instance.url === prop.url));
});

test('shares and deletes each saved content type through its detail overlay', async ({ page }) => {
  const networkEnabled = Boolean(process.env.BASIS_WEBSOCKET_URI && process.env.BASIS_NETWORK_USER);
  for (const bee of fixtures) {
    await command(page, 'select-tab', bee.type);
    await searchFixture(page, bee);
    await command(page, 'click-first-card');
    if (networkEnabled) {
      await command(page, 'click-title-key', 'library.share');
      await command(page, 'click-title-key', 'ui.yes');
      await waitForSnapshot(page, value => value.buttons.some(button => button.tooltip.length > 0));
    }
    await command(page, 'click-title-key', 'library.delete');
    await command(page, 'click-title-key', 'ui.yes');
    await waitForSnapshot(page, value => !value.keys.some(key => key.url === bee.url));
  }
});
