import { expect, test, type Page } from '@playwright/test';

const PERSISTED_FILES = [
  'KeyStore.json',
  'ItemKeyStore.json',
  'BasisActionBindingsV1.json',
  'CameraSettings.json',
  'settingsConfig.json',
  'SavedServers.BAS',
  'trustedUrls.json',
] as const;

function requiredBuildUrl(): URL {
  const value = process.env.BASIS_WEB_BUILD_URL?.trim();
  if (!value) {
    throw new Error('BASIS_WEB_BUILD_URL must point to an already-running WebGL development build.');
  }

  return new URL(value);
}

function phaseUrl(buildUrl: URL, phase: 'seed' | 'verify'): string {
  const url = new URL(buildUrl);
  url.searchParams.set('basisPersistenceE2E', phase);
  return url.toString();
}

async function persistedFileNames(page: Page): Promise<string[]> {
  return page.evaluate(async () => {
    const databaseInfo = await indexedDB.databases();
    const keys: string[] = [];

    for (const database of databaseInfo) {
      if (!database.name) continue;
      const connection = await new Promise<IDBDatabase>((resolve, reject) => {
        const request = indexedDB.open(database.name as string);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });

      try {
        for (const storeName of Array.from(connection.objectStoreNames)) {
          const storeKeys = await new Promise<IDBValidKey[]>((resolve, reject) => {
            const request = connection.transaction(storeName, 'readonly').objectStore(storeName).getAllKeys();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
          });
          keys.push(...storeKeys.map(key => String(key).split('/').pop() ?? String(key)));
        }
      } finally {
        connection.close();
      }
    }

    return keys;
  });
}

async function result(page: Page): Promise<BasisPersistenceE2EResult> {
  return page.evaluate(() => {
    if (!window.basisPersistenceE2E) {
      throw new Error('Web persistence E2E result is unavailable.');
    }
    return window.basisPersistenceE2E;
  });
}

test('WebGL user state survives an IndexedDB-backed browser reload', async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();
  const buildUrl = requiredBuildUrl();

  await page.goto(phaseUrl(buildUrl, 'seed'));
  await page.waitForFunction(() => window.basisPersistenceE2E?.ready === true);
  expect(await result(page)).toMatchObject({ phase: 'seed', ready: true, error: '' });

  await expect.poll(() => persistedFileNames(page)).toEqual(expect.arrayContaining([...PERSISTED_FILES]));

  await page.evaluate(verifyUrl => history.replaceState(null, '', verifyUrl), phaseUrl(buildUrl, 'verify'));
  await page.reload();
  await page.waitForFunction(() => window.basisPersistenceE2E?.ready === true);

  expect(await result(page)).toEqual({
    phase: 'verify',
    ready: true,
    avatar: true,
    prop: true,
    world: true,
    binding: true,
    camera: true,
    settings: true,
    savedServers: true,
    trustedUrls: true,
    error: '',
  });

  await context.close();
});
