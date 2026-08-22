# 現行 Basis SSO Broker (C#) 調査結果

最終更新: 2026-08-22

`Basis/Tools/BasisSsoBroker` 配下の実装(`Program.cs`, `ControlPlane.cs` ほか)と、それが依存するゲームサーバー側コード
(`Basis/Packages/com.basis.server/BasisNetworkCore/**`, `BasisNetworkServer/Security/**`)を調査した結果をまとめる。
Go 移植版 concierge の設計 (`design.md`) の入力資料であり、ここでは現状の仕様を転記・整理するのみで設計判断は行わない。

## 1. 概要

現行 broker は ASP.NET Core の Minimal API による単一プロセスで、コントローラーや MVC を使わず `Program.cs` 内の
`app.Map*` 呼び出しでルートを定義している。ルートは「公開/クライアント向け」と「管理者向け(`AdminAuthorized` で保護)」の
2 グループに分かれる。`ControlPlane.cs` はルートを持たず、`MeetingStore` / `EnrollmentStore` / モデルクラスのみを提供する。

役割は大きく 2 つ。

- OIDC ID トークンを検証し、ゲームサーバーの UDP 入場審査で使う署名付きチケットを発行する(SSO 入場審査)。
- 会議(meeting)の作成・削除・招待発行を管理する(会議室ライフサイクル管理)。管理 UI(AdminUi)からのみ操作する。

## 2. HTTP エンドポイント一覧

すべてのレスポンスは `System.Text.Json` の既定シリアライズを使う。JSON フィールド名は C# 匿名オブジェクトの初期化子に
書かれた名前がそのまま出力される(camelCase)。

### 2.1 公開 / クライアント向け

| Method | Path | 認証 | 用途 |
|---|---|---|---|
| GET | `/client-config/{serverId}` | なし | サーバーの保存済みクライアント設定 JSON を返す。`clientSecret` フィールドを再帰的に除去する。サーバー未知またはファイル未存在なら 404。Content-Type は `application/json; charset=utf-8`。 |
| GET | `/health` | なし | 生存/準備確認。設定済み全サーバーが ready なら 200 `{status:"ready",servers:[...]}`、そうでなければ 503 `{status:"not_ready", error, servers}`。`servers[]` は `{id, ready, providers:[id,...]}`(空でないプロバイダー id のみ)。 |
| POST | `/admission/{serverId}` | なし(ID トークン自体が資格情報) | 入場審査本体。§3 参照。 |
| GET | `/enroll/{token}` | パス中のワンタイムトークン | ボタン付き HTML ランディングページ(日本語文言)。ボタンは `http://127.0.0.1:56831/basis-sso-config?url=<encode 済み /enroll/{token}/config URL>` に遷移する。トークン未知/期限切れは 410。 |
| GET | `/enroll/{token}/config` | パス中のワンタイムトークン、単発使用(`enrollments.Take`) | 組織のクライアント設定 JSON(§4.4)を返し、トークンを消費する。無効/使用済み/期限切れは 410。サーバー/プロバイダー/公開鍵欠落は 404。 |
| GET | `/join/{token}/config` | パス中の招待トークン | 特定会議用のクライアント設定 JSON(サーバートランスポートはその会議の鍵に固定)。会議未知なら 404、組織設定不完全なら 503 Problem。 |
| GET | `/join/{token}` | パス中の招待トークン | `basisdemo://` ディープリンクを計算し、隠し iframe で `http://127.0.0.1:56831/basis-join?config=...&link=...`(ローカル起動中の Basis クライアントへのループバックブリッジ)を試す HTML ページ。`postMessage('basis-join-received')` が 900ms 以内に来なければ `location.href = deepLink` にフォールバックする。招待未知/失効は 404、会議が `ready` でない/ホスト未設定は 409。 |
| GET | `/join/{token}/open` | パス中の招待トークン | ループバック iframe を持たない同等ページ(ブラウザフォールバック/手動「Basis を開く」ボタン用)。404/409 条件は同じ。 |
| GET | `/join/{token}/manifest` | パス中の招待トークン | JSON: `{meeting:{id,title}, connection:{host,port,password}, serverTransport:{...}}`。招待未知なら 404。**平文の参加パスワードを返す唯一のエンドポイント。** |

### 2.2 管理者向け(`AdminAuthorized` で保護、§3.2 参照)

| Method | Path | Body | レスポンス |
|---|---|---|---|
| GET | `/admin/servers` | — | `[{id, ticketSigningKeyEnvironmentVariable, transportPublicKeyEnvironmentVariable, providers, ready, hasTicketSigningKey, hasTransportPublicKey}]` |
| GET | `/admin/organization` | — | `OrganizationOptions`(`{displayName, defaultProviderId, providers:[Provider,...]}`)。`clientSecret` を平文含む(管理者専用)。 |
| PUT | `/admin/organization` | `OrganizationOptions` JSON | 204。構造不正なら 400 `{error}`。副作用として `local` ブートストラップサーバーの `Providers` を新しい組織プロバイダーのコピーで上書きし、`appsettings.json` を永続化する。 |
| GET | `/admin/meetings` | — | `[MeetingView]`。パスワード・招待トークン・署名/トランスポート秘密鍵は**含まない**。 |
| POST | `/admin/meetings` | `CreateMeetingRequest {title, id?, host?, port?, password?}` | 201 `MeetingView`、`Location: /admin/meetings/{id}`。環境変数 `BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS=true` でゲートされる(未設定/false なら 501)。組織 SSO が未設定なら 409。タイトル(必須、120 文字以下)、id パターン(`^[A-Za-z0-9_-]{1,48}$`、指定時のみ)、host(`IsSafeHost`、§3.1 参照)を検証する。会議ごとに新しい X25519 鍵ペアと 48 バイトの HMAC 署名鍵(`MeetingKeys.Generate`)、未指定ならランダムパスワード、`InviteToken` を生成する。`broker.Servers` にも対応する `BrokerServerOptions` エントリを追加する。`meetings.Add` が例外を投げた場合(id 重複)はサーバー一覧への追加をロールバックする。 |
| DELETE | `/admin/meetings/{meetingId}` | — | 204。未知なら 404。会議を削除し、対応する `broker.Servers` エントリを削除し、存在すればディスク上のクライアント設定ファイルを削除し、設定を永続化する。 |
| POST | `/admin/meetings/{meetingId}/invitations` | — | `{url, meetingId}`(`url = {origin}/join/{meeting.InviteToken}`)。会議未知なら 404。招待トークンは再生成しない(会議ごとに固定)。 |
| PUT | `/admin/servers/{serverId}` | `BrokerServerOptions` JSON | 204。`IsStructurallyValid` 失敗なら 400。id で upsert する(パス id がボディ id より優先)。 |
| DELETE | `/admin/servers/{serverId}` | — | 204。未存在なら 404。該当 id のクライアント設定ファイルも削除する。 |
| GET | `/admin/client-config-template/{serverId}` | — | `CreateClientConfiguration` が生成する整形済み JSON(クライアントが本来持つべき設定)。管理者専用のため `clientSecret` を含む。トランスポート公開鍵未設定なら 503。 |
| GET | `/admin/client-config/{serverId}` | — | `ClientConfigDirectory/{serverId}.json` の生バイトを `Results.File` で返す(ストリーミング、`enableRangeProcessing:false`)。サーバー未知/ファイル未存在は 404。 |
| PUT | `/admin/client-config/{serverId}` | 生 JSON ボディ(ルートはオブジェクト) | 204。ボディは 262144 バイト上限(`Content-Length` でチェックのみ、実際の読み取り量は制限しない)。JSON オブジェクトとしてパースできることのみ検証(`ClientConfig.TryValidate`)、スキーマ検証なし。`ClientConfigDirectory/{serverId}.json` に `.tmp` + `File.Move(overwrite:true)` でアトミック書き込みする。`ClientConfigDirectory` 未設定なら 503。 |
| POST | `/admin/enrollment/{serverId}` | — | `{url, expiresInSeconds:600}`。ランダム 32 バイトの登録トークンを発行する(10 分 TTL、インメモリ `EnrollmentStore`)。サーバー未知なら 404。 |

