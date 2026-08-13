const DEFAULT_COORDINATES = Object.freeze({
  library: { x: 465, y: 540 },
  addNew: { x: 857, y: 154 },
  urlField: { x: 640, y: 249 },
  passwordField: { x: 640, y: 336 },
  add: { x: 640, y: 388 },
  itemCard: { x: 360, y: 230 },
  spawn: { x: 760, y: 461 },
  placement: { x: 480, y: 300 },
});

async function clickCanvas(page, point) {
  await page.mouse.move(point.x, point.y);
  await page.mouse.down();
  await page.mouse.up();
  await page.mouse.move(20, 20);
}

async function replaceFocusedText(page, value) {
  await page.keyboard.press("ControlOrMeta+A");
  await page.keyboard.insertText(value);
}

async function waitForLog(logs, pattern, page, timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const match = logs.find((entry) => pattern.test(entry));
    if (match) {
      return match;
    }
    await page.waitForTimeout(250);
  }
  throw new Error(`Unity log did not match ${pattern} within ${timeoutMs}ms.`);
}

export async function verifyPropBee(page, options) {
  const {
    applicationUrl,
    beeUrl,
    password,
    screenshotPath,
    coordinates: coordinateOverrides = {},
    startupTimeoutMs = 120000,
    operationTimeoutMs = 120000,
  } = options;

  if (!applicationUrl || !beeUrl || !password) {
    throw new Error("applicationUrl, beeUrl, and password are required.");
  }

  const coordinates = { ...DEFAULT_COORDINATES, ...coordinateOverrides };

  const logs = [];
  const consoleErrors = [];
  const beeResponses = [];
  page.on("console", (message) => {
    const text = message.text();
    logs.push(text);
    if (message.type() === "error") {
      consoleErrors.push(text);
    }
  });
  page.on("response", (response) => {
    if (response.url() === beeUrl) {
      beeResponses.push({
        range: response.request().headers().range ?? null,
        status: response.status(),
      });
    }
  });

  await page.setViewportSize({ width: 960, height: 600 });
  await page.goto(applicationUrl, { waitUntil: "domcontentloaded", timeout: startupTimeoutMs });
  await page.locator("canvas").waitFor({ state: "visible", timeout: startupTimeoutMs });
  await page.waitForTimeout(30000);

  await clickCanvas(page, coordinates.library);
  await page.waitForTimeout(1000);
  await clickCanvas(page, coordinates.addNew);
  await page.waitForTimeout(1000);
  await clickCanvas(page, coordinates.urlField);
  await replaceFocusedText(page, beeUrl);
  await clickCanvas(page, coordinates.passwordField);
  await replaceFocusedText(page, password);

  const connectorResponse = page.waitForResponse(
    (response) => response.url() === beeUrl && response.status() === 206,
    { timeout: operationTimeoutMs },
  );
  await clickCanvas(page, coordinates.add);
  await connectorResponse;
  await waitForLog(logs, /Item key added:/, page, operationTimeoutMs);
  await page.waitForTimeout(2000);

  await clickCanvas(page, coordinates.itemCard);
  await page.waitForTimeout(1000);
  await clickCanvas(page, coordinates.spawn);
  await waitForLog(logs, /Forcefully closing the main menu/, page, operationTimeoutMs);
  await page.waitForTimeout(1000);
  await clickCanvas(page, coordinates.placement);

  const spawnLog = await waitForLog(logs, /Library provider successfully created item .* with networking: Local/, page, operationTimeoutMs);

  if (!spawnLog.includes(beeUrl)) {
    throw new Error(`The successful spawn log did not reference ${beeUrl}.`);
  }

  if (!beeResponses.some((response) => response.range && response.status === 206)) {
    throw new Error("The Prop BEE was not fetched with a successful HTTP Range request.");
  }
  if (consoleErrors.length !== 0) {
    throw new Error(`Browser console errors:\n${consoleErrors.join("\n")}`);
  }

  if (screenshotPath) {
    await page.screenshot({ path: screenshotPath, fullPage: true });
  }

  return {
    beeResponses,
    consoleErrors,
    spawnLog,
  };
}

export { DEFAULT_COORDINATES };
