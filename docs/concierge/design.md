# concierge 設計ドキュメント

最終更新: 2026-08-22

現行の C# 製 Basis SSO Broker(`Basis/Tools/BasisSsoBroker`)を Go で書き直し、かつ Agones ベースの会議室(部屋)
ライフサイクル管理を統合した新サービス **concierge** の設計をまとめる。入力資料は `research-sso-broker.md`
(現行 broker の調査)と `research-basis-k8s.md`(basis-k8s の調査)。この文書ではそれらを踏まえた設計判断のみを記す。

## 1. 背景と目的

- 現行 broker は SSO 入場審査(OIDC トークン検証 + チケット発行)と、会議(meeting)のメタデータ管理のみを行う。
  実際のゲームサーバープロセスの起動・停止は行わず、`BrokerServerOptions`/`MeetingRecord` に手動または半自動で登録された
  接続情報(host/port)を参照するだけである。
- basis-k8s は逆に、Agones `GameServer` の作成・削除・一覧のみを行う薄い REST API であり、SSO/認証には一切関与しない。
- concierge はこの 2 つを 1 つの Go サービスへ統合し、「会議を作る」という単一の操作が「SSO 用の鍵を発行し、
  Agones 上に実際のゲームサーバー Pod をプロビジョニングし、両者を紐付ける」までを一貫して行えるようにする。

## 2. 命名: broker → concierge

新サービスは「broker」ではなく「concierge」と改名する。理由は次のとおり。

- 現行 broker は SSO 入場審査(門番)の役割のみを表す名前だが、新サービスはそれに加えて会議室(部屋)の
  ライフサイクル管理(作成・削除・一覧)という 2 つ目の役割を持つ。「concierge」はホテルのフロント係のように、
  入場審査(本人確認)と部屋の手配の両方を担う存在を表す語として、2 役を 1 語で表現できる。
- 「Gatekeeper」等の入場審査を想起させる別名も検討対象になり得るが、Kubernetes エコシステムには既存の OSS プロジェクト
  (例: OPA Gatekeeper)がすでに同名で存在し、ドキュメント検索や依存関係の混同を招く。concierge はこの種の既存 OSS との
  名前衝突がない。

## 3. リポジトリ配置とレイアウト

リポジトリルート直下に `concierge/` ディレクトリを新設する(`Basis/` や `Basis Server/` と並列)。basis-k8s と同じ
Go レイアウトを踏襲する。

```
concierge/
├── Dockerfile
├── README.md
├── go.mod
├── go.sum
├── api/
│   └── openapi.yaml          # 公開/管理/部屋管理 API の単一の情報源
├── cmd/
│   └── server/
│       └── main.go           # エントリーポイント。kubeconfig 解決・Agones/静的設定のロード・HTTP サーバー起動
├── deploy/
│   ├── 00-namespace.yaml
│   ├── 10-rbac.yaml
│   ├── 20-deployment.yaml
│   └── 30-service.yaml
├── docs/
│   └── (concierge 固有の運用ドキュメント。basis-k8s の docs/codegen.md 等に相当するものを必要に応じて追加)
└── internal/
    ├── admission/            # OIDC トークン検証 + basis-sso-ticket-v2 発行(現行 broker の TokenValidator/Ticket 相当)
    ├── api/                  # oapi-codegen 生成コード + ハンドラ実装(公開/管理/部屋管理をまとめて配線)
    ├── config/               # appsettings.json 相当の設定ロード(§8 参照)
    ├── controlplane/         # 会議/組織/登録トークンのストア(現行 ControlPlane.cs 相当)
    └── kube/                 # Agones GameServer 管理(basis-k8s の internal/kube を移植・拡張)
```

Go module 名は `github.com/sekaimate/sekaimate/concierge` を提案する。理由: このリポジトリ(`github.com/sekaimate/sekaimate`)
自体は Unity プロジェクトであり Go module を持たないため、既存の厳密な命名規約は存在しない。basis-k8s はリポジトリ直下を
モジュールルートとして GitHub の org/repo パス(`github.com/mvxproto/basis-k8s`)をそのままモジュール名にしている。
concierge はモノレポのサブディレクトリに配置するため、同じ考え方(GitHub 上の実際のパスをモジュール名にする)を
そのディレクトリ階層まで延長し、`github.com/sekaimate/sekaimate/concierge` とするのが最も自然で、`go get`/`import` の
パスがリポジトリ構成と一致する。

