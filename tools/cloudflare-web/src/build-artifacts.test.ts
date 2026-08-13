import assert from 'node:assert/strict';
import test from 'node:test';
import { fixedBuildArtifactKey, rewriteBuildArtifactReferences } from './build-artifacts.ts';

test('uses fixed names for Unity build artifacts', () => {
  assert.equal(fixedBuildArtifactKey('Build/release.loader.js'), 'Build/basis.loader.js');
  assert.equal(fixedBuildArtifactKey('Build/release.data.gz'), 'Build/basis.data.gz');
  assert.equal(fixedBuildArtifactKey('Build/release.framework.js.br'), 'Build/basis.framework.js.br');
  assert.equal(fixedBuildArtifactKey('Build/release.wasm.gz'), 'Build/basis.wasm.gz');
  assert.equal(fixedBuildArtifactKey('StreamingAssets/catalog.json'), 'StreamingAssets/catalog.json');
});

test('rewrites Unity entry point references to fixed artifact names', () => {
  const html = `
    <script src="Build/release.loader.js"></script>
    <script>const codeUrl = "Build/release.wasm.gz";</script>
  `;

  assert.match(rewriteBuildArtifactReferences(html), /Build\/basis\.loader\.js/u);
  assert.match(rewriteBuildArtifactReferences(html), /Build\/basis\.wasm\.gz/u);
});
