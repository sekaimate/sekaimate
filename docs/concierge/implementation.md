# concierge 実装ドキュメント

最終更新: 2026-08-22

`concierge/` の実装内容をまとめる。§1〜§8 は phase 1(Kubernetes 統合を含まない、ワイヤ互換の Go 版 Basis SSO
Broker)の内容、§9 は phase 2(Agones/Kubernetes 統合)の内容。設計判断の根拠は `design.md`、互換性要件の出典は
`research-sso-broker.md` と `Basis/Tools/BasisSsoBroker/` の C# 実装そのものを参照。

## 1. パッケージ構成

```
concierge/
├── go.mod, go.sum          module github.com/sekaimate/sekaimate/concierge, go 1.26.5(agones.dev/agones の要求により phase 2 で 1.26.3 から引き上げ)
├── api/
│   └── openapi.yaml         公開/管理 API 全エンドポイントの単一の情報源
└── internal/
    ├── admission/           OIDC ID トークン検証 + basis-sso-ticket-v2 発行
    │   ├── ticket.go           チケットのバイト列生成(ticket.go の createTicketFields がワイヤ形式の本体)
    │   └── token.go            RS256 限定の JWT 検証、JWKS を毎回フェッチ(キャッシュなし)
    ├── config/              appsettings.json の Broker セクション相当(静的サーバーレジストリ + 組織設定)
    │   ├── config.go           BrokerConfig/ServerConfig/ProviderConfig/OrganizationConfig、Store(mutex 付き)
    │   └── http.go             管理者認証(定数時間比較)、RequestOrigin
    ├── controlplane/        ControlPlane.cs 相当(会議/登録トークンのストア、id・鍵生成)
    │   ├── meeting.go          MeetingRecord、Store(control-plane.json 相当、PascalCase, 0600)
    │   ├── enrollment.go       EnrollmentStore(インメモリ、10 分 TTL、単発使用)
    │   ├── identity.go         NewID/IsValidID/RandomToken/IsSafeHost 等
    │   └── keys.go             GenerateMeetingKeys(X25519 + 48 バイト HMAC 鍵)
    ├── kube/                RoomProvisioner とその Agones 実装(§4, §9 参照)
    │   ├── provisioner.go      RoomProvisioner インターフェース、NoopProvisioner
    │   ├── manager.go          Manager(Agones GameServer + Secret のプロビジョニング、§9.2/§9.3)
    │   ├── reconcile.go        Manager.Reconcile(起動時整合性チェック、§9.5)
    │   └── config.go           ResolveRESTConfig(kubeconfig 解決、§9.7)
    ├── adminui/             AdminUi 静的配信 + /api/* パススルー(§5 参照)
    │   └── adminui.go
    └── api/                 oapi-codegen 生成コード(server.gen.go)+ ハンドラ実装
        ├── cfg.yaml, doc.go, server.gen.go   生成物(手で編集しない。§2 参照)
        ├── deps.go              Deps/serverAPI/NewMux
        ├── admission.go         POST /admission/{serverId}
        ├── health.go            GET /health
        ├── organization.go      /admin/servers, /admin/organization
        ├── meetings.go          /admin/meetings*
        ├── enrollment.go        POST /admin/enrollment/{serverId}
        ├── enroll.go            GET /enroll/{token}*
        ├── join.go              GET /join/{token}*
        ├── clientconfig.go      /client-config/*, /admin/client-config*
        ├── convert.go           内部型 <-> API 型の変換
        ├── html.go              enroll/join ページの HTML テンプレート
        ├── respond.go, urlescape.go, util.go   レスポンス/エスケープ共通処理
        └── cmd/server/main.go   エントリーポイント(concierge/cmd/server/main.go、上記ツリー外)
```

`cmd/server/main.go` はリポジトリ構成上 `concierge/cmd/server/main.go` に置かれている(上のツリーでは省略)。

## 2. ビルド・実行・テスト

