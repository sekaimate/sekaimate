import { expect, test } from "@playwright/test";
import { PNG } from "pngjs";
import {
  type CapabilitySnapshot,
  runtimeCapabilityInternals,
} from "../src/runtime-capability.js";

function snapshot(overrides: Partial<CapabilitySnapshot>): CapabilitySnapshot {
  return {
    animationClipLength: 1,
    animationNormalizedTime: 0,
    audioClipLength: 2,
    audioIsPlaying: true,
    audioTime: 0,
    format: "Avatar",
    instanceId: 7,
    observedAt: 0,
    rendererCenterX: 0,
    rendererVisible: true,
    ...overrides,
  };
}

test("selects evidence only when the same visible fixture advances animation and audio", () => {
  const pair = runtimeCapabilityInternals.findProgressingPair([
    snapshot({}),
    snapshot({ animationNormalizedTime: 0.2, audioTime: 0.2, observedAt: 0.25 }),
  ]);

  expect(pair).toHaveLength(2);
});

test("counts actual changed PNG pixels", () => {
  const before = new PNG({ width: 2, height: 1 });
  const after = new PNG({ width: 2, height: 1 });
  before.data.set([0, 0, 0, 255, 0, 0, 0, 255]);
  after.data.set([255, 0, 0, 255, 0, 0, 0, 255]);

  expect(
    runtimeCapabilityInternals.countDifferentPixels(
      PNG.sync.write(before),
      PNG.sync.write(after),
    ),
  ).toBe(1);
});
