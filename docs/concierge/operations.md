# Concierge 運用・再現手順

この文書が、Concierge を fresh clone から起動・検証・撤去するための canonical runbook です。
旧 C# broker は実行時には使いません。standalone、Docker Compose、Agones/minikube のすべてで
Go の `concierge` を起動します。秘密値、OIDC client secret、証明書の秘密鍵、生成した状態ファイルは
リポジトリへ追加しないでください。

## 1. 前提と clone

macOS では Xcode Command Line Tools、Docker または Podman、`git`、`curl`、`openssl` を用意します。
Agones 検証には `kubectl`、minikube、少なくとも 6 GiB の minikube memory が必要です。
Go、Node、pnpm、kubectl、minikube の検証済みバージョンはリポジトリルートの `mise.toml` に固定しています。

```sh
git clone <repository-url> sekaimate
cd sekaimate

# リポジトリ全体のツールを固定する。mise がない場合は同じバージョンを個別に用意する。
mise install
mise run concierge:check
```

Admin UI の変更を検証する場合は、次も実行します。

```sh
cd concierge/adminui
pnpm install --frozen-lockfile
pnpm run typecheck
pnpm run test
pnpm run build
cd ../..
```

## 2. standalone Basis Server

Basis Server を publish 済みのディレクトリに置き、Concierge を同じディレクトリへ publish します。
publish script は Go binary、`appsettings.json` の雛形、Admin UI の build 済み `dist/` を配置します。
`RequireSso=true`、`AutoStartSsoBroker=true` の設定名は XML 互換のため残っていますが、起動されるのは
Concierge です。

```sh
# server_dir は、Basis Server の実行ファイルと config/ が入っている配置先。
./concierge/publish-for-basis-server.sh /absolute/path/to/BasisServer

# 初回だけ appsettings.json の OIDC issuer/audience/client 情報を編集する。
chmod 600 /absolute/path/to/BasisServer/concierge/appsettings.json
# Basis Server の config.xml で RequireSso=true、AutoStartSsoBroker=true、
# SsoAdmissionTicketSigningKey、SsoTransportPublicKey を設定する。

# Basis Server を起動すると、server/concierge/concierge が子プロセスとして起動する。
```

standalone の Concierge は既定で `127.0.0.1:5080` に bind し、publish script が配置した
`server/concierge/adminui` を `/admin/` から配信します。外部ブラウザから利用する場合は、TLS を
reverse proxy で終端し、`SsoBrokerBindUrl` をその proxy から到達できる bind address に変更します。
Admin bearer token は appsettings の `AdminTokenEnvironmentVariable` が指す環境変数へ、次のように生成して
設定します（値そのものをログや docs に書かないでください）。

```sh
openssl rand -base64 32
```

## 3. Docker Compose（ローカル browser）

Compose 用の Concierge と HTTPS Admin gateway は次の手順で起動します。Basis Server 本体は root の
Compose ファイル、Concierge と gateway は `Docker/sso` の Compose ファイルで管理します。

```sh
cd "Basis Server/Docker/sso"
cp .env.example .env
chmod 600 .env
# .env の Password、BASIS_MEETING_PUBLIC_HOST、BASIS_SSO_ADMIN_TOKEN を編集する。
# token は次で生成し、値を直接 commit しない。
openssl rand -base64 32

# broker/appsettings.example.json を server 用にコピーして編集する。
mkdir -p broker
cp broker/appsettings.example.json broker/appsettings.json
chmod 600 broker/appsettings.json
# Web OIDC を使う場合は WebClientId/WebClientSecret、AllowedWebOrigins、
# TokenEndpoint、JwksUri を実際の IdP に合わせる。
# reverse proxy 配下で外向きの join/config URL を生成する場合だけ、proxy の送信元ネットワークを
# TrustedProxyCIDRs に CIDR で列挙する。空欄のままなら X-Forwarded-* は信頼されない。

cd ../../..
docker compose \
  -f "Basis Server/Docker/docker-compose.yml" \
  -f "Basis Server/Docker/docker-compose.local-web.yml" up -d --build
docker compose -f "Basis Server/Docker/sso/docker-compose.yml" up -d --build
```

または、root の mise task を使えます。

```sh
mise run sso:up
mise run server:up
```

Admin gateway は `https://127.0.0.1:5081/admin/` です。初回は gateway が生成した CA を信頼するか、
ブラウザで証明書警告を許可します。

```sh
mise run sso:ca
mise run sso:trust-ca       # macOS のログインキーチェーンへ登録する場合
```

Basis Server の browser endpoint は、local-web overlay では `ws://127.0.0.1:4297/basis` と
`http://127.0.0.1:4297/server-info` です。TLS/WSS を検証する場合は §5 の `web-e2e` overlay を使います。