### 2.3 ステータスコード

`200 Ok`、`201 Created`、`204 NoContent`、`400 BadRequest {error}`、`401 Unauthorized`(空ボディ、`Results.Unauthorized()`)、
`404 NotFound`、`409 Conflict`(`Results.Problem(..., statusCode:409)` 経由)、`410 Gone`(プレーンテキスト、期限切れ/使用済みトークン用)、
`501 NotImplemented`、`503 ServiceUnavailable`(`Results.Problem` 経由)。

`Results.Problem(...)` は RFC 7807 の `application/problem+json` ボディ(`{type,title,status,detail,...}`、ASP.NET の既定形式)を
生成し、`BadRequest` が使う手組みの `{error}` とは異なるエンベロープになる。クライアントが 4xx と 409/501/503 でエラーボディを
異なる形式として扱う場合、Go 移植でもこの分岐を再現する必要がある。

`Cache-Control`: `POST /admission/{serverId}` はハンドラの最初の処理として `Cache-Control: no-store` を明示的に設定する
(成功/失敗を問わずこのルートの全レスポンスに付く)。他のルートは明示的なキャッシュヘッダを設定しない(ASP.NET の既定に従う)。

## 3. OIDC 入場審査フロー(エンドツーエンド)

### 3.1 クライアント → Broker: `POST /admission/{serverId}`

リクエストボディ(`AdmissionRequest`):

```json
{ "idToken": "<raw JWT>", "did": "did:key:z6Mk..." }
```

両フィールド必須。`idToken` は 16384 文字以下、`did` は 256 文字以下かつ `\n`/`\r` を含んではならない。欠落/超過/不正な形式は
`400 {error:"idToken and did are required"}` または `{error:"invalid did"}`。

サーバー検索: `serverId` は設定済み `BrokerServerOptions.Id` と一致(序数完全一致)し、かつ ready(`IsReady`、§5.1)でなければ
404 または 503 になる。

### 3.2 トークン検証(`TokenValidator.ValidateCoreAsync`、失敗はすべて `null` に収束し 401、詳細は漏らさない)

1. JWT を `header.payload.signature` に分割。ドットで区切って厳密に 3 部分でなければ拒否。
2. ヘッダーとペイロードを base64url デコード(独自の `Decode`: `-`→`+`、`_`→`/`、長さ %4 が 0/2/3 になるよう `=`/`==` でパディング。
   長さ %4 == 1 は不正だが、このコードパスは明示的な例外を投げず暗黙に誤デコードする。Go 移植では %4 == 1 をエラーとして扱うべき)。
3. **`alg` は文字列として厳密に `"RS256"` でなければならない。** 他のアルゴリズムは一切受け付けない(`alg: none` や HMAC 系は
   暗黙に拒否される)。
4. ペイロードの `iss` を読み、そのサーバーのプロバイダー一覧から `Issuer` が `iss` と**完全一致**(序数文字列比較、URI 正規化なし)
   する `ProviderOptions` を探す。
5. プロバイダーは非空の `Audience` と HTTPS の `JwksUri` を持つ必要がある(`Uri.TryCreate` + `Scheme == "https"` によるチェック)。
6. **audience チェック**: `aud` クレームが `provider.Audience` と等しい文字列、またはそれを含む配列であること。
7. **有効期限チェック**: `exp` クレームが存在し、UTC 現在時刻(Unix 秒)より大きいこと。`nbf`/`iat` はチェックしない。
   **`nonce` チェックはどこにも存在しない**(nonce/PKCE/state はクライアント側のみの責務。`docs/sso-spec.md` §4.1 参照。
   broker は iss/aud/exp/署名/alg のみ再検証する)。
8. `sub` クレームが非空であること。
9. **ポリシーチェック**(`Policy`): `provider.AllowedHostedDomains` が非空リストの場合、`hd` クレーム(Google ホストドメイン)が
   そこに含まれること。`provider.AllowedGroups` が非空リストの場合、`groups` クレームがそれと交差すること。各チェックは
   許可リストが空なら無条件に通過する(`Any` ヘルパー: 空の許可リストは常に true)。クレームは JSON 文字列または JSON 文字列配列の
   どちらでもよい。
10. **署名検証**: `jwksUri` を admission リクエストのたびに毎回 HTTPS 経由で取得する(キャッシュなし、ETag/If-None-Match なし、
    `HttpClient` タイムアウト 10 秒)。ヘッダーの `kid` を使い、JWKS の `keys` 配列から `kty=="RSA"` かつ `kid` 一致の鍵を探す。
    base64url の `n`/`e` から直接 `RSAParameters` を構築する。`header.payload` の ASCII バイト列(再エンコードではなく元の
    base64url 部分文字列そのもの)に対する RS256 署名(`RSASignaturePadding.Pkcs1`、SHA256)を、base64url デコードした署名値と
    照合する。
