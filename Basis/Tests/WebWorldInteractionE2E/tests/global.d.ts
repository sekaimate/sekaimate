interface BasisWorldInteractionSnapshot {
  schemaVersion: number;
  ready: boolean;
  stage: string;
  error: string;
  worldLoaded: boolean;
  directTouchReady: boolean;
  fixtureTypes: string[];
  activeTarget: string;
  hoverStarts: number;
  grabStarts: number;
  grabEnds: number;
  useDowns: number;
  seatEntries: number;
  seatExits: number;
  vehicleSeatEntries: number;
  imageGrabStarts: number;
  poolCueGrabStarts: number;
}

interface Window {
  basisWorldInteractionE2E?: BasisWorldInteractionSnapshot;
}
