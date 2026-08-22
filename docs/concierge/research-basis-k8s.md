# basis-k8s 調査結果

最終更新: 2026-08-22

`https://github.com/sekaimate/basis-k8s`(単一コミット `9197cfa`「ベースを作成」時点)の調査結果をまとめる。
Go module 名は `github.com/mvxproto/basis-k8s`、`go.mod` の `go` ディレクティブは `1.26.3`。
Go 移植版 concierge の設計(`design.md`)は、レイアウト・コーディング規約・部屋管理方式について本リポジトリを参考にする。

## 1. 概要

basis-k8s は Kubernetes オペレーター/コントローラー(CRD の reconcile ループを持つもの)では**ない**。Agones の
`GameServer` カスタムリソースへの薄い CRUD ラッパーとして動く、ステートレスな Go REST API サーバーである。

- Basis の部屋(room)ごとに Deployment/StatefulSet を作成することはしない。代わりに **1 つの Agones `GameServer` =
  1 つの Basis Server 部屋** という対応を取る。Pod のライフサイクル自体は Agones が(GameServer の下で Pod を作成する形で)
  管理する。
- 管理 API 自体は単一レプリカの Kubernetes `Deployment`(`deploy/20-deployment.yaml`)としてデプロイされ、
  `LoadBalancer` `Service`(`deploy/30-service.yaml`、ベアメタルでは MetalLB を使用)でフロントされる。
- すべての GameServer/部屋は単一の名前空間(既定 `basis`、`NAMESPACE` 環境変数で変更可)に存在する。部屋ごとの名前空間分離は
  ない。
- 各 `GameServer` は `basis-<instance-name>` という名前で、ルックアップ/一覧用に `app=basis-server`、
  `instance=<name>` のラベルを持つ。

## 2. 部屋作成/削除/一覧 — 実装コンポーネントと言語

- Go 実装、`net/http`(Go 1.22+ の拡張 ServeMux)を使用。HTTP ルーティング/リクエスト/レスポンス型は、OpenAPI 3.0.3 仕様
  (`api/openapi.yaml`、"source of truth")から `oapi-codegen` で生成する(`internal/api/cfg.yaml` の
  `std-http-server` + `strict-server` + `models` 設定、生成ファイルは `internal/api/server.gen.go`、
  `go generate ./...` で再生成。`oapi-codegen` は go.mod の `tool` ディレクティブでピン留めされ、ベンダリングはしない)。
- `internal/api/handler.go`(`serverAPI`)が生成された `StrictServerInterface` を実装する。純粋なビジネス/検証ロジックのみで、
  実処理は `ServerManager` インターフェースへ委譲する。
- `internal/kube/manager.go`(`kube.Manager`)が実際に Kubernetes とやり取りする層。**Agones の型付きクライアントセット**
  (`agones.dev/agones/pkg/client/clientset/versioned`、generated client-go スタイル)と `k8s.io/client-go`/
  `k8s.io/apimachinery` を使う。`controller-runtime` は使わず、informer/watch/controller もない。すべての操作は
  Agones API への直接の同期 `Create`/`Delete`/`Get`/`List` 呼び出しであり(reconcile ループなし)。
- 4 つの操作が `api/openapi.yaml` に沿って REST エンドポイントとして公開されている。
  - `POST /servers` — GameServer を作成する(`kube.Manager.Create`)
  - `GET /servers` — `app=basis-server` ラベルの付いた GameServer を全件一覧する(`kube.Manager.List`)
  - `GET /servers/{name}` — 単一インスタンスの状態を取得する(`kube.Manager.Get`)
  - `DELETE /servers/{name}` — GameServer を削除する(`kube.Manager.Delete`)
- `cmd/server/main.go` が配線を行う。kubeconfig を解決し(まず `rest.InClusterConfig()`、失敗したら `$KUBECONFIG` または
  `$HOME/.kube/config`)、Agones クライアントセットを構築し、`kube.Manager` と `api.NewMux` を構成し、`LISTEN_ADDR`
  (既定 `:8080`)で待ち受ける。
