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
  visibleLabels: string[];
  availableAdminActions: string[];
  volume: number;
  pinned: boolean;
  highlighted: boolean;
  avatarVisible: boolean;
  chatVisible: boolean;
  blocked: boolean;
  temporarilyBlocked: boolean;
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

interface BasisNetworkE2EAvatarInput {
  contentUrl: string;
  unlockPassword: string;
}

interface Window {
  basisNetworkE2EEvents?: BasisNetworkE2EEvent[];
  basisNetworkE2ESendChat?: (message: string) => void;
  basisNetworkE2EReconnect?: () => void;
  basisNetworkE2ESetAvatar?: (input: BasisNetworkE2EAvatarInput) => void;
  basisNetworkE2EShareContent?: (input: BasisNetworkE2EContentShareInput) => void;
  basisNetworkE2ERemoveContent?: (sphereId: string) => void;
  basisNetworkE2ELoadContent?: (sphereId: string) => void;
  basisNetworkE2EOpenPlayerList?: () => void;
  basisNetworkE2EPlayerSearch?: (query: string) => void;
  basisNetworkE2EPlayerSort?: (sort: string) => void;
  basisNetworkE2EOpenPlayer?: (displayName: string) => void;
  basisNetworkE2EPlayerUiAction?: (localizationKey: string) => void;
  basisNetworkE2EPlayerVolume?: (volume: number) => void;
  basisNetworkE2EConfirmDialogue?: (accepted: boolean) => void;
  basisNetworkE2EPlayerState?: () => void;
}
