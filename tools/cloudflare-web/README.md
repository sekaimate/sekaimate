# Cloudflare Web配信

Node.js 24とCloudflareへのログイン済みWranglerを使用し、既存のUnity WebGL成果物をR2へアップロードしてWorkerから配信します。

```sh
cd tools/cloudflare-web
pnpm install
pnpm run publish -- \
  --domain example.com \
  --build-dir /absolute/path/to/web-build
```

Worker名とR2バケット名はドメインから生成されます。明示する場合は`--worker-name`と`--bucket-name`を追加します。スクリプトはR2バケット作成、全ファイルのContent-TypeとContent-Encoding設定、アップロード、前回のデプロイにだけ存在したファイルの削除、WorkerとCustom Domainのデプロイを順番に実行します。Unity本体のファイル名は`Build/basis.*`へ固定され、次回のデプロイで置き換わります。

WebGL SSOのredirect URIには、同じドメインの`/sso-callback`を登録します。WorkerがこのパスだけをOAuth callbackとして処理し、それ以外の静的ファイルはR2から配信します。

Workerだけを更新するときは`--worker-only`を追加します。WASM、データ、バンドルはブラウザーで1日、Cloudflareエッジで1年キャッシュされます。HTMLとAddressablesカタログには短いキャッシュ期間を適用します。
