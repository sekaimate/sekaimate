export const DEPLOYMENT_MANIFEST_KEY = '.basis-web-deployment.json';

interface DeploymentManifest {
  keys: string[];
}

export function parseDeploymentManifest(value: string): DeploymentManifest {
  const parsed: unknown = JSON.parse(value);
  if (typeof parsed !== 'object' || parsed === null || !('keys' in parsed)) {
    throw new Error('Invalid deployment manifest.');
  }
  if (!Array.isArray(parsed.keys) || !parsed.keys.every(key => typeof key === 'string')) {
    throw new Error('Invalid deployment manifest keys.');
  }
  return { keys: parsed.keys };
}

export function deploymentManifest(keys: string[]): string {
  return `${JSON.stringify({ keys: [...keys].sort() })}\n`;
}

export function staleDeploymentKeys(previousKeys: string[], currentKeys: string[]): string[] {
  const current = new Set(currentKeys);
  return previousKeys.filter(key => !current.has(key));
}
