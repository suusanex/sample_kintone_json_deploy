# kintone JavaScriptカスタマイズを「管理者OAuth + refresh token + CI/CD」で自動反映できるか検証する最小実験

## 0. このドキュメントの目的

このドキュメントは、既存リポジトリ `tool_kintone_app_dev` とは切り離して、kintoneのJavaScriptカスタマイズをGit管理し、管理者OAuthのrefresh tokenを使って、2回目以降はブラウザ認証なしでkintoneアプリへ反映できるかを最小構成で検証するためのCodex向け作業指示書である。

Codexは、この会話の文脈を持たない前提で読むこと。

## 1. 実験のゴール

### 最終ゴール

新規に用意したサンプルGitリポジトリから、サンプルJavaScriptファイルをkintoneアプリのJavaScriptカスタマイズとしてAPI登録し、deploy後にkintone画面でそのJavaScriptが実行されることを確認する。

### 検証したい仮説

- 初回だけkintone管理者がOAuth認可を行い、`refresh_token` を取得する。
- 以後は `refresh_token` をSecretとして保持し、CLIまたはCI/CDから `access_token` を再発行する。
- 再発行した `access_token` で次の処理を自動実行できる。
  - JavaScriptファイルをkintoneにアップロードする。
  - 返却された `fileKey` を使ってアプリのJavaScriptカスタマイズ設定を更新する。
  - pre-live設定をlive appへdeployする。
- 2回目以降の反映時に、管理者がブラウザで再認可しなくても動作する。

### 非ゴール

- 既存リポジトリ `tool_kintone_app_dev` の改修は行わない。
- フィールド定義、フォームレイアウト、ワークフローのGit管理は扱わない。
- 本番運用品質のSecret管理、監査、ロールバック設計までは行わない。
- 複数アプリ、複数環境、モバイルJS、CSS、URL型カスタマイズは最小実験では扱わない。

## 2. 公式仕様に基づく前提

### 2.1 OAuthについて

kintone.com はOAuth 2.0のAuthorization Code Grantを使う。初回は認可URLへアクセスし、ユーザーが承認し、認可コードを取得してアクセストークンを発行する流れになる。

公式ドキュメント上、アクセストークンは1時間有効で、`refresh_token` はアクセストークン期限切れ時に新しいアクセストークンを取得するためのトークンである。さらに `refresh_token` は期限切れしないと説明されている。

参考:

- https://kintone.dev/en/docs/common/authentication/how-to-add-oauth-clients/

### 2.2 JavaScriptカスタマイズ設定APIについて

アプリのJavaScript/CSSカスタマイズ設定は、次のAPIで取得・更新する。

- live設定取得: `GET /k/v1/app/customize.json`
- pre-live設定取得: `GET /k/v1/preview/app/customize.json`
- pre-live設定更新: `PUT /k/v1/preview/app/customize.json`

`PUT /k/v1/preview/app/customize.json` はpre-live設定を更新するだけなので、live appへ反映するには別途deploy APIが必要。

重要な制約:

- Update Customization APIはkintone管理者のみ利用可能。
- アプリ管理権限が必要。
- APIトークンは利用不可。
- OAuth 2.0認証は利用可能。

参考:

- https://kintone.dev/en/docs/kintone/rest-api/apps/settings/get-customization/
- https://kintone.dev/en/docs/kintone/rest-api/apps/settings/update-customization/

### 2.3 ファイルアップロードAPIについて

JavaScriptファイルを `FILE` 型で登録する場合、まずファイルアップロードAPIを使ってkintoneへアップロードし、返却された `fileKey` をカスタマイズ設定に指定する。

- `POST /k/v1/file.json`
- `Content-Type: multipart/form-data`
- レスポンスに `fileKey` が含まれる。

参考:

- https://kintone.dev/en/docs/kintone/rest-api/files/upload-file/

### 2.4 deploy APIについて

pre-live設定をlive appへ反映するには、次のAPIを使う。

- `POST /k/v1/preview/app/deploy.json`

