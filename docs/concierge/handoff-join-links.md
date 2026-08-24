# 参加 URL 自動表示：作業記録と引き継ぎ

対象ブランチは `feat/concierge-go` です。2026-08-23 の作業内容、確認済みの範囲、未対応事項、
引き継ぎ用のプロンプトをまとめます。手順そのものは `operations.md §7.1`、実測値は
`verification.md §4.4` にあります。

## 1. 完了した作業

### 1.1 Concierge：参加 URL の自動表示

会議室を作成すると、Admin UI が WebGL と Basis の参加 URL を自動表示します。従来は
「参加リンクをコピー」を押して参加ページを開き、そこで Web とアプリを選ぶ必要がありました。

- `MeetingView` に任意フィールド `webJoinUrl` を追加しました（`api/openapi.yaml`、`server.gen.go` 再生成）。
- `JoinDetails` にあった Web origin 解決処理を `webJoinURL` として切り出し、`meetingToView` から再利用します
  (`internal/api/web.go`、`convert.go`、`meetings.go`)。生成規則は変えていません。
- Admin UI は作成直後にカードを表示し、会議室一覧に「参加 URL」列を追加します
  (`adminui/src/main.tsx`、`api.ts`)。プロビジョニング中は待機表示で、5 秒 polling により
  再読み込みなしで URL へ切り替わります。

`webJoinUrl` の生成規則は次のとおりです。

```
<AllowedWebOrigins の先頭の browser で読み込める origin>/?basisMeeting=1&meetingUrl=<concierge の web-manifest>
```

既存の「参加リンクをコピー」ボタンは要求範囲外のため残しています。

### 1.2 ドキュメント

- `operations.md §4` の WebGL 用 template を `wss://`/`https://` に修正しました。従来の
  `ws://{host}:{port}/basis` は `ValidateBrowserEndpointTemplates` が必ず拒否するため、起動不能でした。
- `operations.md §4.1` に、起動時 ID 衝突の解消手順と、検証用に `/data` を `emptyDir` にする手順を追加しました。
- `operations.md §7.1` に WebGL image/Deployment/Service の構築、port-forward、圧縮 MIME と Range の検査、
  Admin UI からの URL 起動・OIDC 前提を含む参加 URL 自動表示の再現手順を追加しました。
- `verification.md §4.4` に minikube での実測結果、`§5` に見つかった 2 件を追記しました。

### 1.3 Basis：ブランチ固有のビルド破損 2 件

`feat/concierge-go` は WebGL ビルドが通らない状態でした。`developer` と `feature/web-support` には
無い破損で、どちらもこのブランチの commit に混入したものです。

| 箇所 | 内容 | 混入元 | 対応 |
|---|---|---|---|
| `Basis Framework.asmdef` | `Unity.Animation.Rigging` の参照が欠落。`BasisLocalAvatarDriver.cs` の `using` がコンパイル不能だった | `68e4a36ae` が using を差し替え、asmdef 側の変更を取りこぼした | `feature/web-support` と同じ参照を追加 |
| `BasisMediaPlayerSecurity.cs:83` | 対応する `#if` を持たない `#endif` | `491ac25c7` の refactor でインライン実装を消した際の残り | 削除 |

`feature/web-support` には asmdef の参照が存在し、`using` は正当です。`#endif` の方は
`feature/web-support` にも対応する `#if` が無いため、削除以外の選択肢がありません。

## 2. 確認済みの範囲

minikube + Agones で、`concierge:joinlinks-dev` image を Deployment に反映して確認しました。
実 GameServer を使い、`host` を指定しない managed 会議室で `provisioning` から `ready` への
遷移を含めて通しています。詳細な表は `verification.md §4.4` にあります。

- 作成時 201 の時点で `joinUrl` と `webJoinUrl` の両方が返ること。
- `ready` 遷移後に `invitationReady=true` となり、`webJoinUrl` が不変であること。
- `/join/{token}/details` の `webJoinUrl` が `/admin/meetings` の値と一致すること。
- Admin UI のカードと一覧が、待機表示から 2 つの URL へ再読み込みなしで切り替わること。
- `AllowedWebOrigins` が空の構成で、WebGL 側だけ未設定表示になり Basis 側は残ること。
- `wss://`/`https://` template と Secret `basis-web-tls` により、managed 会議室に WebGL 接続先が
  入り、GameServer に証明書が read-only mount されること。
- `concierge-web:dev` の WebGL image、`concierge/deploy/40-web-deployment.yaml` の Deployment/Service、
  `http://127.0.0.1:4173` への port-forward を使って、WebGL の参加 URL を実際に開き、manifest と
  `web-config` を取得すること。
