# concierge 検証ドキュメント(phase 3: minikube + Agones)

最終更新: 2026-08-22

`design.md` §10.3 に定めた手順に沿って、実際の minikube + Agones 環境に対して concierge の結合確認を行った記録。
実行環境は macOS arm64(Podman ドライバー)。事前修正(§2)・検証項目ごとの結果(§4)・検証中に見つかった不具合と
その修正(§5)・basis-server スタブによる代替とその限界(§6)・後片付け手順(§7)・未検証の項目(§8)の順にまとめる。

## 1. 検証環境

| 項目 | バージョン / 値 |
|---|---|
| OS | macOS (Darwin 26.5.1), arm64 |
| コンテナランタイム | Podman 5.8.2(`/opt/podman/bin/podman`)、`podman-machine-default`(libkrun、6 vCPU / 11.18GiB / 93GB disk、既存のマシンをそのまま起動) |
| minikube | v1.38.1、`--driver=podman --cpus=4 --memory=6g --container-runtime=containerd` |
| Kubernetes | client v1.36.4 / server v1.35.1(minikube v1.38.1 の既定プリロード) |
| Agones | v1.60.0(`release-1.60.0` の静的 `install/yaml/install.yaml`、`agones-system` 名前空間) |
| Go | go1.26.5(コンテナビルド内)/ ホスト側 go1.26.5 darwin/arm64 |
| helm | 未導入。Agones は `kubectl apply`(静的マニフェスト)でインストールした。 |

## 2. セットアップ手順

実際に実行したコマンドを、実行順に示す(値は本検証時点のもの)。

```sh
# podman マシンの起動(既存のマシンを使用。新規作成する場合は
# `podman machine init --cpus 4 --memory 8192 --rootful` 相当のリソースを与える)
export PATH="/opt/podman/bin:$PATH"
podman machine start podman-machine-default

# minikube クラスタ起動
minikube start --driver=podman --cpus=4 --memory=6g --container-runtime=containerd

# Agones v1.60.0 インストール(§5.1 参照: 素の適用では失敗するため後述の回避が必要)
kubectl create namespace agones-system
curl -sL https://raw.githubusercontent.com/googleforgames/agones/release-1.60.0/install/yaml/install.yaml \
  -o agones-install.yaml
grep -v "x-kubernetes-patch-strategy:\|x-kubernetes-patch-merge-key:" agones-install.yaml \
  > agones-install-fixed.yaml
kubectl apply --server-side --force-conflicts -f agones-install-fixed.yaml

# concierge イメージのビルド(minikube 内部の buildkit へ、レジストリ不要)
cd concierge
minikube image build -t concierge:dev .

# basis-server スタブのビルド(§6 参照。ソースはリポジトリ外、スクラッチ領域に置いた)
cd <scratch>/basis-server-stub
minikube image build -t basis-server-stub:dev .

# 名前空間・Secret・RBAC・Service を適用
kubectl apply -f concierge/deploy/00-namespace.yaml
kubectl create secret generic concierge-config -n basis --from-file=appsettings.json=./appsettings.json
kubectl create secret generic concierge-admin -n basis --from-literal=token="$(openssl rand -base64 32)"
kubectl apply -f concierge/deploy/10-rbac.yaml
kubectl apply -f concierge/deploy/30-service.yaml

# 20-deployment.yaml は image/imagePullPolicy/BASIS_SERVER_IMAGE を検証用に差し替えて適用
# (image: concierge:dev, imagePullPolicy: Never, BASIS_SERVER_IMAGE: basis-server-stub:dev,
#  GAMESERVER_READY_TIMEOUT_SECONDS: "20" を追加。リポジトリのプレースホルダー
#  image: alc-gitea.kanaru.me/... 自体は変更していない — deploy/ 適用時に運用者が
#  差し替える前提のプレースホルダーであり、バグではないため)
kubectl apply -f 20-deployment-dev.yaml

kubectl port-forward -n basis svc/concierge 15080:5080
```