## 4. アーキテクチャ概要

### 4.1 コンポーネント構成

concierge は単一の Go バイナリ(`cmd/server`)として動作し、内部に大きく 3 つの責務を持つ。

1. **入場審査(admission)**: `research-sso-broker.md` §3 のフローをそのまま踏襲する。`POST /admission/{serverId}` で
   OIDC ID トークンを検証し、`basis-sso-ticket-v2` 形式のチケットを発行する。ステートレス(チケットの保存・消費管理は
   一切行わない)。
2. **コントロールプレーン(controlplane)**: 組織(organization)設定、会議(meeting)のメタデータ、登録
   (enrollment)トークンを管理する。現行 `ControlPlane.cs` の `MeetingStore`/`EnrollmentStore`/`MeetingIdentity`/
   `MeetingKeys` に相当する。
3. **部屋管理(kube)**: basis-k8s の `internal/kube.Manager` を移植し、Agones `GameServer` の作成・削除・一覧を行う。
   basis-k8s との違いは、concierge が GameServer 作成時に SSO 用の鍵(署名鍵 + トランスポート鍵ペア)を生成し、
   Secret 経由で Pod へ注入する点(§5)。

サーバーのレジストリ(`/admission/{serverId}` が参照する「どの会議室が存在し、鍵は何か」という情報)は、現行の
静的な `BrokerServerOptions[]`(設定ファイルに直接書く、外部で運用される既存のゲームサーバー向け)と、concierge が
Agones 上に動的に作成する会議室の両方をマージした 1 つのレジストリとして扱う。既存の静的サーバー運用(手動デプロイの
Basis サーバー、systemd/Docker で運用されるもの)を壊さずに、Agones 管理下の部屋を段階的に追加できるようにするための
設計判断である。同一 id が両方のソースに存在する場合はアプリ起動時にエラーとして拒否する(サイレントな上書きをしない)。

### 4.2 会議(meeting)作成フロー(concierge 管理の部屋の場合)

```
POST /admin/meetings {title, id?, ...}
  │
  ├─ 1. id を決定(未指定なら MeetingIdentity.NewId 相当のロジックで採番)
  ├─ 2. 会議ごとの鍵を生成(§5): X25519 トランスポート鍵ペア + 48 バイトのチケット署名鍵
  ├─ 3. 鍵を Kubernetes Secret として作成(1 会議 = 1 Secret)
  ├─ 4. MeetingRecord を状態 "provisioning" で永続化(host/port は未確定)
  ├─ 5. kube.Manager.Create を呼び、Secret を envFrom で参照する Agones GameServer を作成
  │      (RequireSso=true, AutoStartSsoBroker=false もあわせて注入。§5 参照)
  ├─ 6. 201 Created を返す(この時点では host/port は空。クライアントは "provisioning" として扱う)
  │
  └─ (非同期) バックグラウンドでGameServer の状態をポーリングし、Ready かつ
        Status.Address/Status.Ports が確定した時点で MeetingRecord を
        UpdateStatus(host, port, status="ready") で更新する
```

現行 broker にはすでに `EnsureSingleComposeMeeting` が使う `"provisioning"` ステータス(host 未確定の会議)という
概念が存在するため、これをそのまま流用できる。`POST /admin/meetings` の同期レスポンスは host/port を返せない
(Agones のスケジューリング完了を待つ必要があるため)が、既存の状態モデルとの後方互換を保てる。

### 4.3 会議削除フロー

```
DELETE /admin/meetings/{meetingId}
  │
  ├─ 1. MeetingRecord を検索(未知なら 404、現行と同じ)
  ├─ 2. その会議が concierge 管理の部屋なら kube.Manager.Delete で GameServer を削除
  │      (basis-k8s と同じく Agones が配下の Pod を後始末する)
  ├─ 3. 対応する Kubernetes Secret を削除
  ├─ 4. MeetingRecord を削除し、静的サーバーレジストリからも対応エントリを削除(現行と同じ)
  ├─ 5. 存在すればディスク上のクライアント設定ファイルを削除(現行と同じ)
  └─ 6. 204 を返す
```

