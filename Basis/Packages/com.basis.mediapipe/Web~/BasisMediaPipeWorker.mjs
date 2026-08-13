import {
  FaceLandmarker,
  HandLandmarker,
  PoseLandmarker,
} from "./vision_bundle.mjs";

let faceLandmarker = null;
let handLandmarker = null;
let poseLandmarker = null;
let mirror = false;
let swapHands = false;
let canvas = null;
let context = null;

self.onmessage = async (event) => {
  try {
    if (event.data.type === "initialize") {
      await initialize(event.data.assetRoot, event.data.config);
      self.postMessage({ type: "ready" });
      return;
    }
    if (event.data.type === "frame") {
      processFrame(event.data.bitmap, event.data.timestampMs);
    }
  } catch (error) {
    self.postMessage({ type: "error", message: String(error) });
  }
};

async function initialize(assetRoot, config) {
  mirror = config.mirror;
  swapHands = config.swapHands;
  const wasmFileset = {
    wasmLoaderPath: assetRoot + "vision_wasm_internal.js",
    wasmBinaryPath: assetRoot + "vision_wasm_internal.wasm",
  };
  const taskOptions = {
    runningMode: "VIDEO",
    minTrackingConfidence: 0.5,
  };
  if (config.enableFace) {
    faceLandmarker = await FaceLandmarker.createFromOptions(wasmFileset, {
      ...taskOptions,
      baseOptions: { modelAssetPath: assetRoot + "face_landmarker.task.bytes" },
      numFaces: 1,
      minFaceDetectionConfidence: 0.5,
      minFacePresenceConfidence: 0.5,
      outputFaceBlendshapes: true,
      outputFacialTransformationMatrixes: true,
    });
  }
  if (config.enableHands) {
    handLandmarker = await HandLandmarker.createFromOptions(wasmFileset, {
      ...taskOptions,
      baseOptions: { modelAssetPath: assetRoot + "hand_landmarker.task.bytes" },
      numHands: 2,
      minHandDetectionConfidence: 0.5,
      minHandPresenceConfidence: 0.5,
    });
  }
  if (config.enablePose) {
    poseLandmarker = await PoseLandmarker.createFromOptions(wasmFileset, {
      ...taskOptions,
      baseOptions: { modelAssetPath: assetRoot + "pose_landmarker_lite.task.bytes" },
      numPoses: 1,
      minPoseDetectionConfidence: 0.5,
      minPosePresenceConfidence: 0.5,
      outputSegmentationMasks: false,
    });
  }
}

function processFrame(bitmap, timestampMs) {
  try {
    const image = prepareImage(bitmap);
    const face = faceLandmarker ? faceLandmarker.detectForVideo(image, timestampMs) : null;
    const hands = handLandmarker ? handLandmarker.detectForVideo(image, timestampMs) : null;
    const pose = poseLandmarker ? poseLandmarker.detectForVideo(image, timestampMs) : null;
    const values = encodeResult(timestampMs, face, hands, pose);
    self.postMessage({ type: "result", values: values.buffer }, [values.buffer]);
  } finally {
    bitmap.close();
  }
}

function prepareImage(bitmap) {
  if (!canvas || canvas.width !== bitmap.width || canvas.height !== bitmap.height) {
    canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
    context = canvas.getContext("2d", { alpha: false });
  }
  context.save();
  context.clearRect(0, 0, canvas.width, canvas.height);
  if (mirror) {
    context.translate(canvas.width, 0);
    context.scale(-1, 1);
  }
  context.drawImage(bitmap, 0, 0);
  context.restore();
  return canvas;
}

