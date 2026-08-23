# Webビルド

Unity 6000.5.3f1のWebGLモジュールを導入したmacOS環境で、リポジトリのルートから次を実行します。

```sh
./tools/build-web.sh
```

既定の出力先は`Build/Web`です。別の空の出力先は第1引数で指定できます。

```sh
./tools/build-web.sh /absolute/path/to/output
```

既存の指定出力ディレクトリだけを置き換えます。Unityの`Library`キャッシュは削除しません。同じ出力先を繰り返し使うことで、古いWebGL成果物を残しません。

Unity Hub以外へUnityを導入している場合は、実行ファイルを`BASIS_UNITY_EXECUTABLE`で指定します。ビルド入口はWebGLへ切り替えてからAddressablesとRelease Playerを生成し、配布に必要なHTML、JavaScript、WebAssembly、データ、TemplateData、Addressablesを検査します。不完全な出力は成功扱いになりません。

## 高速な開発用ビルド

開発中は、Addressablesを毎回再ビルドしないDevelopment Buildを使用できます。Addressablesのアセットを変更した場合だけ通常ビルドを実行してください。

初回だけ通常ビルドを実行してAddressablesの成果物を作成します。

```sh
mise run web:release
```

```sh
mise run web:build
mise run web:serve
```

`mise`を使う場合は、次のタスクも利用できます。

```sh
mise run web:build # 高速開発ビルド
mise run server:up     # WebSocket対応Basis Serverを起動
mise run web:serve     # Web版とBEEを配信
mise run server:logs   # Serverログを表示
mise run server:down   # Serverを停止
```

Concierge と HTTPS Admin gateway を Docker で起動する場合は、先に
`Basis Server/Docker/sso/.env` と `broker/appsettings.json` を用意します。fresh clone からの完全な
手順、standalone/minikube/TLS/OIDC の検証は [`docs/concierge/operations.md`](docs/concierge/operations.md) を参照してください。

```sh
mise run sso:up
```

管理画面は`https://127.0.0.1:5081/admin/`です。Admin token は `.env` の
`BASIS_SSO_ADMIN_TOKEN` に生成した値を設定します。停止・ログ確認は次のタスクを使います。

```sh
mise run sso:logs
mise run sso:down
```

Admin Consoleだけを開発サーバーで起動する場合は`mise run sso:dev`を使い、`http://localhost:5173/admin/`を開きます。必要なpnpm依存関係とConcierge/HTTPS gatewayはタスクが先に起動します。

全部を起動する場合は`mise run local:up`を使用します。WebGL成果物が最新ならビルドをスキップし、変更がある場合だけ再ビルドします。配信中はそのターミナルを終了せず、ブラウザーで`http://127.0.0.1:4173/`を開いてください。

`sso:dev` はConciergeとHTTPS Admin gatewayも自動起動します。終了時は`mise run local:down`でConciergeとBasis Serverを停止できます。Web配信のターミナルは`Ctrl-C`で終了してください。

開発用の出力先は`Build/WebDev`です。既存の出力を削除せずに再利用し、ワールドBEEはGit管理外の`local/BEE/world.BEE`から自動的に配置します。BEEをビルド出力の外に置くため、通常のリリース用ビルドでも失われません。通常のリリース用ビルドは従来どおり`./tools/build-web.sh`を使用します。

`mise run server:up` はこのBEEをBasis Serverの`initialresources`へ登録します。これはBasis Serverが起動時にアクティブワールドとしてロードし、後から参加するクライアントにも通知する標準経路です。BEEにパスワードがある場合は`.env.local`に`BASIS_WORLD_BEE_PASSWORD=...`を設定してください。URLを変更する場合は`BASIS_WORLD_BEE_URL`で上書きできます。

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

この検証は1280×720のブラウザー内に表示された960×600のCanvasでLibraryを開き、BEEを追加し、Propカードの`Spawn`を実行して配置を確定します。その後ページを再読込し、永続化されたLibraryエントリから同じPropをもう一度Spawnします。成功条件はBEEのHTTP Range応答が`206`であること、復号後の`Attempting Asset Bundle Load`、instantiate後の`Library provider successfully created item`、再読込後の`Process On Disc Meta Data Async`が出力されること、ブラウザーのコンソールエラーが0件であることです。UIレイアウトを変更した場合は`coordinates`で座標を明示的に上書きします。

## BEEのURL参加とキャッシュ

URL参加では、サーバーがアクティブな`world.BEE`をクライアントへ送信し、WebGLクライアントが次の順番で処理します。

```text
サーバーからリソース受信
→ BEEをHTTP Rangeで取得
→ コネクタとWebGL用セクションを復号
→ AssetBundleを生成
→ キャッシュメタデータを保存
→ シーンを追加ロード・アクティブ化
```

### 今回の不具合

BEEファイル自体やHTTP`206 Partial Content`が原因ではありませんでした。WebGLのキャッシュ処理に、次の2つの問題がありました。

- 初回取得後のメタデータ保存で`File.WriteAllBytesAsync`を使っていた。WebGLのIDBFS上ではこの非同期書き込みが完了せず、シーンロードへ進まないことがある。
- キャッシュ済みBEEを読む経路で`ConfigureAwait(false)`を使っていた。WebGLのUnity同期コンテキストへ戻れず、コネクタ処理後の継続が止まることがある。

対策として、WebGLのメタデータ保存は同期`File.WriteAllBytes`を使用し、キャッシュBEEのコネクタ復号後はUnityの同期コンテキストを維持します。デスクトップ版の非同期ファイル処理は変更していません。

### 再発時の確認ログ

URL参加または手動Connect後、Development Consoleで次のログを確認します。

```text
Received server resource
BEE cache decision
Processing on-disk meta
Successfully processed the Connector and related files.
AssetBundle generation returned
Preparing AssetBundle scene load
Scene Load From Server Complete
```

`Processing on-disk meta`で止まる場合は、キャッシュBEEの読み込み・復号経路を確認します。`Scene Load From Server Complete`まで出れば、BEEのロードは完了しています。