外部で手動運用されている静的サーバー(`Servers[]` 由来)に対する `DELETE` は、現行同様に GameServer 削除を伴わない
(そもそも Agones 管理下にないため)。

## 5. 部屋ごとのチケット署名キーの扱い

現行実装には**2 つの異なる環境変数レイヤー**が存在し、混同すると設計を誤るため明確に区別する(`research-sso-broker.md`
§5.2 参照)。

1. **broker 側の間接参照**: `BrokerServerOptions.TicketSigningKeyEnvironmentVariable`/
   `TransportPublicKeyEnvironmentVariable` に書かれた、任意に選べる環境変数名(慣例的に
   `BASIS_SSO_TICKET_SIGNING_KEY[_<ID>]` 等)。broker プロセス自身がこの名前の環境変数を読んで鍵の値を得る。
2. **ゲームサーバー本体側のリフレクションオーバーライド**: `Configuration.ProcessEnvironmentalOverrides()`
   (`Basis/Packages/com.basis.server/BasisNetworkCore/BasisServerConfiguration.cs:249-302`)が、`Configuration`
   クラスの**公開フィールド名そのもの**を環境変数名として読む。SSO に関係するフィールドは次のとおり(すべて
   `BasisServerConfiguration.cs` で確認済み、確定した事実として扱う)。

   | フィールド名(= 環境変数名) | 型 | 意味 |
   |---|---|---|
   | `RequireSso` | bool | SSO 事前認証ハンドシェイクを必須化する。既定 `false`。 |
   | `SsoTransportPrivateKey` | string | base64url X25519 秘密鍵。サーバーのみが保持する。 |
   | `SsoTransportPublicKey` | string | base64url X25519 公開鍵。クライアントの `basis-sso.json` にも配布される。 |
   | `SsoAdmissionTicketSigningKey` | string | チケット HMAC 署名鍵。HTTPS broker(concierge)とのみ共有する。 |
   | `AutoStartSsoBroker` | bool | `RequireSso` 有効時、同居プロセスとして C# broker を自動起動するかどうか。既定 `true`。 |
   | `SsoBrokerBindUrl` | string | 同居 broker のバインド URL。既定 `http://127.0.0.1:5080`。 |

   `SsoProviders`(`List<SsoProviderConfiguration>`)はこのリフレクション機構では**環境変数から設定できない**
   (`ApplyEnvironmentalOverridesTo` は非プリミティブ/非 string のクラス型フィールドを「ネストしたオブジェクト」として
   再帰するだけで、リストの要素を環境変数から組み立てる処理を持たない)。§5 の設計では `SsoProviders` を空のままにする
   (§3.4 の「追加防御」チェックは `SsoProviders` が非空の場合のみ働く任意のものであり、空でも動作上問題ない)。

### 5.1 concierge の設計

1. `POST /admin/meetings` で会議を作成する際、`MeetingKeys.Generate()` 相当のロジック(Go の `crypto/ecdh` の X25519、
   および CSPRNG による 48 バイトのランダム値、いずれも base64url-nopad エンコード)で、会議ごとに新しい
   X25519 トランスポート鍵ペアとチケット署名鍵を concierge プロセス内で生成する。
2. 生成した 3 つの値(`SsoAdmissionTicketSigningKey`、`SsoTransportPrivateKey`、`SsoTransportPublicKey`)を、
   会議 1 件につき 1 つの Kubernetes `Secret`(例: `basis-<meetingId>-sso`、キー名はフィールド名と同じ 3 つ)として
   作成する。