```sh
cd concierge

# API 仕様(api/openapi.yaml)を変更した場合のみ再生成する。
go generate ./...

go build ./...
go test ./...
gofmt -l .        # 何も出力されなければ整形済み
go vet ./...
```

サーバーの起動:

```sh
BASIS_SSO_BROKER_CONFIG_PATH=./appsettings.json \
BASIS_CONTROL_PLANE_STORE_PATH=./control-plane.json \
LISTEN_ADDR=:5080 \
go run ./cmd/server
```

`api/openapi.yaml` の再生成は basis-k8s と同じ手順([`docs/codegen.md`](../../../基準リポジトリ内 basis-k8s 参照)相当)
に従う。`oapi-codegen` は `go.mod` の `tool` ディレクティブで管理されているため、`go generate ./...` 実行前に
`go get -tool` を個別に行う必要はない(モジュールを取得済みの環境であれば `go generate` だけで動く)。

生成モードは basis-k8s と異なり **`std-http-server` + `models` のみ**(`strict-server` は使わない)。理由は §2 参照。

## 3. oapi-codegen の生成モードについて(basis-k8s との差分)

`design.md` §7.1 は basis-k8s と同じ `std-http-server + strict-server + models` を踏襲する方針を示しているが、
concierge の実装では **`strict-server` を使わなかった**。

理由: `strict-server` は各エンドポイントのレスポンスを型付きの `XxxResponseObject` として表現する生成モードで、
basis-k8s のような JSON 専用 API には適しているが、concierge の公開 API は JSON に加えて HTML ページ
(`/enroll/{token}`, `/join/{token}`, `/join/{token}/open`)、生ファイル配信
(`GET /admin/client-config/{serverId}`)、RFC 7807 と手組み `{error}` の使い分けなど、レスポンス形状が
エンドポイントごとに大きく異なる。`strict-server` でこれらを表現するには結局レスポンス型ごとに手書きの
`VisitXxxResponse` 実装が必要になり、`std-http-server`(ルーティングとパスパラメータのバインドのみ生成し、
レスポンスの組み立てはハンドラに任せる)より複雑になる割に得られる型安全性の向上が小さいと判断した。

`std-http-server + models` でも、ワイヤ互換性上の要件(パス・メソッド・ステータスコード・JSON フィールド名)は
すべて `internal/api/*.go` のハンドラ実装で明示的に満たしている(§6 の互換性チェックリスト参照)。この差分は
コード生成戦略の選択であり、ワイヤフォーマットには影響しない。

## 4. `internal/kube.RoomProvisioner`(phase 2 への接続点)

```go
type RoomProvisioner interface {
    Create(ctx context.Context, meetingID string, keys RoomKeys) error
    Delete(ctx context.Context, meetingID string) error
}
```

`POST /admin/meetings`(`internal/api/meetings.go` の `CreateMeeting`)は、会議レコードとサーバーレジストリエントリを
永続化した直後にこのインターフェースの `Create` を呼び出す(失敗時は両方をロールバックする)。`DELETE /admin/meetings/{id}`
は `Delete` を呼び出す。phase 1 は `kube.NoopProvisioner`(`cmd/server/main.go` で配線)のみを提供し、両メソッドとも
何もせず `nil` を返す。

つまり phase 1 の `POST /admin/meetings` の挙動は、現行 C# broker と完全に同じである。`host` を指定すれば
その場で `status="ready"`、指定しなければ `status="provisioning"` のまま返る。GameServer の作成・削除は一切発生しない。

phase 2 で Agones 対応の `Manager` を実装する際は、`RoomProvisioner` インターフェースを満たす実装を書いて
`cmd/server/main.go` の `kube.NoopProvisioner{}` を差し替えるだけでよく、`internal/api` 側のハンドラ・ロールバック
ロジックは変更不要になるように設計してある。

## 5. AdminUi の配信について(設計からの逸脱)