11. この一連の処理内で発生した例外(ネットワークエラー、不正な JWKS、鍵が見つからない等)はすべて外側の `ValidateAsync` の
    try/catch で捕捉され、`null` を返す(401 になる)。

戻り値は `ValidatedIdentity(issuer, subject)` のみ。他のクレームは残らない。broker は生の ID トークンやクレームを永続化・
ログ出力しない(README で明示的に保証されている)。

### 3.3 チケット発行(`Ticket.Create`)

```
expiry   = now_utc + 1分   (Unix秒。この呼び出し箇所では固定60秒、パラメータ化されていない)
ticketId = Guid.NewGuid().ToString("N")   // 小文字16進32文字、ハイフンなし
body     = UTF8("basis-sso-ticket-v2\n{expiry}\n{ticketId}\n{issuer}\n{subject}\n{did}")
mac      = HMAC-SHA256(key = UTF8(signingKey), message = body)
ticket   = base64url_nopad(body) + "." + base64url_nopad(mac)
```

`base64url_nopad` = 標準 base64 の末尾 `=` を `TrimEnd`、`+`→`-`、`/`→`_`。

レスポンス: `200 {"ticket":"<上記の文字列>"}`。

署名鍵(`server.EffectiveTicketSigningKey`)は `TicketSigningKey` 設定値そのもの、または `TicketSigningKeyEnvironmentVariable`
から都度解決される値(リクエストのたびに再読み込みし、キャッシュしない)のいずれか。設定済みとみなす(`HasTicketSigningKey`)
には 32 文字以上が必要。

**このワイヤ形式は互換性上の要である。** UDP サーバー側の
`Basis/Packages/com.basis.server/BasisNetworkCore/SsoAdmissionTicket.cs::TryValidate` は同一の改行区切りボディに対して
HMAC を再計算し、次を要求する。

- ドット区切りで厳密に 2 部分であること。
- HMAC を定数時間比較で照合すること。
- ボディが `\n` で厳密に 6 フィールドに分割でき、`fields[0]=="basis-sso-ticket-v2"` であること。
- `fields[1]` が `long` としてパースでき、現在時刻より大きいこと(サーバー側でも独立して有効期限を再検証する)。
- `fields[2]`(チケット id)が有効な `N` 形式 GUID(16進32文字)であること。
- `fields[5]`(did)が接続中のピアが名乗る DID(`expectedDid`)と一致すること。これがチケットを接続に紐付け、別 identity への
  リプレイを防ぐ。
- issuer/subject(`fields[3]`/`fields[4]`)を抽出し、非空であること。

Go 移植版 broker は、この HMAC ボディ形式・`basis-sso-ticket-v2` マジック文字列・フィールド順序・`\n` 区切り・両セグメントの
base64url(パディングなし)エンコーディングを**バイト単位で完全に再現しなければならない**。わずかな逸脱でも、無改変の C# UDP
サーバーの検証と非互換になる。

チケット寿命について: `SsoAdmissionTicket.Create`(サーバー側ヘルパー。現行 broker は独自の `Ticket.Create` をインライン実装
しており、これは使われていないと見られる)は寿命を `(0, 1分]` にクランプする。broker 自身の `Ticket.Create` はパラメータなしで
厳密に 60 秒を固定している。両者とも 60 秒上限と `v2` 形式文字列で一致する。

### 3.4 UDP サーバーによるチケットの検証と消費

チケットは UDP 上を平文で流れない。クライアント側(`Basis/Packages/com.basis.integration.sso/Runtime/BasisSsoAdmissionService.cs`
+ `Basis/Packages/com.basis.server/BasisNetworkCore/SsoConnectionAuthPayload.cs`):

1. クライアントが broker の `admissionEndpoint`(`basis-sso.json`/enrollment 設定から取得)へ `{idToken, did}` を POST し、
   `{ticket}` を受け取る。
2. クライアントは `(password, ticket)` をサーバーの固定 X25519 公開鍵(`serverTransport.serverPublicKey`、base64url)に
   暗号化する。
   - エフェメラル X25519 鍵ペアを生成する。
   - `BasisCryptoHandshake.DeriveSsoClientKeys` — ECDH(ephemeralPriv, serverPub)、ソルト = `clientPublic‖serverPublic` の
     HKDF-SHA256、info 文字列 `"basis-sso-v1-client-to-server"` / `"basis-sso-v1-server-to-client"`、32 バイト出力鍵。
   - AEAD = ChaCha20-Poly1305(`BasisAeadCipher`、32 バイト鍵、12 バイト nonce、16 バイトタグ)、AAD = 単一バイト `2`
     (エンベロープバージョンバイト)。
   - ワイヤエンベロープ: `magic("BSSO"+0x02) ‖ clientEphemeralPublicKey(32) ‖ nonce(12) ‖ tag(16) ‖ ciphertext`。
     平文 = .NET `BinaryWriter.Write(string)`(7 ビット符号化長プレフィックス + UTF-8 バイト列)による
     length-prefixed-string(password) + length-prefixed-string(ticket)。
3. サーバー(`SsoConnectionAuthPayload.TryDecodeEncrypted`)は `DeriveSsoServerKeys`(自身の長期秘密鍵とチケットに含まれる
   エフェメラルクライアント公開鍵を使用)で復号し、`(password, ticket)` を取り出す。
4. `BasisDIDAuthIdentity.ProcessConnection` がチケットをピア id ごとに一時保存し(`BasisSsoAdmissionGate.SetPendingTicket`)、
   DID チャレンジ/レスポンスが完了すると `BasisSsoAdmissionGate.ConsumeForDid(peerId, did, config)` が実行される。
   - `config.RequireSso == false` なら no-op(true を返す)。
   - そのピア id の保留チケットを取り出して削除する(存在しなければフェイルクローズ)。
   - `SsoAdmissionTicket.TryValidate(ticket, config.SsoAdmissionTicketSigningKey, did, ...)` — 上記と同じ HMAC/有効期限/did
     紐付けチェック。
   - 追加防御: `config.SsoProviders` が非空なら、チケットの issuer がサーバー設定済みの issuer のいずれかと一致すること
     (broker が既に行ったチェックとは独立)。
   - `ticketId → expiry` をプロセスローカルな `ConcurrentDictionary`(`ConsumedTicketIds`)で追跡し、`TryAdd` が失敗したら
     (同一チケット id のリプレイ)拒否する。これがサーバー側の単発使用の強制であり、broker 自身はチケットの消費/リプレイを
     一切把握していない(同じ形のチケットを何度でも発行できる。一意性は GUID の `ticketId` とサーバーのインメモリ集合のみに
     由来し、この集合は**永続化されない**。UDP サーバー再起動でリプレイウィンドウがリセットされるが、60 秒の TTL を考えれば
     許容範囲とされている)。

