# SekaiMate

SekaiMateは、[Basis](https://github.com/BasisVR/Basis)を基盤にしたソーシャルVRプロジェクトです。
Unityクライアント、Basis Server、SSOと会議室管理を担当するConcierge、ブラウザ向けWebGLクライアントで構成されています。

## このプロジェクトで追加しているもの

### SSOによるサーバー認証・認可

Conciergeが会議室ごとの認証情報と入場チケットを管理します。

- GoogleなどのOIDCプロバイダーによるログイン
- 許可ドメインによる組織アカウント制限
- 会議室ごとの参加リンクと有効期限付きトークン
- Basis ServerへのSSO admission ticket連携
- サーバーごとの認証必須設定
- Webクライアント用のOIDC token relay

認証情報や署名鍵は、クライアントや参加URLへ直接埋め込まず、Conciergeと対象のBasis Server間で管理します。

### Web対応

BasisのWebGLクライアントをDevelopment WebGLビルドとして作成し、BEEワールドをブラウザへ配信します。

- `Build/Web/`を固定の開発用ビルド出力先として使用
- BEEファイルとAddressablesを同一のWebサーバーから配信
- WebSocketとserver-infoによるブラウザ接続
- 会議URLからの自動参加
- Unity WebGLの圧縮アセット、WASM、Rangeリクエストに対応した配信サーバー

### Kubernetes / Agones対応

ConciergeがKubernetes上のAgones GameServerを会議室単位で作成・削除します。

- 会議室ごとのBasis Server用Secretを生成
- Agones GameServerの動的ポートを利用
- GameServerのReady状態を待って参加URLを発行
- Concierge再起動時にGameServerとの整合性を確認
- Minikube + Podmanによるローカル検証

## 開発環境

ツールのバージョンはルートの`mise.toml`で管理します。

```sh
mise install
```

### Unity / WebGL

プロジェクトで指定されているUnityバージョンで`Basis/`を開きます。
WebGL開発時は、既存キャッシュを利用するDevelopmentビルドを使用します。

```sh
mise run web:build
mise run web:serve
```

開発用の固定出力先は`Build/Web/`です。ワールドのBEEファイルも同じWebサーバーから配信されます。

個別のコンポーネントだけを起動する場合は次を使います。

```sh
mise run web:serve
mise run sso:up
mise run sso:dev
mise run server:up
```

`sso:dev`はConcierge gatewayとAdmin ConsoleのVite開発サーバーを起動します。
ローカルHTTPS用CAの確認とmacOSのキーチェーンへの登録は次で行います。

```sh
mise run sso:ca
mise run sso:trust-ca
```

### Minikube / Agones（標準の開発環境）

#### 初回設定

ルートのテンプレートからローカル設定を作成し、BEEの解除パスワードを入力します。

```sh
cp .env.template .env.local
${EDITOR:-vi} .env.local
```

`BASIS_WORLD_BEE_PASSWORD` は必須です。`local/BEE/world.BEE` が存在することも確認してください。
Google SSOを使う場合は、`local/concierge/appsettings.minikube.json` のGoogle Web OAuth Client ID・Secret、
Token endpoint、JWKS URI、許可ドメインを設定します。

#### 起動

Minikube環境をまとめて起動します。

```sh
mise run k8s:up
```

このタスクは次を順番に実行します。

1. 利用可能なコンテナドライバー（PodmanまたはDocker）でMinikubeを起動
2. Agones v1.60.0をインストール
3. Conciergeイメージをビルド
4. Basis Serverイメージをビルド
5. Development WebGLイメージをビルド
6. 開発用TLS証明書とSecretを作成
7. Kubernetesマニフェストを適用
8. ConciergeとWebGLのport-forwardを開始

起動後のURLは次のとおりです。

```text
Admin Console: http://127.0.0.1:15080/admin/
WebGL client:  http://127.0.0.1:4173/
```

初回起動時には、次のローカル設定ファイルがサンプルから生成されます。

```text
local/concierge/appsettings.minikube.json
```

SSOを試す前に、Google OAuthのClient ID・Secretなどをこのファイルへ設定してください。
このファイル、生成されたTLS秘密鍵、管理者トークンはコミットしません。

Admin Consoleの管理トークンは、Minikubeの `basis` namespace にある
`concierge-admin` Secretの `token` キーに保存されます。ログイン画面へ入力する
トークンは、次のコマンドで取得できます。

```sh
kubectl -n basis get secret concierge-admin \
  -o jsonpath='{.data.token}' | base64 -D
```

macOSのクリップボードへ直接コピーする場合は次を使います。

```sh
kubectl -n basis get secret concierge-admin \
  -o jsonpath='{.data.token}' | base64 -D | tr -d '\n' | pbcopy
```

便利なコマンドです。

```sh
mise run k8s:status
mise run k8s:down
```

`k8s:down`はConciergeのDeployment、GameServer、開発用Secret、port-forwardを停止します。
Minikubeクラスタ自体は残るため、次回の起動を短縮できます。

起動時にターミナルへもAdmin tokenが表示されます。表示されない場合は上記のSecret取得コマンドを使用してください。

## ディレクトリ構成

```text
Basis/                 UnityクライアントプロジェクトとBasisパッケージ
Basis Server/          Basis Server本体、Dockerfile、Compose定義
concierge/             Go製SSO・入場審査・会議室管理サービス
  adminui/              Concierge用Cloudscape Admin Console
  cmd/                  Conciergeのエントリーポイント
  internal/             Go実装とテスト
  deploy/               Concierge用Kubernetesマニフェスト
  web.Dockerfile        WebGL静的配信イメージ
tools/                 ビルド、配信、Compose、Kubernetes補助スクリプト
docs/concierge/        設計、実装、運用、検証ドキュメント
local/                 git管理外のローカル設定と秘密情報
Build/                 git管理外のUnity/WebGLビルド成果物
```

## ドキュメント

- [Concierge設計](docs/concierge/design.md)
- [Concierge実装](docs/concierge/implementation.md)
- [Concierge運用手順](docs/concierge/operations.md)
- [Minikube検証記録](docs/concierge/verification.md)

## ライセンスと第三者コンポーネント

Basis由来のコードはMIT Licenseです。著作権表示とライセンス本文は[`LICENSE`](LICENSE)を確認してください。

各Unityパッケージに含まれる依存コンポーネントのライセンス・NOTICE・商標表示は、それぞれのパッケージディレクトリ内に保持しています。
配布時もこれらの表示を削除しないでください。

BasisおよびBasis関連の名称・ロゴの利用については[`TRADEMARK.md`](TRADEMARK.md)を確認してください。
