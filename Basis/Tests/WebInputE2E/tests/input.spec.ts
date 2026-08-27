import { expect, test, type BrowserContext, type Page } from '@playwright/test';

function requiredBuildUrl(): string {
  const value = process.env.BASIS_WEB_BUILD_URL?.trim();
  if (!value) {
    throw new Error('BASIS_WEB_BUILD_URL must point to an already-running WebGL build.');
  }
  return value;
}

function inputUrl(): string {
  const url = new URL(requiredBuildUrl());
  url.searchParams.set('basisInputE2E', '1');
  return url.toString();
}

async function waitUntilReady(page: Page): Promise<void> {
  await page.goto(inputUrl());
  await page.waitForFunction(() => window.basisInputE2E?.ready === true);
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.schemaVersion)).toBe(1);
}

async function snapshot(page: Page): Promise<BasisInputE2ESnapshot> {
  return page.evaluate(() => {
    if (!window.basisInputE2E) {
      throw new Error('Web input E2E snapshot is unavailable.');
    }
    return window.basisInputE2E;
  });
}

function distance(a: BasisInputVector3, b: BasisInputVector3): number {
  return Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
}

async function installGamepad(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const gamepad = {
      axes: [0, 0, 0, 0],
      buttons: Array.from({ length: 17 }, () => ({ pressed: false, touched: false, value: 0 })),
      connected: true,
      id: 'Basis Playwright Gamepad',
      index: 0,
      mapping: 'standard',
      timestamp: 0,
    };
    Object.defineProperty(navigator, 'getGamepads', {
      configurable: true,
      value: () => [gamepad],
    });
    window.basisSetTestGamepad = (moveX: number, moveY: number, lookX: number, lookY: number) => {
      gamepad.axes[0] = moveX;
      gamepad.axes[1] = moveY;
      gamepad.axes[2] = lookX;
      gamepad.axes[3] = lookY;
      gamepad.timestamp = performance.now();
    };
  });
}

test('keyboard movement and pointer-locked mouse look reach the production character drivers', async ({ page }) => {
  await waitUntilReady(page);
  const canvas = page.locator('#unity-canvas');
  const initial = await snapshot(page);

  await canvas.click({ position: { x: 480, y: 300 } });
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.pointerLocked)).toBe(true);

  await page.keyboard.down('KeyW');
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.moveDevice)).toBe('Keyboard');
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.movement.y ?? 0)).toBeGreaterThan(0.5);
  await expect.poll(async () => distance((await snapshot(page)).playerPosition, initial.playerPosition)).toBeGreaterThan(0.01);
  await page.keyboard.up('KeyW');

  await page.mouse.move(480, 300);
  await page.mouse.move(560, 260, { steps: 4 });
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.lookDevice)).toBe('Mouse');
  await expect.poll(async () => Math.abs((await snapshot(page)).lookYaw - initial.lookYaw)).toBeGreaterThan(0.01);
});

test('browser gamepad axes reach the production move and look actions', async ({ browser }) => {
  const context = await browser.newContext();
  await installGamepad(context);
  const page = await context.newPage();
  await waitUntilReady(page);
  const initial = await snapshot(page);

  await page.evaluate(() => window.basisSetTestGamepad?.(0.75, -0.8, 0.6, 0));
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.moveDevice)).toBe('Gamepad');
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.movement.x ?? 0)).toBeGreaterThan(0.5);
  await expect.poll(async () => distance((await snapshot(page)).playerPosition, initial.playerPosition)).toBeGreaterThan(0.01);
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.lookDevice)).toBe('Gamepad');
  await expect.poll(async () => Math.abs((await snapshot(page)).lookYaw - initial.lookYaw)).toBeGreaterThan(0.01);

  await page.evaluate(() => window.basisSetTestGamepad?.(0, 0, 0, 0));
  await context.close();
});

test('touch dragging the real on-screen controls moves and looks', async ({ browser }) => {
  const context = await browser.newContext({ hasTouch: true, isMobile: true, viewport: { width: 960, height: 600 } });
  const page = await context.newPage();
  await waitUntilReady(page);
  await page.waitForFunction(() => window.basisInputE2E?.onScreenControls.ready === true);
  const initial = await snapshot(page);
  const client = await context.newCDPSession(page);
  const left = initial.onScreenControls.leftStick;

  await client.send('Input.dispatchTouchEvent', {
    type: 'touchStart',
    touchPoints: [{ x: left.x, y: left.y, id: 0 }],
  });
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchMove',
    touchPoints: [{ x: left.x, y: left.y - 70, id: 0 }],
  });
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.activeTouches ?? 0)).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.movement.y ?? 0)).toBeGreaterThan(0.25);
  await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await expect.poll(() => page.evaluate(() => window.basisInputE2E?.movement.y ?? 1)).toBe(0);

  const beforeLook = await snapshot(page);
  const right = beforeLook.onScreenControls.rightStick;
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchStart',
    touchPoints: [{ x: right.x, y: right.y, id: 1 }],
  });
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchMove',
    touchPoints: [{ x: right.x + 70, y: right.y, id: 1 }],
  });
  await expect.poll(async () => Math.abs((await snapshot(page)).lookYaw - beforeLook.lookYaw)).toBeGreaterThan(0.01);
  await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await context.close();
});
