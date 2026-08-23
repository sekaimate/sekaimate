# Concierge Admin UI

Concierge の管理画面と参加者向け参加ページです。Admin 画面は `/api/*` 経由で同じ
Concierge の管理 API を呼び出し、参加ページは Concierge の `/join/{token}/manifest`
契約だけを使用します。旧 broker 実装の内部構造や専用 runtime には依存しません。

## 開発

```sh
cd concierge/adminui
pnpm install --frozen-lockfile
CONCIERGE_URL=http://127.0.0.1:5080 pnpm run dev
```

`http://localhost:5173/admin/` を開きます。Vite は `/api/*`、`/health`、`/join/*` を
Concierge にプロキシします。別の WebGL 配信先を使う場合は、次のように指定します。

```sh
VITE_WEB_CLIENT_ORIGIN=http://127.0.0.1:4173 pnpm run dev
```

Admin UI のログイン欄には Concierge の `BASIS_SSO_ADMIN_TOKEN` を入力してください。
参加ページは `/join/{token}/` で、Go の manifest を取得して native deep link を組み立てます。
WebSocket と Server Info URI が manifest に含まれる場合は、`VITE_WEB_CLIENT_ORIGIN` の
WebGL ページへ自動参加するリンクも表示します。

## ビルド

```sh
pnpm run typecheck
pnpm run test
pnpm run build
```

Vite の build output は `dist/` です。`dist/` は生成物なので Git に追加しません。
Concierge の Dockerfile はこのディレクトリを build stage でビルドし、runtime image の
`/adminui` に同梱します。

```sh
cd concierge
docker build -t concierge:local .
```

runtime image の `ADMIN_UI_DIR=/adminui` が静的ファイルの配信先です。開発時に別の
build output を使う場合だけ `ADMIN_UI_DIR=/path/to/dist` で上書きできます。