3. `kube.Manager.Create` が組み立てる Pod テンプレートの `basis-server` コンテナに、この Secret を
   `envFrom.secretRef`(またはキーごとの `valueFrom.secretKeyRef`)で注入し、**フィールド名と同じ環境変数名**
   (`SsoAdmissionTicketSigningKey`/`SsoTransportPrivateKey`/`SsoTransportPublicKey`)で公開する。あわせて
   `RequireSso=true`、`AutoStartSsoBroker=false` も環境変数として注入する(`AutoStartSsoBroker=false` は、
   ゲームサーバー本体が自分自身の子プロセスとして別の C# broker を起動しようとするのを防ぐため。
   `Basis/Packages/com.basis.server/Docker/docker-compose.yml` がすでに `RequireSso`/`AutoStartSsoBroker: false` を
   個別の環境変数として渡す運用実績があり、同じパターンを踏襲する)。
4. concierge 自身は、その会議向けにチケットを発行する際に必要な署名鍵の値を、Secret 作成時に自身のメモリ内
   (`MeetingRecord` 相当の内部状態、あるいは必要に応じて Kubernetes API 経由で Secret を読み戻す)に保持しておく。
   現行 C# broker のように「環境変数名」を設定ファイルに書いて間接参照する必要はない。concierge は 1 プロセスで
   多数の会議を扱うため、会議ごとに環境変数を 1 つずつ用意する現行方式(`TicketSigningKeyEnvironmentVariable`)は
   会議数のぶんだけ環境変数が必要になり非現実的である。concierge が動的に生成する会議については、鍵をプロセス内の
   会議レジストリ(id → 鍵)で直接引けるようにする。
5. `TransportPrivateKey` は concierge 自身には不要(broker はクライアントへの応答に公開鍵のみを含める)。Secret には
   ゲームサーバー Pod が必要とするため含めるが、concierge のプロセス内メモリには公開鍵のみを保持すればよい。

静的サーバー(`Servers[]` 由来、concierge が Agones で管理しない既存デプロイ)については、現行同様
`TicketSigningKeyEnvironmentVariable`/`TransportPublicKeyEnvironmentVariable`(concierge プロセス自身の環境変数名の
間接参照)または設定ファイル内のリテラル値のいずれかを設定できる互換パスを残す。

## 6. 互換性要件

`research-sso-broker.md` §3.3・§8 で確認した、無改変の C# Unity クライアントおよび無改変の C# UDP サーバーとの
相互運用に必要な項目を、そのまま concierge が満たすべき要件として転記する。1 つでも欠けると、ワイヤレベルで非互換になる。

1. **チケット形式**(`Ticket.Create` / `SsoAdmissionTicket.TryValidate`):
   `base64url_nopad(UTF8("basis-sso-ticket-v2\n{unixExpirySeconds}\n{guidN}\n{issuer}\n{subject}\n{did}")) + "." + base64url_nopad(HMAC_SHA256(signingKeyUtf8Bytes, thatBody))`
   - 厳密に 60 秒(1 分)の寿命を署名時に計算する。
   - `ticketId` は小文字16進・ハイフンなしの GUID(`Guid.NewGuid().ToString("N")` 相当、32 文字の16進)でなければならない。
   - フィールドは素の `\n`(`\r\n` ではない)で結合し、HMAC 計算前に UTF-8 エンコードする。
   - base64url アルファベット: 標準 base64 の `+`→`-`、`/`→`_`、パディングなし(両セグメントとも `=` を除去)。
2. **JWT 検証の制約**: `alg:"RS256"` のみを受け付ける。issuer は設定済み `Issuer` に対する**厳密な文字列完全一致**
   (末尾スラッシュの正規化なし、大文字小文字の畳み込みなし)。audience は文字列または配列のいずれでもよい。JWKS は
   リクエストごとに新規取得する(キャッシュなし)。RSA の `n`/`e` は JWKS JSON から直接取り出す(base64url、
   RFC 7517 準拠のパディングなし)。署名検証は文字通りの `header.payload` ASCII 部分文字列(再シリアライズしたもの
   ではない)に対する PKCS1v1.5/SHA256。
