interface BasisNetworkE2EEvent {
  type: string;
  message: string;
  senderPlayerId: number;
  localPlayerId: number;
  connected: boolean;
  remotePlayerCount: number;
  avatarStateReady: boolean;
}

interface Window {
  basisNetworkE2EEvents?: BasisNetworkE2EEvent[];
  basisNetworkE2ESendChat?: (message: string) => void;
  basisNetworkE2EReconnect?: () => void;
}
