const UNITY_BUILD_ARTIFACT = /^Build\/[^/]+(\.loader\.js|\.data(?:\.gz|\.br)?|\.framework\.js(?:\.gz|\.br)?|\.wasm(?:\.gz|\.br)?|\.symbols\.json(?:\.gz|\.br)?)$/u;
const UNITY_BUILD_ARTIFACT_REFERENCE = /[^/"']+(\.loader\.js|\.data(?:\.gz|\.br)?|\.framework\.js(?:\.gz|\.br)?|\.wasm(?:\.gz|\.br)?|\.symbols\.json(?:\.gz|\.br)?)/gu;

export function fixedBuildArtifactKey(key: string): string {
  const match = UNITY_BUILD_ARTIFACT.exec(key);
  return match === null ? key : `Build/basis${match[1]}`;
}

export async function rewriteBuildArtifactReferences(
  html: string,
  versionForArtifact: (key: string) => Promise<string>,
): Promise<string> {
  const suffixes = new Set(
    Array.from(html.matchAll(UNITY_BUILD_ARTIFACT_REFERENCE), match => match[1]),
  );
  const versions = new Map(await Promise.all(Array.from(suffixes, async suffix => {
    const key = `Build/basis${suffix}`;
    return [suffix, encodeURIComponent(await versionForArtifact(key))] as const;
  })));

  return html.replace(
    UNITY_BUILD_ARTIFACT_REFERENCE,
    (_artifact, suffix: string) => `basis${suffix}?v=${versions.get(suffix)}`,
  );
}