3. **HTTP リクエスト/レスポンスの JSON フィールド名**(§2 の全エンドポイントについて、大文字小文字を含め厳密に):
   `idToken`、`did`、`ticket`、`url`、`meetingId`、`expiresInSeconds`、`defaultProviderId`、
   `serverTransport.serverPublicKey`、`serverTransport.admissionEndpoint`、
   `serverTransport.allowUntrustedLoopbackCertificate`、
   `providers[].id/label/issuer/clientId/clientSecret/scopes/displayNameClaims/access.allowedGroups/access.allowedClaims[].claim/values`、
   `redirect.mode/host/port/path`、`enforcement.allowOfflineWithinTokenValidity`、会議の
   `status/statusDetail/host/port/createdAt/updatedAt/joinUrl/invitationReady`、`/admin/servers` と `/health` の
   `id/ready/providers`。クライアント側モデル(`BasisOidcConfig.cs`)はこれらの名前に対して Newtonsoft.Json で
   デシリアライズする。
4. **`Cache-Control: no-store`** を `/admission/{serverId}` の全レスポンスに(処理開始時点で無条件に)付与する。
5. **管理者認証**: `Bearer ` プレフィックス(大文字小文字を区別しない)、定数時間トークン比較、32 文字以上の
   最小長 — `AdminAuthorized` の挙動を厳密に再現する。
6. **`basisdemo://` ディープリンク URL 形式**:
   `basisdemo://{host-or-[ipv6]}:{port}?password={urlencoded}&meeting={urlencoded}`。クエリパラメータ名
   `password`/`meeting` を維持する。
7. **ループバックブリッジの契約**: 固定ポート `56831`、固定パス `basis-sso-config`/`basis-join`、クエリパラメータ名
   `url`/`config`/`link`。join ページの iframe が期待する `postMessage('basis-join-received')` の契約も維持する。
8. **X25519 公開鍵のエンコード**: 生の 32 バイト点を base64url-nopad したもの。
9. **チケット署名鍵**: 32 文字以上の任意の文字列であれば「設定済み」とみなす(`HasTicketSigningKey`)が、新規生成する
   鍵は 48 バイトのランダム値を base64url エンコードしたもの(約 64 文字)にする(`BasisSsoTransportKeys.Ensure`の
   サーバー側生成器との一貫性のため)。
10. **`RemoveSecrets` の再帰的除去**: JSON オブジェクトキー名が(大文字小文字を区別せず)`"clientSecret"` に一致する
    ものを、ネストしたオブジェクト・配列も含めて再帰的に取り除く(`GET /client-config/{serverId}` の出力)。
11. **アトミックなファイル書き込み**: 永続化するファイルはすべて `path + ".tmp"` へ書いてからリネームする。
12. **`control-plane.json` は PascalCase フィールド名を使う**(既存デプロイのファイルを読む場合)。
    `Id`、`Title`、`Status`、`StatusDetail`、`Host`、`Port`、`Password`、`InviteToken`、`TicketSigningKey`、
    `TransportPrivateKey`、`TransportPublicKey`、`CreatedAt`、`UpdatedAt`、ルートは `{"Meetings":[...]}`。
13. **招待トークンの比較は定数時間**でなければならない(`FindInvite`)。管理者ベアラートークンの比較も同様。
14. **会議 `Host` のホスト安全性検証**(`IsSafeHost`): `/ ? # ` や空白を含む場合は拒否、長さ 1〜253、`[`/`]` を
    ストリップした上で DNS 名/IPv4/IPv6 のいずれかとして妥当であること。
15. **`/health` の readiness 定義**: 設定済みの**全**サーバーが個別に ready であることを AND で判定する(OR ではない)。

## 7. API 設計

### 7.1 既存 admin/public API との互換

`research-sso-broker.md` §2 に列挙した全エンドポイント(公開/クライアント向け 9 本、管理者向け 11 本)を、パス・
メソッド・ボディ・レスポンス形状・ステータスコードを含めて concierge がそのまま提供する。`api/openapi.yaml` に
これら全エンドポイントを記述し、basis-k8s と同じく `oapi-codegen`(`std-http-server` + `strict-server` + `models`)で
Go の型とルーティングを生成する。RFC 7807 `application/problem+json`(409/501/503)と手組みの `{error}`(400)の
使い分けも仕様として明記する。

### 7.2 部屋管理 API の統合

