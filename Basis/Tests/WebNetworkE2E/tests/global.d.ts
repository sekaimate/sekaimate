interface BasisNetworkE2EEvent {
  type: string;
  message: string;
  senderPlayerId: number;
  localPlayerId: number;
  connected: boolean;
  remotePlayerCount: number;
  avatarStateReady: boolean;
  sphereId: string;
  contentType: 'Avatar' | 'Prop' | 'World' | 'Server' | '';
  contentUrl: string;
}

interface BasisNetworkE2EContentShareInput {
  sphereId: string;
  contentUrl: string;
  unlockPassword: string;
  contentType: 'Avatar' | 'Prop' | 'World' | 'Server';
  positionX: number;
  positionY: number;
  positionZ: number;
}

interface Window {
  basisNetworkE2EEvents?: BasisNetworkE2EEvent[];
  basisNetworkE2ESendChat?: (message: string) => void;
  basisNetworkE2EReconnect?: () => void;
  basisNetworkE2EShareContent?: (input: BasisNetworkE2EContentShareInput) => void;
  basisNetworkE2ERemoveContent?: (sphereId: string) => void;
}