Deploy App Settings APIは、アプリ設定画面で「Update App」または「Discard Changes」を押すのと同等の結果をもたらす。アプリ管理権限が必要。

参考:

- https://kintone.dev/en/docs/kintone/rest-api/apps/settings/deploy-app-settings/

### 2.5 必要なOAuth scope候補

最小実験で必要なscopeは以下を想定する。

```text
k:app_settings:read k:app_settings:write k:file:write
```

理由:

- `k:app_settings:read`: `Get Customization` / deploy状況確認に必要。
- `k:app_settings:write`: `Update Customization` / `Deploy App Settings` に必要。
- `k:file:write`: `Upload File` に必要。

参考:

- https://kintone.dev/en/docs/common/authentication/oauth-permission-scope/

## 3. 実験に必要なもの

### 3.1 kintone側

- テスト用kintone環境
- テスト用kintoneアプリ
  - アプリIDを控える。
  - 最低限、文字列1行フィールドを1つ作る程度でよい。
- 実験用kintone管理者ユーザー
  - kintoneシステム管理者であること。
  - 対象アプリのアプリ管理権限を持つこと。
- OAuthクライアント
  - Client ID
  - Client Secret
  - Redirect URI
  - 実験用管理者ユーザーがOAuth利用対象に含まれていること。

### 3.2 Git/ローカル側

- 新規の実験用Gitリポジトリ
- Node.js 20以上を推奨
- GitHub Actionsで試す場合はGitHubリポジトリSecrets

## 4. 推奨する最小リポジトリ構成

```text
kintone-js-oauth-ci-poc/
  README.md
  package.json
  .gitignore
  customize.json
  src/
    desktop.js
  scripts/
    get-initial-refresh-token.mjs
    deploy-customize.mjs
  .github/
    workflows/
      deploy.yml       # 任意。ローカル成功後に追加で検証する。
```

## 5. Secret / 環境変数

### ローカル実験用 `.env` またはシェル環境変数

`.env` はGitにコミットしない。

```bash
KINTONE_SUBDOMAIN=example
KINTONE_APP_ID=123
KINTONE_OAUTH_CLIENT_ID=...
KINTONE_OAUTH_CLIENT_SECRET=...
KINTONE_OAUTH_REDIRECT_URI=http://localhost:3000/callback
KINTONE_REFRESH_TOKEN=... # 初回認可後に保存。Gitには絶対にコミットしない。
```

### GitHub Actions Secretsに入れる場合

```text
KINTONE_SUBDOMAIN
KINTONE_APP_ID
KINTONE_OAUTH_CLIENT_ID
KINTONE_OAUTH_CLIENT_SECRET
KINTONE_REFRESH_TOKEN
```

注意: `KINTONE_REFRESH_TOKEN` は管理者権限でアプリ設定を変更できる強いSecretとして扱う。

## 6. 最小実験の流れ

### Phase 1: サンプルJSを作る

`src/desktop.js` を作成する。

```javascript
(function () {
  'use strict';

  kintone.events.on('app.record.index.show', function (event) {
    if (document.getElementById('kintone-js-oauth-ci-poc-banner')) {
      return event;
    }

    const el = document.createElement('div');
    el.id = 'kintone-js-oauth-ci-poc-banner';
    el.textContent = 'kintone JS OAuth CI PoC loaded: ' + new Date().toISOString();
    el.style.padding = '8px';
    el.style.margin = '8px 0';
    el.style.border = '1px solid #2f80ed';
    el.style.background = '#eef5ff';

    const space = kintone.app.getHeaderMenuSpaceElement();
    if (space) {
      space.appendChild(el);
    }

    return event;
  });
})();
```

合格条件:

- kintoneレコード一覧画面を開いたときに、ヘッダー付近にPoC文言が表示される。

### Phase 2: 初回だけOAuth認可してrefresh tokenを取得する

`scripts/get-initial-refresh-token.mjs` を作る。

要件:

