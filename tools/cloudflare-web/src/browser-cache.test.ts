import assert from 'node:assert/strict';
import test from 'node:test';
import {
  BROWSER_CACHE_CLEAR_PATH,
  browserCacheClearResponse,
  injectBrowserCacheControls,
} from './browser-cache.ts';

test('injects one cache reset control before the closing body tag', () => {
  const html = injectBrowserCacheControls('<html><body><canvas></canvas></body></html>');

  assert.equal(html.match(/id="basis-clear-browser-cache"/gu)?.length, 1);
  assert.ok(html.indexOf('basis-clear-browser-cache') < html.indexOf('</body>'));
  assert.match(html, new RegExp(BROWSER_CACHE_CLEAR_PATH, 'u'));
});

test('rejects malformed Unity entry point', () => {
  assert.throws(
    () => injectBrowserCacheControls('<html><canvas></canvas></html>'),
    /closing body tag/u,
  );
});

test('returns a browser cache clearing response only for POST', () => {
  const response = browserCacheClearResponse(new Request(`https://client.example${BROWSER_CACHE_CLEAR_PATH}`, {
    method: 'POST',
  }));

  assert.equal(response.status, 200);
  assert.equal(response.headers.get('clear-site-data'), '"cache"');
  assert.equal(response.headers.get('cache-control'), 'no-store');

  const rejected = browserCacheClearResponse(new Request(`https://client.example${BROWSER_CACHE_CLEAR_PATH}`));
  assert.equal(rejected.status, 405);
  assert.equal(rejected.headers.get('allow'), 'POST');
});
