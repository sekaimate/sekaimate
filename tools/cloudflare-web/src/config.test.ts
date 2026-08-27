import assert from 'node:assert/strict';
import test from 'node:test';
import { createWorkerConfig } from './config.ts';

test('enables Workers Caching for R2 build artifacts', () => {
  const config = createWorkerConfig({
    workerName: 'web-client',
    workerScript: '/project/src/worker.ts',
    compatibilityDate: '2026-08-13',
    domain: 'client.example.com',
    bucketName: 'web-build',
    ssoConfigUrl: 'https://auth.example/web-client-config/local',
  });

  assert.deepEqual(config.cache, { enabled: true });
  assert.deepEqual(config.routes, [{ pattern: 'client.example.com', custom_domain: true }]);
  assert.deepEqual(config.vars, { SSO_CONFIG_URL: 'https://auth.example/web-client-config/local' });
});
