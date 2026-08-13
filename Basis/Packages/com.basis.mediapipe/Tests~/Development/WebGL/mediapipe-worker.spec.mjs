import { expect, test } from "@playwright/test";

test("synthetic camera drives MediaPipe worker and avatar signals", async ({ page }) => {
  test.setTimeout(120_000);
  const browserErrors = [];
  page.on("pageerror", error => browserErrors.push(String(error)));

  await page.goto(process.env.BASIS_MEDIAPIPE_E2E_BASE_URL);
  await page.waitForFunction(() => Boolean(window.BasisMediaPipeE2E));
  const result = await page.evaluate(() => window.BasisMediaPipeE2E.run());

  expect(result.error).toBeNull();
  expect(result.faceDetected).toBe(true);
  expect(result.leftHandDetected).toBe(true);
  expect(result.rightHandDetected).toBe(true);
  expect(result.poseDetected).toBe(true);
  expect(result.handSelectionChanged).toBe(true);
  expect(result.mirrorSelectionChanged).toBe(true);
  expect(result.appliedSettings).toContainEqual({ mirror: false, swapHands: false });
  expect(result.appliedSettings).toContainEqual({ mirror: true, swapHands: false });
  expect(result.appliedSettings).toContainEqual({ mirror: false, swapHands: true });
  expect(result.avatarSignals).toEqual({
    faceBlendshapes: true,
    headTransform: true,
    leftHandTracker: true,
    rightHandTracker: true,
    bodyPose: true,
  });
  expect(browserErrors).toEqual([]);
});
