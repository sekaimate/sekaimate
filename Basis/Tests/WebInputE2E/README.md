# Web Input E2E

配信中の実WebGL Playerへブラウザ入力を送り、Unity Input Systemからキャラクター制御までの正規経路を検証します。テストはHTTPサーバーを起動しません。

- キーボード移動とプレイヤー座標変化
- Pointer Lockとマウス視点移動
- Gamepad APIから移動Input Actionまで
- Touch Eventから実際のOnScreenControlsを経由する移動と視点移動

WebGL成果物のHTTP配信を別shellで起動してから実行します。

```bash
pnpm install --frozen-lockfile
BASIS_WEB_BUILD_URL=http://127.0.0.1:4173/ pnpm test
```

製品側の計測はURLへ`basisInputE2E=1`を指定した場合だけ有効になります。計測APIは読取り専用で、入力を注入しません。
