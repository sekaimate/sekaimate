import { expect, test } from '@playwright/test';
import { launchIwe, skipWithoutIwe } from './iwe';

function requiredEnvironment(name: 'BASIS_WEB_BUILD_URL' | 'BASIS_WORLD_INTERACTION_BEE_URL' | 'BASIS_WORLD_INTERACTION_BEE_PASSWORD'): string {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required.`);
  return value;
}

function directTouchUrl(): string {
  const url = new URL(requiredEnvironment('BASIS_WEB_BUILD_URL'));
  url.searchParams.set('basisWorldInteractionE2E', 'direct-touch');
  url.searchParams.set('basisWorldInteractionBeeUrl', requiredEnvironment('BASIS_WORLD_INTERACTION_BEE_URL'));
  url.searchParams.set('basisWorldInteractionBeePassword', requiredEnvironment('BASIS_WORLD_INTERACTION_BEE_PASSWORD'));
  return url.toString();
}

test('Meta IWE injects the WebXR hand automation runtime', async ({}, testInfo) => {
  test.skip(!skipWithoutIwe(), 'BASIS_IWE_EXTENSION_PATH is not configured; Meta IWE WebXR hand E2E is disabled.');
  const url = directTouchUrl();
  const { context, page } = await launchIwe(testInfo, url);

  try {
    await page.goto(url);
    await page.waitForFunction(() => 'xr' in navigator && window.transformHandles instanceof Map);
    await expect.poll(() => page.evaluate(() => window.transformHandles?.has('left'))).toBe(true);
    await expect.poll(() => page.evaluate(() => window.transformHandles?.has('right'))).toBe(true);
  } finally {
    await context.close();
  }
});
