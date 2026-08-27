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
  leftDirectTouchFingertip: { x: number; y: number; z: number };
  leftPinch: number;
}

interface Window {
  basisWorldInteractionE2E?: BasisWorldInteractionSnapshot;
  transformHandles?: Map<'left' | 'right', {
    position: {
      x: number;
      y: number;
      z: number;
      set(x: number, y: number, z: number): void;
    };
  }>;
  basisWebXR?: {
    schemaVersion: number;
    supported: boolean;
    sessionActive: boolean;
    referenceSpace: string;
    frame: number;
    lastError: string;
    snapshot: {
      sources: Array<{ handedness: string; handTracked: boolean }>;
    };
    basisState?: {
      schemaVersion: number;
      frame: number;
      sessionActive: boolean;
      headDevice: boolean;
      leftHandDevice: boolean;
      rightHandDevice: boolean;
      leftHandTracked: boolean;
      rightHandTracked: boolean;
      leftPinch: number;
      rightPinch: number;
    };
  };
}
