# Web Persistence E2E

Unity WebGLのDevelopment Buildが起動済みであることを前提に、同一origin内で保存と再読込を検証します。

```bash
BASIS_WEB_BUILD_URL=http://127.0.0.1:4173 pnpm test
```

テストは新しいブラウザーコンテキストでseedを実行し、対象ファイルがIndexedDBへ保存されたことを確認してからverifyへ切り替えてページを再読込します。Unityや配信サーバーはこのテストから起動しません。