- 管理 API 自体には**認証/認可がない**。ドキュメントは LoadBalancer の `EXTERNAL-IP` を信頼できるネットワークからのみ
  到達可能にすることを明示的に求めている。

## 3. Agones GameServer による部屋管理(`internal/kube/manager.go`)

`Manager` 構造体は `versioned.Interface`(Agones クライアントセット。テストではフェイククライアントセットに差し替え可能)と
`namespace` を保持する。

`Create` の要点(`kube.CreateOptions{Name, Image, Config}` を受け取る)。

- `validation.IsDNS1123Label(opts.Name)` に失敗すると `ErrInvalidName`。
- `image` 未指定時の既定は `basis-server:latest`。
- ゲームポートは `opts.Config.Port` があればそれ、なければ既定 `4296`(`int32`)。
- `agonesv1.GameServer` を構築する。
  - `ObjectMeta.Name` = `"basis-" + opts.Name`、`Namespace` = マネージャーの namespace、`Labels` =
    `{app: "basis-server", instance: opts.Name}`。
  - `Spec.Container` = `"basis-server"`(実ゲームコンテナ名)。
  - `Spec.Ports` = 1 件、`{Name:"game", PortPolicy: Dynamic, ContainerPort: gamePort, Protocol: UDP}`。
  - `Spec.Template`(Pod テンプレート)は 2 コンテナ。
    - `basis-server`(実際のゲームサーバー、`buildEnv(cfg)` が組み立てた環境変数を注入)。
    - `agones-ready`(イメージ `curlimages/curl:latest`)。`sh -c` で以下のスクリプトを実行するサイドカー。
      ```
      until curl -sf -X POST -H "Content-Type: application/json" -d "{}" http://localhost:9358/ready; do sleep 1; done
      while true; do curl -sf -X POST -H "Content-Type: application/json" -d "{}" http://localhost:9358/health; sleep 2; done
      ```
      `POST /ready` が成功するまでリトライしてから(この呼び出しが成功して初めて GameServer が Ready になる)、
      `POST /health` を 2 秒間隔で送り続ける。Basis Server は Agones SDK を自前で組み込んでいないため、この
      サイドカーが Agones SDK のローカル HTTP ゲートウェイ(ポート 9358)を代理で叩く(Agones の FAQ に記載された
      サイドカーパターン)。
  - Pod テンプレートには liveness/readiness プローブを一切設定していない。Agones が SDK サイドカー経由で Pod の
    ヘルスを完全に管理するため。
  - `m.client.AgonesV1().GameServers(namespace).Create(...)` を呼ぶ。`apierrors.IsAlreadyExists` なら
    `ErrAlreadyExists` を返す。

`Delete`/`Get`/`List` は `gameServerName(name) = "basis-" + name` で対応する GameServer を直接 Delete/Get する、または
`app=basis-server` ラベルセレクターで List する。`List` は Kubernetes API への**キャッシュなしのライブクエリ**であり、
呼び出しごとに実行される(informer もセパレートな状態ストアもない)。

`buildEnv(cfg Config)` — `Config` の各フィールド(すべてポインタ型)が非 nil の場合のみ、対応する環境変数を
`corev1.EnvVar` として `basis-server` コンテナに追加する。

| `Config` フィールド | 環境変数名 | 備考 |
|---|---|---|
| `Port` | `SetPort` | |
| `HealthCheckPort` | `HealthCheckPort` | |
| `MetricsPort` | `PromethusPort` | 上流のタイポをそのまま踏襲 |
| `PeerLimit` | `PeerLimit` | |
| `Password` | `Password` | 平文で注入(Secret は作らない) |
| `EnableStatistics` | `EnableStatistics` | |
| `EnableConsole` | `EnableConsole` | |
| `DisallowHeadless` | `DisallowHeadless` | |

