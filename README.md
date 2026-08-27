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
5. 開発用TLS証明書とSecretを作成
6. Kubernetesマニフェストを適用（WebGLイメージはGHCRから取得）
7. ConciergeとWebGLのport-forwardを開始

WebGLイメージは`ghcr.io/sekaimate/concierge-web:dev`をpullします。この手順にUnityは不要です。
別のイメージを使う場合は`.env.local`の`WEB_IMAGE`で上書きします。

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

#### WebGLイメージの公開

WebGLクライアントを更新したときは、Unityを導入した環境で次を実行し、GHCRへpushします。

```sh
podman login ghcr.io   # dockerの場合は docker login ghcr.io。write:packagesのトークンを使用
mise run web:publish
```

このタスクはDevelopment WebGLビルドを作成し、`linux/amd64`と`linux/arm64`のイメージを
`ghcr.io/sekaimate/concierge-web:dev`へpushします。コンテナエンジンはPodmanとDockerのどちらでも動作し、
両方ある環境ではPodmanを使います。`CONTAINER_ENGINE`で明示的に選べます。既存の`Build/Web`を
再利用する場合は`./tools/publish-web-image.sh --skip-build`を使います。初回のpush後は、GitHubのPackages設定で
パッケージをpublicに変更してください。publicにすると、イメージへ同梱されるBEEも誰でも取得できます。
公開範囲を絞る場合はパッケージをprivateにし、imagePullSecretを別途設定してください。

### OCI / k3s での公開デプロイ

Minikubeの`k8s:up`はローカル開発用で、すべてを`127.0.0.1`のport-forwardで繋ぎます。インターネットへ公開する場合は、
Linuxサーバー上のシングルノードk3sへデプロイし、CaddyでTLSを終端します。Agonesの動的ポートはノードへ直接
バインドされるため、ノードがホスト自身になるk3sを使います。

#### 前提

- Linuxのインスタンス（OCIのAmpere A1など）と、停止・起動で変わらない予約パブリックIP
- `concierge`、`web`、`rooms`の3つのAレコードをそのIPへ向けたドメイン

k3s、Agones、CaddyはすべてこのあとのコマンドがインストールするのでOS側の準備は不要です。Caddyのバイナリは
`mise.toml`の`[tools]`から入ります。

#### 設定

`.env.local`にドメインとBEEのパスワードを設定します。サブドメインは自動で組み立てます。

```sh
BASIS_PUBLIC_DOMAIN=example.com
BASIS_WORLD_BEE_PASSWORD=...
CADDY_ADMIN_ALLOW_IPS=203.0.113.10
```

#### 起動

```sh
mise install
mise run k3s:up
```

`k3s:up`が次を順番に実行します。

1. k3sをTraefikなしでインストール（80番と443番はCaddyが使うため）
2. Agones v1.60.0をインストール
3. Caddyをsystemdサービスとして導入し、ドメインから生成したCaddyfileを反映
4. ConciergeとBasis Serverのイメージをインスタンス上でビルドしてk3sのcontainerdへ取り込み
5. Secretとブラウザ向けエンドポイントのConfigMapを作成
6. roomsの証明書を待って`basis-web-tls`Secretへ同期
7. マニフェストを適用（WebGLイメージはGHCRから取得）

初回は`local/concierge/appsettings.public.json`がドメイン置換済みで生成されるので、GoogleのOAuth設定を記入してから
再実行してください。Google側のredirect URIには`https://web.example.com/sso-callback`を登録します。

個別に実行する場合は`caddy:install`、`caddy:apply`、`caddy:sync-cert`、`k3s:cluster`も使えます。

証明書はCaddyが取得し、GameServerは`basis-web-tls`をマウントして自身でwssを終端します。会議ごとにポートが変わり、
固定ポートのreverse proxyに載せられないためです。更新後は`mise run caddy:sync-cert`を実行してください。実行中の会議は
作成時の証明書を使い続け、次に作る会議から新しい証明書を使います。

証明書の発行にはDNSの反映と80番の開放が必要です。まだ通っていない場合、`k3s:up`は警告を出して残りの適用を続けます。
ポートを開けたあとに`k3s:up`を再実行すれば同期されます。

#### ネットワーク

OCIはセキュリティリスト（またはNSG）とOSファイアウォールの両方で許可が必要です。片方だけでは通りません。

| ポート | プロトコル | 送信元 | 用途 |
| --- | --- | --- | --- |
| 22 | TCP | 管理者のIP | SSH |
| 80 | TCP | 0.0.0.0/0 | ACME HTTP-01とHTTPSへのリダイレクト |
| 443 | TCP | 0.0.0.0/0 | ConciergeとWebGL |
| 7000-8000 | TCP | 0.0.0.0/0 | GameServerのwss |
| 7000-8000 | UDP | 0.0.0.0/0 | ネイティブクライアント |

```sh
sudo firewall-cmd --permanent --add-port=80/tcp --add-port=443/tcp
sudo firewall-cmd --permanent --add-port=7000-8000/tcp --add-port=7000-8000/udp
sudo firewall-cmd --reload
```

ConciergeとWebGLのNodePort（既定30080と30173）はループバック経由でCaddyが使うだけなので、公開しません。

#### 停止と確認

```sh
mise run k3s:status
mise run k3s:down
```

`k3s:down`はConciergeのDeployment、GameServer、Secret、ConfigMapを削除します。k3sとAgonesは残ります。

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