つまり **broker はチケットに関してステートレス**である(署名して忘れる)。リプレイ防止と DID 紐付けの強制はすべて UDP サーバー
側にある。Go 移植版 broker はチケット用のストレージを一切必要としない。

## 4. コントロールプレーン / 管理コンソール

### 4.1 `ControlPlane.cs` の責務

`BrokerOptions`(OIDC 設定)とは完全に分離された永続的な会議/コントロールプレーン状態を定義する。公開エンドポイントからは
一切返らないと明記されている。

- `MeetingStore` — スレッドセーフ(単一の `lock (_gate)`)な `MeetingRecord` のインメモリリスト。JSON として
  `BASIS_CONTROL_PLANE_STORE_PATH`(既定 `{AppContext.BaseDirectory}/control-plane.json`)に永続化する。すべての変更操作は
  `SaveLocked()` を呼ぶ(`{path}.tmp` に書き込み、`File.Move(overwrite:true)`、その後ベストエフォートで `chmod 0600`。
  Unix ファイルモードを持たないプラットフォーム(Windows 等)では例外を握りつぶす)。
  - `List()` — `CreatedAt` の新しい順。呼び出し側が内部状態を変更できないよう深いコピー(`MemberwiseClone`)を返す。
  - `Find(id)` — 序数完全一致。
  - `FindInvite(token)` — 招待トークン推測に対するタイミング攻撃を防ぐため、保存済みの全招待トークンに対して
    **定数時間比較**(`CryptographicOperations.FixedTimeEquals`)を行う。参加リクエストのたびに全会議を線形走査する O(n)
    実装であり、現状の単一会議規模では問題ないが、会議数が多いケースを想定する Go 移植では留意すべき。
  - `EnsureSingleComposeMeeting(...)` — id `"local"` の `BrokerServerOptions` が存在する場合に broker 起動時
    (`Program.cs` のトップレベルコード)に一度だけ呼ばれるブートストラップロジック。タイトル/ホスト/ポート/パスワード/
    トランスポート公開鍵/ステータスのいずれかがディスク上の内容と異なるたびに、`local` という名前の会議を 1 つだけ作成/更新
    する。これにより、Compose スタックを新しい `Password`/`BASIS_MEETING_PUBLIC_HOST`/再生成された SSO 鍵で再起動すると、
    ブートストラップ会議の接続情報とステータスが自動的に再同期される。
  - `Add`、`UpdateStatus`、`Delete` — 単純な CRUD、id スコープ、すべて同じロック内。
- `MeetingIdentity` — id 生成/検証ヘルパー。
  - `NewId(requestedId, title, existsPredicate)`: 小文字の英数字+ハイフンに正規化(`-`/`_`/空白の連続を単一の `-` に折り畳む)、
    42 文字に切り詰め、その後ランダムな 5 文字の base64url サフィックス(初回試行)または連番サフィックスを付け、
    `existsPredicate` が false を返すまで再試行する(合計 48 文字まで)。
  - `IsValid(id)`: `^[A-Za-z0-9_-]{1,48}$`。
  - `RandomToken(bytes=24)` / `RandomPassword()`(`RandomToken(18)`) — `RandomNumberGenerator.GetBytes` + base64url-nopad。
  - `KubernetesName(id)` — DNS ラベル互換のため `_`→`-` に正規化(将来を見据えたもので、現状 k8s プロビジョニングは存在しない)。
- `MeetingKeys.Generate()` — .NET の OpenSSL バックエンドによる `ECDiffieHellman`(curve `X25519`)で会議ごとの X25519
  鍵ペアを生成し、PKCS8 秘密鍵 DER / SubjectPublicKeyInfo DER の**末尾 32 バイト**を生の scalar/point として抽出する
  (X25519 鍵が固定サイズであることを利用した DER パースの近道。Go 移植は `golang.org/x/crypto/curve25519` または
  `crypto/ecdh` の X25519 を直接使えばよく、この DER 切り出しのハックを再現する必要はない)。合わせて CSPRNG で
  48 バイトのランダムな `TicketSigningKey` を生成し、base64url-nopad でエンコードする(サーバー自身の鍵生成器
  `BasisSsoTransportKeys.Ensure` と同じエンコーディング・同じサイズ)。

### 4.2 管理者認証(`AdminAuthorized`、`Program.cs`)

```
if broker.AllowUnauthenticatedAdmin: 許可(開発専用のループバックモード)
else:
  configured = env[broker.AdminTokenEnvironmentVariable]   // リクエストごとに再読み込み、キャッシュしない
  if len(configured) < 32: 拒否                             // 最小トークン長を強制
  ヘッダーは "Authorization: Bearer <token>" である必要がある(大文字小文字を区別しないプレフィックス一致)
  configured と supplied を CryptographicOperations.FixedTimeEquals でバイト比較
    (長さ不一致は false に短絡する。長さのサイドチャネルは残るが内容のサイドチャネルはない)
```

セッションなし、クッキーなし、CSRF トークンなし。単一の静的ベアラートークンをすべての管理者呼び出し元(AdminUi SPA を含む)で
共有する。**AdminUi の `api.ts` は現状 `Authorization` ヘッダーを一切付与しない**。同一オリジンの Nginx プロキシと、おそらく
開発/Compose デプロイでの `AllowUnauthenticatedAdmin=true`、または本番でリバースプロキシが帯域外にヘッダーを注入することに
全面的に依存している。これは既知の問題として §6 に記載する(黙って修正すべきではない)。

### 4.3 AdminUi(`AdminUi/src/api.ts`, `main.tsx`)が呼ぶもの

すべての呼び出しは `fetch('/api' + path, {cache:'no-store'})` を経由する。クライアント側で `Authorization` ヘッダーは
付与されない(上記参照)。使用エンドポイント:

