# WebXR input

The WebGL player uses the browser-standard `navigator.xr` API for immersive input. Native players continue to use the existing OpenXR backend.

## Runtime requirements

- The page must be served in a secure context. `https://` is required outside the `localhost` development exception.
- The browser or an installed extension must expose `navigator.xr` and support an `immersive-vr` session.
- The user must select the `Enter XR` button. Browsers require this user activation before `requestSession()`.
- Hand tracking is optional. Controllers remain available when the runtime does not grant the `hand-tracking` feature.

Meta Immersive Web Emulator is the supported desktop test runtime. It provides the same `navigator.xr` interface consumed by the player; the production player does not import or call extension-specific APIs.

## Development diagnostics

Append `basisWebXRE2E=1` to expose the Basis device state under `window.basisWebXR.basisState`. The raw browser snapshot remains available under `window.basisWebXR.snapshot`.

The diagnostics are intended for Development WebGL validation and include registered head and hand devices, hand tracking, pinch, trigger, and both controller axes.

Specifications:

- [WebXR Device API](https://www.w3.org/TR/webxr/)
- [WebXR Hand Input Module](https://www.w3.org/TR/webxr-hand-input-1/)
- [WebXR Gamepads Module](https://www.w3.org/TR/webxr-gamepads-module-1/)
