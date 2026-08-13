const sourceCanvas = document.querySelector("#camera-source");
const sourceContext = sourceCanvas.getContext("2d", { alpha: false });
const cameraPreview = document.querySelector("#camera-preview");
const resultElement = document.querySelector("#result");
const fixtureNames = {
  face: "mediapipe-face-business-person.png",
  hand: "mediapipe-hand-thumbs-up.jpg",
  pose: "mediapipe-pose-test-image.jpg",
};

const state = {
  ready: false,
  running: false,
  appliedSettings: [],
  observations: [],
  faceDetected: false,
  leftHandDetected: false,
  rightHandDetected: false,
  poseDetected: false,
  handSelectionChanged: false,
  avatarSignals: {
    faceBlendshapes: false,
    headTransform: false,
    leftHandTracker: false,
    rightHandTracker: false,
    bodyPose: false,
  },
  error: null,
};

let worker;
let pendingResult;
let cameraStream;
let timestampMs = 0;

function renderState() {
  resultElement.textContent = JSON.stringify(state, null, 2);
  document.body.dataset.status = state.error ? "error" : state.running ? "running" : state.ready ? "ready" : "idle";
}

function nextWorkerMessage(type) {
  return new Promise((resolve, reject) => {
    pendingResult = { type, resolve, reject };
  });
}

async function initializeWorker(settings) {
  worker = new Worker(new URL("./runtime/BasisMediaPipeWorker.mjs", import.meta.url), { type: "module" });
  worker.addEventListener("message", event => {
    if (event.data.type === "error") {
      pendingResult?.reject(new Error(event.data.message));
      pendingResult = undefined;
      return;
    }
    if (pendingResult?.type === event.data.type) {
      pendingResult.resolve(event.data);
      pendingResult = undefined;
    }
  });
  const readyMessage = nextWorkerMessage("ready");
  worker.postMessage({
    type: "initialize",
    assetRoot: new URL("./runtime/", import.meta.url).href,
    config: {
      enableFace: true,
      enableHands: true,
      enablePose: true,
      mirror: settings.mirror,
      swapHands: settings.swapHands,
    },
  });
  const ready = await readyMessage;
  state.appliedSettings.push(ready.appliedConfig);
  state.ready = true;
  renderState();
}

async function initializeSyntheticCamera() {
  cameraStream = sourceCanvas.captureStream(30);
  cameraPreview.srcObject = cameraStream;
  await cameraPreview.play();
}

async function loadFixture(name) {
  const image = new Image();
  image.src = new URL(`./fixtures/${fixtureNames[name]}`, import.meta.url).href;
  await image.decode();
  sourceContext.drawImage(image, 0, 0, sourceCanvas.width, sourceCanvas.height);
  await new Promise(resolve => setTimeout(resolve, 70));
}

function decodeResult(buffer, fixture) {
  const values = new Float32Array(buffer);
  const flags = values[1];
  const blendshapeCount = values[2];
  const leftHandCount = values[3];
  const rightHandCount = values[4];
  const poseCount = values[5];
  const poseWorldCount = values[6];
  const faceSize = values[25];
  const observation = {
    fixture,
    faceDetected: (flags & 1) !== 0,
    leftHandDetected: (flags & 2) !== 0,
    rightHandDetected: (flags & 4) !== 0,
    poseDetected: (flags & 8) !== 0,
    blendshapeCount,
    leftHandCount,
    rightHandCount,
    poseCount,
    poseWorldCount,
    faceSize,
  };
  state.observations.push(observation);
  state.faceDetected ||= observation.faceDetected;
  state.leftHandDetected ||= observation.leftHandDetected;
  state.rightHandDetected ||= observation.rightHandDetected;
  state.poseDetected ||= observation.poseDetected;
  state.avatarSignals.faceBlendshapes ||= observation.faceDetected && blendshapeCount === 52;
  state.avatarSignals.headTransform ||= observation.faceDetected && faceSize > 0;
  state.avatarSignals.leftHandTracker ||= leftHandCount === 21;
  state.avatarSignals.rightHandTracker ||= rightHandCount === 21;
  state.avatarSignals.bodyPose ||= poseCount === 33 && poseWorldCount === 33;
  renderState();
  return observation;
}

async function inferFixture(name) {
  await loadFixture(name);
  const bitmap = await createImageBitmap(cameraPreview);
  timestampMs += 100;
  const resultMessage = nextWorkerMessage("result");
  worker.postMessage({ type: "frame", bitmap, timestampMs }, [bitmap]);
  return decodeResult((await resultMessage).values, name);
}

function handSide(observation) {
  if (observation.leftHandCount === 21) return "left";
  if (observation.rightHandCount === 21) return "right";
  return "none";
}

async function run() {
  state.running = true;
  state.error = null;
  renderState();
  try {
    await initializeSyntheticCamera();
    await initializeWorker({ mirror: false, swapHands: false });
    await inferFixture("face");
    const originalHand = await inferFixture("hand");
    await inferFixture("pose");

    worker.terminate();
    state.ready = false;
    await initializeWorker({ mirror: true, swapHands: true });
    const swappedHand = await inferFixture("hand");
    state.handSelectionChanged = handSide(originalHand) !== "none"
      && handSide(swappedHand) !== "none"
      && handSide(originalHand) !== handSide(swappedHand);
    state.running = false;
    renderState();
    return structuredClone(state);
  } catch (error) {
    state.error = String(error);
    state.running = false;
    renderState();
    throw error;
  }
}

window.BasisMediaPipeE2E = { run, state };
renderState();
