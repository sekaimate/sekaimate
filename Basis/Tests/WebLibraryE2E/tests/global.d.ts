interface BasisLibraryE2EKey {
  mode: 'Avatar' | 'Prop' | 'World';
  pinned: boolean;
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
  currentPage: 'Avatar' | 'Prop' | 'World' | 'Instantiated';
  dropdowns: Array<{ entries: string[]; title: string; value: string }>;
  instances: BasisLibraryE2EInstance[];
  keys: BasisLibraryE2EKey[];
  ready: boolean;
  search: string;
}

interface BasisLibraryE2ECommand {
  action: string;
  target?: string;
  value?: string;
}

interface Window {
  basisLibraryE2E?: {
    command(command: BasisLibraryE2ECommand): void;
    snapshot?: BasisLibraryE2ESnapshot;
  };
}