basis-k8s の `/servers`(`POST`/`GET`/`GET {name}`/`DELETE`)相当の操作は、独立した別 API としては公開しない。
理由: concierge の会議(meeting)は「SSO の入場審査対象」と「Agones 上の実体」の両方を表す 1 つの概念であり、
これを 2 つの別々の API(会議管理 API と部屋管理 API)に分けると、両者が同期しなくなるリスクがある(例: 部屋だけ
削除されて会議メタデータが残る)。そのため、basis-k8s の 4 操作は既存の管理 API に統合する。

- `POST /admin/meetings` — 会議作成 + (concierge 管理対象の場合)Agones GameServer 作成を 1 操作で行う(§4.2)。
- `DELETE /admin/meetings/{meetingId}` — 会議削除 + GameServer 削除を 1 操作で行う(§4.3)。
- `GET /admin/meetings` — 既存のレスポンス形状(`MeetingView`)に、GameServer の現在状態
  (basis-k8s の `ServerStatus.state`/`address`/`ports` に相当する情報)を任意で追加する拡張を検討する
  (既存フィールドの意味は変えず、新規フィールドの追加のみに留める。互換性要件 §6-3 を壊さない)。
- basis-k8s のような**部屋単体の直接操作**(concierge が管理していない GameServer を直接叩く用途)は、
  現時点では concierge のスコープに含めない。すべての部屋操作は会議(meeting)のライフサイクル経由で行う。

## 8. 設定

現行の `appsettings.json`(`Broker` セクション)と環境変数を、concierge でどう表現するかの対応表を示す。

### 8.1 環境変数・設定ファイルの対応表

| 現行(C# broker) | concierge での扱い |
|---|---|
| `appsettings.json` `Broker.PublicBaseUrl` | 設定ファイルの同名相当キー。挙動(HTTPS 限定、非 HTTPS は無視)を維持。 |
| `appsettings.json` `Broker.ClientConfigDirectory` | 同左。 |
| `appsettings.json` `Broker.AdminTokenEnvironmentVariable` | 同左(環境変数名を指す設定は維持。concierge プロセス自身が読む)。 |
| `appsettings.json` `Broker.AllowUnauthenticatedAdmin` | 同左。 |
| `appsettings.json` `Broker.Servers[]`(`BrokerServerOptions`) | 「静的サーバー」として §4.1 のレジストリの一方の入力にする。フィールド構成は現行と同じ。 |
| `appsettings.json` `Broker.Organization` | 同左。 |
| `BASIS_SSO_BROKER_CONFIG_PATH` | 同名の環境変数、または concierge 独自の設定ファイルパス環境変数として維持(名称は既存踏襲を基本とし、変更する場合はユーザーに確認する)。 |
| `BASIS_CONTROL_PLANE_STORE_PATH` | 同左(`control-plane.json` 相当ファイルの保存先)。 |
| `BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS` | 同左。 |
| `BASIS_MEETING_PUBLIC_HOST` / `SetPort` / `Password`(`local` ブートストラップ会議用) | 静的な `local` 会議のブートストラップとして維持するか、concierge では明示的な `Servers[]`/`Organization` 設定のみに寄せて廃止するかは、既存の Compose/systemd デプロイとの互換性次第であり要検討(§12 未確認事項)。 |
| `BASIS_SERVER_CONFIG` / `BASIS_SSO_CONFIG_WAIT_SECONDS`(Docker サイドカーモード用) | concierge が Agones 経由で会議ごとに独自に鍵を生成・注入する設計(§5)では、この「ゲームサーバーの config.xml から鍵をスクレイプする」モードは Agones 管理下の部屋には不要になる。静的サーバー運用でのみ引き続き有効。 |
| （新規）`NAMESPACE` | Agones GameServer を作成する Kubernetes 名前空間。basis-k8s の同名環境変数を踏襲(既定 `basis`)。 |
| （新規)`KUBECONFIG` | basis-k8s と同じ kubeconfig 解決順序(in-cluster 優先、フォールバックで `$KUBECONFIG`→`$HOME/.kube/config`)。 |

### 8.2 設定ファイル

- 静的サーバー/組織設定は、現行の `appsettings.json` の `Broker` セクションに相当する JSON(または YAML)ファイルを
  引き続き使う。フィールド名・ネスト構造は §6-3 の互換性要件を壊さない範囲で維持する。
