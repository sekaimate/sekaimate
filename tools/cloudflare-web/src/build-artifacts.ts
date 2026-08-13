const UNITY_BUILD_ARTIFACT = /^Build\/[^/]+(\.loader\.js|\.data(?:\.gz|\.br)?|\.framework\.js(?:\.gz|\.br)?|\.wasm(?:\.gz|\.br)?|\.symbols\.json(?:\.gz|\.br)?)$/u;

export function fixedBuildArtifactKey(key: string): string {
  const match = UNITY_BUILD_ARTIFACT.exec(key);
  return match === null ? key : `Build/basis${match[1]}`;
}

export function rewriteBuildArtifactReferences(html: string): string {
  return html.replace(/Build\/[^/"']+\.(?:loader\.js|data(?:\.gz|\.br)?|framework\.js(?:\.gz|\.br)?|wasm(?:\.gz|\.br)?|symbols\.json(?:\.gz|\.br)?)/gu, fixedBuildArtifactKey);
}