- `GET /admin/meetings` → `Meeting[]`
- `GET /admin/organization` → `Organization`
- `PUT /admin/organization`(JSON ボディ)→ void(204)
- `POST /admin/meetings/{id}/invitations` → `{url, meetingId}`
- `POST /admin/enrollment/{serverId}`(空ボディ、UI 内では `serverId` に `"local"` をハードコード)→ `{url, expiresInSeconds}`

UI は React 19 + Cloudscape の 2 ページ SPA(`react-router-dom` v7、Vite の `BASE_URL` から `basename` を取る
`BrowserRouter`、`/admin/` にビルド)。

- `/`(`Meetings`): 会議一覧を Cloudscape `Table` で表示(タイトル/id、ready/not-ready ステータス、host:port、招待リンクを
  取得してクリップボードにコピーする「copy invite link」ボタン)。
- `/organization`(`OrganizationSettings`): ちょうど 2 つのプロバイダーにハードコードされたフォーム。`google`(issuer
  `https://accounts.google.com`、JWKS `https://www.googleapis.com/oauth2/v3/certs` 固定、クライアント id/シークレット/
  許可ホストドメインは編集可)と `okta`(issuer/クライアント id/シークレット/JWKS URL/許可グループをすべて編集可)。
  「登録 URL を発行」ボタンと保存ボタンを持つ。クライアント側検証(`validateOrganizationForm`)はサーバー側の
  `IsStructurallyValid` を(緩く)踏襲する。

デプロイ: `AdminUi/Dockerfile` は静的な Vite バンドルをビルドし、Nginx(`AdminUi/nginx.conf`)で配信する。この Nginx は
broker + UI 全体の **TLS 終端も担う**。`nginx-entrypoint.sh` が初回起動時に自己署名の開発用 CA を生成する(RSA 4096 CA、
RSA 2048 リーフ、SAN `localhost`/`127.0.0.1`/`::1`、CA 10 年/リーフ 825 日)。Nginx は `/api/*` を
`http://basis-sso-broker:5080/`(プレフィックス除去)に、それ以外(`/`)を `http://basis-sso-broker:5080`
(プレフィックス保持)にプロキシする。つまり join/enroll の HTML ページや `/health`/`/admission/*` は Nginx オリジンで
直接到達可能(`/api` プレフィックスなし)で、管理 JSON API のみが `/api` 配下に名前空間化されている。

## 5. 設定サーフェス

### 5.1 `appsettings.json` の `Broker` セクション(`IOptions<BrokerOptions>`、`builder.Configuration.GetSection("Broker")`)

| キー | 型 | 未指定時の既定 | 備考 |
|---|---|---|---|
| `PublicBaseUrl` | string? | null | 設定され、絶対 **HTTPS** URI としてパースできれば、`RequestOrigin()` は生成する全リンクにこの scheme+authority を使う(受信リクエストの `Host`/`Scheme` を信頼しない)。HTTPS でない値は黙って無視され、リクエスト由来のオリジンにフォールバックする。 |
| `ClientConfigDirectory` | string? | null | `GET/PUT /admin/client-config/{id}` と公開の `/client-config/{id}` のベースディレクトリ。未設定ならこれらのルートは 404/503 になる。 |
| `AdminTokenEnvironmentVariable` | string? | null | 管理者ベアラートークンを保持する環境変数の**名前**(トークン自体ではない)。 |
| `AllowUnauthenticatedAdmin` | bool | false | 開発専用の管理者認証バイパス。 |
| `Servers` | `BrokerServerOptions[]?` | null/空 | Basis サーバー(会議)ごとの入場審査設定。下記参照。 |
| `Organization` | `OrganizationOptions?` | null → 合成 | 下記の `GetOrganization()` フォールバックロジック参照。 |

`BrokerServerOptions`(Basis ゲームサーバー/会議ごとに 1 つ):

| フィールド | 備考 |
|---|---|
| `Id` | `/admission/{id}` 等のルートセグメント。`IsStructurallyValid`(管理者による書き込み時のみチェック、ディスクからのロード時は再検証しない)を通すには `^[A-Za-z0-9_-]+$` に一致する必要がある。 |
| `TicketSigningKeyEnvironmentVariable` | HMAC 鍵の値を保持する環境変数名(レガシーな単一サーバーデプロイ方式)。 |
| `TransportPublicKeyEnvironmentVariable` | base64url X25519 公開鍵を保持する環境変数名。 |
| `TicketSigningKey` | 鍵の値そのもの(コントロールプレーン管理の会議は環境変数ではなくここに直接格納する)。 |
| `TransportPublicKey` | 鍵の値そのもの、同上の理由。 |
| `Providers` | `ProviderOptions[]`。 |
| `EffectiveTicketSigningKey` | 両方設定されていればリテラル値が環境変数より優先される。 |
| `EffectiveTransportPublicKey` | 同様の優先順位。 |
| `HasTicketSigningKey` | `EffectiveTicketSigningKey.Length >= 32`。 |
| `HasTransportPublicKey` | 空白でないこと。 |
| `IsReady` | `Id` 非空 かつ プロバイダー 1 件以上 かつ 両方の鍵を保持。`/health` と admission の 503 判定を左右する。 |
| `IsStructurallyValid` | 管理者 PUT のみで使用。id パターン、両方の鍵について(リテラル値 OR 環境変数名)、有効なプロバイダー 1 件以上を要求する。 |

`ProviderOptions`: `{Id, Label, Issuer, Audience, ClientSecret, JwksUri, AllowedHostedDomains[], AllowedGroups[]}`。
`IsStructurallyValid()`: id 非空、`Issuer` は絶対 HTTPS URI、`Audience` 非空、`JwksUri` は絶対 HTTPS URI。
`ClientSecret` は受け付けて保存されるが **broker 自身は一切使用しない**(broker は ID トークンの検証のみ行い、トークン交換は
行わない)。自身のトークン交換が必要なネイティブクライアントへ再配布するためだけに存在し、公開配信される JSON から
唯一取り除かれるフィールド(`ClientConfig.RemoveSecrets`)である。

`OrganizationOptions`: `{DisplayName, DefaultProviderId, Providers[]}`。`GetOrganization()` のフォールバック:
`Organization.Providers` が空/null なら、`Servers` の中でプロバイダーを持つ**最初**の `BrokerServerOptions` から合成する
(組織設定導入前のデプロイとの後方互換パス)。`DefaultProviderId` はそのレガシーサーバーの最初のプロバイダー id になる。

### 5.2 環境変数

