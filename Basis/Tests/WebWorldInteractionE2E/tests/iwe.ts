import { readFileSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { chromium, type BrowserContext, type Page, type TestInfo, type Worker } from '@playwright/test';

const requiredIweVersion = '2.0.0';

interface ExtensionManifest {
  version: string;
  manifest_version: number;
}

interface ChromeScriptingApi {
  getRegisteredContentScripts(): Promise<Array<{ id: string }>>;
  unregisterContentScripts(options: { ids: string[] }): Promise<void>;
  registerContentScripts(scripts: Array<{
    id: string;
    matches: string[];
    js: string[];
    allFrames: boolean;
    runAt: 'document_start';
    world: 'MAIN';
    persistAcrossSessions: boolean;
  }>): Promise<void>;
}

interface ChromeServiceWorkerGlobal {
  chrome: {
    runtime: { getManifest(): ExtensionManifest };
    scripting: ChromeScriptingApi;
  };
}

export interface IweSession {
  context: BrowserContext;
  page: Page;
}

function extensionPath(): string | undefined {
  const configured = process.env.BASIS_IWE_EXTENSION_PATH?.trim();
  return configured ? resolve(configured) : undefined;
}

export function skipWithoutIwe(): string | undefined {
  return extensionPath();
}

function validateExtension(directory: string): void {
  if (!statSync(directory).isDirectory()) {
    throw new Error(`BASIS_IWE_EXTENSION_PATH is not a directory: ${directory}`);
  }
  const manifestValue: unknown = JSON.parse(readFileSync(resolve(directory, 'manifest.json'), 'utf8'));
  if (!isExtensionManifest(manifestValue)) {
    throw new Error('Immersive Web Emulator manifest is invalid.');
  }
  if (manifestValue.manifest_version !== 3 || manifestValue.version !== requiredIweVersion) {
    throw new Error(`Immersive Web Emulator ${requiredIweVersion} MV3 is required.`);
  }
  statSync(resolve(directory, 'build/iwe.min.js'));
  statSync(resolve(directory, 'build/service-worker.min.js'));
}

function isExtensionManifest(value: unknown): value is ExtensionManifest {
  if (typeof value !== 'object' || value === null) return false;
  return 'version' in value && typeof value.version === 'string'
    && 'manifest_version' in value && typeof value.manifest_version === 'number';
}

async function extensionWorker(context: BrowserContext): Promise<Worker> {
  return context.serviceWorkers()[0] ?? context.waitForEvent('serviceworker');
}

async function registerRuntime(worker: Worker, pageUrl: string): Promise<void> {
  const origin = new URL(pageUrl).origin;
  if (!origin.startsWith('http://') && !origin.startsWith('https://')) {
    throw new Error(`WebXR E2E requires an HTTP(S) build URL: ${origin}`);
  }

  const version = await worker.evaluate(() => {
    const extensionGlobal = globalThis as typeof globalThis & ChromeServiceWorkerGlobal;
    return extensionGlobal.chrome.runtime.getManifest().version;
  });
  if (version !== requiredIweVersion) {
    throw new Error(`Immersive Web Emulator ${requiredIweVersion} is required, received ${version}.`);
  }

  await worker.evaluate(async ({ matches }) => {
    const extensionGlobal = globalThis as typeof globalThis & ChromeServiceWorkerGlobal;
    const scriptId = 'basis-iwe-e2e';
    const scripts = await extensionGlobal.chrome.scripting.getRegisteredContentScripts();
    if (scripts.some((script) => script.id === scriptId)) {
      await extensionGlobal.chrome.scripting.unregisterContentScripts({ ids: [scriptId] });
    }
    await extensionGlobal.chrome.scripting.registerContentScripts([{
      id: scriptId,
      matches,
      js: ['build/iwe.min.js'],
      allFrames: true,
      runAt: 'document_start',
      world: 'MAIN',
      persistAcrossSessions: false,
    }]);
  }, { matches: [`${origin}/*`] });
}

export async function launchIwe(testInfo: TestInfo, pageUrl: string): Promise<IweSession> {
  const directory = extensionPath();
  if (!directory) {
    throw new Error('BASIS_IWE_EXTENSION_PATH is required.');
  }
  validateExtension(directory);

  const context = await chromium.launchPersistentContext(testInfo.outputPath('iwe-profile'), {
    channel: 'chromium',
    headless: false,
    args: [`--disable-extensions-except=${directory}`, `--load-extension=${directory}`],
  });
  const worker = await extensionWorker(context);
  await registerRuntime(worker, pageUrl);
  const page = context.pages()[0] ?? await context.newPage();
  return { context, page };
}
