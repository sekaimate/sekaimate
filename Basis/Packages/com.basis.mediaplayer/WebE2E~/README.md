# Web Media E2E

This test exercises the production `BasisMediaPlayer` WebGL path. The runtime fixture is compiled only by `BasisHeadlessBuild.BuildWebE2E`; release WebGL and native builds do not include it.

Build the WebGL development player with `BasisHeadlessBuild.BuildWebE2E`, then run:

```sh
BASIS_WEB_BUILD_PATH=/absolute/path/to/web-build pnpm install --frozen-lockfile
BASIS_WEB_BUILD_PATH=/absolute/path/to/web-build pnpm test
```

The test uses Chromium's VP8/Opus support to generate an eight-second media fixture in the browser. `app.lvh.me` and `media.lvh.me` resolve to loopback but remain distinct origins. The media response permits only the app origin with `Access-Control-Allow-Origin`, and the product backend requests it with `crossOrigin = "anonymous"`. The development fixture calls `BasisMediaPlayer.Pause`, `Seek`, and `Play`, so playback controls are exercised through the product facade.

Chromium is started with `--autoplay-policy=no-user-gesture-required`. This test verifies playback, texture upload, and the WebAudio route; it does not verify the browser's user-gesture autoplay prompt. HTTPS deployments must serve media over HTTPS because browsers block mixed content.