Basis Server のゲーム本体側がこれらを読む仕組みは、SSO 用の `SsoAdmissionTicketSigningKey` 等と同じ
`Configuration.ApplyEnvironmentalOverridesTo`(フィールド名 = 環境変数名のリフレクションベースのオーバーライド)である。
`research-sso-broker.md` §5.2 参照。

エラー型は `errors.New` で定義したセンチネル(`ErrAlreadyExists`、`ErrNotFound`、`ErrInvalidName`)を
`fmt.Errorf("%w: ...")` でラップし、`errors.Is` で判定する。HTTP ステータスへの変換は `internal/api/handler.go` の
`switch`/`errors.Is` 連鎖で行う。

## 4. API(OpenAPI)

`api/openapi.yaml` は OpenAPI 3.0.3。パスは 2 つ。

- `POST /servers`(`createServer`) / `GET /servers`(`listServers`)
- `GET /servers/{name}`(`getServer`) / `DELETE /servers/{name}`(`deleteServer`)

主なスキーマ:

```yaml
CreateServerRequest:
  name: string, maxLength 57, pattern "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$"  # 必須
  image: string
  config: ConfigRequest

ConfigRequest:
  port, healthCheckPort, metricsPort, peerLimit: integer(int32)
  password: string
  enableStatistics, enableConsole, disallowHeadless: boolean

ServerStatus:
  name, state, address: string
  ports: PortStatus[]   # {name, port}

Error:
  error: string
```

レスポンスコードは `201 Created`(ボディなし)、`200 OK`、`204 No Content`、`400`/`404`/`409`/`500` を `Error` スキーマ
(`{error}`)で返す。`409` は同名インスタンスの重複作成時。

`name` の 57 文字上限は、導出される GameServer 名(`"basis-" + name`、最大 63 文字)を Kubernetes の DNS-1123 ラベル長
制限内に収めるためのもの。生成された OpenAPI スキーマ検証ミドルウェアが配線されていないため、ハンドラ層の長さチェックと
`kube.Manager.Create` 内の `validation.IsDNS1123Label` チェックの 2 箇所で二重に強制している。

`internal/api/handler.go` は、生成された `StrictServerInterface` に対する薄い実装。`requestErrorHandler`/
`responseErrorHandler` により、oapi-codegen 移行前の手書きルーターが使っていた `{"error": "..."}` という JSON エラー
形式を(strict サーバー自身が処理する不正な JSON ボディ等の失敗についても)維持している。

## 5. ネットワーク

- 各 GameServer は `PortPolicy: Dynamic` で名前 `game` の UDP ポート 1 つを要求する。`containerPort` = `config.port`
  (既定 4296)。
- 実際の公開は Agones が行う。`PortPolicy: Dynamic` では、Agones が Pod の配置先ノード上の **hostPort**
  (既定のポート範囲 7000–8000/UDP)を割り当て、コンテナポートへマッピングする。これは NodePort Service や
  LoadBalancer、Kubernetes ネイティブの機構ではなく、完全に Agones のスケジューラー/ポートアロケーターへ委譲されている。
- クライアントは `GameServer.Status.Address`(Pod が配置されたノードの外部 IP)と割り当てられた
  `Status.Ports[].Port` に対して UDP で直接接続する。どちらも `GET /servers/{name}` から取得できる。
- 運用上、ノードのファイアウォールで Agones の UDP ポート範囲(既定 7000–8000/UDP)を開放しておく必要がある
  (`docs/setup.md`)。
- Basis Server は Agones SDK をネイティブ統合していないため、各部屋の Pod テンプレートに `agones-ready` サイドカー
  (イメージ `curlimages/curl:latest`)が注入される。§3 参照。
