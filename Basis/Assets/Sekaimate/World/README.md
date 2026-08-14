# U☆PoC concept comparison world

`MinimalWalkable.unity` and the materials under `Materials/` are the source of truth. Edit the scene directly in Unity.

- The shared lobby leads to three equal-sized, walkable concept zones: prototype exhibition (A), final presentation venue (B), and virtual-first collaboration (C).
- The concepts intentionally share one visual language and prop set so the team can compare layout and experience instead of modeling quality.
- Kenney furniture and space-station props are stored under `Assets/Sekaimate/ThirdParty/Kenney/`; their source and CC0 license records live beside the imported files.
- `Assets/Sekaimate/Documentation/ComparisonEvidence/` contains fixed review views of the lobby and each concept.
- The `.BEE` output remains derived local data and is never committed.

## Build and local smoke test

1. Open `MinimalWalkable.unity`.
2. Run `Sekaimate > UPoC > Mac用BEEを生成`.
3. Play `Packages/com.basis.framework/Scenes/initialization.unity`.
4. Drag the generated `.BEE` from Finder into Unity's Scene view while Play Mode is active.
5. Open Library, select Worlds, and load `UPoC Concept Comparison Gallery` as local content.

The build helper targets macOS only and temporarily uses the Standalone Mono backend to avoid the current Mac IL2CPP `Unity.Scripting` reference failure. It restores the previous build target and backend afterward.

Generated `.BEE` files, build reports, and password sidecars are ignored by Git. Keep the password sidecar local and never commit or paste its contents into an issue or documentation.
