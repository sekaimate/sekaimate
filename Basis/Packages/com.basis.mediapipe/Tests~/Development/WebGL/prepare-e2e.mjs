import { cp, mkdir, rm } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const sourceDirectory = fileURLToPath(new URL("./", import.meta.url));
const packageDirectory = fileURLToPath(new URL("../../../", import.meta.url));
const outputDirectory = process.env.BASIS_MEDIAPIPE_E2E_OUTPUT;

if (!outputDirectory) {
  throw new Error("BASIS_MEDIAPIPE_E2E_OUTPUT must name the disposable output directory.");
}

await rm(outputDirectory, { recursive: true, force: true });
await mkdir(`${outputDirectory}/runtime`, { recursive: true });
await mkdir(`${outputDirectory}/fixtures`, { recursive: true });

for (const file of ["index.html", "mediapipe-e2e.mjs"]) {
  await cp(`${sourceDirectory}/${file}`, `${outputDirectory}/${file}`);
}
for (const file of [
  "BasisMediaPipeWorker.mjs",
  "vision_bundle.mjs",
  "vision_wasm_internal.js",
  "vision_wasm_internal.wasm",
]) {
  await cp(`${packageDirectory}/Web~/${file}`, `${outputDirectory}/runtime/${file}`);
}
for (const file of [
  "face_landmarker.task.bytes",
  "hand_landmarker.task.bytes",
  "pose_landmarker_lite.task.bytes",
]) {
  await cp(`${packageDirectory}/Models/${file}`, `${outputDirectory}/runtime/${file}`);
}
for (const file of [
  "mediapipe-face-business-person.png",
  "mediapipe-hand-thumbs-up.jpg",
  "mediapipe-pose-test-image.jpg",
]) {
  await cp(`${sourceDirectory}/fixtures/${file}`, `${outputDirectory}/fixtures/${file}`);
}