- `./tools/build-web.sh --dev Build/Web` のコンパイルが `totalErrors=0` で完了すること。

Go の `build`/`vet`/`gofmt`/`test`、Admin UI の `typecheck`/`test`/`build` はすべて通ります。

初期検証用 Secret は `verification-*` の placeholder OIDC 設定だったため、WebGL の
`BasisSsoAuthController.IsSignedIn` は `false` のままで、実 peer の入室までは確認していません。
実入室の合格判定には、§6 の実 Web OIDC 設定と許可ユーザーが必要です。

## 3. 未対応・未確認事項（NA）

### 3.1 WebGL クライアントの配信手段（対応済み）

`concierge/web.Dockerfile` と `tools/build-web-image.sh` を追加し、Development WebGL 成果物を
`concierge-web:dev` image に同梱できるようにしました。`tools/serve-web.mjs` はコンテナでは
`HOST=0.0.0.0` で listen し、`.gz`/`.br` の `Content-Encoding`、圧縮前拡張子に応じた MIME
（WASM は `application/wasm`）、BEE の HTTP Range を返します。

`concierge/deploy/40-web-deployment.yaml` に `concierge-web` Deployment と ClusterIP Service を追加
しました。ローカル browser からは `kubectl -n basis port-forward svc/concierge-web 4173:4173` を使い、
`Broker.AllowedWebOrigins` を `http://127.0.0.1:4173` に設定します。image build は一時 context を使う
`./tools/build-web-image.sh` が標準手順で、リポジトリ全体や Unity `Library` を minikube builder に送りません。

### 3.2 `AllowedWebOrigins` の意味が二重になっている

コード上の定義は「browser CORS と redirect callback に許可する origin」です。使用箇所は
`cors.go`（CORS 判定）、`web.go` の redirect 許可判定、そして `webJoinURL` の 3 つで、
「Web 版の配信元」として読み替えているのは 3 つ目だけです。

これは今回入れた挙動ではなく、`JoinDetails` が以前から同じ読み替えをしています。参加ページの
「Webで参加」ボタンも同じ URL を使います。分離する場合は `Broker.WebClientOrigin` のような専用設定を
追加し、未設定なら `AllowedWebOrigins` へフォールバックする形が考えられます。参加ページと
Admin UI の両方に影響します。

### 3.3 WebGL ビルドと Kubernetes 配信（確認済み）

`./tools/build-web-image.sh`（内部で `./tools/build-web.sh --dev Build/Web` を実行）で Development
WebGL ビルドが完了し、Unity ログの `totalErrors=0` を確認しました。`concierge-web:dev` image を
minikube に取り込み、Deployment/Service を apply、`http://127.0.0.1:4173` へ port-forward して、
圧縮 MIME、`Content-Encoding`、WASM MIME、BEE Range (`206`) の応答を確認しました。

### 3.4 WebGL URL の起動と実入室（起動確認済み、実入室は OIDC 待ち）

minikube の Admin UI で会議室を作成し、provisioning から ready への遷移、Admin UI に WebGL/Basis の
参加 URL が表示されること、WebGL URL を実際に開いて manifest と `web-config` を取得できることを
確認しました。Performance entries では admission、server-info、WebSocket のリクエストは発生していません。

一方、検証用 Secret の OIDC は `verification-*` placeholder で、manifest と `web-config` の取得後、
admission、server-info、WebSocket より前に WebGL の `BasisSsoAuthController.IsSignedIn` 待ちで停止し、
実サーバー log の peer 数は 0 のままでした。従って実 peer の入室は未達です。実入室を合格とするには、operations.md §6 の実 Web OIDC 設定
（有効な `WebClientId`、`WebClientSecret`、`TokenEndpoint`、`JwksUri` と許可ユーザー）を投入し、
認証完了後の peer 接続を再確認する必要があります。placeholder のままの WebGL 起動・manifest/
`web-config` 確認を「入室合格」とは扱いません。

### 3.5 起動時 ID 衝突は起動時にしか検出されない

`checkNoStaticMeetingIDCollision` は起動時にしか走りません。会議を API 以外の経路で消すと、
稼働中の pod は正常なまま次回起動だけが CrashLoopBackOff になります。解消手順は
`operations.md §4.1` に書きましたが、コードは変更していません。

### 3.6 作業ツリーに Unity ビルドの副作用が残っている

`Basis/` 配下に、意図した 2 件以外の差分が 25 件あります。Addressables 設定、Quality 設定、
XR 設定、`baked-paths.txt`、`BasisSetup.json`、`Modified - Web.asset` などで、Unity がプロジェクトを
開いた際やビルド時に書き換えたものです。`ProjectSettings/ProjectVersion.txt` も
`6000.5.2f1` から `6000.5.3f1` へ上がっています。

