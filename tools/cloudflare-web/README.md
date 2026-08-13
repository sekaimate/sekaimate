# Cloudflare Web配信

Node.js 24とCloudflareへのログイン済みWranglerを使用し、既存のUnity WebGL成果物をR2へアップロードしてWorkerから配信します。

```sh
cd tools/cloudflare-web
pnpm install
pnpm run publish -- \
  --domain example.com \
  --build-dir /absolute/path/to/web-build
```

Worker名とR2バケット名はドメインから生成されます。明示する場合は`--worker-name`と`--bucket-name`を追加します。スクリプトはR2バケット作成、全ファイルのContent-TypeとContent-Encoding設定、アップロード、WorkerとCustom Domainのデプロイを順番に実行します。