| 変数 | 読まれる場所 | 用途 |
|---|---|---|
| `ASPNETCORE_URLS` | ASP.NET ホスト | バインドアドレス。Docker イメージの既定は `http://0.0.0.0:5080`、`run-broker.sh` は `BASIS_SSO_BIND_URL` の指定がなければ `http://127.0.0.1:5080` を既定にする(systemd ユニットは `http://127.0.0.1:5080` を固定)。 |
| `BASIS_MEETING_PUBLIC_HOST` | `Program.cs` トップレベルのブートストラップ | 自動登録される `local` 会議の公開ホスト/DNS。空なら会議は `provisioning` のまま。 |
| `SetPort` | 同上 | `local` 会議のポート。`ushort` としてパース、パース失敗時の既定は `4296`。 |
| `Password` | 同上 | `local` 会議の参加パスワード。 |
| `BrokerServerOptions.TicketSigningKeyEnvironmentVariable` の値(通例 `BASIS_SSO_TICKET_SIGNING_KEY[_<ID>]`) | `EffectiveTicketSigningKey` | チケット署名用の HMAC 鍵。 |
| `BrokerServerOptions.TransportPublicKeyEnvironmentVariable` の値(通例 `BASIS_SSO_TRANSPORT_PUBLIC_KEY[_<ID>]`) | `EffectiveTransportPublicKey` | クライアントに広告する X25519 公開鍵。 |
| `AdminTokenEnvironmentVariable` の値(通例 `BASIS_SSO_ADMIN_TOKEN`) | `AdminAuthorized` | 管理者ベアラートークン。32 文字以上必須。 |
| `BASIS_SSO_BROKER_CONFIG_PATH` | `SaveBrokerConfigurationAsync` | `appsettings.json`(`Broker` セクションのみ、`{Broker:{...}}` でラップ)を管理者による書き込みのたびに永続化する先。既定 `{AppContext.BaseDirectory}/appsettings.json`。 |
| `BASIS_CONTROL_PLANE_STORE_PATH` | `MeetingStore` コンストラクタ | `control-plane.json`(会議)を永続化する先。既定 `{AppContext.BaseDirectory}/control-plane.json`。 |
| `BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS` | `POST /admin/meetings` | 大文字小文字を区別せず厳密に `"true"` でなければエンドポイントは 501 になる。 |
| `BASIS_SERVER_CONFIG` | `docker-entrypoint.sh` | 鍵をスクレイプする Basis サーバーの `config.xml` のパス。既定 `/basis-server-config/config.xml`。 |
| `BASIS_SSO_CONFIG_WAIT_SECONDS` | `docker-entrypoint.sh` | そのファイルに両方の鍵が現れるまでポーリングする秒数。既定 60。 |

**重要な注意**: 上記の `BASIS_SSO_TICKET_SIGNING_KEY[_<ID>]` 等は broker プロセス自身が読む環境変数名であり、
`BrokerServerOptions.TicketSigningKeyEnvironmentVariable` に設定された(broker の運用者が自由に選べる)任意の名前である。
これは、実際にゲームサーバー(UDP サーバー)本体が読む環境変数名とは**別の仕組み**である。ゲームサーバー本体は
`Configuration.ProcessEnvironmentalOverrides()`(`Basis/Packages/com.basis.server/BasisNetworkCore/BasisServerConfiguration.cs:249-302`)
により、リフレクションで `Configuration` クラスの**フィールド名そのもの**を環境変数名として読む(例:
`SsoAdmissionTicketSigningKey`、`SsoTransportPrivateKey`、`SsoTransportPublicKey`、`RequireSso`、`AutoStartSsoBroker`、
`SsoBrokerBindUrl` など)。この 2 層の違いは Go 移植・k8s 統合の設計上、正確に区別する必要がある(`design.md` §5 参照)。

### 5.3 ファイル形式

- **`appsettings.json`** — 標準の ASP.NET Core JSON 設定。このアプリにとって意味があるのは `Broker` セクションのみ(§5.1)。
  管理者による変更のたびに `SaveBrokerConfigurationAsync` によって再生成される(整形済み、UTF-8 no-BOM、アトミックな
  一時ファイル + リネーム)。**重要な非対称性**: この保存パスは生きた `BrokerOptions` オブジェクトをシリアライズするため、
  コントロールプレーン経由で作成された会議の `TicketSigningKey`/`TransportPublicKey` の**リテラル値**を含む(環境変数参照
  ではなくインラインで格納される)。つまり `POST /admin/meetings` で会議を 1 つでも作成すると `appsettings.json` は
  シークレットファイルになる。Docker デプロイはプライベートな `/state` ボリュームをマウントし、ASP.NET 起動前にイメージへ
  コピーすることでこれに対処している(`docker-entrypoint.sh`)。`MeetingStore.SaveLocked` は `control-plane.json` を
  `0600` に chmod するが、`appsettings.json` 自体は `SaveBrokerConfigurationAsync` による書き込み後に chmod されて**いない**。
  黙って踏襲するのではなく修正すべき点として記載する(0600 化)。
- **`broker.env`** — シェルソース可能な `KEY=VALUE` ファイル、単一行 `BASIS_SSO_TICKET_SIGNING_KEY=<value>`。
  `prepare-broker.sh` が `umask 077` の後 `chmod 600` で作成する。`run-broker.sh` が `set -a; . "$env_file"; set +a` で
  読み込む。
- **`control-plane.json`** — `{"Meetings":[MeetingRecord,...]}`、
  `MeetingRecord = {Id,Title,Status,StatusDetail,Host,Port,Password,InviteToken,TicketSigningKey,TransportPrivateKey,TransportPublicKey,CreatedAt,UpdatedAt}`
  (PascalCase — `System.Text.Json` の既定プロパティ命名。**camelCase ではない**。これはすべての HTTP レスポンスが camelCase
  匿名オブジェクトを使うのと異なる)。Go 移植が既存の `control-plane.json` を読む場合、このフィールド名の大文字小文字を
  厳密に一致させるか、一度限りの移行を行う必要がある。
- **クライアント設定 JSON ファイル**(`ClientConfigDirectory/{serverId}.json`) — 自由形式の JSON オブジェクト。
  サーバー側では構造検証(パース可能かつオブジェクトであること)のみを強制する。UI/README は `CreateClientConfiguration`
  (§5.4)が生成する形を期待するが、エンドポイント自体はそのスキーマを強制しない。