## 4. minikube + Agones

以下は repository 内の manifest と実 Basis Server image だけを使う手順です。検証用の
`20-deployment-dev.yaml` やリポジトリ外の stub は必要ありません。

```sh
minikube start --driver=podman --cpus=4 --memory=6g --container-runtime=containerd

# Agones v1.60.0。環境で server-side apply が patch metadata を拒否する場合の除去も含む。
curl -fsSL https://raw.githubusercontent.com/googleforgames/agones/release-1.60.0/install/yaml/install.yaml \
  -o /tmp/agones-install.yaml
sed -E '/x-kubernetes-patch-strategy:|x-kubernetes-patch-merge-key:/d' \
  /tmp/agones-install.yaml > /tmp/agones-install-fixed.yaml
kubectl apply --server-side --force-conflicts -f /tmp/agones-install-fixed.yaml
kubectl wait --for=condition=available deployment/agones-controller -n agones-system --timeout=180s

# Concierge と実 Basis Server image を minikube 内へ build する。
minikube image build -t concierge:dev ./concierge
(cd "Basis Server" && minikube image build -t basis-server:dev -f Docker/Dockerfile .)

# 先に seed Secret を作成する。appsettings は gitignored の local/ に保存し、commit しない。
./tools/apply-concierge-config.sh --init
kubectl apply -f concierge/deploy/00-namespace.yaml
kubectl apply -f concierge/deploy/10-rbac.yaml
kubectl apply -f concierge/deploy/20-deployment-dev.yaml
kubectl apply -f concierge/deploy/30-service.yaml
kubectl -n basis create secret generic concierge-admin \
  --from-literal=token="$(openssl rand -base64 32)"
./tools/apply-concierge-config.sh --yes
```

minikube の overlay は `concierge:dev`、`basis-server:dev`、`imagePullPolicy: Never` を設定します。
WebGL transport も確認する場合は、先に §5.1 の手順で Secret `basis-web-tls` を作成してから、Concierge
Deployment に次の環境変数を追加して再起動します。

```sh
kubectl -n basis set env deployment/concierge \
  BASIS_SERVER_WEBSOCKET_ENABLED=true \
  BASIS_SERVER_WEBSOCKET_USE_TLS=true \
  BASIS_SERVER_WEBSOCKET_ALLOWED_ORIGINS=http://127.0.0.1:4173 \
  BASIS_SERVER_WEBSOCKET_URI_TEMPLATE='wss://{host}:{port}/basis' \
  BASIS_SERVER_INFO_URI_TEMPLATE='https://{host}:{port}/server-info'
kubectl rollout status deployment/concierge -n basis --timeout=180s
```

managed GameServer の template には `ws://`・`http://` を指定できません。
`ValidateBrowserEndpointTemplates` は `{host}` を非ループバックの検証用ホスト名へ置換してから URI を
検査するため、`ws://{host}:{port}/basis` は実際の割り当て先に関係なく
`managed WebSocket URI template: ws:// is only allowed for loopback endpoints` で起動に失敗します。
managed GameServer のアドレスは常に非ループバックなので、`wss://` と `https://` を使用してください。

Concierge の API と Admin UI を localhost へ forward します。

```sh
kubectl -n basis port-forward svc/concierge 15080:5080
```

別ターミナルで `http://127.0.0.1:15080/admin/` を開きます。token は Secret の値を安全に取得できます。

```sh
ADMIN_TOKEN="$(kubectl --context minikube --namespace basis \
  get secret concierge-admin --output jsonpath='{.data.token}' | base64 --decode)"
test -n "$ADMIN_TOKEN" || { echo 'Admin token is empty' >&2; exit 1; }
printf '%s\n' "$ADMIN_TOKEN"
```

API の smoke check と meeting lifecycle は次で確認できます。

```sh
curl --fail http://127.0.0.1:15080/health
curl --fail -H "Authorization: Bearer $ADMIN_TOKEN" \
  http://127.0.0.1:15080/admin/meetings
curl --fail -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"title":"minikube verification"}' \
  http://127.0.0.1:15080/admin/meetings
```

GameServer が `Ready` になり、`kubectl -n basis get gameservers,secrets`、`/admin/meetings`、
`/join/<token>/manifest` に同じ host/port と browser URI が出ることを確認します。

### 4.1 起動時の ID 衝突を解消して image を入れ替える

image を入れ替えたあと新しい pod が次のログで CrashLoopBackOff になる場合があります。

```text
concierge: meeting id "<id>" is registered both as a static Servers[] entry and as a control-plane meeting
```

`checkNoStaticMeetingIDCollision` は起動時にしか走りません。稼働中の pod は衝突が発生しても動き続け、
次に再起動したときだけ落ちます。原因は PVC 上に残った `/data/appsettings.json` の静的 `Servers[]`
エントリと `/data/control-plane.json` の会議レコードが同じ ID を持つことです。会議を API 以外の経路で
消すと発生します。

