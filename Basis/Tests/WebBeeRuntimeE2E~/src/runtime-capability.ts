import type { Locator, Page } from "@playwright/test";
import { PNG } from "pngjs";

export type BeeFormat = "Avatar" | "Prop" | "World";

export interface CapabilitySnapshot {
  animationClipLength: number;
  animationNormalizedTime: number;
  audioClipLength: number;
  audioIsPlaying: boolean;
  audioTime: number;
  format: BeeFormat;
  instanceId: number;
  observedAt: number;
  rendererCenterX: number;
  rendererVisible: boolean;
}

interface CapabilityDiagnostics {
  snapshots: Partial<Record<BeeFormat, CapabilitySnapshot[]>>;
}

export interface RuntimeCapabilityEvidence {
  animationTimeDelta: number;
  audioTimeDelta: number;
  differentPixelCount: number;
  format: BeeFormat;
  instanceId: number;
}

function countDifferentPixels(before: Buffer, after: Buffer): number {
  const first = PNG.sync.read(before);
  const second = PNG.sync.read(after);
  if (first.width !== second.width || first.height !== second.height) {
    throw new Error("Canvas screenshots must have equal dimensions.");
  }

  let differentPixelCount = 0;
  for (let index = 0; index < first.data.length; index += 4) {
    const difference =
      Math.abs(first.data[index] - second.data[index]) +
      Math.abs(first.data[index + 1] - second.data[index + 1]) +
      Math.abs(first.data[index + 2] - second.data[index + 2]);
    if (difference >= 24) {
      differentPixelCount += 1;
    }
  }
  return differentPixelCount;
}

function findProgressingPair(
  snapshots: CapabilitySnapshot[],
): [CapabilitySnapshot, CapabilitySnapshot] | undefined {
  for (let firstIndex = 0; firstIndex < snapshots.length - 1; firstIndex += 1) {
    const first = snapshots[firstIndex];
    for (let secondIndex = firstIndex + 1; secondIndex < snapshots.length; secondIndex += 1) {
      const second = snapshots[secondIndex];
      if (
        first.instanceId === second.instanceId &&
        first.rendererVisible &&
        second.rendererVisible &&
        second.animationNormalizedTime - first.animationNormalizedTime >= 0.1 &&
        first.audioIsPlaying &&
        second.audioIsPlaying &&
        Math.abs(second.audioTime - first.audioTime) >= 0.05
      ) {
        return [first, second];
      }
    }
  }
  return undefined;
}

async function readSnapshots(page: Page, format: BeeFormat): Promise<CapabilitySnapshot[]> {
  return page.evaluate((requestedFormat) => {
    const diagnostics = Reflect.get(
      globalThis,
      "BasisBeeRuntimeCapabilityDiagnostics",
    ) as CapabilityDiagnostics | undefined;
    return diagnostics?.snapshots[requestedFormat] ?? [];
  }, format);
}

export async function verifyRuntimeCapability(
  page: Page,
  format: BeeFormat,
  canvas: Locator = page.locator("canvas"),
): Promise<RuntimeCapabilityEvidence> {
  await canvas.waitFor({ state: "visible" });
  await page.waitForFunction(
    (requestedFormat) => {
      const diagnostics = Reflect.get(
        globalThis,
        "BasisBeeRuntimeCapabilityDiagnostics",
      ) as CapabilityDiagnostics | undefined;
      return (diagnostics?.snapshots[requestedFormat]?.length ?? 0) > 0;
    },
    format,
  );

  const before = await canvas.screenshot();
  await page.waitForTimeout(750);
  const after = await canvas.screenshot();
  const snapshots = await readSnapshots(page, format);
  const pair = findProgressingPair(snapshots);
  if (!pair) {
    throw new Error(`${format} did not prove visible renderer, Animator progress, and AudioSource progress.`);
  }

  const [first, second] = pair;
  const differentPixelCount = countDifferentPixels(before, after);
  if (differentPixelCount < 32) {
    throw new Error(`${format} changed only ${differentPixelCount} canvas pixels.`);
  }

  if (first.animationClipLength < 1 || first.audioClipLength < 1) {
    throw new Error(`${format} fixture clips must each be at least one second long.`);
  }

  return {
    animationTimeDelta: second.animationNormalizedTime - first.animationNormalizedTime,
    audioTimeDelta: Math.abs(second.audioTime - first.audioTime),
    differentPixelCount,
    format,
    instanceId: first.instanceId,
  };
}

export const runtimeCapabilityInternals = {
  countDifferentPixels,
  findProgressingPair,
};
