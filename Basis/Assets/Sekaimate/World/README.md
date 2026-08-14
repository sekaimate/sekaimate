# U☆PoC concept comparison world

`MinimalWalkable.unity` and the materials under `Materials/` are the source of truth. Edit the scene directly in Unity.

- The shared lobby retains the three comparison concepts: prototype exhibition (A), final presentation venue (B), and virtual-first collaboration (C).
- The east-side connector leads to the selected B202-inspired lecture hall (D). Its upper entrance looks across six descending seating terraces toward a stage roughly three metres below.
- D v2 uses four seating blocks on every terrace: 24 long desks and 96 chairs in total. The centre and two side stair lanes remain clear.
- The upper rear landing is 2.75 metres deep. Its connector, entrance wall, and three ceiling tiles share exact boundary coordinates so the room has no longitudinal roof gaps or coplanar overlap.
- The D stage includes a large presentation screen, an ASCII `U*PoC 2026 / SEKAIMATE` placeholder, two speakers, two spotlights, and ten replaceable poster surfaces. The asterisk avoids a missing-glyph box in the current TMP font; displayed content is temporary.
- The concepts intentionally share one visual language and prop set so the team can compare layout and experience instead of modeling quality.
- Kenney furniture and space-station props are stored under `Assets/Sekaimate/ThirdParty/Kenney/`; their source and CC0 license records live beside the imported files.
- `Assets/Sekaimate/Documentation/ComparisonEvidence/` contains the original comparison views and fixed D v2 review views.
- The `.BEE` output remains derived local data and is never committed.

## Build and local smoke test

1. Open `MinimalWalkable.unity`.
2. Run `Sekaimate > UPoC > Mac用BEEを生成`.
3. Play `Packages/com.basis.framework/Scenes/initialization.unity`.
4. Drag the generated `.BEE` from Finder into Unity's Scene view while Play Mode is active.
5. Open Library, select Worlds, and load `UPoC Concept Comparison Gallery` as local content.

The build helper targets macOS only and temporarily uses the Standalone Mono backend to avoid the current Mac IL2CPP `Unity.Scripting` reference failure. It restores the previous build target and backend afterward.

Generated `.BEE` files, build reports, and password sidecars are ignored by Git. Keep the password sidecar local and never commit or paste its contents into an issue or documentation.
