import assert from 'node:assert/strict';
import test from 'node:test';
import { cacheControlFor } from './worker.ts';

test('long-lived build artifacts use edge and browser caches', () => {
  assert.equal(
    cacheControlFor('Build/client.wasm'),
    'public, max-age=86400, s-maxage=31536000, immutable',
  );
});

test('entry point and addressable catalogs can be updated safely', () => {
  assert.equal(cacheControlFor('index.html'), 'no-cache');
  assert.equal(
    cacheControlFor('StreamingAssets/aa/catalog.bin'),
    'public, max-age=300, s-maxage=300, must-revalidate',
  );
});
