import { type Locator, type Page } from '@playwright/test';

export type BeeFormat = 'Avatar' | 'Prop' | 'World';
export type CapabilityOwner = 'LocalAvatar' | 'RemoteAvatar' | 'Content';

interface CapabilitySnapshot {
  animationClipLength: number;
  animationNormalizedTime: number;
  audioClipLength: number;
  audioIsPlaying: boolean;
  audioTime: number;
  format: BeeFormat;
  instanceId: number;
  observedAt: number;
  ownerKind: string;
  ownerPlayerId: number;
  rendererVisible: boolean;
}

interface CapabilityDiagnostics {
  snapshots: Partial<Record<BeeFormat, CapabilitySnapshot[]>>;
}

function countDifferentImageBytes(before: Buffer, after: Buffer): number {
  if (before.length !== after.length) return Math.max(before.length, after.length);
  let count = 0;
  for (let index = 0; index < before.length; index += 1) {
    if (before[index] !== after[index]) count += 1;
  }
  return count;
}

async function snapshots(
  page: Page,
  format: BeeFormat,
  ownerKind: CapabilityOwner,
  ownerPlayerId?: number,
): Promise<CapabilitySnapshot[]> {
  return page.evaluate(({ requestedFormat, requestedOwner, requestedPlayerId }) => {
    const diagnostics = Reflect.get(globalThis, 'BasisBeeRuntimeCapabilityDiagnostics') as CapabilityDiagnostics | undefined;
    return (diagnostics?.snapshots[requestedFormat] ?? []).filter(snapshot =>
      snapshot.ownerKind === requestedOwner
      && (requestedPlayerId === undefined || snapshot.ownerPlayerId === requestedPlayerId));
  }, { requestedFormat: format, requestedOwner: ownerKind, requestedPlayerId: ownerPlayerId });
}

export async function verifyRenderedCapability(
  page: Page,
  format: BeeFormat,
  ownerKind: CapabilityOwner,
  ownerPlayerId?: number,
  canvas: Locator = page.locator('#unity-canvas'),
): Promise<void> {
  await canvas.waitFor({ state: 'visible' });
  await page.waitForFunction(({ requestedFormat, requestedOwner, requestedPlayerId }) => {
    const diagnostics = Reflect.get(globalThis, 'BasisBeeRuntimeCapabilityDiagnostics') as CapabilityDiagnostics | undefined;
    return (diagnostics?.snapshots[requestedFormat] ?? []).some(snapshot =>
      snapshot.ownerKind === requestedOwner
      && (requestedPlayerId === undefined || snapshot.ownerPlayerId === requestedPlayerId));
  }, { requestedFormat: format, requestedOwner: ownerKind, requestedPlayerId: ownerPlayerId });

  const before = await canvas.screenshot();
  await page.waitForTimeout(750);
  const after = await canvas.screenshot();
  const observed = await snapshots(page, format, ownerKind, ownerPlayerId);
  const progresses = observed.some((first, firstIndex) => observed.slice(firstIndex + 1).some(second =>
    first.instanceId === second.instanceId
    && first.rendererVisible
    && second.rendererVisible
    && second.animationNormalizedTime - first.animationNormalizedTime >= 0.1
    && first.audioIsPlaying
    && second.audioIsPlaying
    && Math.abs(second.audioTime - first.audioTime) >= 0.05
    && first.animationClipLength >= 1
    && first.audioClipLength >= 1));
  if (!progresses) {
    throw new Error(`${format}/${ownerKind} did not render while animation and audio progressed.`);
  }
  const changedImageBytes = countDifferentImageBytes(before, after);
  if (changedImageBytes < 32) {
    throw new Error(`${format}/${ownerKind} changed only ${changedImageBytes} canvas image bytes.`);
  }
}