`design.md` §9 は「既存の Cloudscape AdminUi のビルド済み静的アセットを concierge が配信する」ことを想定しているが、
このリポジトリの `Basis/Tools/BasisSsoBroker/AdminUi/` には **ソースのみ**が存在し、コミットされたビルド成果物
(`dist/` 等)は存在しない。concierge の実装スコープには Node.js/Vite のビルドパイプラインは含まれないため、
ビルド成果物をこの phase で新規に生成・コミットすることはしなかった。

代わりに、`internal/adminui.Mount` は環境変数 `ADMIN_UI_DIR` で指定した任意のディレクトリ(オペレーターが
`pnpm build` 等で生成した `dist/` を指す想定)を `/admin/` 以下に配信する。`ADMIN_UI_DIR` 未設定時は `/admin/` への
アクセスは 404 とその理由を返す(サイレントに何も配信しない、という状態を避けるため)。SPA のクライアントサイド
ルーティングに対応するため、ディスク上に存在しないパスは `index.html` にフォールバックする。

`/api/*` は同じマルチプレクサ上で `http.StripPrefix("/api", ...)` によりプレフィックスを除去したうえで
`/admin/...` を含む全ハンドラへ再ディスパッチする(既存 Nginx の `/api/* -> broker/`、それ以外 `-> broker`
というプロキシ構成を、外部プロセスなしで concierge プロセス内のルーティングとして再現している)。

## 6. 設定(環境変数)

| 環境変数 | 既定値 | 説明 |
|---|---|---|
| `BASIS_SSO_BROKER_CONFIG_PATH` | `{実行ファイルのディレクトリ}/appsettings.json` | `{"Broker": {...}}` 形式の設定ファイル。既存デプロイと同じパス変数名・同じ JSON 構造(PascalCase)を維持。 |
| `BASIS_CONTROL_PLANE_STORE_PATH` | `{実行ファイルのディレクトリ}/control-plane.json` | 会議(meeting)の永続化ファイル。`{"Meetings":[...]}`、PascalCase フィールド。 |
| `BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS` | (未設定) | `"true"`(大文字小文字を区別しない)でない限り `POST /admin/meetings` は 501。 |
| `BASIS_MEETING_PUBLIC_HOST` | 空文字 | `local` ブートストラップ会議の公開ホスト。 |
| `SetPort` | `4296` | `local` 会議のポート。 |
| `Password` | 空文字 | `local` 会議のパスワード。 |
| `ADMIN_UI_DIR` | (未設定 = `/admin/` は 404) | ビルド済み AdminUi 静的アセットのディレクトリ(§5 参照、新規)。 |
| `LISTEN_ADDR` | `:5080` | バインドアドレス(新規、basis-k8s の慣例に合わせた)。 |
| `ASPNETCORE_URLS` | — | `LISTEN_ADDR` 未設定時のフォールバックとして 1 つ目の URL のホスト:ポートを解釈する(既存デプロイからの移行を容易にするための任意対応、新規)。 |
| `KUBECONFIG` / `NAMESPACE` | — / `basis` | phase 2 で有効化(§9.7)。basis-k8s と同じ解決順序(in-cluster → `$KUBECONFIG` → `$HOME/.kube/config`)。Kubernetes 設定が見つからない場合は `NoopProvisioner` にフォールバックし、phase 1 と同じ挙動になる。他の Kubernetes 関連の環境変数(`BASIS_SERVER_IMAGE` 等)は §9.7 を参照。 |

`appsettings.json` の `Broker` セクション、`BrokerServerOptions`(`Servers[]` の各要素)、`ProviderOptions` の
フィールド名は現行 C# broker と同じ PascalCase を維持している(`internal/config/config.go` の struct タグ参照)。

## 7. 設計からの逸脱・注記

