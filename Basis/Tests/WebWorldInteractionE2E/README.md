# World interaction WebGL E2E

このPlaywright suiteは、WebGL Development Buildで実際のworld BEEを読み込み、本番interaction componentをブラウザー入力で検証します。

## Desktop interaction

次の環境変数が必須です。

- `BASIS_WEB_BUILD_URL`: `pnpm`などで配信しているWebGL buildのURL
- `BASIS_WORLD_INTERACTION_BEE_URL`: fixtureを含むworld BEEのURL
- `BASIS_WORLD_INTERACTION_BEE_PASSWORD`: world BEEのpassword

```sh
pnpm install --frozen-lockfile
pnpm test
```

## WebXR hand DirectTouch

WebXR hand testには、Meta公式Immersive Web Emulatorの`v2.0.0-alpha`が必須です。stable版`v1.3.0`ではなく、IWER runtime、DevUI overlay、動的XR inputを備えるalpha版を使用します。

1. [Meta公式v2.0.0-alpha release](https://github.com/meta-quest/immersive-web-emulator/releases/tag/v2.0.0-alpha)からsource archiveを取得します。
2. [公式README](https://github.com/meta-quest/immersive-web-emulator#develop)に従って`pnpm install`と`pnpm build`を実行します。
3. `manifest.json`と`build/iwe.min.js`を含むunpacked extension directoryを`BASIS_IWE_EXTENSION_PATH`へ設定します。
4. ChromeまたはChromiumをheaded modeで実行できる環境を用意します。extensionのminimum Chrome versionは96です。

Playwrightはpersistent contextへこのunpacked extensionだけを読み込み、extension service workerの`chrome.scripting` APIで対象originへ公式runtimeを注入します。hand mode切替、hand pose、位置、pinchは公式DevUIを操作します。テスト用のhand inputをUnityへ直接注入しません。

`BASIS_IWE_EXTENSION_PATH`が未設定の場合だけ、WebXR hand testは理由を表示してskipします。設定済みの場合は、extension version、runtime injection、XR session、hand input、DirectTouch hover、press、releaseのいずれかが成立しなければ失敗します。desktop inputへの代替は行いません。

公式資料:

- [Immersive Web Emulator](https://github.com/meta-quest/immersive-web-emulator)
- [v2.0.0-alpha release](https://github.com/meta-quest/immersive-web-emulator/releases/tag/v2.0.0-alpha)
- [Immersive Web Emulation Runtime](https://github.com/meta-quest/immersive-web-emulation-runtime)
