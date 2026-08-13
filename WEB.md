# Webビルド

Unity 6000.5.2f1のWebGLモジュールを導入したmacOS環境で、リポジトリのルートから次を実行します。

```sh
./tools/build-web.sh
```

既定の出力先は`Build/Web`です。別の空の出力先は第1引数で指定できます。

```sh
./tools/build-web.sh /absolute/path/to/output
```

Unity Hub以外へUnityを導入している場合は、実行ファイルを`BASIS_UNITY_EXECUTABLE`で指定します。ビルド入口はWebGLへ切り替えてからAddressablesとRelease Playerを生成し、配布に必要なHTML、JavaScript、WebAssembly、データ、TemplateData、Addressablesを検査します。不完全な出力は成功扱いになりません。

## ブラウザでの実行

ビルドにWebサーバーは不要です。生成後の成果物をブラウザで実行するときだけHTTPまたはHTTPSで配信します。

現在のビルドはGzip圧縮を使用します。`.gz`ファイルには`Content-Encoding: gzip`が必要です。また、WebAssemblyには`Content-Type: application/wasm`、JavaScriptには`Content-Type: application/javascript`、`.data`と`.bundle`には`Content-Type: application/octet-stream`を設定します。圧縮済みファイルの配信要件は[Unity公式ドキュメント](https://docs.unity3d.com/6000.5/Documentation/Manual/webgl-deploying.html)に基づいています。

対応ブラウザはWebGL 2、WebAssembly、64bitをサポートするデスクトップ版Chrome、Firefox、Safari、Edgeです。詳細は[Unityのブラウザ互換性](https://docs.unity3d.com/6000.5/Documentation/Manual/webgl-browsercompatibility.html)を参照してください。

## Prop BEEのWebGL検証

WebGL用のProp BEEは、保存済みのシーンを開いた状態で次のrunnerから生成できます。出力先は空のディレクトリを指定します。

```sh
"$BASIS_UNITY_EXECUTABLE" \
  -batchmode \
  -projectPath "$PWD/Basis" \
  -buildTarget WebGL \
  -executeMethod BasisPropBeeWebBuildRunner.RunFromCommandLine \
  -basisPropBeeOutput /absolute/path/to/empty/prop-bee \
  -basisPropBeePassword BasisWebPropE2EPassword0123456789 \
  -logFile -
```

runnerは`SimpleSeat.prefab`を検証用Propとして使用し、生成物がWebGLの`GameObject`セクションを1つだけ持つこと、メタデータに`BasisProp`が含まれること、BEEのファイル長がconnectorとセクションの構造に一致することを検査します。生成処理は元Prefabを変更しません。

生成したBEEをWeb成果物と同一オリジンで配信し、Playwright MCPの`browser_run_code`へ[検証関数](tools/playwright/verify-prop-bee.mjs)と次の呼び出しを渡します。サーバーはこの手順では起動しません。

```js
return await verifyPropBee(page, {
  applicationUrl: "http://127.0.0.1:4173/?prop-bee-e2e=1",
  beeUrl: "http://127.0.0.1:4173/BEE/web-prop-verification.BEE",
  password: "BasisWebPropE2EPassword0123456789",
  screenshotPath: "/absolute/path/to/prop-bee-spawned.png",
});
```

この検証は960×600のCanvasでLibraryを開き、BEEを追加し、Propカードの`Spawn`を実行して配置を確定します。成功条件はBEEのHTTP Range応答が`206`であること、`Library provider successfully created item`が出力されること、ブラウザーのコンソールエラーが0件であることです。UIレイアウトを変更した場合は`coordinates`で座標を明示的に上書きします。
