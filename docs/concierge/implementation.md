# concierge phase 1 実装ドキュメント

最終更新: 2026-08-22

`concierge/` に実装した phase 1(Kubernetes 統合を含まない、ワイヤ互換の Go 版 Basis SSO Broker)の内容をまとめる。
設計判断の根拠は `design.md`、互換性要件の出典は `research-sso-broker.md` と `Basis/Tools/BasisSsoBroker/` の C# 実装
そのものを参照。

## 1. パッケージ構成

```
concierge/
├── go.mod, go.sum          module github.com/sekaimate/sekaimate/concierge, go 1.26.3
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
    ├── kube/                phase 2 が実装する RoomProvisioner のインターフェースのみ(§4 参照)
    │   └── provisioner.go
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
| `KUBECONFIG` / `NAMESPACE` | — | phase 1 では未使用(Kubernetes 統合なし)。phase 2 で basis-k8s と同じ解決順序を導入予定。 |

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

## 8. phase 2 が把握しておくべきこと

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
