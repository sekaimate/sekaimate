interface BasisLibraryE2EKey {
  mode: 'Avatar' | 'Prop' | 'World';
  pinned: boolean;
  title: string;
  url: string;
}

interface BasisLibraryE2EInstance {
  id: string;
  mode: 'Avatar' | 'GameObject' | 'Scene';
  networked: boolean;
  persistent: boolean;
  selected: boolean;
  static: boolean;
  url: string;
}

interface BasisLibraryE2ESnapshot {
  buttons: Array<{ title: string; tooltip: string }>;
  connected: boolean;
  currentPage: 'Avatar' | 'Prop' | 'World' | 'Instantiated';
  dropdowns: Array<{ entries: string[]; title: string; value: string }>;
  instances: BasisLibraryE2EInstance[];
  keys: BasisLibraryE2EKey[];
  lastError: string;
  lastRequestId: number;
  ready: boolean;
  search: string;
  shareables: Array<{ id: string; kind: string; title: string }>;
}

interface BasisLibraryE2ECommand {
  action: string;
  requestId: number;
  target?: string;
  value?: string;
}

interface Window {
  basisLibraryE2E?: {
    command(command: BasisLibraryE2ECommand): void;
    snapshot?: BasisLibraryE2ESnapshot;
  };
}
