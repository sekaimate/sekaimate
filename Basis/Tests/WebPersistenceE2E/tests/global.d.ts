interface BasisPersistenceE2EResult {
  phase: 'seed' | 'verify';
  ready: boolean;
  avatar: boolean;
  prop: boolean;
  world: boolean;
  binding: boolean;
  camera: boolean;
  settings: boolean;
  savedServers: boolean;
  trustedUrls: boolean;
  error: string;
}

interface Window {
  basisPersistenceE2E?: BasisPersistenceE2EResult;
}