`tools/build-web.sh` は `ProjectVersion.txt` から Unity executable を選び、導入済みの Unity 6000.5.3f1
（WebGL module 有り）でビルドが成功したため、`ProjectSettings/ProjectVersion.txt` の 6000.5.3f1 への
更新は意図した変更として取り込みます。その他の Basis 副作用は意図した修正と混ぜず、commit 対象から
除外します。

## 4. minikube の現状

検証のためにクラスターを書き換えています。次の状態で残しています。

- Deployment `concierge` の image は `concierge:joinlinks-dev`。
- `/data` は PVC ではなく `emptyDir`。PVC `concierge-data` は削除せず残しています。
- `BASIS_SERVER_IMAGE=basis-server-stub:dev`、WebGL 関連の環境変数と `wss://`/`https://` template を設定。
- Secret `basis-web-tls`（自己署名、有効期限 7 日）を追加。
- `BASIS_SSO_ADMIN_TOKEN` は Secret `concierge-admin` 参照に戻してあります。

Secret `concierge-config` の `appsettings.json` は検証用の内容で上書きしました。元の内容は
サンドボックスの制約で読み取れず、バックアップがありません。OIDC provider は
`verification-native-client-id` などのプレースホルダーです。実 IdP の設定が入っていた場合は
再設定が必要です。

port-forward は停止しています。Admin UI を開く場合は次を実行します。

```sh
kubectl -n basis port-forward svc/concierge 15080:5080
```

## 5. 引き継ぎプロンプト

以下をそのまま渡せます。

```text
sekaimate リポジトリの feat/concierge-go ブランチで、WebGL クライアントを Kubernetes 上で
配信できるようにしてほしい。背景と現状は docs/concierge/handoff-join-links.md にまとまっている
ので、まず §3.1 から §3.4 を読むこと。

やってほしいことは 3 つ。

1. WebGL ビルドを通す。./tools/build-web.sh がコンパイルエラーで止まる。原因は
   feat/concierge-go 固有の破損で、developer と feature/web-support には無い。既に 2 件
   （Basis Framework.asmdef の Unity.Animation.Rigging 参照欠落と、BasisMediaPlayerSecurity.cs の
   孤立した #endif）を直してあるが、3 件目以降が残っている可能性がある。エラーが出たら
   git show developer:<path> と git show feature/web-support:<path> で差分を取り、
   どちらのブランチに無い変更かを確認してから直すこと。安易に using や行を消さず、
   web-support に参照や定義がある場合はそちらを足す方向で直す。

2. WebGL クライアントを配信するイメージと Kubernetes manifest を追加する。単純な nginx では
   不足で、.gz に Content-Encoding: gzip、.wasm に application/wasm、BEE 取得のための
   HTTP Range が必要。tools/serve-web.mjs がこれらを実装済みなので、Node イメージへ同梱するのが
   確実。Deployment と Service を concierge/deploy 配下に置き、minikube で起動できるようにする。

3. Concierge の Broker.AllowedWebOrigins を、2 で作った Service の URL に向ける。そのうえで
   Admin UI（http://127.0.0.1:15080/admin/）から会議室を作成し、表示される WebGL の参加 URL を
   実際に開いて入室できるところまで確認する。手順は docs/concierge/operations.md §7.1 にある。

制約と注意点。

- minikube の環境は自由に使ってよい。現在の状態は handoff-join-links.md §4 に書いてある。
  Deployment concierge は image concierge:joinlinks-dev、/data は emptyDir、Secret concierge-config は
  検証用のプレースホルダーで上書き済み。
- Basis/ 配下に Unity ビルドの副作用が 25 件ある（handoff-join-links.md §3.6）。意図した変更と
  混ぜて commit しないこと。ProjectVersion.txt が 6000.5.2f1 から 6000.5.3f1 に上がっている点は
  取り込むか別途判断が必要。
- Unity は 6000.5.3f1 に WebGL モジュールが入っている。ディスク残量が 13 GiB 程度しかないので、
  ビルド前に空きを確認すること。
- local/BEE/world.BEE は配置済み。BEE にパスワードがある場合は .env.local に
  BASIS_WORLD_BEE_PASSWORD を設定する。
- mise run web:serve は serve-web.sh --dev を実行して Build/WebDev を配信する。リリースビルドの
  出力は Build/Web なので、配信には ./tools/serve-web.sh を直接使う。
- 余力があれば §3.2 の AllowedWebOrigins の意味の二重化についても検討してほしい。ただし
  JoinDetails と参加ページの挙動に影響するため、変更するなら両方まとめて。
```