- **`config.xml`**(Basis サーバー自身の設定。broker から見ると読み取り専用) — broker/スクリプトは `<SsoAdmissionTicketSigningKey>`
  と `<SsoTransportPublicKey>` の 2 要素のみを `sed` で正規表現スクレイプする。

### 5.4 `CreateClientConfiguration` — クライアント設定 JSON の正規形

```json
{
  "defaultProviderId": "google",
  "serverTransport": {
    "serverPublicKey": "<base64url X25519>",
    "admissionEndpoint": "https://.../admission/{serverId}",
    "allowUntrustedLoopbackCertificate": false
  },
  "providers": [
    {
      "id": "google",
      "label": "Google Workspace",
      "issuer": "https://accounts.google.com",
      "clientId": "<Audience>",
      "clientSecret": "<ClientSecret または省略>",
      "scopes": ["openid","email","profile"],
      "displayNameClaims": ["name","preferred_username","email"],
      "access": {
        "allowedGroups": [...],
        "allowedClaims": [{"claim":"hd","values":["example.com"]}, ...]
      }
    }
  ],
  "redirect": {"mode":"loopback","host":"127.0.0.1","port":0,"path":"/callback"},
  "enforcement": {"allowOfflineWithinTokenValidity": true}
}
```

broker 内部の `ProviderOptions` からクライアント向けの形へのフィールド名変換に注意: `Audience`→`clientId`、
`AllowedHostedDomains`→ `access.allowedClaims` 内に `{claim:"hd", values:[domain]}`(ドメインごとに 1 エントリ)として畳み込む、
`AllowedGroups`→ そのまま `access.allowedGroups`。これは
`Basis/Packages/com.basis.integration.sso/Runtime/BasisOidcConfig.cs` のデシリアライズモデル
(`ProviderConfig`、`AccessConfig`、`ClaimRule`、`ServerTransportConfig`、`RedirectConfig`)と厳密に一致する。
Unity クライアントはこの JSON を Newtonsoft.Json でその C# モデルへデシリアライズするため、**Go 移植はこれらの JSON
フィールド名とネスト構造を完全に再現しなければならない**。

`allowUntrustedLoopbackCertificate` は保存された値ではなく計算値。admission エンドポイントの URI 自体がループバックホストに
解決される場合(`Uri.IsLoopback`)のみ true になる。本番の HTTPS broker は決してこれを true にすべきではない。

## 6. 運用形態

- **ポート**: broker は既定で `5080` を待ち受ける(Docker イメージの env では `http://0.0.0.0:5080`、systemd ユニットと
  開発スクリプトでは `http://127.0.0.1:5080`)。別立てのヘルス/メトリクスポートはない。AdminUi の Nginx ゲートウェイは
  自身のコンテナで `443`(TLS)を待ち受ける。Docker ネットワーク名は `basis-sso`(external、ゲームサーバー Compose
  スタックと共有。ただし README/AdminUi README が言及する `Packages/com.basis.server/Docker/sso` サブスタックはこの
  リポジトリツリーに**現存しない**。`Packages/com.basis.server/Docker/docker-compose.yml` のみが存在し、
  `AutoStartSsoBroker` は `false` 固定で broker サービスの定義もない。既知の問題として §7 に記載する)。
- **ヘルスチェック**: `GET /health`(§2.1)。`/ready` と `/live` の分離はない。`docker-entrypoint.sh` 自体は明示的な
  ヘルスチェックを行わない。Compose/K8s 側で `/health` を叩くよう設定する必要がある。
- **TLS**: broker 自体は常にプレーン HTTP(`Kestrel`、設定された `ASPNETCORE_URLS`。提供されているどの設定にも
  `https://` は現れない)。TLS 終端は常に外部で行う(README の systemd ベースデプロイでは本番 Nginx/リバースプロキシ、
  または同梱の `AdminUi` Nginx ゲートウェイコンテナ)。`ForwardedHeaders` ミドルウェアが `X-Forwarded-Host`/`X-Forwarded-Proto`
  を尊重するよう構成されているが、`KnownNetworks`/`KnownProxies` が**空**のため、ASP.NET Core の既定挙動では
  **任意の**プロキシが無条件に信頼される。broker が正確に 1 つの信頼済みリバースプロキシの背後にある前提であれば意図通りだが、
  よくある設定ミスの落とし穴でもあるため、Go 移植で黙って落とすべきではない点として明記する。
- **プロセスライフサイクル/パッケージング**: 3 通りのデプロイモードをサポートする。
  1. **手動/開発**: `prepare-broker.sh`(`config.xml` をスクレイプし、`appsettings.json` とモード 600 の `broker.env` を
     書く)の後に `run-broker.sh`(`appsettings.json` に明らかなプレースホルダー文字列 — `REPLACE_WITH_`、
     `YOUR_OKTA_DOMAIN`、`example.okta.com`、`example.com` — が残っていれば起動を拒否する。`broker.env` を読み込み、
     `dotnet run` を実行する)。
  2. **単体 Basis サーバーの子プロセスとして同居**(`BasisSsoBrokerProcess.cs`): `Configuration.RequireSso &&
     Configuration.AutoStartSsoBroker`(SSO 有効時はいずれも既定 true)かつ `SsoAdmissionTicketSigningKey` が設定済みの
     場合のみ起動する。`SsoBrokerDirectory`(既定 `./sso-broker`、絶対パスでなければサーバー自身のベースディレクトリから
     解決)配下の `BasisSsoBroker.dll`+`appsettings.json` を探し、`dotnet <dll path>` で起動する。`BASIS_SSO_TICKET_SIGNING_KEY`
     と `ASPNETCORE_URLS`(`SsoBrokerBindUrl` から、既定 `http://127.0.0.1:5080`)を**子プロセスの環境のみ**に注入する
     (子自身の設定ファイルには書き込まない)。サーバーの `Dispose()` 時に kill される(`Process.Kill()` + 5 秒
     `WaitForExit`)。`publish-for-basis-server.sh` がこのディレクトリを用意する(`dotnet publish` + 存在しなければ
     example から `appsettings.json` をシード)。
  3. **Docker サイドカー**: `docker-entrypoint.sh` は永続化済みの `/state/appsettings.json` があればイメージへコピーし、
     `$BASIS_SERVER_CONFIG`(ゲームサーバーと共有するバインドマウント/共有ボリューム)を `$BASIS_SSO_CONFIG_WAIT_SECONDS`
     までポーリングして `<SsoAdmissionTicketSigningKey>` と `<SsoTransportPublicKey>` の両方が現れるのを待ち、
     `BASIS_SSO_TICKET_SIGNING_KEY`/`BASIS_SSO_TRANSPORT_PUBLIC_KEY` としてエクスポートしてから
     `exec dotnet BasisSsoBroker.dll` する。タイムアウトしたらハードに失敗する(exit 1)。
  4. **systemd**(`basis-sso-broker.service.example`): `Type=simple`、`EnvironmentFile=broker.env`、
     `ASPNETCORE_URLS=http://127.0.0.1:5080` 固定、`Restart=on-failure`(5 秒バックオフ)、専用の `basis`/`basis` ユーザーで
     実行、`NoNewPrivileges=true`、`PrivateTmp=true`。
