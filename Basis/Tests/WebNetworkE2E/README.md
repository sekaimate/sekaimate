# Web Network E2E

実WebGL Development Buildを2つの独立したChromiumコンテキストで開き、既存Basis ServerのWebSocket実装を通る通信を検証します。テストはHTTPサーバーもBasis Serverも起動しません。

対象は次の通信です。

- WebSocketの`Hello`と`Accept`
- DID認証challenge/responseとmetadataによる認証完了
- 2プレイヤーのjoinとremote player生成
- avatar stateの送受信
- chatの送受信
- 切断、新規WebSocket接続、再認証を伴うreconnect

WebGL成果物はDevelopment Buildとしてビルドしてください。テスト開始前に成果物のHTTP配信とBasis Serverを別shellで起動し、Basis ServerでWebSocketを有効化します。

```bash
pnpm install --frozen-lockfile
BASIS_WEB_BUILD_URL=http://127.0.0.1:4173/ \
BASIS_WEBSOCKET_URI=ws://127.0.0.1:4297/basis \
BASIS_SERVER_PASSWORD=test-password \
pnpm test
```

`BASIS_WEB_BUILD_URL`と`BASIS_WEBSOCKET_URI`は必須です。`BASIS_SERVER_PASSWORD`はパスワードなしのテストサーバーでは省略できます。