正規の解消手順は、稼働中の pod の API から該当会議を削除することです。`DELETE` は会議レコード、静的
`Servers[]` エントリ、GameServer、Secret をまとめて削除します。

```sh
curl --fail -X DELETE -H "Authorization: Bearer $ADMIN_TOKEN" \
  "http://127.0.0.1:15080/admin/meetings/<id>"
```

検証用に `/data` を毎回まっさらな状態から始める場合は、PVC ではなく `emptyDir` を使います。pod が
起動するたびに init container が Secret `concierge-config` から `appsettings.json` を seed し直すため、
前回の検証で残った状態を引き継ぎません。

```sh
kubectl -n basis scale deployment/concierge --replicas=0
kubectl -n basis patch deployment concierge --type=json \
  -p '[{"op":"replace","path":"/spec/template/spec/volumes/1","value":{"name":"data","emptyDir":{}}}]'
kubectl -n basis scale deployment/concierge --replicas=1
```

PVC `concierge-data` は削除されずに残ります。永続状態へ戻す場合は同じ path を
`{"name":"data","persistentVolumeClaim":{"claimName":"concierge-data"}}` に戻してください。

`emptyDir` にすると、pod を再起動した時点で会議レコードが消える一方、その会議の GameServer と Secret は
cluster に残ります。API から削除しようとしても会議が存在しないため `404` になります。再起動する前に
検証用の会議を `DELETE /admin/meetings/{id}` で削除するか、再起動後に label 指定でまとめて削除してください。

```sh
kubectl -n basis delete gameservers -l app=basis-server --ignore-not-found
kubectl -n basis delete secrets -l app=basis-server --ignore-not-found
```

## 5. 実 Basis TLS、CORS、WebSocket

ローカル Docker でブラウザ向け TLS を検証するには、`mkcert` で localhost 証明書を生成します。
証明書と秘密鍵は作業ディレクトリ外の保護された一時ディレクトリへ置きます。

```sh
mkcert -install
cert_dir="$(mktemp -d)"
chmod 700 "$cert_dir"
mkcert -cert-file "$cert_dir/basis-local.pem" \
  -key-file "$cert_dir/basis-local-key.pem" localhost 127.0.0.1 ::1

export BASIS_SERVER_CERTIFICATE_DIRECTORY="$cert_dir"
export BASIS_SERVER_CONFIG_DIR="$(mktemp -d)"
export BASIS_SERVER_INITIAL_RESOURCES_DIR="$(mktemp -d)"
export BASIS_WEBSOCKET_ALLOWED_ORIGINS="http://127.0.0.1:4173,http://localhost:4173"
docker compose \
  -f "Basis Server/Docker/docker-compose.yml" \
  -f "Basis Server/Docker/docker-compose.web-e2e.yml" up -d --build
```

確認対象は `https://127.0.0.1:4297/server-info` の許可 Origin が `200` と CORS header を返すこと、
未許可 Origin が `403` になること、`wss://127.0.0.1:4297/basis` の Upgrade が許可 Origin で `101`、
拒否 Origin で `403` になることです。証明書検証には生成した CA を使います。

```sh
curl --fail --cacert "$(mkcert -CAROOT)/rootCA.pem" \
  -H 'Origin: http://127.0.0.1:4173' \
  https://127.0.0.1:4297/server-info
```

Agones 経由の実 image TLS 検証、port-forward を含む過去の実測値は `verification.md §4.2` に記録しています。
外部 IP／Ingress 経路は別の証明書・DNS・firewall 設定が必要です。

### 5.1 Agones GameServer へ証明書 Secret を渡す

Basis Server 自身に TLS を終端させる場合は、Concierge が会議ごとに作成する
GameServer の Pod へ、あらかじめ作成した Kubernetes Secret を read-only で mount します。
証明書を Concierge Pod に mount する必要はありません。`appsettings.json` の
`Broker.Kubernetes` に Secret 名、2 つのキー、mount 先を明示してください。

次の例は minikube のローカル検証用です。証明書の SAN には、ブラウザまたは curl が実際に
接続する名前/IP を全て含めます。生成した秘密鍵と Secret の manifest はリポジトリへ commit
しないでください。

```sh
set -eu
cert_dir="$(mktemp -d)"
chmod 700 "$cert_dir"
openssl req -x509 -newkey rsa:2048 -sha256 -nodes \
  -keyout "$cert_dir/tls.key" \
  -out "$cert_dir/tls.crt" \
  -days 7 \
  -subj '/CN=basis-web.local' \
  -addext 'subjectAltName=DNS:basis-web.local,DNS:localhost,IP:127.0.0.1'

kubectl --context minikube --namespace basis create secret tls basis-web-tls \
  --cert="$cert_dir/tls.crt" --key="$cert_dir/tls.key" \
  --dry-run=client --output yaml | kubectl --context minikube apply -f -
```