- `control-plane.json` 相当のファイルは、既存デプロイからの読み込み互換のため PascalCase フィールド名を維持する
  (§6-12)。ただし、Agones 管理下の会議については、GameServer 自体が Kubernetes 上の実体として存在するため、
  concierge 側のファイルへの永続化に加えて、Kubernetes API を信頼できるソースとして扱うかどうかは実装時に検討する
  (§12 未確認事項)。

## 9. AdminUi

既存の Cloudscape AdminUi(`Basis/Tools/BasisSsoBroker/AdminUi`)はコードを変更せず、ビルド済み静的アセットを
concierge が自ら配信する。

- AdminUi は Vite で `base: "/admin/"` としてビルドされ、`fetch('/api' + path)` で API を呼ぶ(`api.ts`)。
- concierge は次の 2 つを提供する。
  - `GET /admin/` 以下: ビルド済み静的ファイルの配信(`try_files` 相当、SPA のクライアントサイドルーティングに
    対応するため未知のパスは `index.html` にフォールバックする)。
  - `/api/*`: プレフィックスを取り除いた上で、concierge 自身の管理 API ハンドラ(`/admin/...`)へ内部的にディスパッチ
    する。これは現行 Nginx(`nginx.conf`)が行っていた `/api/*` → broker `/` へのプレフィックス除去プロキシと
    同じ役割を、concierge 内部のルーティングとして持つ。
- 既存の TLS 終端専用 Nginx コンテナ(自己署名 CA 生成を含む)は concierge の設計に含めない。TLS 終端は運用環境の
  リバースプロキシ/Ingress に委ねる(現行の「TLS は常に外部で終端する」という前提を維持する。§6 相当の設計はここでは
  変更しない)。
- `research-sso-broker.md` §4.3・§8-3 で指摘した「AdminUi が `Authorization: Bearer` ヘッダーを送らない」既知の問題は、
  concierge でも AdminUi のコード自体を変更しない限り解消しない。運用上は `AllowUnauthenticatedAdmin=true` を
  信頼できるネットワーク内でのみ使うか、リバースプロキシ側でヘッダーを注入する運用を維持する必要がある。この点は
  AdminUi 自体の改修を伴うため、concierge の実装スコープからは切り離し、必要であれば別途ユーザーに確認する。

## 10. テスト・検証計画

### 10.1 Go 単体テスト

- `internal/admission`: JWT 検証(RS256 限定、iss/aud/exp/policy チェック、JWKS 取得失敗時のフェイルクローズ)を
  テーブル駆動でテストする。
- **ワイヤ互換のゴールデンテスト**: `basis-sso-ticket-v2` のチケット生成について、既知の入力(固定の
  signingKey/expiry/ticketId/issuer/subject/did)に対する出力バイト列を C# 実装から採取した既知の期待値と突き合わせる
  ゴールデンテストを用意する。HMAC 計算・base64url エンコード(パディングなし、`+`/`/` の置換)・フィールド区切りの
  `\n` を 1 バイトも違わずに再現できているかを検証する。同様に `CreateClientConfiguration` が出力する JSON の
  フィールド名・ネスト構造についても、既知の入力に対する出力 JSON をスナップショット比較する。
- 管理者認証(`AdminAuthorized` 相当)の定数時間比較・32 文字最小長・大文字小文字を区別しない `Bearer` プレフィックスを
  テストする。
- `RemoveSecrets`(`clientSecret` の再帰除去)を、ネストしたオブジェクト・配列を含む JSON でテストする。

### 10.2 fake clientset テスト

basis-k8s の手法をそのまま踏襲する。`internal/kube` は Agones のフェイククライアントセット
(`agones.dev/agones/pkg/client/clientset/versioned/fake`)を使い、GameServer の作成・削除・一覧に加えて、concierge
独自の Secret 作成・削除(`k8s.io/client-go/kubernetes/fake`)もあわせてテストする。`internal/api` は `httptest` を
使ったフル HTTP ラウンドトリップテストで、会議作成 → Secret 作成 → GameServer 作成の一連の呼び出し順序と、
エラー時のロールバック(例: GameServer 作成失敗時に Secret も削除する)を検証する。モックフレームワーク/testify は
使わず、basis-k8s と同じく stdlib の `testing` のみで書く。

