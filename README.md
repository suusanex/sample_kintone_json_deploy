# sample_kintone_json_deploy

このリポジトリでは、kintone の JavaScript カスタマイズを
管理者 OAuth（refresh token）を使ってデプロイする最小実験の PoC を実装する。

実装本体は `kintone-js-oauth-ci-poc/` 配下。

## 実行手順（ローカル）

初回だけ以下を順に実行して認可フローを通し、`refresh token` を取得する。

```powershell
$env:KINTONE_SUBDOMAIN = "suusanex-test"                # 例: suusanex-test
$env:KINTONE_OAUTH_CLIENT_ID = "<クライアントID>"
$env:KINTONE_OAUTH_CLIENT_SECRET = "<クライアントシークレット>"
$env:KINTONE_OAUTH_REDIRECT_URI = "https://localhost:54187/oauth" # kintone 側の登録済み callback と一致
$env:KINTONE_OAUTH_SCOPE = "k:app_settings:read k:app_settings:write k:file:write"
$env:KINTONE_OAUTH_TIMEOUT_SECONDS = "300"

dotnet run --project kintone-js-oauth-ci-poc/src/KintoneJsDeploy.Cli -- get-token
```

`get-token` 実行時の流れは次。
- 認可 URL を表示し、ブラウザを自動起動
- ローカルの HTTPS/HTTP callback を一時起動（Kestrel）
- `https://localhost:54187/oauth?code=...&state=...` の `code` は短時間有効な認可コード。`refresh token` ではない
- 受け取った `code` と `state` を検証
- `/oauth2/token` で `grant_type=authorization_code` を送信
- `refresh token` を画面に表示

`refresh token` はこの実行時だけ表示され、保存はしない。

次回以降は `refresh token` を使って `deploy` 時に `access token` を更新する。

```powershell
dotnet run --project kintone-js-oauth-ci-poc/src/KintoneJsDeploy.Cli -- deploy
```

### エラー対応メモ

`localhost` 側で `HTTP ERROR 500` が出る場合、まず次を疑う。

1. ポート競合で、前回の `get-token` 実行が終了せずリッスンを残している
   - CLI は起動時に `https://localhost:54187/oauth` の待ち受けポートを検査し、占有されていれば最初に停止する
   - もし旧プロセスを手動で潰す必要が出た場合は、次で該当プロセスを確認して停止できる

```powershell
netstat -ano -p TCP | Select-String '54187'
Stop-Process -Id <PID> -Force
```

2. Redirect URI の不一致
   - Kintone 管理画面の登録済み Callback URL と `KINTONE_OAUTH_REDIRECT_URI` が一致しているか再確認（`/oauth` が抜けるとコールバック自体が来ない）

## `deploy` で必要な環境変数

```powershell
$env:KINTONE_SUBDOMAIN = "<サブドメイン>"
$env:KINTONE_OAUTH_CLIENT_ID = "<クライアントID>"
$env:KINTONE_OAUTH_CLIENT_SECRET = "<クライアントシークレット>"
$env:KINTONE_OAUTH_REFRESH_TOKEN = "<refresh token>"
$env:KINTONE_APP_ID = "<対象 appId>"
$env:KINTONE_DESKTOP_JS_PATH = "<任意: desktop.js のパス>"

dotnet run --project kintone-js-oauth-ci-poc/src/KintoneJsDeploy.Cli -- deploy
```