- **`strict-server` を使わない**(§3 参照)。
- **AdminUi はビルド成果物を同梱せず、`ADMIN_UI_DIR` で配信先を指定する運用にした**(§5 参照)。
- **`appsettings.json` を 0600 に chmod するようにした**(C# 版は `control-plane.json` のみ chmod し、
  `appsettings.json` は chmod していない。`research-sso-broker.md` §8 がこれを是正すべき欠陥として明示的に
  指摘しているため、サイレントに再現せず修正した)。
- **`DELETE /admin/servers/{id}` と `DELETE /admin/meetings/{id}` の実装順序を修正した。** C# 版はサーバー
  レジストリからエントリを削除した *後* に `ClientConfigPath` を解決しており、`ClientConfigPath` はサーバーが
  レジストリに存在することを要求するため、削除後は常に `null` を返し、クライアント設定ファイルの削除が
  実質的に一度も実行されない(未文書化のオーダリングバグ)。concierge はパスを *削除前* に解決することで、
  意図されていたとおりファイル削除が動作するようにした。
- **JWT の base64url セグメントで長さ%4==1 を明示的にエラーとして拒否する。** C# 実装は独自のパディング処理で
  この長さをサイレントに誤デコードする(`research-sso-broker.md` §2.2 step 2 が Go 移植ではエラーとすべきと
  明記)。Go 標準の `base64.RawURLEncoding` を使うことで自然にこの修正を得ている。
- **`PUT /admin/client-config/{serverId}` のボディ読み取りに `http.MaxBytesReader` を追加した。** C# 版は
  `Content-Length` ヘッダーのみで 262144 バイト上限をチェックし、実際の読み取り量は制限していない。
  `Content-Length` を偽った場合の際限のない読み取りを防ぐため、Go 版は読み取り自体にも同じ上限を課している。
  正直な `Content-Length` を送るクライアントから見た挙動は変わらない。
- **JSON レスポンスの省略可能フィールドは、値が空/未設定のとき `null` ではなくキー自体を省略する。**
  C# の `System.Text.Json` 既定シリアライズは(`DefaultIgnoreCondition` 未設定のため)`null` 値でもキーを出力するが、
  concierge の生成モデル(`omitempty` を使用)は省略する。フィールド名・型は変わらないため、名前ベースで
  デシリアライズする既存クライアント(`BasisOidcConfig.cs` の Newtonsoft.Json モデル等)には影響しない。
- **`IsSafeHost` の DNS 名判定は `Uri.CheckHostName` の再実装であり、バイト完全一致ではない。** RFC 1123 ラベル
  文法に基づく正規表現での近似実装(`internal/controlplane/identity.go`)。これは管理者が入力する接続ホストの
  安全性チェックであり、クライアント/サーバー間のワイヤ互換性チェックリスト(`research-sso-broker.md` §7)には
  含まれない。

## 8. phase 2 が把握しておくべきこと(phase 1 完了時点の申し送り)

この節は phase 1 完了時点で書かれた申し送り事項であり、当時の未実装状態をそのまま記録している。実際に
phase 2 で何を実装したか(この節で「未実装」としていた項目がどう解消されたかを含む)は §9 を参照。

- **Kubernetes/Agones 統合は一切ない。** `internal/kube` には `RoomProvisioner` インターフェースと `NoopProvisioner`
  のみが存在する。`KUBECONFIG`/`NAMESPACE` の解決、GameServer/Secret の作成・削除、basis-k8s の `internal/kube.Manager`
  の移植は phase 2 のスコープ。
- **`design.md` §12 の決定事項 2/3 (source of truth は Kubernetes API、Ready 待ちタイムアウト 120 秒既定)は
  phase 2 で実装する。** phase 1 には突き合わせ処理もポーリングもタイムアウトも存在しない。
