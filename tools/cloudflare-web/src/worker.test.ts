import assert from 'node:assert/strict';
import test from 'node:test';
import { cacheControlFor, contentEncodingFor, responseInitFor } from './worker.ts';

test('fixed build artifacts revalidate in browsers and remain cached at the edge', () => {
  assert.equal(
    cacheControlFor('Build/basis.wasm.gz'),
    'public, max-age=0, s-maxage=31536000, must-revalidate, no-transform',
  );
});

test('non-build static assets keep long-lived browser caching', () => {
  assert.equal(
    cacheControlFor('TemplateData/style.css'),
    'public, max-age=86400, s-maxage=31536000, immutable, no-transform',
  );
});

test('precompressed build artifacts bypass automatic response encoding', () => {
  const headers = new Headers();

  assert.equal(contentEncodingFor('Build/client.framework.js.gz'), 'gzip');
  assert.equal(contentEncodingFor('Build/client.wasm.br'), 'br');
  assert.equal(contentEncodingFor('Build/client.loader.js'), null);
  assert.equal(responseInitFor('Build/client.data.gz', headers).encodeBody, 'manual');
  assert.equal(responseInitFor('Build/client.data', headers).encodeBody, undefined);
});

test('entry point and addressable catalogs can be updated safely', () => {
  assert.equal(cacheControlFor('index.html'), 'no-cache');
  assert.equal(
    cacheControlFor('StreamingAssets/aa/catalog.bin'),
    'public, max-age=300, s-maxage=300, must-revalidate',
  );
});
