import { expect, test, type Page } from "@playwright/test";

type SettingsOperation = "snapshotAll" | "exerciseAll" | "restore";

type ControlResult = {
  bindingKey: string;
  title: string;
  type: "toggle" | "slider" | "dropdown" | "text";
  before: string;
  current: string;
  outcome: "mutated" | "no-binding" | "not-interactable" | "no-alternative";
};

type TabResult = {
  key: string;
  title: string;
  opened: boolean;
  controls: ControlResult[];
};

type SettingsResult = {
  requestId: number;
  operation: SettingsOperation;
  succeeded: boolean;
  authorized: boolean;
  error: string;
  tabs: TabResult[];
};

type RestoreValue = {
  bindingKey: string;
  type: ControlResult["type"];
  value: string;
};

declare global {
  interface Window {
    basisSettingsE2E?: {
      ready: boolean;
      request(operation: SettingsOperation, restoreValues?: RestoreValue[]): Promise<SettingsResult>;
    };
  }
}

const buildUrl = process.env.BASIS_WEB_SETTINGS_URL;
test.skip(buildUrl === undefined, "BASIS_WEB_SETTINGS_URL must point to a served development WebGL build");

const regularTabKeys = [
  "settings.tab.general",
  "settings.tab.audio",
  "settings.tab.microphone",
  "settings.tab.graphics",
  "settings.tab.myavatar",
  "settings.tab.controls",
  "settings.tab.chat",
  "settings.tab.bodytracking",
  "settings.tab.trackerlinking",
  "settings.tab.downloadsurls",
  "settings.tab.developer",
] as const;

const privilegedTabKeys = ["settings.tab.moderator", "settings.tab.admin"] as const;

test("opens every regular Settings tab, changes every bound control, and persists values across reload", async ({ page }) => {
  const target = withMode(buildUrl as string, "regular");
  await page.goto(target);
  await waitUntilReady(page);

  const exercise = await request(page, "exerciseAll");
  expect(exercise.succeeded, exercise.error).toBe(true);
  expect(exercise.authorized).toBe(false);
  assertTabsOpened(exercise, regularTabKeys);
  expect(tabKeys(exercise)).not.toEqual(expect.arrayContaining(privilegedTabKeys));

  const boundControls = exercise.tabs.flatMap((tab) => tab.controls).filter((control) => control.bindingKey !== "");
  expect(boundControls.length).toBeGreaterThan(0);
  expect(boundControls.filter((control) => control.outcome !== "mutated")).toEqual([]);
  for (const control of boundControls) {
    expect(control.current, `${control.bindingKey} did not change`).not.toBe(control.before);
  }

  const expectedValues = new Map(boundControls.map((control) => [control.bindingKey, control.current]));
  const restoreValues = boundControls.map<RestoreValue>((control) => ({
    bindingKey: control.bindingKey,
    type: control.type,
    value: control.before,
  }));

  try {
    await page.reload();
    await waitUntilReady(page);
    const persisted = await request(page, "snapshotAll");
    expect(persisted.succeeded, persisted.error).toBe(true);
    assertTabsOpened(persisted, regularTabKeys);
    assertControlValues(persisted, expectedValues);
  } finally {
    await waitUntilReady(page);
    const restored = await request(page, "restore", restoreValues);
    expect(restored.succeeded, restored.error).toBe(true);
  }

  await page.reload();
  await waitUntilReady(page);
  const restoredSnapshot = await request(page, "snapshotAll");
  assertControlValues(restoredSnapshot, new Map(restoreValues.map((entry) => [entry.bindingKey, entry.value])));
});

test("shows Moderator and Admin only under their permission conditions", async ({ page }) => {
  await page.goto(withMode(buildUrl as string, "regular"));
  await waitUntilReady(page);
  const regular = await request(page, "snapshotAll");
  expect(tabKeys(regular)).not.toEqual(expect.arrayContaining(privilegedTabKeys));

  await page.goto(withMode(buildUrl as string, "authorized"));
  await waitUntilReady(page);
  const authorized = await request(page, "snapshotAll");
  expect(authorized.succeeded, authorized.error).toBe(true);
  expect(authorized.authorized).toBe(true);
  assertTabsOpened(authorized, privilegedTabKeys);
});

function withMode(url: string, mode: "regular" | "authorized"): string {
  const target = new URL(url);
  target.searchParams.set("basisSettingsE2E", mode);
  return target.toString();
}

async function waitUntilReady(page: Page): Promise<void> {
  await expect.poll(() => page.evaluate(() => window.basisSettingsE2E?.ready)).toBe(true);
}

async function request(
  page: Page,
  operation: SettingsOperation,
  restoreValues: RestoreValue[] = [],
): Promise<SettingsResult> {
  return page.evaluate(
    ({ requestedOperation, values }) => {
      if (window.basisSettingsE2E === undefined) {
        throw new Error("Settings E2E API is unavailable");
      }
      return window.basisSettingsE2E.request(requestedOperation, values);
    },
    { requestedOperation: operation, values: restoreValues },
  );
}

function tabKeys(result: SettingsResult): string[] {
  return result.tabs.map((tab) => tab.key);
}

function assertTabsOpened(result: SettingsResult, expectedKeys: readonly string[]): void {
  const tabsByKey = new Map(result.tabs.map((tab) => [tab.key, tab]));
  for (const key of expectedKeys) {
    const tab = tabsByKey.get(key);
    expect(tab, `${key} was not listed`).toBeDefined();
    expect(tab?.opened, `${key} was not opened`).toBe(true);
  }
}

function assertControlValues(result: SettingsResult, expected: ReadonlyMap<string, string>): void {
  const actual = new Map(
    result.tabs.flatMap((tab) => tab.controls).filter((control) => control.bindingKey !== "").map((control) => [control.bindingKey, control.current]),
  );
  for (const [bindingKey, value] of expected) {
    expect(actual.get(bindingKey), `${bindingKey} did not persist`).toBe(value);
  }
}