- Authorization URLを生成する。
- ローカルHTTPサーバーを一時的に起動し、redirect URIで `code` を受け取る。
- `POST https://{subdomain}.kintone.com/oauth2/token` に `grant_type=authorization_code` でリクエストする。
- レスポンスから `refresh_token` を表示する。
- refresh tokenは画面表示のみ。ファイルには保存しない。

OAuth認可URLのscope:

```text
k:app_settings:read k:app_settings:write k:file:write
```

疑似コード:

```text
1. stateをランダム生成
2. http://localhost:3000/callback で待ち受け
3. ブラウザで authorization URL を開く
4. 管理者がAllowする
5. callbackで code と state を検証
6. Basic base64(client_id:client_secret) を付けて /oauth2/token へPOST
7. access_token, refresh_token, expires_in, scope を表示
8. refresh_token を環境変数またはGitHub Secretへ手動登録
```

合格条件:

- `refresh_token` が取得できる。
- scopeに `k:app_settings:read k:app_settings:write k:file:write` が含まれる。
- 以後、このスクリプトを使わずにdeployできる。

### Phase 3: refresh tokenからaccess tokenを取得する

`scripts/deploy-customize.mjs` 内に `getAccessTokenFromRefreshToken()` を実装する。

リクエスト:

```http
POST https://{subdomain}.kintone.com/oauth2/token
Content-Type: application/x-www-form-urlencoded
Authorization: Basic base64(client_id:client_secret)

grant_type=refresh_token&refresh_token={refresh_token}
```

合格条件:

- ブラウザを開かずに `access_token` を取得できる。
- `expires_in` が返る。

### Phase 4: JavaScriptファイルをアップロードしてfileKeyを得る

`deploy-customize.mjs` で `POST /k/v1/file.json` を実行する。

要件:

- `Authorization: Bearer {access_token}` を使う。
- `multipart/form-data` で `src/desktop.js` をアップロードする。
- レスポンスの `fileKey` を取得する。

疑似コード:

```text
const form = new FormData();
form.append('file', new Blob([jsContent], { type: 'application/javascript' }), 'desktop.js');
fetch(`https://${subdomain}.kintone.com/k/v1/file.json`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${accessToken}` },
  body: form
});
```

合格条件:

- HTTP 200。
- レスポンスに `fileKey` が含まれる。

### Phase 5: pre-liveのJavaScriptカスタマイズ設定を更新する

`PUT /k/v1/preview/app/customize.json` を実行する。

最小実験では、desktop JSだけを登録する。scopeは安全のため最初は `ADMIN` にする。

リクエスト例:

```json
{
  "app": "${KINTONE_APP_ID}",
  "scope": "ADMIN",
  "desktop": {
    "js": [
      {
        "type": "FILE",
        "file": {
          "fileKey": "${uploadedFileKey}"
        }
      }
    ],
    "css": []
  },
  "mobile": {
    "js": [],
    "css": []
  }
}
```

注意:

- この実験では既存のJS/CSS設定を上書きする可能性がある。
- 既存設定を守りたい場合は、先に `GET /k/v1/preview/app/customize.json?app={appId}` で取得して、desktop.js配列にPoCファイルだけ追加する実装にする。
- テスト用新規アプリを使う場合は上書きでよい。

合格条件:

- HTTP 200。
- レスポンスに `revision` が含まれる。

### Phase 6: deployする

`POST /k/v1/preview/app/deploy.json` を実行する。

リクエスト例:

```json
{
  "apps": [
    {
      "app": "${KINTONE_APP_ID}"
    }
  ]
}
```

合格条件:

- HTTP 200相当で完了する。
- 必要に応じてdeploy status APIで `SUCCESS` を確認する。
- kintoneのアプリ画面を開くと `src/desktop.js` の表示が出る。

### Phase 7: 2回目の反映でブラウザ認証が不要なことを確認する

`src/desktop.js` の文言を変更する。

例:

```javascript
el.textContent = 'kintone JS OAuth CI PoC loaded v2: ' + new Date().toISOString();
```

再度 `deploy-customize.mjs` を実行する。

合格条件:

- ブラウザ認証なしで完了する。
- 新しいJSがアップロードされる。
- カスタマイズ設定が更新される。
- deploy後、画面表示がv2に変わる。

## 7. `deploy-customize.mjs` の最低限の仕様

### 入力

環境変数:

```text
KINTONE_SUBDOMAIN
KINTONE_APP_ID
KINTONE_OAUTH_CLIENT_ID
KINTONE_OAUTH_CLIENT_SECRET
KINTONE_REFRESH_TOKEN
```

### 処理順

```text
1. 環境変数を検証する。
2. refresh tokenでaccess tokenを取得する。
3. src/desktop.jsを読み込む。
4. /k/v1/file.json へアップロードし、fileKeyを取得する。
5. /k/v1/preview/app/customize.json をPUTする。
6. /k/v1/preview/app/deploy.json をPOSTする。
7. 可能ならdeploy statusをポーリングしてSUCCESSを確認する。
8. 成功/失敗を標準出力に明確に表示する。
```

### ログ方針

- access token、refresh token、client secretは絶対に表示しない。
- fileKey、revision、appId、endpoint、HTTP statusは表示してよい。

## 8. GitHub Actionsでの追加検証案

ローカルで成功した後、任意で `.github/workflows/deploy.yml` を追加する。

```yaml
name: Deploy kintone JavaScript customization PoC

