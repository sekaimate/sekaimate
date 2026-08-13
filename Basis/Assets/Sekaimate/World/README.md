# Minimal walkable world

`MinimalWalkable.unity` and its two materials are the source of truth for the first U☆PoC map milestone. Edit the scene directly in Unity.

- The scene contains a floor, four collision boundaries, lighting, a `Basis Scene`, and a spawn point.
- The `.BEE` output is derived data and is handled separately in Issue #21.

## Build and local smoke test

1. Open `MinimalWalkable.unity`.
2. Run `Sekaimate > UPoC > Mac用BEEを生成`.
3. Play `Packages/com.basis.framework/Scenes/initialization.unity`.
4. Drag the generated `.BEE` from Finder into Unity's Scene view while Play Mode is active.
5. Open Library, select Worlds, and load `UPoC Minimal Walkable Space` as local content.

The build helper targets macOS only and temporarily uses the Standalone Mono backend to avoid the current Mac IL2CPP `Unity.Scripting` reference failure. It restores the previous build target and backend afterward.

Generated `.BEE` files, build reports, and password sidecars are ignored by Git. Keep the password sidecar local and never commit or paste its contents into an issue or documentation.