`appsettings.json` には次を追加します（`CertificateKey` と `PrivateKeyKey` は Secret の
data key と一致させます）。

```json
{
  "Broker": {
    "Kubernetes": {
      "WebSocketTlsSecretName": "basis-web-tls",
      "CertificateKey": "tls.crt",
      "PrivateKeyKey": "tls.key",
      "MountPath": "/run/basis-web-tls"
    }
  }
}
```

`BASIS_SERVER_WEBSOCKET_ENABLED=true` と
`BASIS_SERVER_WEBSOCKET_USE_TLS=true` を Concierge Deployment に設定して再起動すると、
managed GameServer の `basis-server` コンテナには次が生成されます。

- Secret `basis-web-tls` の `tls.crt`/`tls.key` を `/run/basis-web-tls` に read-only mount
- `WebSocketCertificatePath=/run/basis-web-tls/tls.crt`
- `WebSocketCertificateKeyPath=/run/basis-web-tls/tls.key`

作成内容は次で確認できます。値そのものは表示せず、Secret の存在・キー名・Pod の
volume/volumeMount と、コンテナ内のファイル mode だけを確認します。

```sh
kubectl --context minikube --namespace basis get secret basis-web-tls \
  --output jsonpath='{.type}{"\n"}{.data.tls\.crt}{"\n"}{.data.tls\.key}{"\n"}' >/dev/null
kubectl --context minikube --namespace basis get gameserver -o yaml \
  | rg -n 'websocket-tls|basis-web-tls|WebSocketCertificate(Path|KeyPath)|readOnly'
```

TLS が無効な構成ではこの Secret 設定は無視され、既存の UDP-only/WebSocket plaintext
構成は変わりません。TLS 有効時に 4 項目のいずれかが欠けている場合は会議作成を拒否し、
不完全な Secret や GameServer を作成しません。

## 6. Web OIDC（test fixture と実 IdP）

### 6.1 設定内容

minikube 用の完全な雛形は [`concierge/appsettings.minikube.example.json`](../../concierge/appsettings.minikube.example.json)
です。秘密値は `replace-*` placeholder のままなので、実 OIDC で検証する場合だけ Google Cloud Console
で発行した値へ置き換えてください。入力元は gitignored の `local/concierge/appsettings.minikube.json` に統一します。

`concierge/appsettings.example.json` は汎用・production 向けの skeleton で、静的な `Broker.Servers` と
native client 用の `Audience` を中心にしています。`PublicBaseUrl`/origin は example の HTTPS placeholder
で、Web OIDC の `WebClientId`、`WebClientSecret`、`TokenEndpoint` は含まれていません。そのまま今回の
minikube WebGL 検証へ使わず、production で WebGL OIDC を使う場合は必要な Web fields と managed endpoint
templates を追加してください。

一方、`appsettings.minikube.example.json` は今回の managed Agones/WebGL 専用です。loopback の URL、
`ManagedWebSocketUriTemplate`/`ManagedServerInfoUriTemplate`、`Organization` provider の Web OIDC fields
を含み、静的 `Servers` は持たず managed meeting が Organization を継承します。静的 server 用の
`ClientConfigDirectory` もこの managed-only 雛形では省略しています。`./tools/apply-concierge-config.sh --init`
がコピーするのはこちらの minikube example です。

| フィールド | 用途 |
|---|---|
| `Broker.PublicBaseUrl` | Concierge API の browser 向け origin。minikube port-forward は `http://127.0.0.1:15080`。 |
| `Broker.AllowedWebOrigins` | CORS、redirect 許可、WebGL URL の生成元。WebGL port-forward は `http://127.0.0.1:4173`。 |
| `Broker.AdminTokenEnvironmentVariable` | Admin bearer token を読む環境変数（`BASIS_SSO_ADMIN_TOKEN`）。 |
| `Broker.Kubernetes` | GameServer に mount する `basis-web-tls` Secret の名前、キー、mount path。 |
| `Organization.Providers[].Audience` | Basis native client の client ID。雛形は WebGL-only 用に空文字で、ネイティブ admission token も使う場合だけ native client ID を設定する。 |
| `Organization.Providers[].WebClientId` | Google OAuth の Web application client ID。参加ページの OAuth に使う。 |
| `Organization.Providers[].WebClientSecret` | Concierge の token relay が使う Web client secret。commit・ログ出力禁止。 |
| `Organization.Providers[].TokenEndpoint` / `JwksUri` | OAuth token relay / JWT 検証の HTTPS endpoint。 |
| `Organization.Providers[].AllowedHostedDomains` | Google `hd` claim の許可リスト。空配列は hosted-domain 制限なし。 |

