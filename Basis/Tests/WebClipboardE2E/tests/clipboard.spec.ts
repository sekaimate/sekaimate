import { expect, test, type BrowserContext, type Page } from '@playwright/test';

function requiredBuildUrl(): URL {
  const value = process.env.BASIS_WEB_BUILD_URL?.trim();
  if (!value) {
    throw new Error('BASIS_WEB_BUILD_URL must point to an already-running WebGL development build.');
  }

  const url = new URL(value);
  url.searchParams.set('basisClipboardE2E', '1');
  return url;
}

async function grantClipboardPermissions(context: BrowserContext, url: URL): Promise<void> {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], {
    origin: url.origin,
  });
}

async function waitForResult(
  page: Page,
  operation: BasisClipboardE2EResult['operation'],
  resultIndex: number,
): Promise<BasisClipboardE2EResult> {
  await page.waitForFunction(
    ({ expectedOperation, expectedIndex }) => {
      const result = window.basisClipboardE2E?.results[expectedIndex];
      return result?.operation === expectedOperation;
    },
    { expectedOperation: operation, expectedIndex: resultIndex },
  );

  return page.evaluate(index => {
    const result = window.basisClipboardE2E?.results[index];
    if (!result) {
      throw new Error(`Clipboard result ${index} was not published.`);
    }
    return result;
  }, resultIndex);
}

test('Unity copy and paste actions use the real browser clipboard', async ({ browser }) => {
  const url = requiredBuildUrl();
  const context = await browser.newContext();
  await grantClipboardPermissions(context, url);
  const page = await context.newPage();

  await page.goto(url.toString());
  await page.waitForFunction(() => window.basisClipboardE2E?.ready === true);

  const capabilities = await page.evaluate(() => ({
    secureContext: window.basisClipboardE2E?.secureContext,
    clipboardAvailable: window.basisClipboardE2E?.clipboardAvailable,
  }));
  expect(capabilities).toEqual({ secureContext: true, clipboardAvailable: true });

  const copiedText = `Unity→OS Clipboard 日本語🦊 ${Date.now()}`;
  await page.evaluate(text => window.basisClipboardE2E?.setWriteText(text), copiedText);
  await page.locator('#basis-clipboard-e2e-write').click();
  const writeResult = await waitForResult(page, 'write', 0);
  expect(writeResult).toEqual({ operation: 'write', succeeded: true, text: '', error: '' });
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(copiedText);

  const pastedText = `OS→Unity Clipboard 改行\n${Date.now()}`;
  await page.evaluate(text => navigator.clipboard.writeText(text), pastedText);
  await page.locator('#basis-clipboard-e2e-read').click();
  const readResult = await waitForResult(page, 'read', 1);
  expect(readResult).toEqual({ operation: 'read', succeeded: true, text: pastedText, error: '' });

  await context.close();
});
