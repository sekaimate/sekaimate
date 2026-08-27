import { expect, test, type Page } from '@playwright/test';

type InteractionTarget = 'pickup' | 'seat' | 'vehicle' | 'image' | 'pool';

function requiredEnvironment(name: 'BASIS_WEB_BUILD_URL' | 'BASIS_WORLD_INTERACTION_BEE_URL' | 'BASIS_WORLD_INTERACTION_BEE_PASSWORD'): string {
  const value = process.env[name]?.trim();
  if (!value) {
    throw new Error(`${name} is required.`);
  }
  return value;
}

function interactionUrl(target: InteractionTarget): string {
  const url = new URL(requiredEnvironment('BASIS_WEB_BUILD_URL'));
  url.searchParams.set('basisWorldInteractionE2E', target);
  url.searchParams.set('basisWorldInteractionBeeUrl', requiredEnvironment('BASIS_WORLD_INTERACTION_BEE_URL'));
  url.searchParams.set('basisWorldInteractionBeePassword', requiredEnvironment('BASIS_WORLD_INTERACTION_BEE_PASSWORD'));
  return url.toString();
}

async function openTarget(page: Page, target: InteractionTarget): Promise<void> {
  await page.goto(interactionUrl(target));
  await page.waitForFunction(() => window.basisWorldInteractionE2E?.ready === true);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.error)).toBe('');
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.activeTarget)).toBe(target);
}

async function clickProductionRay(page: Page): Promise<void> {
  const canvas = page.locator('#unity-canvas');
  await canvas.click({ position: { x: 480, y: 300 } });
  await page.mouse.down();
}

test('world BEE exposes every required production interaction type', async ({ page }) => {
  await openTarget(page, 'pickup');
  const requiredTypes = [
    'BasisPickupInteractable',
    'BasisSeat',
    'BasisVehiclePilotSeat',
    'BasisImagePickupObject',
    'CueGrip',
  ];
  for (const typeName of requiredTypes) {
    await expect.poll(() => page.evaluate((name) => window.basisWorldInteractionE2E?.fixtureTypes.includes(name), typeName)).toBe(true);
  }
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.directTouchReady)).toBe(true);
});

test('desktop ray hover, grab, use, and drop mutate the production pickup', async ({ page }) => {
  await openTarget(page, 'pickup');
  await clickProductionRay(page);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.hoverStarts ?? 0)).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.grabStarts ?? 0)).toBeGreaterThan(0);
  await page.mouse.up();

  await page.keyboard.down('KeyV');
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.useDowns ?? 0)).toBeGreaterThan(0);
  await page.keyboard.up('KeyV');

  await page.mouse.click(480, 300, { button: 'right' });
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.grabEnds ?? 0)).toBeGreaterThan(0);
});

test('seat and vehicle pilot seat are entered through the production ray input', async ({ page }) => {
  await openTarget(page, 'seat');
  await clickProductionRay(page);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.seatEntries ?? 0)).toBeGreaterThan(0);
  await page.mouse.up();

  await openTarget(page, 'vehicle');
  await clickProductionRay(page);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.vehicleSeatEntries ?? 0)).toBeGreaterThan(0);
  await page.mouse.up();
});

test('image pickup and pool cue use their production pickup components', async ({ page }) => {
  await openTarget(page, 'image');
  await clickProductionRay(page);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.imageGrabStarts ?? 0)).toBeGreaterThan(0);
  await page.mouse.up();

  await openTarget(page, 'pool');
  await clickProductionRay(page);
  await expect.poll(() => page.evaluate(() => window.basisWorldInteractionE2E?.poolCueGrabStarts ?? 0)).toBeGreaterThan(0);
  await page.mouse.up();
});