`appsettings.json`(Secret `concierge-config` の中身)は次の内容で検証した。実際の IdP を使った OIDC 疎通は
スコープ外(§8)なので、`Issuer`/`JwksUri` はダミーの値。

```json
{
  "Broker": {
    "AdminTokenEnvironmentVariable": "BASIS_SSO_ADMIN_TOKEN",
    "Organization": {
      "DisplayName": "concierge phase-3 verification",
      "Providers": [
        {
          "Id": "dummy",
          "Label": "Dummy OIDC (verification only)",
          "Issuer": "https://issuer.example.invalid",
          "Audience": "concierge-verification",
          "JwksUri": "https://issuer.example.invalid/jwks"
        }
      ]
    }
  }
}
```

## 3. Step 1 の事前修正(コード)

`POST /admin/meetings` が `host` の有無に関わらず常に `Provisioner.Create` を呼んでいた挙動(design.md §4.2 の
「concierge 管理の部屋の場合」という前提と矛盾していた)を修正した。詳細は `implementation.md` §9.10 を参照。

- `host` を明示指定した場合は `Provisioner.Create`/`Provisioner.Delete` を一切呼ばず、即座に `"ready"` になる
  (C# broker と同じ挙動)。
- `controlplane.MeetingRecord` に `Managed bool`(JSON キー `Managed`、`omitempty`)を追加し、どちらの経路で
  作成された会議かを永続化した。既存の `control-plane.json` にこのキーが無いレコードは `Managed=false` として
  読み込まれる。
- `DELETE /admin/meetings/{id}` は `Managed=true` の会議に対してのみ `Provisioner.Delete` を呼ぶ。
- `Manager.Reconcile` も `Managed=false` の会議は GameServer が無くても `"failed"` にしない。

単体テストは `internal/api/handler_test.go`(`TestCreateMeeting_ExplicitHost_SkipsProvisioning`、
`TestCreateMeeting_NoHost_ProvisionsAndDeletes`)と `internal/kube/reconcile_test.go`
(`TestReconcile_IgnoresUnmanagedMeetingWithoutGameServer`)に追加した。

## 4. 検証項目と結果

design.md §10.3 の手順(a〜h)に対応させた。すべて実際の minikube クラスタに対して `kubectl port-forward` +
`curl` で確認した。

| # | 項目 | 結果 |
|---|---|---|
| a | `GET /health` | Pass。会議が 1 件も無い状態では `503`(`{"status":"not_ready"}`)、会議作成後は `200`。仕様どおりの挙動で、バグではない。 |
| b | `POST /admin/meetings`(host 未指定)→ `201 provisioning` → Secret `basis-<id>-sso`/GameServer `basis-<id>` 作成 → GameServer `Ready` → 会議が `ready` + host/port に遷移 | Pass。GameServer は数秒で `Ready` になり、`GET /admin/meetings` の host/port が実際の `Status.Address`/動的ポートに更新された。 |
| c | 2 件目の会議を作成し、2 部屋が別ポートで共存する | Pass。同一ノードアドレス `192.168.49.2` に対し、動的に割り当てられた別ポート(例: 7910 と 7596)が付与された。 |
| d | `host` を明示指定した `POST /admin/meetings` で GameServer/Secret が作成されない | Pass(§3 の修正の直接確認)。作成直後に `kubectl get gameservers/secrets -n basis` を確認し、既存の 2 件から増えていないことを確認した。 |
| e | `GET /admin/meetings`/`GET /admin/servers` が全件を反映する | Pass。3 件(concierge 管理 2 件 + 外部ホスト 1 件)すべてが両エンドポイントに `ready`/`hasTicketSigningKey: true` 等で反映された。 |
| f | k8s 管理の会議を `DELETE` → GameServer + Secret 削除、レジストリからも消える | Pass。`DELETE` は即座に `204` を返す。GameServer は Agones 側の終了処理(`agones-ready` サイドカー等)のため実際に消えるまで数十秒かかるが、最終的に削除される(§4 の f のとおり非同期)。Secret は即時に削除される。 |
| g | 残った会議の GameServer を `kubectl delete` で直接消し、concierge Pod を再起動 → 起動時に `"failed"` になる | Pass。`kube: reconcile: meeting <id> has no matching GameServer ...; marking failed` のログを確認し、`statusDetail` が `"No matching Kubernetes GameServer was found at startup reconciliation."` になった。同時に存在した `Managed=false`(外部ホスト)の会議は `"ready"` のまま変化しなかった(§3 の修正の副次確認)。 |
| h | `GAMESERVER_READY_TIMEOUT_SECONDS` を極端に短く(`1`)設定し、Ready 待ちタイムアウトで `"failed"` になる | Pass。ログに `GameServer basis-<id> did not become Ready within 1s; marking failed` が出力され、`statusDetail` も一致した。タイムアウト後に自動リトライしないこと(design.md §12 決定事項 3)も、しばらく監視して確認した。 |

`h` は当初「存在しないイメージを指定する」方法で試したが、`agones-ready` サイドカー(`curlimages/curl`)は
`basis-server` コンテナの状態と無関係に自分自身の `POST /ready` を成功させるため、`basis-server` が
`ErrImagePull` のままでも GameServer は `Ready` になってしまい、タイムアウト経路を再現できなかった。
代わりに `GAMESERVER_READY_TIMEOUT_SECONDS=1` を用いる方法に切り替えて再現した(§6 に詳細)。

## 5. 検証中に見つかった不具合と修正

いずれもコードまたは `deploy/` マニフェストを修正し、コミットして再デプロイ・再検証した。

1. **`deploy/10-rbac.yaml` に `agones-sdk` ServiceAccount/RoleBinding が無かった。** Agones は GameServer Pod に
   `agones-sdk` という ServiceAccount を要求するが、Agones のインストール自体は `default` 名前空間にしか
   作成しない。`basis` 名前空間に GameServer を作ろうとすると
   `pods "basis-<id>" is forbidden: error looking up service account basis/agones-sdk: serviceaccount "agones-sdk" not found`
   で GameServer が `Error` になった。`basis` 名前空間にも同名の ServiceAccount と、Agones インストールが作る
   クラスタスコープの `ClusterRole agones-sdk` への RoleBinding を追加して解消した。
2. **`deploy/20-deployment.yaml` が `BASIS_SSO_BROKER_CONFIG_PATH` を Secret から `readOnly: true` でマウント
   していた。** `internal/config.Store` は `POST /admin/meetings`・`DELETE`・組織設定の更新のたびに同じパスへ
   書き戻す(design.md §8)。Kubernetes の Secret ボリュームは `readOnly` 設定に関わらずそもそも書き戻しが
   できない(Pod ローカルの tmpfs コピーへの書き込みも Pod 再起動で失われる)ため、初回の
   `POST /admin/meetings` から必ず失敗し、`AddServer` のエラーが `meetings.go` の一般化されたエラーメッセージ
   `"A server with that ID already exists."` として返っていた(実際の原因はディスク書き込み失敗で、
   メッセージが実態と食い違っていた点も含めて紛らわしい)。`concierge-config` Secret はシードとしてのみ扱い、
   `seed-config` initContainer で `concierge-data` PVC(`control-plane.json` と同じボリューム)へ 1 回だけ
   コピーし、以降はそのコピーを読み書きするよう変更した。
3. **`checkNoStaticMeetingIDCollision` が、会議が 1 件でも存在する状態での再起動を必ず `log.Fatalf` で拒否
   していた。** `CreateMeeting` は会議ごとに Servers[] と control-plane meetings の両方へ同じ id を登録する
   (admission ルーティングに必要なため、design どおりの意図的な挙動)。この起動時チェックは「両方のソースに
   同じ id がある = 事故的な衝突」とみなして `"local"` 以外を無条件に拒否していたため、concierge が管理する
   会議が 1 件でも存在すると次回起動が必ず失敗する重大な回帰だった(g の再起動検証で実際に再現した)。
   `config.ServerConfig` に `FromMeeting bool` を追加し、`CreateMeeting` が作る Servers[] エントリにのみ立てる
   ようにしたうえで、`checkNoStaticMeetingIDCollision` は `FromMeeting` が立っているエントリとの衝突を
   無視するよう修正した(運用者が手で `appsettings.json` に書いた静的エントリとの本物の衝突は引き続き検出する)。

## 6. basis-server スタブによる代替と、その限界

実際の Basis Server(C#、`Basis/Packages/com.basis.server/Docker/Dockerfile`)イメージのビルドは本検証の
スコープ外(タスク定義どおり)。代わりに、スクラッチ領域(リポジトリ外)に置いた最小限の Go 製 UDP エコー
リスナーを `basis-server-stub:dev` としてビルドし、`BASIS_SERVER_IMAGE` に指定した。

- 動作: `SetPort` 環境変数(既定 4296)で UDP リッスンし、受信したデータグラムに `"echo: "` を付けて送り返すのみ。
  起動時に `RequireSso`/`AutoStartSsoBroker` の値をログ出力する(§4 の b で、Secret 経由の値が正しく注入されて
  いることの確認に使った)。
- **検証できたこと**: GameServer/Secret の作成・削除ライフサイクル、Agones SDK サイドカーによる Ready 化、
  動的ポート割り当てと複数会議室の共存、Kubernetes を source of truth とした再起動時の整合性チェック、
  Ready タイムアウトの `"failed"` 遷移。いずれも Basis Server 本体の実装に依存しない、concierge 側の
  プロビジョニングロジックの検証。
- **検証できていないこと**: Basis Server 本体が実際に `RequireSso`/`SsoTransportPrivateKey`/
  `SsoTransportPublicKey`/`SsoAdmissionTicketSigningKey` の環境変数オーバーライドを正しく読み、SSO 事前認証
  ハンドシェイクを行うかどうか。concierge が発行する `basis-sso-ticket-v2` チケットを実際のゲームサーバーが
  検証できるかどうか。UDP ゲームプロトコル自体の疎通。これらは実際の Basis Server イメージが無ければ検証
  できない(§8)。

## 7. 後片付けコマンド

検証終了後にクラスタごと破棄する場合。

```sh
export PATH="/opt/podman/bin:$PATH"
minikube delete
podman machine stop podman-machine-default   # 再利用しない場合のみ
```

namespace `basis` の中身だけを消して concierge/Agones の再検証に使う場合。

```sh
kubectl delete namespace basis
kubectl delete -f concierge/deploy/00-namespace.yaml   # 上と同義
```

本検証終了時点では、ユーザーが確認できるようクラスタは起動したままにしている(`minikube status` で確認可能)。
`basis` 名前空間の会議はすべて削除済み(`GET /admin/meetings` が空配列を返す状態)。

## 8. 未検証の項目

- 実際の Basis Server イメージによる SSO 事前認証ハンドシェイク・UDP ゲームプロトコルの疎通(§6)。
- 実際の OIDC プロバイダ(Google/Auth0 等)に対する `POST /admission/{serverId}` の入場審査
  (`appsettings.json` はダミーの `Issuer`/`JwksUri` を使用しており、JWKS 取得も ID トークン検証も行っていない)。
- 複数ノードクラスタでの Agones GameServer スケジューリング(minikube は単一ノード)。
- concierge Pod の水平スケールやローリングアップデート時の挙動(design.md §11 のとおり non-goal のため未検証)。
