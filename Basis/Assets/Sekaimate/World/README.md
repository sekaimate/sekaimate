# U☆PoC B202 lecture hall

`MinimalWalkable.unity` and the materials under `Materials/` are the source of truth. Edit the scene directly in Unity.

- The active layout is a D-only, B202-inspired lecture hall. The upper entrance looks across six descending seating terraces toward the stage.
- Four straight seating columns and five straight stair lanes form a symmetric fan. Every desk column keeps one fixed angle and is centred between its neighbouring aisles. The centre and side stairs are `1.20` metres wide, and the two outer stairs are `1.125` metres wide.
- The accepted layout has 20 variable-width desks and 64 chairs. The per-column chair schedule from the stage side is `0 / 2 / 2 / 4 / 4 / 4`.
- The empty first seating terrace is the lowest floor at `y=0`; the stage top and first occupied terrace are at `y=0.5`. Each later terrace rises by another `0.5` metre.
- Furniture packages are centred front-to-back in their terrace cells with roughly `0.30` metre of visible margin on each side. The closest occupied furniture is about `3.16` metres from the stage.
- The stage includes a large presentation screen, an ASCII `U-PoC` placeholder, two speakers, two spotlights, and four replaceable poster surfaces on the entrance wall. Displayed content is temporary.
- Kenney furniture and space-station props are stored under `Assets/Sekaimate/ThirdParty/Kenney/`; their source and CC0 license records live beside the imported files.
- The rejected A/B/C concepts remain recoverable from Git history and the existing comparison evidence; they are not part of the active world.
- The `.BEE` output remains derived local data and is never committed.

## Build and local smoke test

1. Open `MinimalWalkable.unity`.
2. Run `Sekaimate > UPoC > Mac用BEEを生成`.
3. Play `Packages/com.basis.framework/Scenes/initialization.unity`.
4. Drag the generated `.BEE` from Finder into Unity's Scene view while Play Mode is active.
5. Open Library, select Worlds, and load `UPoC B202 Radial Hall` as local content.

The build helper targets macOS only and temporarily uses the Standalone Mono backend to avoid the current Mac IL2CPP `Unity.Scripting` reference failure. It restores the previous build target and backend afterward.

Generated `.BEE` files, build reports, and password sidecars are ignored by Git. Keep the password sidecar local and never commit or paste its contents into an issue or documentation.
