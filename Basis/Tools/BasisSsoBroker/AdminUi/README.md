# Basis Control Plane Admin UI

組織共通の SSO 設定と会議の招待リンクを扱う、元来 C# broker 用の React 管理画面です。
C# broker は現在も単体 Basis Server/Compose で現役です。Concierge では管理画面部分を暫定再利用し、
API の Bearer admin token、会議室の作成・削除・provisioning polling、静的サーバー管理、
サーバー登録リンク、WebGL URI 設定に対応します。

参加者向け `join.tsx` と OAuth の web endpoint は C# broker の契約（`/details`、`/web-config`、
`/web-manifest`、`/web-oidc`）に依存します。Concierge の join API は別契約のため、UI 全体が両 backend
で完全互換という意味ではありません。将来 Concierge 専用 UI に分離する場合は `concierge/adminui` へ
移動し、Go イメージへの build/assets 同梱と `ADMIN_UI_DIR` 設定を追加します。

## 開発

```sh
cd Tools/BasisSsoBroker/AdminUi
direnv allow # 初回だけ。direnv がない場合は nix develop で入る
vp install    # 初回だけ
vp run dev
```

`http://localhost:5173/` を開きます。開発サーバーは
`https://localhost` の broker API へプロキシします。ローカルの自己署名証明書を
開発ブラウザで一度信頼しておいてください。

別の broker を使う場合は起動時に URL を渡せます。

```sh
BASIS_SSO_BROKER_URL=https://sso.example.org vp run dev
```

```sh
vp run typecheck
vp run build
```

`dist/` は静的サイトです。Docker では `basis-sso-admin` の Nginx gateway が
これを `/admin/` に配信し、同じオリジンの `/api/` を broker に転送します。

Go Concierge に同梱して確認する場合は、まず `vp build` を実行し、Concierge 起動時に
`ADMIN_UI_DIR=$PWD/dist` を指定します。Concierge は `/admin/` で SPA を配信し、
`/api/*` を管理 API に内部転送します。管理画面のログイン欄には
`BASIS_SSO_ADMIN_TOKEN` の値を入力してください。

```sh
cd Packages/com.basis.server/Docker
docker compose up -d --build

cd sso
docker compose up -d --build
```

ローカルでの管理URLは `https://localhost/admin/` です。Nginx が TLS と静的 UI を担当し、
管理 API と admission endpoint を同一オリジンで broker に転送します。初回だけ
`sso/README.md` の手順でローカル CA を macOS に信頼させてください。