on:
  workflow_dispatch:

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
      - run: npm ci
      - run: node scripts/deploy-customize.mjs
        env:
          KINTONE_SUBDOMAIN: ${{ secrets.KINTONE_SUBDOMAIN }}
          KINTONE_APP_ID: ${{ secrets.KINTONE_APP_ID }}
          KINTONE_OAUTH_CLIENT_ID: ${{ secrets.KINTONE_OAUTH_CLIENT_ID }}
          KINTONE_OAUTH_CLIENT_SECRET: ${{ secrets.KINTONE_OAUTH_CLIENT_SECRET }}
          KINTONE_REFRESH_TOKEN: ${{ secrets.KINTONE_REFRESH_TOKEN }}
```

GitHub Actionsでの合格条件:

- `workflow_dispatch` で手動実行できる。
- ブラウザ認証なしでdeployまで成功する。
- ActionsログにSecretが出力されない。
- kintone画面で反映済みJSが動く。

## 9. 合否判定

### PASS

以下をすべて満たせばPASS。

- 初回OAuthでrefresh tokenを取得できる。
- refresh tokenを使い、ブラウザ認証なしでaccess tokenを再発行できる。
- access tokenで `/k/v1/file.json` にJSをアップロードできる。
- 返却されたfileKeyで `/k/v1/preview/app/customize.json` を更新できる。
- `/k/v1/preview/app/deploy.json` でlive appへ反映できる。
- kintoneのレコード一覧画面でサンプルJSの表示を確認できる。
- JSを変更して2回目deployを実施しても、ブラウザ認証なしで反映できる。

### FAIL

以下のいずれかに該当すればFAIL、または追加調査が必要。

- refresh tokenでaccess tokenを取得できない。
- `Update Customization` が権限不足で失敗する。
- `Upload File` は成功するが、`Update Customization` にfileKeyを渡すと失敗する。
- deployは成功するが画面でJSが動かない。
- 2回目deployでブラウザ認証が必要になる。
- GitHub ActionsでSecretがログに漏れる。

## 10. 想定される失敗原因と確認ポイント

### 10.1 `invalid_grant`

- refresh tokenが誤っている。
- 別のOAuthクライアントのrefresh tokenを使っている。
- Client ID / Client Secretの組み合わせが違う。

### 10.2 `403` または権限エラー

- OAuth認可したユーザーがkintone管理者ではない。
- 対象アプリのアプリ管理権限がない。
- OAuth scopeに `k:app_settings:write` または `k:file:write` が不足している。
- OAuthクライアントの利用対象ユーザーに実験用管理者が含まれていない。

### 10.3 JSが画面で動かない

- deployが完了していない。
- scopeが `ADMIN` のため、管理者以外のユーザーで見ている。
- desktop側ではなくmobile側だけを見ている。
- 対象イベントが画面と合っていない。
- ブラウザキャッシュの影響。シークレットウィンドウやハードリロードで確認する。

### 10.4 既存カスタマイズを消してしまった

- `PUT /k/v1/preview/app/customize.json` は指定したdesktop/mobile設定で置き換える動きになるため、既存設定を保持したい場合は先にGETしてマージする必要がある。
- 最小実験では必ず新規テストアプリを使うこと。

## 11. セキュリティ注意事項

- `refresh_token`、`client_secret`、`access_token` はGitにコミットしない。
- `.env` を `.gitignore` に入れる。
- ログにSecretを出さない。
- GitHub Actions Secretsに保存する場合は、実験用OAuthクライアントと実験用管理者を使う。
- 実験終了後、不要なOAuthクライアント、refresh token、GitHub Secretsを削除する。
- kintone環境でIP制限を使っている場合は、OAuthクライアント側のIPが許可されている必要がある可能性がある。

## 12. Codexへの具体的な作業指示

1. 新規ディレクトリ `kintone-js-oauth-ci-poc` を作成する。
2. Node.jsベースの最小プロジェクトを作る。
3. `src/desktop.js` を作成する。
4. `scripts/get-initial-refresh-token.mjs` を作成する。
5. `scripts/deploy-customize.mjs` を作成する。
6. `.env.example` と `.gitignore` を作成する。
7. `README.md` に実行手順を書く。
8. ローカルで `npm run get-token` と `npm run deploy` が実行できる形にする。
9. 可能なら `npm run lint` または `node --check` 相当の構文チェックを入れる。
10. GitHub Actionsはローカル成功後に追加する。最初から必須にしない。

## 13. `package.json` の例

```json
{
  "name": "kintone-js-oauth-ci-poc",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "get-token": "node scripts/get-initial-refresh-token.mjs",
    "deploy": "node scripts/deploy-customize.mjs",
    "check": "node --check scripts/get-initial-refresh-token.mjs && node --check scripts/deploy-customize.mjs && node --check src/desktop.js"
  },
  "dependencies": {
    "dotenv": "latest"
  }
}
```

Node 20以上なら `fetch`, `FormData`, `Blob` が利用できる想定。ただし、ファイルアップロードでNode標準の `FormData` が扱いにくい場合は、`form-data` や `undici` を使ってよい。

## 14. 実験結果の記録テンプレート

```markdown
# 実験結果

