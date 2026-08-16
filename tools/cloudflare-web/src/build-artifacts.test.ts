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

test('rewrites Unity entry point references with each artifact version', async () => {
  const html = `
    <script src="Build/release.loader.js"></script>
    <script>
      const buildUrl = "Build";
      const codeUrl = buildUrl + "/release.wasm.gz";
    </script>
  `;

  const rewritten = await rewriteBuildArtifactReferences(
    html,
    async key => key === 'Build/basis.loader.js' ? 'loader-42' : 'wasm-84',
  );
  assert.match(rewritten, /Build\/basis\.loader\.js\?v=loader-42&cache=2/u);
  assert.match(rewritten, /buildUrl \+ "\/basis\.wasm\.gz\?v=wasm-84"/u);
});
