# concierge 検証ドキュメント(phase 3: minikube + Agones)

最終更新: 2026-08-23

`design.md` §10.3 に定めた手順に沿って、実際の minikube + Agones 環境に対して concierge の結合確認を行った記録。
実行環境は macOS arm64(Podman ドライバー)。事前修正(§2)・検証項目ごとの結果(§4)・検証中に見つかった不具合と
その修正(§5)・basis-server スタブによる代替とその限界(§6)・後片付け手順(§7)・未検証の項目(§8)の順にまとめる。

## 1. 検証環境

| 項目 | バージョン / 値 |
|---|---|
| OS | macOS (Darwin 26.5.1), arm64 |
| コンテナランタイム | Podman 5.8.2(`/opt/podman/bin/podman`)、`podman-machine-default`(libkrun、6 vCPU / 11.18GiB / 93GB disk、既存のマシンをそのまま起動) |
| minikube | v1.38.1、`--driver=podman --cpus=4 --memory=6g --container-runtime=containerd` |
| Kubernetes | client v1.36.4 / server v1.35.1(minikube v1.38.1 の既定プリロード) |
| Agones | v1.60.0(`release-1.60.0` の静的 `install/yaml/install.yaml`、`agones-system` 名前空間) |
| Go | go1.26.5(コンテナビルド内)/ ホスト側 go1.26.5 darwin/arm64 |
| helm | 未導入。Agones は `kubectl apply`(静的マニフェスト)でインストールした。 |

上表の go/kubectl/minikube のバージョンは、本検証後に追加した `concierge/mise.toml`(mise 設定、詳細は
`implementation.md` §2.1)にも同一の値で固定している。本検証自体は mise を導入する前に実施したため、
以下のセットアップ手順は素の CLI コマンドのまま記録している。

## 2. セットアップ手順

実際に実行したコマンドを、実行順に示す(値は本検証時点のもの)。

```sh
# podman マシンの起動(既存のマシンを使用。新規作成する場合は
# `podman machine init --cpus 4 --memory 8192 --rootful` 相当のリソースを与える)
export PATH="/opt/podman/bin:$PATH"
podman machine start podman-machine-default

# minikube クラスタ起動
minikube start --driver=podman --cpus=4 --memory=6g --container-runtime=containerd

# Agones v1.60.0 インストール(§5.1 参照: 素の適用では失敗するため後述の回避が必要)
kubectl create namespace agones-system
curl -sL https://raw.githubusercontent.com/googleforgames/agones/release-1.60.0/install/yaml/install.yaml \
  -o agones-install.yaml
grep -v "x-kubernetes-patch-strategy:\|x-kubernetes-patch-merge-key:" agones-install.yaml \
  > agones-install-fixed.yaml
kubectl apply --server-side --force-conflicts -f agones-install-fixed.yaml

# concierge イメージのビルド(minikube 内部の buildkit へ、レジストリ不要)
cd concierge
minikube image build -t concierge:dev .

# 実 Basis Server image の build（現在の再現手順。過去の stub は使用しない）
cd "../Basis Server"
minikube image build -t basis-server:dev -f Docker/Dockerfile .
cd ..

# 名前空間・Secret・RBAC・Service を適用
kubectl apply -f concierge/deploy/00-namespace.yaml
kubectl create secret generic concierge-config -n basis --from-file=appsettings.json=./appsettings.json
kubectl create secret generic concierge-admin -n basis --from-literal=token="$(openssl rand -base64 32)"
kubectl apply -f concierge/deploy/10-rbac.yaml
kubectl apply -f concierge/deploy/30-service.yaml

# committed minikube overlay が image/imagePullPolicy/BASIS_SERVER_IMAGE を設定する。
kubectl apply -f concierge/deploy/00-namespace.yaml
kubectl apply -f concierge/deploy/10-rbac.yaml
kubectl apply -f concierge/deploy/20-deployment-dev.yaml
kubectl apply -f concierge/deploy/30-service.yaml

kubectl port-forward -n basis svc/concierge 15080:5080
```

`appsettings.json`(Secret `concierge-config` の中身)は次の内容で検証した。実際の IdP を使った OIDC 疎通は
スコープ外(§8)なので、`Issuer`/`JwksUri` はダミーの値。