### 10.3 minikube + Agones での動作確認

ローカルの minikube 環境で、実際の Agones インストールに対する結合確認を行う手順の概要は次のとおり。

1. minikube を起動し、Agones を公式手順(basis-k8s の `docs/setup.md` が参照する
   `https://agones.dev/site/docs/installation/`)に従ってインストールする。
2. concierge の `deploy/`(basis-k8s の `deploy/` 相当。Namespace/RBAC/Deployment/Service)を適用する。
3. `basis-server:latest` イメージ(`Basis/Packages/com.basis.server/Docker/Dockerfile`)を minikube のローカル
   Docker デーモンにビルドする(`eval $(minikube docker-env)` 経由、または `minikube image load`)。
4. `POST /admin/meetings` で会議を作成し、対応する Secret と GameServer が作成されることを `kubectl get gameservers`/
   `kubectl get secrets` で確認する。
5. GameServer が `Ready` になり `Status.Address`/`Status.Ports` が確定するまで待ち、`GET /admin/meetings` の host/port
   が `"provisioning"` から実際の値に遷移することを確認する。
6. 実際の Basis クライアント(または最小限の UDP テストクライアント)から、発行された `basisdemo://` リンクまたは
   `/join/{token}/manifest` の接続情報を使って接続できることを確認する(SSO 入場審査 → チケット発行 → UDP 接続 →
   サーバー側でのチケット検証、までのエンドツーエンド確認)。
7. `DELETE /admin/meetings/{meetingId}` で GameServer と Secret が両方削除されることを確認する。

## 11. 非スコープ

- **C# broker の削除は行わない。** concierge は既存の C# broker と共存する新規サービスであり、既存の C# broker の
  コード・デプロイ手順・ドキュメントを削除・非推奨化する作業はこの設計のスコープに含めない。
- **スケーリング・アイドル停止は対象外。** basis-k8s 同様、Fleet/Fleet Autoscaler のようなプールベースのスケーリング、
  部屋の自動アイドル検出・停止、ウォームスタンバイの仕組みは設計しない。会議(部屋)は作成リクエストのたびに
  同期的・個別に 1 つの GameServer として作成される。

## 12. 未確認事項

以下は `broker-spec.md`/`basis-k8s-report.md`/現行コードのいずれからも断定できず、実装前にユーザーへ確認するか、
実装時に別途調査が必要な事項として明示する。

- `local` ブートストラップ会議(`BASIS_MEETING_PUBLIC_HOST`/`SetPort`/`Password` による自動登録)を concierge でも
  同じ形で維持するか、それとも `Servers[]`/`Organization` の明示設定のみに一本化するかは未確認。既存の Compose
  デプロイとの後方互換性に関わるため、ユーザーへの確認が必要。
- `control-plane.json` 相当のファイル永続化と、Kubernetes 上の GameServer/Secret という「実体」との間で、どちらを
  信頼できるソース(source of truth)とするか(ファイルが真実か、Kubernetes API が真実か、両者をどう整合させるか)は
  未確認。特に concierge プロセスが再起動した場合に、ファイルの内容と実際に存在する GameServer 群が食い違うケースの
  扱いを設計時に詰める必要がある。
- Agones の GameServer 作成から `Ready`(host/port 確定)までの典型的な所要時間、および万一 `Ready` に到達しないまま
  タイムアウトした場合に concierge がどう振る舞うべきか(エラーとして会議を失敗状態にする、リトライする等)は
  未確認。basis-k8s 自体もこの点を扱っていない(同期的に作成するのみ)。
- 既存の `BasisSsoBrokerProcess.cs`(単体 Basis サーバーの子プロセスとして broker を同居起動する方式)を concierge が
  代替するのか、それとも維持するのかは未確認。concierge は主に Agones 管理下の部屋を想定しているが、単体運用の
  Basis サーバーが concierge を子プロセスとして同居起動する運用を求められるかどうかは、ユーザーへの確認が必要。