- **`POST /admin/meetings` はまだ非同期のバックグラウンド更新を行わない。** `design.md` §4.2 の「(非同期)
  バックグラウンドで GameServer の状態をポーリングし...`MeetingRecord` を更新する」というステップは phase 2 の
  `RoomProvisioner` 実装(および `controlplane.Store.UpdateStatus` の呼び出し元)が担う。`UpdateStatus` 自体は
  phase 1 から実装済みで、ストアテスト(`internal/controlplane/meeting_test.go`)でカバーしている。
- **静的サーバー(`Servers[]`)と concierge 管理下の会議の id 重複チェックは実装済み。** `POST /admin/meetings` の
  id 採番(`controlplane.NewID`)は `controlplane.Store.Exists` と `config.Store.FindServer` の両方を見て重複を
  避ける(`design.md` §4.1 の「同一 id が両方のソースに存在する場合は起動時にエラーとして拒否する」という要件のうち、
  ここでは「新規作成時に重複させない」側のみ実装済み。**起動時の突き合わせ検証(既存の静的設定ファイルと
  control-plane.json の間で id が重複していたら拒否する)はまだ実装していない** — 追加が必要であれば phase 2 の
  スコープに含めるか、ユーザーに確認すること。
- **`AdminUi` の `Authorization` ヘッダー未送信問題は解消していない。** `research-sso-broker.md` §4.3/§8-3 で
  指摘されている既知の問題で、AdminUi 自体のコード改修が必要なため、concierge の実装スコープからは意図的に
  切り離している(`design.md` §9 の記載どおり)。

## 9. phase 2: Kubernetes/Agones 統合

phase 1 で `internal/kube.RoomProvisioner`/`NoopProvisioner` のみだった `internal/kube` に、Agones バックエンドの
実装(`Manager`)を追加した。`internal/api` のハンドラ(`meetings.go` の `CreateMeeting`/`DeleteMeeting`)は変更して
いない — `RoomProvisioner` インターフェース(`Create`/`Delete` のシグネチャ)は phase 1 のまま。

### 9.1 追加したファイル

```
concierge/
├── Dockerfile                      2 段階ビルド(golang:1.26.5 → gcr.io/distroless/static:nonroot)
├── deploy/
│   ├── 00-namespace.yaml            Namespace basis
│   ├── 10-rbac.yaml                 ServiceAccount/Role/RoleBinding concierge
│   ├── 20-deployment.yaml           Deployment concierge + PersistentVolumeClaim concierge-data
│   └── 30-service.yaml              Service concierge(ClusterIP)
├── cmd/server/main.go               checkNoStaticMeetingIDCollision, buildProvisioner を追加(既存関数は変更なし)
└── internal/kube/
    ├── manager.go                   Manager(RoomProvisioner の Agones 実装)
    ├── manager_test.go
    ├── reconcile.go                 Manager.Reconcile(起動時整合性チェック)
    ├── reconcile_test.go
    └── config.go                    ResolveRESTConfig(kubeconfig 解決)
```

### 9.2 `internal/kube.Manager`(`RoomProvisioner` の Agones 実装)

`Manager.Create(ctx, meetingID, keys)` は次を **同期的に** 行う(`design.md` §4.2 のステップ 3〜5 に対応)。

1. Secret `basis-<meetingId>-sso` を作成する。データキーは Basis `Configuration` のフィールド名そのもの
   (`SsoAdmissionTicketSigningKey`/`SsoTransportPrivateKey`/`SsoTransportPublicKey`)。`envFrom.secretRef` で
   参照すると、各キーが同名の環境変数としてコンテナに公開される(`design.md` §5.1 の設計どおり)。
2. GameServer `basis-<meetingId>` を作成する。`basis-server` コンテナに上記 Secret を `envFrom` で注入したうえで、
   個別の環境変数として `RequireSso=true`・`AutoStartSsoBroker=false`・`SetPort=<ContainerPort>` を設定する
   (`SetPort` は design.md に明記された 3 変数には含まれないが、GameServer の `ContainerPort` と実際にコンテナが
   listen するポートが食い違わないようにするための追加。`BASIS_SERVER_PORT` でデフォルト 4296 以外に変更した場合も
   自動的に一致する)。
3. GameServer の作成に失敗した場合、直前に作成した Secret を削除してロールバックする(孤立 Secret を残さない)。
4. `agones-ready` サイドカー(`curlimages/curl:latest`)を basis-k8s と同一のスクリプトで注入する。Basis Server は
   Agones SDK を自前で統合していないため、このサイドカーがローカルの Agones SDK HTTP ゲートウェイ(`:9358`)に対して
   `POST /ready` を成功するまでリトライし、以後 2 秒おきに `POST /health` を送り続けることで GameServer の Ready 化と
   ヘルスチェックを代行する。

GameServer/Secret のラベルは basis-k8s と同じ形(`app=basis-server`, `instance=<meetingId>`)。

`RoomKeys`(`internal/kube/provisioner.go` で定義済み)のフィールドと Secret データキーの対応は次のとおり。

| `RoomKeys` フィールド | Secret データキー = コンテナ環境変数名 |
|---|---|
| `TicketSigningKey` | `SsoAdmissionTicketSigningKey` |
| `TransportPrivateKey` | `SsoTransportPrivateKey` |
| `TransportPublicKey` | `SsoTransportPublicKey` |

### 9.3 Ready 待ち(非同期)

`design.md` §4.2 は「Secret/GameServer 作成」を同期ステップ、その後の「Ready になるまでポーリングして
`MeetingRecord` を更新する」を非同期ステップとして分けている。`RoomProvisioner` インターフェース
(`Create(ctx, meetingID, keys) error`)を変更せずにこれを実現するため、`Manager.Create` は Secret/GameServer 作成に
成功した直後、`NewManager` に渡された `*controlplane.Store` が非 nil であればバックグラウンド goroutine
(`watchReady`)を起動して即座に return する。`internal/api` 側は今までどおり `Create` の戻り値(エラーの有無)だけを
見て 201/500 を返し、以後の状態遷移には関与しない。

`watchReady` は `cfg.PollInterval`(既定 2 秒)ごとに GameServer を `Get` し、`Status.State == Ready` かつ
`Status.Address`/`Status.Ports` が確定した時点で `meetings.UpdateStatus(id, "ready", ..., address, port)` を呼ぶ。
`cfg.ReadyTimeout`(既定 120 秒、`GAMESERVER_READY_TIMEOUT_SECONDS` で変更可)を超えても Ready にならなければ
`meetings.UpdateStatus(id, "failed", ..., "", 0)` を呼んで終了する(`design.md` §12 決定事項 3 のとおり、自動リトライ
はしない)。GameServer が待機中に削除された場合(`NotFound`)は何も更新せず終了する。

### 9.4 削除フロー

`Manager.Delete(ctx, meetingID)` は GameServer → Secret の順で削除する。どちらも `apierrors.IsNotFound` で
「すでに存在しない」を許容し、その場合はエラーを返さない(`RoomProvisioner` のドキュメントコメントが要求する
「未知の meetingID に対する Delete は no-op 成功」という契約を満たす。`internal/api` の作成ロールバック経路が
これに依存している)。

### 9.5 起動時整合性チェック(`Manager.Reconcile`)

`design.md` §12 決定事項 2 のとおり、Kubernetes を concierge 管理下の会議に関する source of truth として扱う。
`cmd/server/main.go` の `buildProvisioner` が Kubernetes 統合を有効化した場合、`Manager` 構築直後に一度
`Reconcile(ctx)` を呼ぶ。

- ラベル `app=basis-server` を持つ GameServer/Secret を一覧し、`instance` ラベルから会議 id の集合を作る。
- `controlplane.Store` の全 `MeetingRecord` のうち、id が `"local"`(Compose ブートストラップ会議。`Manager.Create`
  を一度も経由しないため対象外)以外で、対応する GameServer が見つからないものは
  `UpdateStatus(id, "failed", ...)` で failed にする。
- 対応する `MeetingRecord` がない GameServer/Secret は「孤立」としてログ出力するのみで、**削除はしない**
  (どういう経緯で孤立したか分からないリソースを推測で消さないため)。

`Reconcile` の失敗(一覧取得エラー等)は `main` では致命的エラーにせず、ログに warning を出して起動を継続する
(RBAC の一時的な不整合等でサーバー全体が起動不能になるのを避けるため)。

### 9.6 起動時 id 重複チェック(`cmd/server/main.go`)

implementation.md phase 1 の §8 で「未実装」としていた、静的 `Servers[]` と `control-plane.json` の起動時 id 重複
チェック(`design.md` §4.1)を `checkNoStaticMeetingIDCollision` として実装した。`"local"` は
`bootstrapLocalMeeting` が意図的に両方に同じ id で登録する唯一の例外(`design.md` §12 決定事項 1)なので、
チェック対象から除外する。それ以外で id が両方に存在すれば `log.Fatalf` で起動を拒否する。

### 9.7 Kubernetes 統合の有効化条件と環境変数

`cmd/server/main.go` の `buildProvisioner` は `kube.ResolveRESTConfig()`(in-cluster → `$KUBECONFIG` →
`$HOME/.kube/config` の順、basis-k8s と同じ解決順序)で Kubernetes 設定が見つかった場合にのみ `Manager` を構築する。
見つからない場合は `NoopProvisioner` にフォールバックし、phase 1 と完全に同じ挙動になる(kubeconfig ファイルが
存在するのに `clientcmd.BuildConfigFromFlags` がパースに失敗した場合のみ起動時エラーとして扱う)。

| 環境変数 | 既定値 | 説明 |
|---|---|---|
| `KUBECONFIG` | (未設定なら `$HOME/.kube/config`) | kubeconfig ファイルのパス。in-cluster config が取得できない場合のフォールバック。 |
| `NAMESPACE` | `basis` | GameServer/Secret を作成する Kubernetes 名前空間。basis-k8s と同じ既定値。 |
| `BASIS_SERVER_IMAGE` | `basis-server:latest` | GameServer の `basis-server` コンテナイメージ。 |
| `BASIS_SERVER_PORT` | `4296` | GameServer が要求する Dynamic UDP ポート(`ContainerPort`)。同じ値が `SetPort` としてコンテナにも注入される。 |
| `GAMESERVER_READY_TIMEOUT_SECONDS` | `120` | `watchReady` が Ready を待つ上限秒数。超過すると会議は `failed` になり、自動リトライしない(`design.md` §12 決定事項 3)。 |

### 9.8 `Dockerfile`/`deploy/` の使い方

`deploy/` は `design.md` §3 のツリーどおり 4 ファイル構成(`00-namespace.yaml`/`10-rbac.yaml`/
`20-deployment.yaml`/`30-service.yaml`)。適用前に、コミットしていない 2 つの Secret を作成する必要がある
(`20-deployment.yaml` の先頭コメントにも同じ手順を記載)。

```sh
kubectl create secret generic concierge-config -n basis \
  --from-file=appsettings.json=./appsettings.json
kubectl create secret generic concierge-admin -n basis \
  --from-literal=token="$(openssl rand -base64 32)"
kubectl apply -f deploy/
```

- `appsettings.json` の `Broker.AdminTokenEnvironmentVariable` は `"BASIS_SSO_ADMIN_TOKEN"` にしておくこと
  (`20-deployment.yaml` がその名前の環境変数を `concierge-admin` Secret の `token` キーから注入する)。
- `control-plane.json`(会議のパスワード・鍵・招待トークンを含む実行時状態)は Secret ではなく
  `20-deployment.yaml` にバンドルした `PersistentVolumeClaim`(`concierge-data`、`/data` にマウント)に置く。
  basis-k8s と異なり concierge はローカル状態を持つため、Pod 再起動をまたいで残す必要がある。
- RBAC(`10-rbac.yaml`)は namespace スコープの `get`/`list`/`create`/`delete` のみを `agones.dev/gameservers` と
  `secrets` に付与する。`update`/`patch` は付与していない — `Manager` は作成済みの GameServer/Secret を書き換える
  ことがない(鍵のローテーションは新しい会議を作り直す形になる)ため。
- `30-service.yaml` は basis-k8s の `LoadBalancer`+MetalLB とは異なり `ClusterIP`。concierge は公開の
  admission/enroll/join エンドポイントも兼ねるため、TLS 終端を行う既存のリバースプロキシ/Ingress の背後に置く
  (`design.md` §9 の前提どおり)。

### 9.9 テスト

`internal/kube` は basis-k8s と同じ手法(`agones.dev/agones/pkg/client/clientset/versioned/fake` +
`k8s.io/client-go/kubernetes/fake`、stdlib `testing` のみ、モックフレームワーク不使用)で次をカバーする。

- `manager_test.go`: Secret/GameServer の作成内容(ラベル・`envFrom`・env・sidecar)、カスタム
  image/port、GameServer 作成失敗時の Secret ロールバック、削除(正常系・すでに存在しない場合の許容)、
  Ready 待ちの成功(`Status` を手動で更新して `MeetingRecord` が `ready`+host/port になることを確認)と
  タイムアウト(短い `ReadyTimeout` で `failed` になることを確認)。
- `reconcile_test.go`: GameServer が無い `MeetingRecord` が `failed` になること、対応する GameServer がある
  会議は変更されないこと、`"local"` 会議が対象外であること、孤立した GameServer/Secret が削除されずログのみに
  なること、`meetings` が nil の `Manager` で `Reconcile` がエラーを返すこと。
- `cmd/server/main_test.go`: `checkNoStaticMeetingIDCollision` の衝突検出・`"local"` 例外・非衝突ケース。

`go build ./...`・`go test ./...`・`go vet ./...`・`gofmt -l .` はすべてクリーン。

### 9.10 既知の注記・phase 3(minikube 検証)が把握しておくべきこと

- **`POST /admin/meetings` に `host` を明示指定した場合でも、`Manager.Create` は変わらず GameServer/Secret を
  作成する。** phase 1 のハンドラ実装(`internal/api/meetings.go`)は host の有無に関わらず常に
  `Provisioner.Create` を呼んでおり、phase 2 ではこのハンドラを変更していないため、`host` 指定は
  「concierge 管理外の接続先を上書きする」という意味には *ならない*。外部で手動運用しているサーバーを
  登録する用途には(従来どおり)静的 `Servers[]` を使うこと。この挙動は既存仕様(ハンドラの呼び出し順序)を
  そのまま維持したものであり、phase 2 で新たに導入したものではない。
- **Agones バージョン:** `go.mod` は `agones.dev/agones v1.60.0` を要求する(`k8s.io/api`・
  `k8s.io/apimachinery`・`k8s.io/client-go` は `v0.36.4`)。minikube 環境には対応する Agones リリースを
  インストールすること。
- **`basis-server` コンテナイメージ:** 既定は `basis-server:latest`(`BASIS_SERVER_IMAGE` で上書き可)。
  Basis Server 本体が `RequireSso`/`AutoStartSsoBroker`/`SsoTransportPrivateKey` 等の環境変数オーバーライドに
  対応している必要がある(`BasisServerConfiguration.cs:249-302` で確認済み)。
- **`agones-ready` サイドカー:** GameServer は Agones SDK を自前で呼ばないため、`curlimages/curl:latest`
  サイドカーが `localhost:9358` の SDK HTTP ゲートウェイに対して ready/health を代行する。minikube 側で
  Agones SDK サイドカー注入(`agones.dev/sdk` の自動注入)が有効になっていることを前提とする。