## 実験日時

YYYY-MM-DD

## 対象

- kintone subdomain:
- app id:
- OAuth scope:
- 実行環境: local / GitHub Actions

## 結果

- 初回refresh token取得: PASS / FAIL
- refresh tokenによるaccess token再発行: PASS / FAIL
- JS upload: PASS / FAIL
- customize update: PASS / FAIL
- deploy: PASS / FAIL
- 画面でJS表示: PASS / FAIL
- 2回目deployでブラウザ認証不要: PASS / FAIL

## エラー

- HTTP status:
- response body:
- 推定原因:

## 結論

- 実現可能 / 追加調査が必要 / 不可
- 次に既存リポジトリへ拡張する場合の論点:
```

## 15. この実験が成功した後の拡張候補

- `customize.json` をGit管理し、desktop/mobile、JS/CSS、scope、読み込み順を宣言的に管理する。
- 既存カスタマイズをGETしてマージするのではなく、Git側を唯一の正として完全置換するかを決める。
- `scope: ADMIN` で先行反映し、確認後に `scope: ALL` へ切り替える2段階deployにする。
- 複数アプリ・複数環境に対応する。
- refresh tokenの保管先をGitHub Actions Secrets、Vault、クラウドSecret Managerなどにする。
- deploy前後で `GET /k/v1/app/customize.json` を取得し、反映内容をログに要約する。
- rollback用に直前のcustomize設定をartifactとして保存する。