この minikube 雛形は managed WebGL 会議室だけを対象にするため、`Audience` は空文字です。コード上は
`Audience` が空でも `WebClientId`、`WebClientSecret`、HTTPS の `TokenEndpoint` が揃えば provider の構造検証を
通せます。Basis native client も使う場合、`Audience` を native client ID に置き換えてください。native
admission token の audience 検証では `Audience` または `WebClientId` が使われます。`WebClientSecret` は
ブラウザーへ返さず、server-side relay だけが使用します。

Google Cloud Console では OAuth client の JavaScript origin に `http://127.0.0.1:4173` を登録し、redirect
URI に `http://127.0.0.1:4173/sso-callback` を登録します。次の順で雛形を生成・編集・反映できます。

#### Google OAuth Web client の作成

現行の Google Auth Platform UI では、次の順で Web client を作成します。

1. Google Cloud Console で対象 project を選択するか、新しい project を作成する。
2. [Google Auth Platform の Get started](https://support.google.com/cloud/answer/15544987) を開く。
   Branding で app name、user support email、contact information を設定する。
3. Audience で `External`（外部テスト）または Google Workspace の `Internal` を選ぶ。`External` の
   `Testing` を使う場合は、実際にログインする Google account を Test users に追加する。
   [Audience の公式手順](https://support.google.com/cloud/answer/15549945)も参照してください。
4. `Clients` → `Create client` → Application type `Web application` を選び、client name を入力する。
5. Authorized JavaScript origins に `http://127.0.0.1:4173` を追加する（path と末尾 `/` は付けない）。
   Authorized redirect URIs に `http://127.0.0.1:4173/sso-callback` を追加する（この path を含む完全一致）。
6. `Create` を押し、表示された Client ID と Client secret を、秘密値を commit しないよう
   `local/concierge/appsettings.minikube.json` の `WebClientId` と `WebClientSecret` へ入力する。

`127.0.0.1` と `localhost` は別 origin なので、ブラウザー、`AllowedWebOrigins`、Google の origin/redirect
登録で混在させないでください。production では `https://` の実際の Web origin と、その origin の
`/sso-callback` へ置き換えます。Google の OAuth code model では redirect URI は登録値と scheme、host、path、
末尾 slash まで完全一致する必要があり、違う場合は `redirect_uri_mismatch` になります（[Web server OAuth の公式説明](https://developers.google.com/identity/protocols/oauth2/web-server)）。

Data Access の追加 scope は通常不要です。Concierge の現在のコードは OAuth 要求で `openid email profile`
だけを使うため、まずこの基本 scope で動作を確認し、不要な Google API scope を追加しないでください。

```sh
./tools/apply-concierge-config.sh --init
# local/concierge/appsettings.minikube.json の placeholder を実際の OIDC 値へ編集する。
./tools/apply-concierge-config.sh --yes
```

`--init` は既存の `local/concierge/appsettings.minikube.json` を絶対に上書きしません。既存の設定を反映する場合や
実 OIDC 用の別保管場所を使う場合は `./tools/apply-concierge-config.sh --yes /secure/path/appsettings.json`
を使います。`emptyDir` の `/data` を使う検証環境では rollout restart により meeting records が消え、
GameServer が孤立する可能性があるため、実行前に meeting と GameServer を確認してください。
旧版の既定ファイル `local/concierge/appsettings.json` が残っている場合、script は自動移動・上書きせず停止します。
その場合は `mv local/concierge/appsettings.json local/concierge/appsettings.minikube.json` で移行してから再実行してください。

OIDC の署名検証は、外部 IdP の秘密値を共有せずに再現できる Go test fixture を使用します。これは
`httptest.NewTLSServer` が生成する一時 CA を Validator の HTTP client にだけ信頼させ、RS256 JWKS、issuer、
audience、expiry、signature を検証するものです。

```sh
cd concierge
go test ./internal/admission -run 'TestValidator_Validate_Success|TestValidator_Validate_WebClientAudience' -count=1
go test ./internal/api -run 'TestWebOidcRelayForwardsOnlyAllowedFieldsAndServerSecret|TestWebConfigManifestDetailsAndCors' -count=1
cd ..
```

この fixture は実ブラウザの OAuth redirect を模倣するものではありません。実 IdP の設定内容、Google
Cloud Console の origin/redirect 登録、`local/concierge/appsettings.minikube.json` への反映方法は §6.1 を参照して
ください。`BASIS_SSO_ADMIN_TOKEN`、client secret、refresh token はコマンドライン履歴や commit に残さず、
終了後に Compose state を削除してください。

## 7. browser/Admin 確認

Compose では `https://127.0.0.1:5081/admin/`、minikube port-forward では
`http://127.0.0.1:15080/admin/` を開き、Admin token を入力します。次を順に確認します。

1. `/health` が表示される。
2. 会議を作成し、`provisioning` から `ready` へ polling される。
3. invite URL を発行し、join page が manifest と browser endpoint を表示する。
4. WebGL client を `mise run web:build && mise run web:serve` で別 origin に配信し、join link の auto-join を確認する。
5. server-info と WebSocket の Origin 制限がブラウザの Network/Console でも一致する。

### 7.1 参加 URL の自動表示を minikube で検証する

会議室を作成すると Admin UI が WebGL と Basis の参加 URL を自動表示します。この節は、その挙動を
minikube + Agones の managed 会議室で再現する手順です。§4 の cluster が起動済みであることが前提です。

この検証では WebGL Service を localhost へ port-forward し、ブラウザーからも同じ origin を使います。
`webJoinUrl` は `Broker.AllowedWebOrigins` の先頭にある、ブラウザーが実際に読み込める origin
(HTTPS、またはループバックの HTTP)から生成します。空の場合は Admin UI が URL の代わりに未設定の
理由を表示します。`concierge-web` の Service DNS 名はホストのブラウザーから解決できないため、
port-forward の URL (`http://127.0.0.1:4173`) を AllowedWebOrigins に設定します。

WebGL の image は GHCR の `ghcr.io/sekaimate/concierge-web:dev` を pull します。この検証に Unity は
不要です。

```sh
kubectl apply -f concierge/deploy/40-web-deployment.yaml
kubectl rollout status deployment/concierge-web -n basis --timeout=180s
```

WebGL クライアントを更新した場合は、Unity を導入した環境で `./tools/publish-web-image.sh` を実行して
image を push します。スクリプトは `Build/Web` を入力に使い、Unity の `Library` やリポジトリ全体を image
builder へ送らない一時 context を作成します。Development build は Addressables と Unity のキャッシュを
再利用する incremental build であり、`clean_build` は使用しません。

別ターミナルで WebGL Service を公開します。検証中はこの port-forward を終了しないでください。

```sh
kubectl -n basis port-forward svc/concierge-web 4173:4173
```

Secret `concierge-config` の `appsettings.json` は、次のように Concierge の port-forward と WebGL
Service の port-forward の両方を指す必要があります。

```json
{
  "Broker": {
    "PublicBaseUrl": "http://127.0.0.1:15080",
    "AllowedWebOrigins": ["http://127.0.0.1:4173"]
  }
}
```

設定ファイルの生成・編集・Secret 反映は §6.1 の手順を使います。この検証では特に
`PublicBaseUrl=http://127.0.0.1:15080` と `AllowedWebOrigins=["http://127.0.0.1:4173"]` を設定してください。
`tools/apply-concierge-config.sh` は JSON を検証してから Secret を apply し、Concierge の rollout
restart/status を実行します。秘密値は表示しません。`--yes` は安全確認のため必須です。`emptyDir` の
`/data` を使っている場合、再起動で meeting records が消え、既存の GameServer が孤立する可能性があります。
実行前に検証用 meeting を API から削除し、GameServer/Secret の残存を確認してください。`--yes` を付けない
実行はこの警告だけ表示して変更せず終了します。

`20-deployment-dev.yaml` を PVC のまま使っている場合、Secret は初回起動時だけ `/data/appsettings.json`
へコピーされます。検証環境をまっさらにする場合は、§4.1 の `emptyDir` overlay を適用してから Secret
を更新してください。`PublicBaseUrl` は Concierge の port-forward、`AllowedWebOrigins` は WebGL の
port-forward とそれぞれ一致させます。どちらかが実際のアクセス先と違うと、生成される参加 URL は
ブラウザーから開けません。

image を入れ替え、§4.1 の `emptyDir` でまっさらな `/data` から起動します。

```sh
minikube image build -t concierge:joinlinks-dev ./concierge
kubectl -n basis set image deployment/concierge concierge=concierge:joinlinks-dev
kubectl -n basis set env deployment/concierge BASIS_SERVER_IMAGE=basis-server-stub:dev
kubectl rollout status deployment/concierge -n basis --timeout=180s
kubectl -n basis port-forward svc/concierge 15080:5080
```

WebGL Service が実際に必要なレスポンスを返すことを、会議室作成前に確認します。Development build の
raw `.wasm`/`.data` を標準確認し、Unity の圧縮設定で `.gz` または `.br` が生成されている場合は、
存在する圧縮ファイルも追加で確認します。`Build/Web` 配下のパスと Service URL のパスは一致しないため、
次のループで `Build/Web/` を取り除いて URL を組み立てます。

```sh
curl --fail --silent -o /dev/null http://127.0.0.1:4173/
for asset_path in \
  Build/Web/Build/Web.data Build/Web/Build/Web.data.gz Build/Web/Build/Web.data.br \
  Build/Web/Build/Web.wasm Build/Web/Build/Web.wasm.gz Build/Web/Build/Web.wasm.br; do
  if [ -f "$asset_path" ]; then
    asset_url="/${asset_path#Build/Web/}"
    curl --fail --silent --head "http://127.0.0.1:4173$asset_url" \
      | tr -d '\r' | grep -E '^(content-type:|content-encoding:|accept-ranges:)'
  fi
done
```

raw と圧縮済みの `Web.wasm*` の `Content-Type` は `application/wasm`、`.data*` は
`application/octet-stream`、圧縮形式に応じて `Content-Encoding: gzip` または `Content-Encoding: br`、
全ファイルで `Accept-Ranges: bytes` になれば合格です。BEE は Range 取得を確認します。

```sh
curl --fail --silent --dump-header /tmp/concierge-web-range.headers \
  --range 0-15 -o /tmp/concierge-web-range.bin \
  http://127.0.0.1:4173/BEE/world.BEE
grep -E '^(HTTP/|content-range:|accept-ranges:)' /tmp/concierge-web-range.headers | tr -d '\r'
test "$(wc -c < /tmp/concierge-web-range.bin | tr -d ' ')" -eq 16
```

`HTTP/1.1 206`、`Content-Range: bytes 0-15/...`、`Accept-Ranges: bytes` が必要です。

別ターミナルで `ADMIN_TOKEN` を §4 の手順で取得し、`host` を指定せずに会議室を作成します。`host` を
省略すると Kubernetes が GameServer をプロビジョニングするため、`provisioning` から `ready` への遷移を
そのまま観察できます。

```sh
curl --fail --silent -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"title":"Join links verification"}' \
  http://127.0.0.1:15080/admin/meetings
```

201 の時点で `status` が `provisioning`、`invitationReady` が `false`、`joinUrl` と `webJoinUrl` の
両方が入っていることを確認します。GameServer が `Ready` になったあと、同じ会議室が `ready` と
`invitationReady: true` へ変わることを確認します。

```sh
until [ "$(kubectl -n basis get gameservers \
  -o jsonpath='{.items[0].status.state}')" = Ready ]; do sleep 3; done
curl --fail --silent -H "Authorization: Bearer $ADMIN_TOKEN" \
  http://127.0.0.1:15080/admin/meetings
```

参加ページと、`webJoinUrl` のクエリ `meetingUrl` が指す concierge 側のエンドポイントを確認します。
`<token>` は `joinUrl` の末尾です。

`webJoinUrl` の前半が `http://127.0.0.1:4173` であり、`meetingUrl` が Concierge の
`/join/<token>/web-manifest` を指すことを確認します。次の curl は Concierge 側の参加ページと
manifest の到達性を確認します。WebGL origin の到達性は、上記の Service 検査と後述のブラウザー確認で
検証済みになるため、ここでホスト側の `tools/serve-web.sh` を別途起動する必要はありません。

```sh
curl --fail --silent -o /dev/null -w '%{http_code}\n' \
  "http://127.0.0.1:15080/join/<token>"
curl --fail --silent -o /dev/null -w '%{http_code}\n' \
  -H 'Origin: http://127.0.0.1:4173' \
  "http://127.0.0.1:15080/join/<token>/web-manifest"
curl --fail --silent "http://127.0.0.1:15080/join/<token>/details"
```

参加ページと web manifest がどちらも `200` を返し、`details` の `webJoinUrl` が `/admin/meetings` の
`webJoinUrl` と一致することが合格条件です。この 2 つは同じ生成関数を使うため、値がずれた場合は
片方の経路だけが更新されています。

Admin UI では `http://127.0.0.1:15080/admin/` を開き、`host` を空欄のまま会議室を作成して次を確認します。

Docker driver の minikube では、GameServer の `status.address`（例: `192.168.49.2`）へ macOS の
ブラウザーから直接到達できないことがあります。その場合は会議室作成フォームで `host` は空欄のまま、
次の 2 項目を明示します。これらはブラウザー向け URI だけを上書きし、GameServer の Kubernetes
プロビジョニングは引き続き実行されます。

```text
WebSocket URI:   wss://127.0.0.1:4297/basis
Server Info URI: https://127.0.0.1:4297/server-info
```

会議室が `Ready` になったら、別ターミナルでその Pod を転送します。`<meeting-id>` は Admin UI の
会議室 ID です。

```sh
kubectl -n basis port-forward "pod/basis-<meeting-id>" 4297:4297
```

ブラウザーで先に `https://127.0.0.1:4297/server-info` を開き、ローカル検証用証明書の警告を確認して
許可します。証明書の SAN に `127.0.0.1` が必要です。許可しないままでは `fetch` と `wss` の両方が
ブラウザーに遮断されます。公開環境では警告を回避せず、公開 DNS 名に対する信頼済み証明書を使います。

minikube の `PublicBaseUrl=http://127.0.0.1:15080` から生成される admission endpoint は loopback
HTTP です。WebGL client は HTTPS を標準としつつ、このローカル検証に限って loopback HTTP を許可します。
リモートホストの HTTP admission endpoint は引き続き設定検証で拒否されます。

1. 作成直後、一覧の参加 URL 列が「起動待ち」になり、カードが「サーバーの起動を待っています。準備が完了すると参加 URL を表示します。」を表示する。
2. GameServer が `Ready` になると、5 秒ごとの polling でカードが WebGL と Basis の 2 つの URL へ切り替わる。ページの再読み込みは不要です。
3. 一覧の参加 URL 列に「WebGL で参加」「Basis で参加」の 2 つのリンクが出て、`href` がカードの URL と一致する。
4. 「WebGL で参加」をクリックし、`http://127.0.0.1:4173/?basisMeeting=1&meetingUrl=...` を開く。
   Unity のローディング画面から manifest と `web-config` の取得まで進み、ブラウザーの Console に WebGL、
   CORS、Range のエラーが出ない。

Admin UI に表示された `webJoinUrl` を直接別タブへ貼り付けても同じ確認ができます。Network では
`meetingUrl` の manifest、`/web-config`、`server-info`、WebSocket の順にリクエストが成功し、WebGL
クライアントが実際に managed GameServer へ接続できることを、実 OIDC 設定を用いた場合の合格条件とします。

初期状態の minikube Secret は `verification-*` の placeholder OIDC 設定です。この状態でも WebGL URL は
開き、manifest と `web-config` を取得できます。これらの取得後に WebGL の
`BasisSsoAuthController.IsSignedIn` が `true` になるのを待ちますが、placeholder のままでは `false` が
続き、admission、server-info、WebSocket より前で停止するため、実 peer の入室は完了しません。実入室を確認する場合は、§6 の
実 Web OIDC 設定（有効な `WebClientId`、`WebClientSecret`、`TokenEndpoint`、`JwksUri` および許可ユーザー）
を設定し、認証完了後に GameServer の server log の peer 接続も確認してください。placeholder のままでは
「WebGL 起動・manifest/web-config 確認」として記録し、「入室合格」とは扱いません。

この検証では `AllowedWebOrigins` を WebGL Service のブラウザー到達 origin として使い続けます。§3.2
で述べた専用 `WebClientOrigin` への分離は、JoinDetails と参加ページ、Admin UI の生成結果を同時に
変更する必要があり、今回の配信追加の範囲では行いません。

`AllowedWebOrigins` を空にして pod を再起動すると、カードが「Web 版の配信元を appsettings.json の
AllowedWebOrigins に設定すると表示されます。」、一覧が「WebGL: 未設定」に変わります。Basis の参加 URL は
`AllowedWebOrigins` に依存しないため、この構成でも表示されます。

## 8. cleanup と restore

検証用の meeting、GameServer、Secret を削除し、port-forward と Compose を停止します。

```sh
kubectl -n basis delete gameservers,secrets -l app=basis-server --ignore-not-found
kubectl -n basis delete secret concierge-config concierge-admin --ignore-not-found
kubectl delete -f concierge/deploy/40-web-deployment.yaml --ignore-not-found
kubectl delete -f concierge/deploy/30-service.yaml --ignore-not-found
kubectl delete -f concierge/deploy/20-deployment-dev.yaml --ignore-not-found
kubectl delete -f concierge/deploy/10-rbac.yaml --ignore-not-found
kubectl delete -f concierge/deploy/00-namespace.yaml --ignore-not-found
minikube delete

docker compose -f "Basis Server/Docker/sso/docker-compose.yml" down --volumes
docker compose \
  -f "Basis Server/Docker/docker-compose.yml" \
  -f "Basis Server/Docker/docker-compose.local-web.yml" down --volumes
```

`kubectl delete meetings` は標準 Kubernetes resource ではないため、未対応環境ではエラーを無視します。
Concierge 管理 meeting は API から削除するのが正規です。作業中の port-forward は Ctrl-C で終了し、
一時証明書・一時 config・`concierge/adminui/dist` は作業後に削除します。macOS の CA keychain に登録した
CA は `mise run sso:trust-ca` の逆操作を行うか、Keychain Access から削除してください。

検証結果の追記先は `docs/concierge/verification.md` です。環境固有の hostname、token、client secret、
証明書の秘密鍵は記録しないでください。
