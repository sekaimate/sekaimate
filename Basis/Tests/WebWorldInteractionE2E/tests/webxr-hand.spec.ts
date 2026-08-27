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
  url.searchParams.set('basisWebXRE2E', '1');
  return url.toString();
}

async function moveLeftFingertip(page: import('@playwright/test').Page, signedDistance: number): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt++) {
    const error = await page.evaluate((distance) => {
      const snapshot = window.basisWorldInteractionE2E;
      const handle = window.transformHandles?.get('left');
      if (!snapshot || !handle) throw new Error('DirectTouch probe and IWE left hand are required.');
      const target = {
        x: snapshot.directTouchCenter.x + snapshot.directTouchNormal.x * distance,
        y: snapshot.directTouchCenter.y + snapshot.directTouchNormal.y * distance,
        z: snapshot.directTouchCenter.z + snapshot.directTouchNormal.z * distance,
      };
      const current = snapshot.leftDirectTouchFingertip;
      const delta = { x: target.x - current.x, y: target.y - current.y, z: target.z - current.z };
      handle.position.set(handle.position.x + delta.x, handle.position.y + delta.y, handle.position.z - delta.z);
      return Math.hypot(delta.x, delta.y, delta.z);
    }, signedDistance);
    if (error < 0.003) return;
    await page.evaluate(() => new Promise<void>((resolve) => {
      requestAnimationFrame(() => requestAnimationFrame(() => resolve()));
    }));
  }
  throw new Error('IWE left fingertip did not converge on the DirectTouch target.');
}

async function setLeftPinch(page: import('@playwright/test').Page, value: number): Promise<void> {
  const slider = page.locator('input[type="range"][min="0"][max="100"]').first();
  await slider.evaluate((element, sliderValue) => {
    const input = element as HTMLInputElement;
    input.value = String(sliderValue);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }, value);
}

test('Meta IWE hand input drives production DirectTouch', async ({}, testInfo) => {
  test.skip(!skipWithoutIwe(), 'BASIS_IWE_EXTENSION_PATH is not configured; Meta IWE WebXR hand E2E is disabled.');
  const url = directTouchUrl();
  const { context, page } = await launchIwe(testInfo, url);

  try {
    await page.goto(url);
    await page.waitForFunction(() => 'xr' in navigator && window.transformHandles instanceof Map);
    await expect.poll(() => page.evaluate(() => window.transformHandles?.has('left'))).toBe(true);
    await expect.poll(() => page.evaluate(() => window.transformHandles?.has('right'))).toBe(true);

    await page.locator('button[title="Click to toggle input mode"]').click();
    const enterXr = page.locator('#basis-webxr-enter');
    await expect(enterXr).toBeVisible();
    await enterXr.click();
    await expect.poll(() => page.evaluate(() => window.basisWebXR?.sessionActive)).toBe(true);
    await expect.poll(() => page.evaluate(() => window.basisWebXR?.snapshot.sources.some(
      (source) => source.handedness === 'left' && source.handTracked,
    ))).toBe(true);
    await expect.poll(() => page.evaluate(() => window.basisWebXR?.snapshot.sources.some(
      (source) => source.handedness === 'right' && source.handTracked,
    ))).toBe(true);
    await page.waitForFunction(() => window.basisWorldInteractionE2E?.ready === true);
    await expect.poll(() => page.evaluate(() => window.basisWebXR?.basisState?.leftHandDevice)).toBe(true);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.leftHandInputReady)).toBe(true);

    await setLeftPinch(page, 100);
    await expect.poll(() => page.evaluate(() => window.basisWebXR?.basisState?.leftPinch ?? 0)).toBeGreaterThan(0.8);
    await setLeftPinch(page, 0);
    await expect.poll(() => page.evaluate(() => window.basisWebXR?.basisState?.leftPinch ?? 1)).toBeLessThan(0.2);

    await moveLeftFingertip(page, 0.02);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.leftDirectTouching)).toBe(true);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.directTouchPointerEnters ?? 0)).toBeGreaterThan(0);

    await moveLeftFingertip(page, 0.005);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.directTouchPointerDowns ?? 0)).toBeGreaterThan(0);

    await moveLeftFingertip(page, 0.03);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.directTouchPointerUps ?? 0)).toBeGreaterThan(0);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.directTouchClicks ?? 0)).toBeGreaterThan(0);
    await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.directTouchEnds ?? 0)).toBeGreaterThan(0);
  } finally {
    await context.close();
  }
});
