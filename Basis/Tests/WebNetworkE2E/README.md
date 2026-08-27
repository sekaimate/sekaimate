# Web Network E2E

実WebGL Development Buildを2つの独立したChromiumコンテキストで開き、既存Basis ServerのWebSocket実装を通る通信を検証します。テストはHTTPサーバーもBasis Serverも起動しません。

対象は次の通信です。

- WebSocketの`Hello`と`Accept`
- DID認証challenge/responseとmetadataによる認証完了
- 2プレイヤーのjoinとremote player生成
- avatar stateの送受信
- chatの送受信
- プレイヤー一覧の検索と名前順ソート
- 個別プレイヤーUIのmute、volume、pin、highlight、avatar表示、chat表示、block
- blockの相手側一時block反映
- 接続者の権限に対応する管理操作だけが個別プレイヤーUIに表示されること
- Avatar、Prop、World、ServerのContentShare送受信
- 遅延参加者への既存ContentShare再送
- 明示削除と共有者切断時のContentShare清掃
- 切断、新規WebSocket接続、再認証を伴うreconnect

WebGL成果物はDevelopment Buildとしてビルドしてください。テスト開始前に成果物のHTTP配信とBasis Serverを別shellで起動し、Basis ServerでWebSocketを有効化します。

```bash
pnpm install --frozen-lockfile
BASIS_WEB_BUILD_URL=http://127.0.0.1:4173/ \
BASIS_WEBSOCKET_URI=ws://127.0.0.1:4297/basis \
BASIS_SERVER_PASSWORD=test-password \
BASIS_AVATAR_BEE_URL=http://127.0.0.1:4173/BEE/avatar.BEE \
BASIS_AVATAR_BEE_PASSWORD=avatar-password \
BASIS_PROP_BEE_URL=http://127.0.0.1:4173/BEE/prop.BEE \
BASIS_PROP_BEE_PASSWORD=prop-password \
BASIS_WORLD_BEE_URL=http://127.0.0.1:4173/BEE/world.BEE \
BASIS_WORLD_BEE_PASSWORD=world-password \
BASIS_SHARED_SERVER_CONNECTION=127.0.0.1:4296#test-password \
pnpm test
```

`BASIS_WEB_BUILD_URL`、`BASIS_WEBSOCKET_URI`、3形式のBEE URL・復号パスワード、`BASIS_SHARED_SERVER_CONNECTION`は必須です。`BASIS_SERVER_PASSWORD`はパスワードなしのテストサーバーでは省略できます。