- 管理 API 自体の HTTP ポート(8080/TCP)は、ベアメタル/オンプレ向けに MetalLB の固定 IP アノテーション
  (`metallb.io/loadBalancerIPs`)を付けた `LoadBalancer` Service で別途公開される。クラウドマネージド Kubernetes では
  MetalLB は不要で、クラウド側のネイティブ LB がそのまま使われる。

## 6. 設定・シークレット管理

- 設定は API サーバー Deployment 上の環境変数のみで完結する。`LISTEN_ADDR`(既定 `:8080`)、`NAMESPACE`(既定 `basis`)。
  API サーバー自体に設定ファイルや ConfigMap/Secret はない。
- 部屋ごとの設定(`POST /servers` リクエストボディの `config` オブジェクト)は、部屋の `basis-server` コンテナへ
  **平文の環境変数としてそのまま**注入される。`Password`(部屋のパスワード)を含め、Kubernetes `Secret` オブジェクトは
  一切作成されない。フィールドはすべてオプショナルなポインタ型(`*int32`/`*string`/`*bool`)であり、省略されたフィールドは
  環境変数を注入せず Basis Server イメージ自身の既定値に委ねる。
- RBAC: 専用の ServiceAccount/Role/RoleBinding(`deploy/10-rbac.yaml`)が、`basis` 名前空間内の
  `agones.dev/gameservers` に対する `get`/`list`/`create`/`delete` のみを付与する。クラスタ全体への権限はなく、
  CRD 管理/patch/update 系の verb もない。
- 部屋のイメージ名は呼び出し側がリクエストごとに指定できる(`image` フィールド、既定 `basis-server:latest`)。
  API サーバー自身のイメージ名は `deploy/20-deployment.yaml` でプライベートレジストリ
  (`alc-gitea.kanaru.me/kanaru0928/basis-k8s:v1.0.0`)を指しており、デプロイ者が上書きすべきプレースホルダーと見られる。

## 7. Go 実装規約

- 標準的なフラットモジュールレイアウト: `cmd/server`(エントリーポイント)、`internal/api`(HTTP 層。生成コード+手書き)、
  `internal/kube`(ドメイン/Kubernetes ロジック)。`pkg/` はなく、OpenAPI 仕様以外の `api/` Go 型もない。
- `client-go` と Agones の生成済みクライアントセット(`versioned.Interface`)を直接使う。`controller-runtime` なし、
  カスタムコントローラー/オペレーターフレームワークなし、informer キャッシュなし(すべての呼び出しが API サーバーに
  直接ヒットする)。
- ロギング: 標準ライブラリの `log` パッケージのみ(`log.Printf`、`log.Fatalf`)。構造化/レベル付きロギングライブラリは
  使わない。
- 依存性注入は小さなインターフェースで行う。`api.ServerManager` インターフェース(`kube.Manager` が実装)が HTTP 層を
  Kubernetes から切り離し、テスト容易性を確保している。
- テスト: `testing` パッケージのみを使うシンプルなテスト(テーブル駆動ではない)。`internal/kube`(Manager の直接テスト)と
  `internal/api`(`httptest` 経由のフル HTTP ラウンドトリップテスト)の両方で**フェイククライアントセット**
  (`agones.dev/agones/pkg/client/clientset/versioned/fake`)を使う。テスト用に、ポインタ型の設定フィールドを組み立てる
  小さなジェネリックヘルパー `ptr[T any](v T) *T` がある。モックフレームワークは使わず、testify も使わない。純粋な
  stdlib のアサーション(`t.Errorf`/`t.Fatalf`)。
- ビルド: マルチステージ Dockerfile。ビルドステージは `golang:1.26.3`、`CGO_ENABLED=0` で `./cmd/server` をビルドする。
  実行ステージは `gcr.io/distroless/static:nonroot`(最小、非 root、最終イメージにシェルなし。部屋の `agones-ready`
  サイドカーイメージ `curlimages/curl` は別物で、リトライスクリプト用のシェルを持つ)。