function encodeResult(timestampMs, faceResult, handResult, poseResult) {
  const faceLandmarks = faceResult?.faceLandmarks?.[0] ?? [];
  const blendshapeCategories = faceResult?.faceBlendshapes?.[0]?.categories ?? [];
  const matrix = faceResult?.facialTransformationMatrixes?.[0]?.data ?? identityMatrix();
  const hands = splitHands(handResult, swapHands);
  const poseLandmarks = poseResult?.landmarks?.[0] ?? [];
  const poseWorldLandmarks = poseResult?.worldLandmarks?.[0] ?? [];
  let flags = 0;
  if (faceLandmarks.length > 0) flags |= 1;
  if (hands.left.length > 0) flags |= 2;
  if (hands.right.length > 0) flags |= 4;
  if (poseLandmarks.length > 0 || poseWorldLandmarks.length > 0) flags |= 8;

  const blendshapes = new Float32Array(52);
  for (const category of blendshapeCategories) {
    if (category.index >= 0 && category.index < blendshapes.length) {
      blendshapes[category.index] = category.score;
    }
  }
  const hasFace = faceLandmarks.length > 152;
  const headX = hasFace ? faceLandmarks[1].x : 0;
  const headY = hasFace ? faceLandmarks[1].y : 0;
  const faceSize = hasFace ? Math.abs(faceLandmarks[152].y - faceLandmarks[10].y) : 0;
  const tongueOut = hasFace ? computeTongueOut(faceLandmarks) : 0;
  const valueCount = 7 + 20 + blendshapes.length
    + (hands.left.length + hands.right.length + poseLandmarks.length + poseWorldLandmarks.length) * 3;
  const output = new Float32Array(valueCount);
  let offset = 0;
  output[offset++] = timestampMs;
  output[offset++] = flags;
  output[offset++] = blendshapes.length;
  output[offset++] = hands.left.length;
  output[offset++] = hands.right.length;
  output[offset++] = poseLandmarks.length;
  output[offset++] = poseWorldLandmarks.length;
  for (let index = 0; index < 16; index++) output[offset++] = matrix[index] ?? 0;
  output[offset++] = headX;
  output[offset++] = headY;
  output[offset++] = faceSize;
  output[offset++] = tongueOut;
  output.set(blendshapes, offset);
  offset += blendshapes.length;
  offset = writeLandmarks(output, offset, hands.left);
  offset = writeLandmarks(output, offset, hands.right);
  offset = writeLandmarks(output, offset, poseLandmarks);
  writeLandmarks(output, offset, poseWorldLandmarks);
  return output;
}

function splitHands(result, shouldSwap) {
  const output = { left: [], right: [] };
  const landmarks = result?.landmarks ?? [];
  const handedness = result?.handedness ?? result?.handednesses ?? [];
  for (let index = 0; index < landmarks.length; index++) {
    const name = handedness[index]?.[0]?.categoryName;
    const mappedName = shouldSwap ? (name === "Left" ? "Right" : "Left") : name;
    if (mappedName === "Left") output.left = landmarks[index];
    else if (mappedName === "Right") output.right = landmarks[index];
  }
  return output;
}

function computeTongueOut(landmarks) {
  const openAmount = Math.abs(landmarks[14].y - landmarks[13].y);
  if (openAmount < 0.02) return 0;
  const x0 = Math.min(landmarks[78].x, landmarks[308].x);
  const x1 = Math.max(landmarks[78].x, landmarks[308].x);
  const y0 = (landmarks[13].y + landmarks[14].y) * 0.5;
  const y1 = landmarks[14].y + openAmount * 0.5;
  const pixelX0 = Math.max(0, Math.floor(x0 * canvas.width));
  const pixelX1 = Math.min(canvas.width - 1, Math.ceil(x1 * canvas.width));
  const pixelY0 = Math.max(0, Math.floor(y0 * canvas.height));
  const pixelY1 = Math.min(canvas.height - 1, Math.ceil(y1 * canvas.height));
  if (pixelX1 <= pixelX0 || pixelY1 <= pixelY0) return 0;
  const pixels = context.getImageData(pixelX0, pixelY0, pixelX1 - pixelX0 + 1, pixelY1 - pixelY0 + 1);
  const stepX = Math.max(1, Math.floor(pixels.width / 16));
  const stepY = Math.max(1, Math.floor(pixels.height / 16));
  let total = 0;
  let tongue = 0;
  for (let y = 0; y < pixels.height; y += stepY) {
    for (let x = 0; x < pixels.width; x += stepX) {
      const index = (y * pixels.width + x) * 4;
      const red = pixels.data[index];
      const green = pixels.data[index + 1];
      const blue = pixels.data[index + 2];
      total++;
      if (red > 60 && red > green + 12 && red > blue + 12 && red + green + blue < 600) tongue++;
    }
  }
  const fraction = total === 0 ? 0 : tongue / total;
  return Math.max(0, Math.min(1, (fraction - 0.25) / 0.75));
}

function writeLandmarks(output, offset, landmarks) {
  for (const landmark of landmarks) {
    output[offset++] = landmark.x;
    output[offset++] = landmark.y;
    output[offset++] = landmark.z;
  }
  return offset;
}

function identityMatrix() {
  return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
}
