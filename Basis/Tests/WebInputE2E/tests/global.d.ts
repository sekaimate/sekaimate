interface BasisInputVector2 {
  x: number;
  y: number;
}

interface BasisInputVector3 extends BasisInputVector2 {
  z: number;
}

interface BasisOnScreenControlsSnapshot {
  ready: boolean;
  leftStick: BasisInputVector2;
  rightStick: BasisInputVector2;
  jump: BasisInputVector2;
  crouch: BasisInputVector2;
}

interface BasisInputE2ESnapshot {
  schemaVersion: number;
  frame: number;
  ready: boolean;
  pointerLocked: boolean;
  moveAction: BasisInputVector2;
  moveDevice: string;
  movement: BasisInputVector2;
  playerPosition: BasisInputVector3;
  lookAction: BasisInputVector2;
  lookDevice: string;
  lookVector: BasisInputVector2;
  lookYaw: number;
  lookPitch: number;
  activeTouches: number;
  onScreenControls: BasisOnScreenControlsSnapshot;
  screenSize: BasisInputVector2;
}

interface Window {
  basisInputE2E?: BasisInputE2ESnapshot;
  basisSetTestGamepad?: (moveX: number, moveY: number, lookX: number, lookY: number) => void;
}
