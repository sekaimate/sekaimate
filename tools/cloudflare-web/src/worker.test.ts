import assert from 'node:assert/strict';
import test from 'node:test';
import {
  browserSsoCallbackResponse,
  cacheControlFor,
  contentEncodingFor,
  enableUnityBuildBrowserCache,
  responseInitFor,
  webSsoConfigurationResponse,
} from './worker.ts';

test('Unity loader uses its IndexedDB cache for versioned build artifacts', () => {
  const loader = 'cacheControl: function (url) { return (url == Module.dataUrl || url.match(/\\.bundle/)) ? "must-revalidate" : "no-store"; }';
  assert.ok(enableUnityBuildBrowserCache(loader).includes('/(^|\\/)Build\\//.test(url) ? "immutable"'));
});

test('versioned build artifacts remain cached in browsers and at the edge', () => {
  assert.equal(
    cacheControlFor('Build/basis.wasm.gz'),
    'public, max-age=31536000, s-maxage=31536000, immutable, no-transform',
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
  assert.equal(cacheControlFor('index.html'), 'no-store');
  assert.equal(
    cacheControlFor('StreamingAssets/aa/catalog.bin'),
    'public, max-age=300, s-maxage=300, must-revalidate',
  );
});

test('browser SSO callback stores the OAuth result and returns to the client', async () => {
  const response = browserSsoCallbackResponse();
  assert.equal(response.headers.get('cache-control'), 'no-store');
  const body = await response.text();
  assert.match(body, /basis\.sso\.callback/u);
  assert.match(body, /window\.location\.replace/u);
});

test('web SSO configuration is streamed from the broker without caching', async () => {
  const response = await webSsoConfigurationResponse(
    'https://auth.example/web-client-config/local',
    async request => {
      assert.equal(request, 'https://auth.example/web-client-config/local');
      return new Response('{"providers":[{"id":"google"}]}');
    },
  );

  assert.equal(response.status, 200);
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.deepEqual(await response.json(), { providers: [{ id: 'google' }] });
});
