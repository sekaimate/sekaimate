# Basis Control Plane Admin UI

組織共通の SSO 設定と会議の招待リンクを扱う React 管理画面です。認証・admission ticket
発行は `../` の .NET broker が担当し、このアプリは control-plane API を表示・編集します。
現行の Docker 開発構成では、Compose が起動する `local` の一会議室を表示し、その参加 URL
を発行できます。Kubernetes の会議作成・停止・スケールはまだこの UI の対象ではありません。

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

```sh
cd Packages/com.basis.server/Docker
docker compose up -d --build

cd sso
docker compose up -d --build
```

ローカルでの管理URLは `https://localhost/admin/` です。Nginx が TLS と静的 UI を担当し、
管理 API と admission endpoint を同一オリジンで broker に転送します。初回だけ
`sso/README.md` の手順でローカル CA を macOS に信頼させてください。
