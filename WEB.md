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