```json
{
  "Broker": {
    "AdminTokenEnvironmentVariable": "BASIS_SSO_ADMIN_TOKEN",
    "Organization": {
      "DisplayName": "concierge phase-3 verification",
      "Providers": [
        {
          "Id": "dummy",
          "Label": "Dummy OIDC (verification only)",
          "Issuer": "https://issuer.example.invalid",
          "Audience": "concierge-verification",
          "JwksUri": "https://issuer.example.invalid/jwks"
        }
      ]
    }
  }
}
```

## 3. Step 1 の事前修正(コード)

`POST /admin/meetings` が `host` の有無に関わらず常に `Provisioner.Create` を呼んでいた挙動(design.md §4.2 の
「concierge 管理の部屋の場合」という前提と矛盾していた)を修正した。詳細は `implementation.md` §9.10 を参照。

- `host` を明示指定した場合は `Provisioner.Create`/`Provisioner.Delete` を一切呼ばず、即座に `"ready"` になる
  (C# broker と同じ挙動)。
- `controlplane.MeetingRecord` に `Managed bool`(JSON キー `Managed`、`omitempty`)を追加し、どちらの経路で
  作成された会議かを永続化した。既存の `control-plane.json` にこのキーが無いレコードは `Managed=false` として
  読み込まれる。
- `DELETE /admin/meetings/{id}` は `Managed=true` の会議に対してのみ `Provisioner.Delete` を呼ぶ。
- `Manager.Reconcile` も `Managed=false` の会議は GameServer が無くても `"failed"` にしない。

単体テストは `internal/api/handler_test.go`(`TestCreateMeeting_ExplicitHost_SkipsProvisioning`、
`TestCreateMeeting_NoHost_ProvisionsAndDeletes`)と `internal/kube/reconcile_test.go`
(`TestReconcile_IgnoresUnmanagedMeetingWithoutGameServer`)に追加した。

## 4. 検証項目と結果

design.md §10.3 の手順(a〜h)に対応させた。すべて実際の minikube クラスタに対して `kubectl port-forward` +
`curl` で確認した。

| # | 項目 | 結果 |
|---|---|---|
| a | `GET /health` | Pass。会議が 1 件も無い状態では `503`(`{"status":"not_ready"}`)、会議作成後は `200`。仕様どおりの挙動で、バグではない。 |
| b | `POST /admin/meetings`(host 未指定)→ `201 provisioning` → Secret `basis-<id>-sso`/GameServer `basis-<id>` 作成 → GameServer `Ready` → 会議が `ready` + host/port に遷移 | Pass。GameServer は数秒で `Ready` になり、`GET /admin/meetings` の host/port が実際の `Status.Address`/動的ポートに更新された。 |
| c | 2 件目の会議を作成し、2 部屋が別ポートで共存する | Pass。同一ノードアドレス `192.168.49.2` に対し、動的に割り当てられた別ポート(例: 7910 と 7596)が付与された。 |
| d | `host` を明示指定した `POST /admin/meetings` で GameServer/Secret が作成されない | Pass(§3 の修正の直接確認)。作成直後に `kubectl get gameservers/secrets -n basis` を確認し、既存の 2 件から増えていないことを確認した。 |
| e | `GET /admin/meetings`/`GET /admin/servers` が全件を反映する | Pass。3 件(concierge 管理 2 件 + 外部ホスト 1 件)すべてが両エンドポイントに `ready`/`hasTicketSigningKey: true` 等で反映された。 |
| f | k8s 管理の会議を `DELETE` → GameServer + Secret 削除、レジストリからも消える | Pass。`DELETE` は即座に `204` を返す。GameServer は Agones 側の終了処理(`agones-ready` サイドカー等)のため実際に消えるまで数十秒かかるが、最終的に削除される(§4 の f のとおり非同期)。Secret は即時に削除される。 |
| g | 残った会議の GameServer を `kubectl delete` で直接消し、concierge Pod を再起動 → 起動時に `"failed"` になる | Pass。`kube: reconcile: meeting <id> has no matching GameServer ...; marking failed` のログを確認し、`statusDetail` が `"No matching Kubernetes GameServer was found at startup reconciliation."` になった。同時に存在した `Managed=false`(外部ホスト)の会議は `"ready"` のまま変化しなかった(§3 の修正の副次確認)。 |
| h | `GAMESERVER_READY_TIMEOUT_SECONDS` を極端に短く(`1`)設定し、Ready 待ちタイムアウトで `"failed"` になる | Pass。ログに `GameServer basis-<id> did not become Ready within 1s; marking failed` が出力され、`statusDetail` も一致した。タイムアウト後に自動リトライしないこと(design.md §12 決定事項 3)も、しばらく監視して確認した。 |
| i | WebGL 有効化(`BASIS_SERVER_WEBSOCKET_ENABLED=true`)→UDP/TCP named ports→Ready 後の URI 永続化・API/join/deep link | Pass(2026-08-22 再検証)。現行ブランチを `concierge:webgl4` として再ビルドし、`webgl-diag-utpbzji` を作成。GameServer は `game` UDP `7192` と `websocket` TCP `7195`、`WebSocket*` 6 環境変数を持って Ready になり、`GET /admin/meetings`/`/admin/servers`、`/admin/client-config-template/{id}`、`/join/{token}/manifest`/`config` の全てに `wss://192.168.49.2:7195/basis` と `https://192.168.49.2:7195/server-info` が反映された。`join/{token}/open` の deep link にも `websocketUri` が含まれることを確認した。 |

`h` は当初「存在しないイメージを指定する」方法で試したが、`agones-ready` サイドカー(`curlimages/curl`)は
`basis-server` コンテナの状態と無関係に自分自身の `POST /ready` を成功させるため、`basis-server` が
`ErrImagePull` のままでも GameServer は `Ready` になってしまい、タイムアウト経路を再現できなかった。
代わりに `GAMESERVER_READY_TIMEOUT_SECONDS=1` を用いる方法に切り替えて再現した(§6 に詳細)。

### 4.1 remote 親への rebase 後の smoke check

`feat/concierge-go` を `origin/feature/web-support` (`de834dec5`) に rebase した後、同じ minikube プロファイルへ
現行ソースから `concierge:rebase5ac24` をビルドして再デプロイした。既存の phase 3 検証で確認済みの Agones
ライフサイクルを再度全項目実行するのではなく、rebase の影響範囲である管理 API と URI 伝播を確認した。

- `/health`: `503` (`servers` 未設定のため `not_ready`)。
- 一時的な静的 server に `wss://room.example/basis` と
  `https://room.example/server-info` を `PUT /api/admin/servers/{id}` で設定。
- `GET /api/admin/servers` が両 URI と `ready: true` を返すことを確認。
- その server を `DELETE /api/admin/servers/{id}` で削除し、検証用データを残していない。

この smoke check は browser UI の build/test と組み合わせて、remote 側の Web OAuth/join/Admin 認証を保持した
まま Concierge の WebGL endpoint 管理が動作することを確認するもの。実 Basis Server の listener/handshake 等の追加結果は
次節 §4.2 に記録する。

### 4.2 実 Basis Server による WebGL/TLS E2E (2026-08-23)

§8 に残っていた実サーバー経路を、現ブランチの `Basis Server/Docker/Dockerfile` からビルドしたイメージで再検証した。
既存の Podman ドライバーの minikube プロファイルを再利用し、検証中に作成した会議/GameServer は終了後に削除した。

検証で作成した meeting は `real-basis-wss-e2e-fixed-9zblpvm`。結果は次のとおり。

| 項目 | 結果 |
|---|---|
| 実イメージ build | Pass。`.NET 10` SDK publish が完了し、現行 web-support の WebSocket server-info 実装を含むイメージを minikube 内へロードした。 |
| GameServer Ready | Pass。`basis-real-basis-wss-e2e-fixed-9zblpvm` が `Ready`。`Status.Address=192.168.49.2`、UDP `game=7028`、TCP `websocket=7612`。 |
| 実サーバー listener | Pass。実プロセスの起動後に UDP `4296` と TLS WebSocket listener の起動を確認した。Agones の ready サイドカーだけによる誤判定ではない。 |
| server-info payload | Pass。`GET /server-info` は `200`、`{"online":0,"max":1024,"protocolVersion":1,"name":"Basis Server","motd":""}`。 |
| 許可 Origin | Pass。`Origin: http://allowed.example:4173` に `Access-Control-Allow-Origin` を返し、WebSocket Upgrade は `101 Switching Protocols`。 |
| 拒否 Origin | Pass。`Origin: http://evil.example:4173` の server-info と WebSocket Upgrade は `403 Forbidden`。 |
| WebSocket protocol | Pass(transport/protocol 入口まで)。TLS WebSocket HTTP Upgrade 後に Basis binary `Hello` frame を送信し、実サーバーから `Reject` data frame (`03 00 02 15 00 49 6e 76 61 6c 69 64 20 63 6c 69 65 6e 74 20 64 61 74 61 2e`) を受信した。空の hello payload のため admission は拒否されたが、listener がフレームを受理・処理したことを確認した。完全な認証済み接続は実 OIDC/JWKS または有効な admission ticket が必要なため未実施。 |
| TLS/WSS | Pass。自己署名証明書を検証専用イメージへ組み込み、`openssl s_client` で SAN と subject/issuer を確認した。curl は `-k`、または同証明書を CA として指定して検証できる。 |
| API/join 整合 | Pass。`GET /api/admin/meetings`、`GET /api/admin/servers`、`/join/{token}/manifest`、`/join/{token}/config` の全てが `wss://192.168.49.2:7612/basis` と `https://192.168.49.2:7612/server-info` を返した。 |

Podman の minikube ネットワークではホストから `192.168.49.2:7612` へ直接到達できなかったため、listener/payload/Origin/TLS の HTTP 検証は
実 GameServer Pod の `4297` へ `kubectl port-forward` した経路で行った。これは実 Basis Server プロセスと実 TLS 設定を通るが、
ノード外部 IP のファイアウォール/Ingress 経路の検証ではない。`Status.Address`/named port の解決と API/join への伝播は実クラスタの値で確認済み。

検証中に見つかった実装上の不足も修正した。Concierge が TLS 証明書パスを GameServer に渡せるよう
`BASIS_SERVER_WEBSOCKET_CERTIFICATE_PATH`/`..._KEY_PATH` を追加し、Basis の .NET 10 Kestrel が設定由来の HTTPS endpoint を
読み込むため `UseKestrelHttpsConfiguration()` を呼ぶようにした。これらの変更を含む実イメージで再デプロイ・再検証した。

同じ経路を現 HEAD (`359dbe3c5`) の Concierge image と既存の `basis-server:real-e2e-tls2` で追加再確認した
(meeting `real-basis-e2e-rerun-xaez_cs`, GameServer `basis-real-basis-e2e-rerun-xaez-cs`)。GameServer は
`Ready`、`Status.Address=192.168.49.2`、`game=7343`、`websocket=7918` となった。実 Pod の環境変数・起動ログで
`WebSocketEnabled=true`、TLS 証明書パス、許可 Origin、UDP `4296` と HTTPS listener `4297` の起動を確認した。
証明書を `--cacert`/`-CAfile` に明示して、server-info は許可 Origin が `200` + `Access-Control-Allow-Origin`、拒否 Origin が
`403`、WebSocket は許可 Origin が `101 Switching Protocols`、拒否 Origin が `403` となることを再現した。管理 API、
client-config、join manifest/config、join deep-link の全てに同一の `wss://192.168.49.2:7918/basis` /
`https://192.168.49.2:7918/server-info` が伝播した。検証会議と GameServer は削除し、Deployment は元の stub 構成へ復元した。

### 4.3 現 HEAD の Admin UI/API smoke E2E (2026-08-23)

現 HEAD `c9b1380b7` から Concierge image (`concierge:e2e-c9b1380-fix`) を
`minikube image build` し、`basis/concierge` Deployment に反映した。image build の Admin UI stage で
Vite+ の native HTTP client が必要とする CA bundle が `node:24-bookworm-slim` に無かったため、
`concierge/Dockerfile` に build-stage 専用の `ca-certificates` 導入を追加した。runtime image は引き続き
distroless のままである。

以下を port-forward (`localhost:5080`) 経由で確認した。

- `/health` は会議なしで `503 not_ready`、`/admin/` と全参照 asset は `200`、Admin API は token 無しで `401`。
- Admin bearer token で static server の PUT/GET/DELETE を行い、`ready`、WebSocket/server-info URI の保存・取得を確認。
  GET レスポンスには ticket signing key / transport public key の実値が含まれないことも確認した。
- meeting を作成し `provisioning` → Agones GameServer `Ready` → `ready` の遷移、動的 host/port、
  `GET /admin/servers` への反映、invitation 発行、DELETE と GameServer の非同期削除を確認した。
- invitation の `details`、`config`、`manifest`、`web-config`、`web-manifest`、`open`、join HTML を `200` で確認。
  `web-config` には Web client secret が含まれず、`Cache-Control: no-store`、許可されない Origin は `403` となった。
- OIDC token relay の authorization-code/refresh-token forwarding、redirect allowlist、secret 非露出は
  `internal/api/web_test.go` の HTTPS fake upstream fixture で再確認した。実クラスタでは fake upstream を
  production の outbound public-HTTPS 制約へ持ち込まず、browser config/route/CORS までを確認した。

検証用 meeting、GameServer、Web provider credential は終了時に削除・復元し、他の既存 meeting は削除していない。
ユーザーのブラウザ確認用に minikube と Concierge Deployment、`localhost:5080` port-forward は維持している。

同じ検証を、TLS Secret の自動 mount 実装を含む現 HEAD の Concierge image (`localhost/concierge:tls-secret`)で再実施した。
既存のブラウザ確認用 Deployment/PVC は変更せず、一時的な `concierge-tls-e2e` Deployment/PVC/設定 Secret を追加して
実測後に専用リソースだけ削除した。`Broker.Kubernetes` には次を設定した。

```json
{
  "WebSocketTlsSecretName": "basis-web-tls",
  "CertificateKey": "tls.crt",
  "PrivateKeyKey": "tls.key",
  "MountPath": "/run/basis-web-tls"
}
```

`basis-web-tls` は minikube 内で SAN (`basis-web.local`, `localhost`, `127.0.0.1`) 付き自己署名証明書を生成し、
`kubectl create secret tls` で作成した。実 image `basis-server:real-e2e-tls2` の meeting
`tls-secret-e2e-n_a_zd0` / GameServer `basis-tls-secret-e2e-n-a-zd0` は `Ready` となり、
`Status.Address=192.168.49.2`、UDP `game=7776`、TCP `websocket=7168` を得た。GameServer YAML で
`basis-web-tls` の `tls.crt`/`tls.key` read-only volume、`/run/basis-web-tls` mount、
`WebSocketCertificatePath=/run/basis-web-tls/tls.crt`、`WebSocketCertificateKeyPath=/run/basis-web-tls/tls.key` を確認し、
実 Pod のログでも HTTPS listener (`4297`) の起動を確認した。

実 Pod の `4297` へ一時 port-forward (`127.0.0.1:17917`) して再実測した結果は次のとおり。

| 項目 | 結果 |
|---|---|
| 許可 Origin の server-info | Pass。`Origin: http://allowed.example:4173` が `200` と `Access-Control-Allow-Origin` を返した。 |
| 拒否 Origin の server-info | Pass。`Origin: http://evil.example:4173` が `403 Forbidden` を返した。 |
| 許可 Origin の WSS Upgrade | Pass。`wss://127.0.0.1:17917/basis` が `101 Switching Protocols` を返した。 |
| 拒否 Origin の WSS Upgrade | Pass。同じ URL に拒否 Origin を付けると `403 Forbidden` を返した。 |

これにより、証明書を別途イメージへ焼き込まず、Kubernetes Secret → GameServer read-only volume → Basis Server TLS
環境変数という現行実装の経路を minikube で再現できることを確認した。検証用 meeting/GameServer、専用 Concierge
Deployment/PVC/Secret、port-forward は検証後に削除し、既存のブラウザ確認用 Concierge Deployment/port-forward は維持した。

### 4.4 参加 URL の自動表示 (2026-08-23)

会議室の作成直後に Admin UI が WebGL と Basis の参加 URL を表示する変更を、minikube + Agones で検証した。
再現手順は `operations.md §7.1` にある。Concierge image `concierge:joinlinks-dev` を
`minikube image build` し、`basis/concierge` Deployment へ反映した。`/data` は §4.1 の `emptyDir` へ
切り替え、毎回まっさらな状態から起動した。`appsettings.json` は
`PublicBaseUrl=http://127.0.0.1:15080`、`AllowedWebOrigins=["http://127.0.0.1:4173"]` を設定した。

`host` を指定しない managed 会議室で確認した結果は次のとおり。

| 項目 | 結果 |
|---|---|
| 作成時 201 の `webJoinUrl` | Pass。`status=provisioning`、`invitationReady=false` の時点で `joinUrl` と `webJoinUrl` の両方が入っていた。 |
| `ready` 遷移後の `GET /admin/meetings` | Pass。GameServer `Ready` 後に `invitationReady=true`、`host=192.168.49.2` と動的 port が入り、`webJoinUrl` は不変だった。 |
| `/join/{token}/details` との一致 | Pass。`details.webJoinUrl` が `/admin/meetings` の `webJoinUrl` と完全一致した。 |
| concierge 側エンドポイントの到達性 | Pass。参加ページと `web-manifest` (`Origin: http://127.0.0.1:4173`) がともに `200` を返した。 |
| WebGL origin (`http://127.0.0.1:4173`) の到達性 | 未検証。`webJoinUrl` の前半は `AllowedWebOrigins` の設定値で、concierge はそこに WebGL クライアントが配信されているかを検証しない。本検証では 4173 へ何も配信していない。 |
| Admin UI カードの待機表示 | Pass。`provisioning` の間はカードが「サーバーの起動を待っています。準備が完了すると参加 URL を表示します。」を表示した。 |
| Admin UI カードの自動切り替え | Pass。5 秒 polling で、再読み込みなしに WebGL と Basis の 2 つの URL へ切り替わった。 |
| 一覧の参加 URL 列 | Pass。`provisioning` は「起動待ち」、`ready` は「WebGL で参加」「Basis で参加」の 2 リンクで、`href` がカードの URL と一致した。 |
| Console エラー | Pass。0 件。 |
| `AllowedWebOrigins` が空の構成 | Pass。カードが「Web 版の配信元を appsettings.json の AllowedWebOrigins に設定すると表示されます。」、一覧が「WebGL: 未設定」となり、Basis の参加 URL は表示され続けた。 |

Admin UI の操作は、Chrome を `--headless=new --remote-debugging-port` で起動し、Node の組み込み
WebSocket から CDP を呼ぶ方式で行った。ブラウザ自動化のための依存関係はリポジトリへ追加していない。

WebGL 用の browser endpoint も同時に確認した。`BASIS_SERVER_WEBSOCKET_ENABLED=true`、
`BASIS_SERVER_WEBSOCKET_USE_TLS=true`、`wss://{host}:{port}/basis` と `https://{host}:{port}/server-info`
の template、および §5.1 の Secret `basis-web-tls` を設定した状態で、managed 会議室に
`webSocketUri=wss://192.168.49.2:<port>/basis` と `serverInfoUri=https://192.168.49.2:<port>/server-info`
が入り、GameServer に `/run/basis-web-tls` の read-only mount と
`WebSocketCertificatePath`/`WebSocketCertificateKeyPath` が生成されることを確認した。

検証用の会議室、GameServer、Secret は終了時に削除した。Deployment は `BASIS_SSO_ADMIN_TOKEN` を
Secret `concierge-admin` 参照へ戻し、`concierge:joinlinks-dev` と `emptyDir` の構成で起動したままにしている。

## 5. 検証中に見つかった不具合と修正

いずれもコードまたは `deploy/` マニフェストを修正し、コミットして再デプロイ・再検証した。

1. **`deploy/10-rbac.yaml` に `agones-sdk` ServiceAccount/RoleBinding が無かった。** Agones は GameServer Pod に
   `agones-sdk` という ServiceAccount を要求するが、Agones のインストール自体は `default` 名前空間にしか
   作成しない。`basis` 名前空間に GameServer を作ろうとすると
   `pods "basis-<id>" is forbidden: error looking up service account basis/agones-sdk: serviceaccount "agones-sdk" not found`
   で GameServer が `Error` になった。`basis` 名前空間にも同名の ServiceAccount と、Agones インストールが作る
   クラスタスコープの `ClusterRole agones-sdk` への RoleBinding を追加して解消した。
2. **`deploy/20-deployment.yaml` が `BASIS_SSO_BROKER_CONFIG_PATH` を Secret から `readOnly: true` でマウント
   していた。** `internal/config.Store` は `POST /admin/meetings`・`DELETE`・組織設定の更新のたびに同じパスへ
   書き戻す(design.md §8)。Kubernetes の Secret ボリュームは `readOnly` 設定に関わらずそもそも書き戻しが
   できない(Pod ローカルの tmpfs コピーへの書き込みも Pod 再起動で失われる)ため、初回の
   `POST /admin/meetings` から必ず失敗し、`AddServer` のエラーが `meetings.go` の一般化されたエラーメッセージ
   `"A server with that ID already exists."` として返っていた(実際の原因はディスク書き込み失敗で、
   メッセージが実態と食い違っていた点も含めて紛らわしい)。`concierge-config` Secret はシードとしてのみ扱い、
   `seed-config` initContainer で `concierge-data` PVC(`control-plane.json` と同じボリューム)へ 1 回だけ
   コピーし、以降はそのコピーを読み書きするよう変更した。
3. **`checkNoStaticMeetingIDCollision` が、会議が 1 件でも存在する状態での再起動を必ず `log.Fatalf` で拒否
   していた。** `CreateMeeting` は会議ごとに Servers[] と control-plane meetings の両方へ同じ id を登録する
   (admission ルーティングに必要なため、design どおりの意図的な挙動)。この起動時チェックは「両方のソースに
   同じ id がある = 事故的な衝突」とみなして `"local"` 以外を無条件に拒否していたため、concierge が管理する
   会議が 1 件でも存在すると次回起動が必ず失敗する重大な回帰だった(g の再起動検証で実際に再現した)。
   `config.ServerConfig` に `FromMeeting bool` を追加し、`CreateMeeting` が作る Servers[] エントリにのみ立てる
   ようにしたうえで、`checkNoStaticMeetingIDCollision` は `FromMeeting` が立っているエントリとの衝突を
   無視するよう修正した(運用者が手で `appsettings.json` に書いた静的エントリとの本物の衝突は引き続き検出する)。
4. **生成された meeting ID の `_` が Kubernetes 名として不正だった。** ID の互換仕様は `_` を許可しているが、
   `NewID` のランダム suffix に `_` が出ると `basis-<id>` の Secret/GameServer 作成が RFC 1123 違反で拒否され、
   API は `500 failed to provision meeting` になった。`controlplane.KubernetesName` を Secret/GameServer の全ての
   オブジェクト参照に適用して解消した(会議 ID 自体と admission のキーは元の値を保持する)。
5. **`basisdemo://` deep link が `html/template` の URL サニタイズで `#ZgotmplZ` になっていた。** WebGL URI を含む
   join deep link の実環境確認で発見し、生成値を `template.URL` として href/JavaScript fallback に渡すよう修正した。
6. **`operations.md §4` の WebGL 用 template が起動不能な値だった(ドキュメント側の不具合)。** 記載されていた
   `BASIS_SERVER_WEBSOCKET_URI_TEMPLATE='ws://{host}:{port}/basis'` と
   `BASIS_SERVER_INFO_URI_TEMPLATE='http://{host}:{port}/server-info'` を設定すると、Concierge は
   `managed WebSocket URI template: ws:// is only allowed for loopback endpoints` で `log.Fatalf` し起動しない。
   `ValidateBrowserEndpointTemplates` は `{host}` を非ループバックの検証用ホスト名へ置換してから URI を検査するため、
   この template は実際の割り当て先に関係なく必ず失敗する。managed GameServer のアドレスは常に非ループバックであり、
   `wss://`/`https://` を使うのが正しい。コードは仕様どおりのため、§4 の記載を `wss://`/`https://` と
   §5.1 の Secret 作成手順への参照に修正した。
7. **`checkNoStaticMeetingIDCollision` の衝突が、稼働中の pod では検知されず再起動時にだけ表面化する。**
   §5-3 の `FromMeeting` 修正後も、会議を API 以外の経路で消すと PVC 上に静的 `Servers[]` エントリと会議レコードが
   同じ id で残り、次の pod 起動が CrashLoopBackOff になる。起動時にしか検査しないため、稼働中の pod は正常なままで、
   image 入れ替え時に初めて失敗する。解消手順(稼働中 pod の API から該当会議を DELETE する方法と、検証用に
   `/data` を `emptyDir` にする方法)を `operations.md §4.1` に追記した。コードの変更は行っていない。

## 6. 初期 stub 検証の位置付けと、その限界

初期の phase-3 検証では、実イメージのビルドを待たず一時的な最小限の Go 製 UDP エコーリスナーを
`basis-server-stub:dev` として使った。これは検証時限りの代替で、ソースや build 定義はリポジトリに含めていない。
現在の再現手順は `docs/concierge/operations.md` に統一し、実 Basis Server image を使う。

- 動作: `SetPort` 環境変数(既定 4296)で UDP リッスンし、受信したデータグラムに `"echo: "` を付けて送り返すのみ。
  起動時に `RequireSso`/`AutoStartSsoBroker` の値をログ出力する(§4 の b で、Secret 経由の値が正しく注入されて
  いることの確認に使った)。
- **検証できたこと**: GameServer/Secret の作成・削除ライフサイクル、Agones SDK サイドカーによる Ready 化、
  動的ポート割り当てと複数会議室の共存、Kubernetes を source of truth とした再起動時の整合性チェック、
  Ready タイムアウトの `"failed"` 遷移。いずれも Basis Server 本体の実装に依存しない、concierge 側の
  プロビジョニングロジックの検証。
- **このスタブでは検証できていないこと**: Basis Server 本体が実際に `RequireSso`/`SsoTransportPrivateKey`/
  `SsoTransportPublicKey`/`SsoAdmissionTicketSigningKey` の環境変数オーバーライドを正しく読み、SSO 事前認証
  ハンドシェイクを行うかどうか。concierge が発行する `basis-sso-ticket-v2` チケットを実際のゲームサーバーが
  検証できるかどうか。UDP ゲームプロトコル自体の疎通。これらは実際の Basis Server イメージが無ければ検証
  できない(§8)。

## 7. 後片付けコマンド

検証終了後にクラスタごと破棄する場合。

```sh
export PATH="/opt/podman/bin:$PATH"
minikube delete
podman machine stop podman-machine-default   # 再利用しない場合のみ
```

namespace `basis` の中身だけを消して concierge/Agones の再検証に使う場合。

```sh
kubectl delete namespace basis
kubectl delete -f concierge/deploy/00-namespace.yaml   # 上と同義
```

本検証終了時点では、ユーザーが確認できるようクラスタは起動したままにしている(`minikube status` で確認可能)。
`basis` 名前空間の会議はすべて削除済み(`GET /admin/meetings` が空配列を返す状態)。

## 8. 未検証の項目

- 実際の Basis Server イメージによる有効な SSO admission ticket を使った認証済み UDP ゲームプロトコルの疎通。
- ノード外部 IP/Ingress を経由した WebSocket listener の到達性。§4.2 の実サーバー検証は Pod port-forward 経路であり、
  Podman ネットワーク外から `Status.Address` の TCP port へ直接到達できることまでは確認していない。
- 実際の OIDC プロバイダ(Google/Auth0 等)に対する browser OAuth redirect を含む `POST /admission/{serverId}` の入場審査。
  OIDC の署名検証・web token relay は `operations.md §6` の Go test fixture で再現する。
- 複数ノードクラスタでの Agones GameServer スケジューリング(minikube は単一ノード)。
- concierge Pod の水平スケールやローリングアップデート時の挙動(design.md §11 のとおり non-goal のため未検証)。
