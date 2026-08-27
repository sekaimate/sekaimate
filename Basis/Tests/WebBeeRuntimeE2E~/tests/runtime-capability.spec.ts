import { expect, test } from "@playwright/test";
import { type BeeFormat, verifyRuntimeCapability } from "../src/runtime-capability.js";

const formats: BeeFormat[] = ["Avatar", "Prop", "World"];

for (const format of formats) {
  test.describe(`${format} BEE`, () => {
    const applicationUrl = process.env[`BASIS_${format.toUpperCase()}_BEE_READY_URL`];
    test.skip(!applicationUrl, `BASIS_${format.toUpperCase()}_BEE_READY_URL is required.`);

    test("renders and advances animation and audio", async ({ page }) => {
      const url = new URL(applicationUrl as string);
      url.searchParams.set("basisBeeRuntimeE2E", "1");
      await page.goto(url.toString(), { waitUntil: "domcontentloaded" });

      const evidence = await verifyRuntimeCapability(page, format);
      expect(evidence.animationTimeDelta).toBeGreaterThanOrEqual(0.1);
      expect(evidence.audioTimeDelta).toBeGreaterThanOrEqual(0.05);
      expect(evidence.differentPixelCount).toBeGreaterThanOrEqual(32);
    });
  });
}
