import { expect, test, type Download, type Page } from "@playwright/test";
import { readFile } from "node:fs/promises";

declare global {
  interface Window {
    basisCameraE2E?: {
      mode: string;
      stage: string;
      width: number;
      height: number;
      distinctPixelSamples: number;
      error: string;
    };
  }
}

const buildUrl = process.env.BASIS_WEB_CAMERA_URL;
test.skip(buildUrl === undefined, "BASIS_WEB_CAMERA_URL must point to a served development WebGL build");

type CaptureCase = {
  mode: "flat-png" | "flat-exr" | "panorama-png" | "panorama-exr";
  filename: RegExp;
  width: number;
  height: number;
  format: "png" | "exr";
};

const captures: CaptureCase[] = [
  { mode: "flat-png", filename: /^Screenshot_E2E_320x180\.png$/, width: 320, height: 180, format: "png" },
  { mode: "flat-exr", filename: /^Screenshot_E2E_320x180\.exr$/, width: 320, height: 180, format: "exr" },
  { mode: "panorama-png", filename: /^Screenshot360_Mono_E2E_256x128\.png$/, width: 256, height: 128, format: "png" },
  { mode: "panorama-exr", filename: /^Screenshot360_Mono_E2E_256x128\.exr$/, width: 256, height: 128, format: "exr" },
];

for (const capture of captures) {
  test(`${capture.mode} downloads rendered image bytes`, async ({ page }) => {
    const downloadPromise = page.waitForEvent("download");
    await page.goto(withCaptureMode(buildUrl as string, capture.mode));
    const download = await downloadPromise;
    await assertProbeCompleted(page, capture);
    await assertDownloadedImage(download, capture);
  });
}

function withCaptureMode(url: string, mode: CaptureCase["mode"]): string {
  const target = new URL(url);
  target.searchParams.set("basisCameraE2E", mode);
  return target.toString();
}

async function assertProbeCompleted(page: Page, capture: CaptureCase): Promise<void> {
  await expect.poll(() => page.evaluate(() => window.basisCameraE2E?.stage)).toBe("downloaded");
  const result = await page.evaluate(() => window.basisCameraE2E);
  expect(result).toMatchObject({
    mode: capture.mode,
    stage: "downloaded",
    width: capture.width,
    height: capture.height,
    error: "",
  });
  expect(result?.distinctPixelSamples).toBeGreaterThan(1);
}

async function assertDownloadedImage(download: Download, capture: CaptureCase): Promise<void> {
  expect(download.suggestedFilename()).toMatch(capture.filename);
  const path = await download.path();
  expect(path).not.toBeNull();
  const bytes = await readFile(path as string);
  expect(bytes.length).toBeGreaterThan(100);

  const dimensions = capture.format === "png" ? readPngDimensions(bytes) : readExrDimensions(bytes);
  expect(dimensions).toEqual({ width: capture.width, height: capture.height });
}

function readPngDimensions(bytes: Buffer): { width: number; height: number } {
  expect(bytes.subarray(0, 8)).toEqual(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]));
  expect(bytes.subarray(12, 16).toString("ascii")).toBe("IHDR");
  return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) };
}

function readExrDimensions(bytes: Buffer): { width: number; height: number } {
  expect(bytes.readUInt32LE(0)).toBe(20000630);
  let offset = 8;
  while (offset < bytes.length && bytes[offset] !== 0) {
    const nameEnd = bytes.indexOf(0, offset);
    const name = bytes.subarray(offset, nameEnd).toString("ascii");
    const typeEnd = bytes.indexOf(0, nameEnd + 1);
    const size = bytes.readUInt32LE(typeEnd + 1);
    const valueOffset = typeEnd + 5;
    if (name === "dataWindow") {
      expect(size).toBe(16);
      const minX = bytes.readInt32LE(valueOffset);
      const minY = bytes.readInt32LE(valueOffset + 4);
      const maxX = bytes.readInt32LE(valueOffset + 8);
      const maxY = bytes.readInt32LE(valueOffset + 12);
      return { width: maxX - minX + 1, height: maxY - minY + 1 };
    }
    offset = valueOffset + size;
  }
  throw new Error("OpenEXR dataWindow attribute is missing");
}
