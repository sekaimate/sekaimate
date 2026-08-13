import assert from 'node:assert/strict';
import test from 'node:test';
import {
  deploymentManifest,
  parseDeploymentManifest,
  staleDeploymentKeys,
} from './deployment-manifest.ts';

test('serializes deterministic deployment manifests', () => {
  assert.equal(deploymentManifest(['z', 'a']), '{"keys":["a","z"]}\n');
  assert.deepEqual(parseDeploymentManifest('{"keys":["a","z"]}').keys, ['a', 'z']);
});

test('finds objects removed from the current deployment', () => {
  assert.deepEqual(staleDeploymentKeys(['old', 'shared'], ['shared', 'new']), ['old']);
});

test('rejects malformed deployment manifests', () => {
  assert.throws(() => parseDeploymentManifest('{"keys":[1]}'), /manifest keys/u);
});
