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
  leftHandInputReady: boolean;
  rightHandInputReady: boolean;
  leftDirectTouching: boolean;
  rightDirectTouching: boolean;
  directTouchStarts: number;
  directTouchEnds: number;
  directTouchPointerEnters: number;
  directTouchPointerDowns: number;
  directTouchPointerUps: number;
  directTouchClicks: number;
  directTouchCenter: { x: number; y: number; z: number };
  directTouchNormal: { x: number; y: number; z: number };
}

interface Window {
  basisWorldInteractionE2E?: BasisWorldInteractionSnapshot;
}
