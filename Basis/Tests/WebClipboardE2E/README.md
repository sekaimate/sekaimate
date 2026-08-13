# Web Clipboard E2E

このテストはWebGLのDevelopment Buildを使い、実際のクリップボード転送を双方向で検証します。

Unityビルドと静的ファイルサーバーは事前に起動してください。このテスト自体はUnityをビルドせず、サーバーも起動しません。

```sh
pnpm install --frozen-lockfile
BASIS_WEB_BUILD_URL=http://localhost:4173 pnpm test
```

ビルドURLにはsecure contextが必要です。HTTPSと`http://localhost`などのループバックoriginが該当します。テストは対象originだけにクリップボード権限を付与し、transient user activationを維持するため実際にボタンをクリックします。転送結果は`navigator.clipboard`を通じてブラウザーのシステムクリップボードで確認します。