- **マルチテナンシー**: 1 つの broker プロセスが多数の `BrokerServerOptions` エントリ(多数の Basis サーバー/会議)を
  それぞれ独自の署名鍵とプロバイダーポリシーで、`{serverId}` パスセグメントによってルーティングして提供できる。
  意図的に「デフォルト」/無指定の `/admission` ルートは存在しない(設定ミスのクライアントが誤ったサーバー向けのチケットを
  黙って取得できないようにするため)。

## 7. 状態と並行性

| 状態 | 保存先 | 寿命 | 並行性制御 |
|---|---|---|---|
| `BrokerOptions`(組織+サーバーごとの OIDC 設定、署名/トランスポート鍵またはその環境変数名) | 起動時に一度 `IOptions<T>` 経由でロードされ、シングルトンとしてメモリ上に保持される `appsettings.json` | プロセス寿命。管理者 PUT/POST ハンドラによってインプレースで変更され、その都度永続化される | 共有 `BrokerOptions` シングルトンの変更(`broker.Organization = organization`、`broker.Servers.Add(...)`、`broker.Servers.RemoveAll(...)`、リストインデックス代入)を囲む**ロックが一切ない**。同時実行の管理者リクエストは競合しうる(更新の消失、または .NET BCL 上未定義動作となる複数スレッドからの `List<T>` 変更)。現行実装の実在するギャップである。Go 移植は `BrokerOptions` の変更〜保存シーケンス全体をミューテックスで囲むべき(ASP.NET Core の Minimal API もハンドラ実行を直列化しないため、管理者 QPS が低い現状では顕在化しにくいだけの潜伏バグである)。 |
| `MeetingStore` の会議 | `control-plane.json`(ディスク)。`MeetingStore` シングルトンが保持するインメモリ `ControlPlaneState` | プロセス寿命、変更のたびに永続化 | すべての読み取り**と**書き込みメソッドを単一の `object _gate` ロックで正しく直列化している。 |
| `EnrollmentStore` のトークン | **インメモリのみ**、永続化されない | 10 分 TTL、単発使用(`Take` で削除)、遅延掃除(`RemoveExpired` はタイマーではなく `Issue`/`Exists`/`Take` の呼び出しごとに実行) | `object _gate` ロックで正しく直列化されている。**永続化されない** — broker 再起動で未処理の登録リンクはすべて無効になる(10 分 TTL と単一マシンデプロイを前提とすれば許容範囲だが、Go 移植を複数レプリカでロードバランサーの背後に置く場合、登録/招待トークンは Redis 等の共有ストアが必要になる点は関係する)。 |
| チケット発行 | **完全にステートレス** — broker 側でチケットは一切記録されない(§3.4 参照)。リプレイ防止は UDP サーバーのインメモリ `ConsumedTicketIds` にすべて依存する。 | n/a | n/a |
| `TokenValidator` | JWKS 取得用に共有の `HttpClient`(`Timeout=10s`)を 1 つ保持。キャッシュなし、`HttpClientFactory` なし、BCL 既定を超えるコネクションプール調整なし | プロセス寿命 | `HttpClient` インスタンス以外はステートレス。`HttpClient` は構造上並行使用に対して安全。 |

**全体として**、broker は**単一インスタンス**での実行を前提に設計されている(分散ロックなし、共有キャッシュなし、
ローカルなアトミックリネームによるファイルベースの永続化)。水平スケールには `EnrollmentStore`(理想的には
`BrokerOptions` の変更の直列化も)を共有ストレージへ移す必要があり、現行設計のスコープ外である。

## 8. 既知の問題点(黙って解決せず、ユーザーへ明示する)

1. `Basis/Tools/BasisSsoBroker/BasisSsoBroker.csproj` が現在の `developer` ブランチのツリーに**存在しない**。
   `Dockerfile`、`prepare-broker.sh`、`publish-for-basis-server.sh`、`run-broker.sh` はすべてこのファイルをパス指定で
   参照している。Git 履歴ではコミット `5ba48b0c5`(「feat: add WebGL OIDC SSO flow」)で `.csproj` が追加されているが
   `HEAD` には存在せず、このファイルが復元/再作成されるまで `dotnet build`/`dotnet publish` でビルドできない。
2. README(`Basis/Tools/BasisSsoBroker/README.md`)と `AdminUi/README.md` はいずれも `Packages/com.basis.server/Docker/sso`
   という Compose サブスタックについて説明しているが、このリポジトリのどこにも存在しない。存在するのは
   `Packages/com.basis.server/Docker/docker-compose.yml`(ゲームサーバー単体。`AutoStartSsoBroker: false`、
   broker/AdminUi サービスの定義なし)のみ。
3. AdminUi の `api.ts` は `Authorization: Bearer` ヘッダーを一切付与しない。`BASIS_SSO_ADMIN_TOKEN` ベースの認証が
   機能するのは、リバースプロキシが帯域外でそのヘッダーを注入する場合、または `AllowUnauthenticatedAdmin=true` の場合のみ。
   同梱の `nginx.conf` もヘッダー注入を行っていない。
4. 管理者による書き込みパス(`/admin/organization`、`/admin/servers/*`、`/admin/meetings`)における `BrokerOptions`
   の変更には、`MeetingStore`/`EnrollmentStore` が両方とも正しくロックされているのとは異なり、共有シングルトンを
   囲むロックが存在しない。
5. `appsettings.json` は、コントロールプレーン経由で会議/サーバーが 1 つでも作成されると平文の署名/秘密鍵を含むが、
   `control-plane.json` と異なり、`SaveBrokerConfigurationAsync` による(再)書き込み後に `0600` へ chmod されない。