- API ファーストなワークフロー: OpenAPI 仕様(`api/openapi.yaml`)を唯一の情報源とし、Go の型/ルーティングは
  `go:generate` ディレクティブ + `go tool oapi-codegen` で生成する。生成ファイルはコミットされるが「手で編集しないこと」
  と明記されている。

## 8. マルチルーム管理・スケーリング・ライフサイクル

- オートスケーリング、アイドル停止、部屋数の上限制御は本リポジトリのどこにも存在しない。`peerLimit` は部屋ごとの設定値
  として `basis-server` コンテナへ環境変数(`PeerLimit`)として渡されるのみで、実際の強制はゲームサーバーバイナリ自身の
  責務と見られる。basis-k8s はこの値を読んだり作用したりしない。
- 部屋プール、ウォームスタンバイ、事前プロビジョニングの概念はない。すべての部屋は `POST /servers` のたびに同期的・
  個別に作成される。Fleet/Fleet Autoscaler(部屋プール向けの Agones 自身のスケーリングプリミティブ)は一切使わず、
  単体の `GameServer` オブジェクトのみを作成する。つまり組み込みのスケールツーゼロ、バッファプール、クラッシュ時の
  自動再作成(単体・非プールの GameServer に対して Agones がネイティブに行うもの以外)は存在しない。
- 一覧(`GET /servers`)は呼び出しごとの Kubernetes API へのライブなラベルセレクタークエリであり、キャッシュ/informer も
  別状態ストアもない。
- `kubectl delete -f deploy/` で `basis` Namespace 全体を削除すると、配下の全 GameServer/部屋がカスケード削除される
  (`docs/deploy.md` に明示的な注意事項として記載)。

## 9. デプロイ

`deploy/` 配下に 4 つのマニフェストがあり、ファイル名の番号プレフィックス(`00-`→`10-`→`20-`→`30-`)により
`kubectl apply -f deploy/` で Namespace → RBAC → Deployment → Service の順に適用される。

| ファイル | 作成するリソース | 内容 |
|---|---|---|
| `00-namespace.yaml` | Namespace `basis` | ラベル `app: basis-k8s` |
| `10-rbac.yaml` | ServiceAccount / Role / RoleBinding `basis-k8s` | `agones.dev` の `gameservers` に対する `get`/`list`/`create`/`delete` |
| `20-deployment.yaml` | Deployment `basis-k8s` | レプリカ数 1、`serviceAccountName: basis-k8s`、環境変数 `NAMESPACE=basis`、コンテナポート 8080/TCP、readiness/liveness とも `8080` への TCP ソケットプローブ(`initialDelaySeconds: 5`、`periodSeconds: 10`) |
| `30-service.yaml` | Service `basis-k8s` | `type: LoadBalancer`、ポート 8080/TCP、アノテーション `metallb.io/loadBalancerIPs`(固定 IP) |

前提条件: `kubectl` が対象クラスタに接続できること、ビルドイメージを push できるレジストリ、クラスタに Agones が
インストール済みであること、ベアメタル/オンプレでは MetalLB の `IPAddressPool`/`L2Advertisement` が適用済みであること
(クラウドマネージド Kubernetes では不要)。

## 10. concierge との関係(スコープの整理)

basis-k8s のリポジトリ自体には SSO/認証 broker のコードは一切含まれない。部屋のプロビジョニング REST API のみであり、
管理 API は明示的に無認証(ネットワーク信頼のみ)である。Basis SSO broker/Cloudscape 管理コンソールはこの
sekaimate モノレポの Git 履歴に別コンポーネントとして存在する(コミット `813599717`)。concierge は、この 2 つの機能
(SSO 入場審査 broker と Agones ベースの部屋管理)を 1 つの Go サービスへ統合する設計であり、basis-k8s は「部屋管理」側の
実装パターン(レイアウト、Agones 連携方式、テスト手法)の参照実装として扱う。詳細は `design.md` を参照。
