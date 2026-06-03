# kintone JavaScript OAuth CI/CD 最小実験 実装計画

## 1. 結論

このリポジトリには、kintone の JavaScript カスタマイズを OAuth refresh token 経由で自動反映するための最小 PoC を作成する。

リポジトリ上で作成するものは、登録対象の JavaScript ファイルを除き、原則として .NET 10 の C# で実装する。kintone テナント、kintone アプリ、OAuth クライアント、GitHub Secrets など外部サービス側でしか実行できない作業は、手順書として整備する。

## 2. 前提

- 実験用 kintone テナントは既に存在する。
- 対象アプリは新規または実験専用アプリを使う。
- OAuth 認可を行うユーザーは kintone システム管理者であり、対象アプリのアプリ管理権限を持つ。
- OAuth クライアントは Authorization Code Grant を使う。
- 最小実験では desktop JavaScript のみを対象にする。
- 最小実験では CSS、mobile JavaScript、複数アプリ、複数環境、既存カスタマイズの高度なマージは扱わない。

## 3. 目標

## 3.1 最終ゴール

サンプル JavaScript ファイルを kintone アプリの JavaScript カスタマイズとして API 登録し、deploy 後に kintone のレコード一覧画面でその JavaScript が実行されることを確認する。

## 3.2 検証する仮説

- 初回のみ kintone 管理者が OAuth 認可を行い、refresh token を取得できる。
- 2回目以降は refresh token から access token を再発行できる。
- 再発行した access token で JavaScript ファイルをアップロードできる。
- アップロードで返却された fileKey を使い、pre-live の JavaScript カスタマイズ設定を更新できる。
- pre-live 設定を live app へ deploy できる。
- 2回目以降の反映ではブラウザ認証が不要になる。

## 4. 成果物

## 4.1 リポジトリ上に作成するもの

```text
kintone-js-oauth-ci-poc/
  README.md
  .gitignore
  .env.example
  customize.json
  src/
    desktop.js
  src/KintoneJsDeploy.Cli/
    KintoneJsDeploy.Cli.csproj
    Program.cs
    Options/
    Services/
    Models/
  docs/
    external-setup.md
    experiment-result-template.md
  .github/
    workflows/
      deploy.yml
```

`deploy.yml` はローカル実験が成立した後に使う追加検証用として作成する。ただし、初回実装時点では手動実行の `workflow_dispatch` のみ有効にする。

## 4.2 外部作業として手順化するもの

- kintone テストアプリ作成手順
- kintone OAuth クライアント作成手順
- 初回 OAuth 認可と refresh token 取得手順
- GitHub Actions Secrets 登録手順
- kintone 画面での動作確認手順
- 実験終了後の Secret / OAuth クライアント削除手順

## 5. 技術方針

## 5.1 C# / .NET 方針

- .NET 10 の Console アプリとして CLI を作成する。
- HTTP 通信は `HttpClient` を使う。
- JSON 処理は `System.Text.Json` を使う。
- multipart file upload は `MultipartFormDataContent` を使う。
- ローカル callback 受信は `HttpListener` または ASP.NET Core Minimal API のどちらかを採用する。
- 依存パッケージは最小限にする。
- Secret は標準出力に表示しない。
- 例外発生時は `Exception.ToString()` を trace ログへ出力する。

## 5.2 JavaScript 方針

- `src/desktop.js` は kintone に登録する実験対象として作成する。
- レコード一覧画面表示時にヘッダー領域へ PoC バナーを表示する。
- 2回目 deploy 確認時に文言変更しやすい実装にする。

## 5.3 カスタマイズ設定方針

- 最小実験では `scope: "ADMIN"` を使う。
- 実験専用アプリを前提に、既存 desktop/mobile JS/CSS 設定は上書きする。
- 既存設定保護が必要な場合の拡張案は README に明記する。

## 6. 実装フェーズ

## 6.1 Phase 1: プロジェクト骨格の作成

作成内容:

- `kintone-js-oauth-ci-poc/README.md`
- `kintone-js-oauth-ci-poc/.gitignore`
- `kintone-js-oauth-ci-poc/.env.example`
- `kintone-js-oauth-ci-poc/customize.json`
- `kintone-js-oauth-ci-poc/src/desktop.js`
- `kintone-js-oauth-ci-poc/src/KintoneJsDeploy.Cli/`

受け入れ条件:

- CLI プロジェクトが .NET 10 を対象にしている。
- `.env` や Secret 類が Git 管理対象外になっている。
- サンプル JS が kintone レコード一覧画面で確認しやすい表示を行う。

## 6.2 Phase 2: 初回 refresh token 取得 CLI

CLI コマンド例:

```powershell
dotnet run --project src/KintoneJsDeploy.Cli -- get-token
```

実装内容:

- 必須環境変数を検証する。
- OAuth authorization URL を生成する。
- `state` をランダム生成する。
- ローカル callback endpoint を一時起動する。
- callback で `code` と `state` を検証する。
- `/oauth2/token` に `grant_type=authorization_code` を送信する。
- access token、refresh token、expires_in、scope の結果を表示する。

Secret 取り扱い:

- refresh token は取得時だけユーザーに見せる。
- refresh token はファイル保存しない。
- client secret と access token はログ出力しない。

受け入れ条件:

- refresh token を取得できる。
- scope に `k:app_settings:read k:app_settings:write k:file:write` が含まれる。

## 6.3 Phase 3: refresh token から access token を取得

CLI 内部処理:

- `KINTONE_REFRESH_TOKEN` を読み込む。
- `/oauth2/token` に `grant_type=refresh_token` を送信する。
- access token と expires_in を取得する。

受け入れ条件:

- ブラウザを開かずに access token を取得できる。
- Secret をログに出さない。

## 6.4 Phase 4: JavaScript ファイルアップロード

実装内容:

- `src/desktop.js` を読み込む。
- `POST /k/v1/file.json` に `multipart/form-data` でアップロードする。
- レスポンスから `fileKey` を取得する。

受け入れ条件:

- HTTP 成功応答を得られる。
- `fileKey` が取得できる。
- ログには endpoint、HTTP status、fileKey のみを出す。

## 6.5 Phase 5: pre-live カスタマイズ設定更新

実装内容:

- `PUT /k/v1/preview/app/customize.json` を実行する。
- desktop JS にアップロード済み fileKey を指定する。
- mobile JS/CSS と desktop CSS は空配列にする。

リクエスト方針:

```json
{
  "app": "KINTONE_APP_ID",
  "scope": "ADMIN",
  "desktop": {
    "js": [
      {
        "type": "FILE",
        "file": {
          "fileKey": "uploaded-file-key"
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

受け入れ条件:

- HTTP 成功応答を得られる。
- レスポンスから revision を取得できる。

## 6.6 Phase 6: deploy 実行

実装内容:

- `POST /k/v1/preview/app/deploy.json` を実行する。
- 可能なら deploy status API をポーリングして `SUCCESS` を確認する。
- deploy status のポーリング上限時間を設ける。

受け入れ条件:

- deploy API が成功する。
- deploy status が確認できる場合は `SUCCESS` になる。
- kintone のレコード一覧画面で PoC バナーが表示される。

## 6.7 Phase 7: 2回目 deploy の確認

作業内容:

- `src/desktop.js` の表示文言を変更する。
- `deploy` コマンドを再実行する。
- ブラウザ認証なしで反映されることを確認する。

受け入れ条件:

- 初回認可スクリプトを使わずに deploy できる。
- kintone 画面の表示文言が更新される。

## 7. CLI コマンド設計

## 7.1 get-token

用途:

- 初回 OAuth 認可を実行し、refresh token を取得する。

入力環境変数:

```text
KINTONE_SUBDOMAIN
KINTONE_OAUTH_CLIENT_ID
KINTONE_OAUTH_CLIENT_SECRET
KINTONE_OAUTH_REDIRECT_URI
```

## 7.2 deploy

用途:

- refresh token から access token を取得し、JS upload、customize update、deploy を一括実行する。

入力環境変数:

```text
KINTONE_SUBDOMAIN
KINTONE_APP_ID
KINTONE_OAUTH_CLIENT_ID
KINTONE_OAUTH_CLIENT_SECRET
KINTONE_REFRESH_TOKEN
```

## 7.3 check

用途:

- C# プロジェクトのビルド確認を行う。
- JS は構文チェックの方法を README に記載する。

候補:

```powershell
dotnet build src/KintoneJsDeploy.Cli
```

## 8. ドキュメント計画

## 8.1 README.md

記載内容:

- 実験の目的
- 全体構成
- 必要な .NET SDK
- 必要な環境変数
- 初回 refresh token 取得手順
- ローカル deploy 手順
- 2回目 deploy 検証手順
- GitHub Actions 実行手順
- Secret をログに出さない注意
- 既存カスタマイズを上書きする注意

## 8.2 docs/external-setup.md

記載内容:

- kintone アプリ作成手順
- OAuth クライアント作成手順
- 必要 scope
- Redirect URI 設定
- OAuth 利用対象ユーザー設定
- GitHub Secrets 登録手順
- 実験終了後の片付け手順

## 8.3 docs/experiment-result-template.md

記載内容:

- 実験日時
- 対象 kintone subdomain
- app id
- OAuth scope
- 実行環境
- 各 Phase の PASS / FAIL
- エラー記録欄
- 結論欄

## 9. GitHub Actions 計画

作成ファイル:

```text
.github/workflows/deploy.yml
```

方針:

- `workflow_dispatch` の手動実行のみ。
- `actions/setup-dotnet` で .NET 10 SDK を指定する。
- `dotnet run --project ... -- deploy` を実行する。
- 必要値は GitHub Actions Secrets から渡す。
- Secret 値はログに出さない。

受け入れ条件:

- 手動実行で deploy まで成功する。
- Actions ログに access token、refresh token、client secret が出ない。
- kintone 画面で JS が反映される。

## 10. 環境変数

ローカル:

```text
KINTONE_SUBDOMAIN=example
KINTONE_APP_ID=123
KINTONE_OAUTH_CLIENT_ID=...
KINTONE_OAUTH_CLIENT_SECRET=...
KINTONE_OAUTH_REDIRECT_URI=http://localhost:3000/callback
KINTONE_REFRESH_TOKEN=...
```

GitHub Actions Secrets:

```text
KINTONE_SUBDOMAIN
KINTONE_APP_ID
KINTONE_OAUTH_CLIENT_ID
KINTONE_OAUTH_CLIENT_SECRET
KINTONE_REFRESH_TOKEN
```

## 11. 合否判定

PASS 条件:

- 初回 OAuth で refresh token を取得できる。
- refresh token から access token を再発行できる。
- `/k/v1/file.json` に JS をアップロードできる。
- `/k/v1/preview/app/customize.json` を更新できる。
- `/k/v1/preview/app/deploy.json` で live app へ反映できる。
- kintone レコード一覧画面でサンプル JS の表示を確認できる。
- 2回目 deploy でブラウザ認証が不要なことを確認できる。

FAIL または追加調査条件:

- refresh token で access token を取得できない。
- customization update が権限不足になる。
- file upload は成功するが fileKey を customization update に渡すと失敗する。
- deploy は成功するが JS が画面で動かない。
- 2回目 deploy でブラウザ認証が必要になる。
- Secret がログに出力される。

## 12. 想定リスクと対策

## 12.1 既存カスタマイズの上書き

リスク:

- `PUT /k/v1/preview/app/customize.json` は指定した設定で置き換えるため、既存 JS/CSS を消す可能性がある。

対策:

- 最小実験では必ず実験専用アプリを使う。
- README に上書きリスクを明記する。
- 将来拡張として、GET してからマージする方式を検討する。

## 12.2 OAuth 権限不足

リスク:

- scope、管理者権限、アプリ管理権限、OAuth 利用対象ユーザーの不足で 403 になる。

対策:

- `docs/external-setup.md` に確認チェックリストを置く。
- エラー時は HTTP status と response body を Secret を除いて表示する。

## 12.3 Secret 漏えい

リスク:

- access token、refresh token、client secret がログや Git に残る。

対策:

- `.env` を `.gitignore` に含める。
- CLI のログ出力を明示的に制御する。
- 例外ログにも Secret を含めない設計にする。
- GitHub Actions では Secrets 経由でのみ渡す。

## 12.4 deploy 完了前の画面確認

リスク:

- deploy API 呼び出し直後は反映が完了していない可能性がある。

対策:

- deploy status API のポーリングを実装する。
- タイムアウト時は失敗として明示する。

## 13. 実装順序

1. ディレクトリ構成とサンプル JS を作成する。
2. .NET 10 CLI プロジェクトを作成する。
3. 環境変数読み取りと入力検証を実装する。
4. OAuth authorization code flow の `get-token` を実装する。
5. refresh token flow を実装する。
6. file upload API 呼び出しを実装する。
7. customization update API 呼び出しを実装する。
8. deploy API と deploy status 確認を実装する。
9. README と外部手順書を作成する。
10. GitHub Actions workflow を追加する。
11. ローカルで初回 refresh token 取得を実施する。
12. ローカルで deploy を実施する。
13. JS 文言変更後、2回目 deploy を実施する。
14. 必要に応じて GitHub Actions で追加検証する。

## 14. 作業境界

リポジトリ上で実施する作業:

- C# CLI 実装
- サンプル JavaScript 作成
- `.env.example` / `.gitignore` 作成
- README 作成
- 外部作業手順書作成
- 実験結果テンプレート作成
- GitHub Actions workflow 作成

キミまたは管理者が外部で実施する作業:

- kintone アプリ作成
- kintone OAuth クライアント作成
- 初回 OAuth 認可操作
- refresh token の安全な保管
- GitHub Secrets 登録
- kintone 画面での最終目視確認
- 実験終了後の OAuth クライアント / Secrets 削除

## 15. 完了条件

この計画の完了条件は次の通り。

- リポジトリに PoC 実装一式が存在する。
- ローカル実行手順が README にまとまっている。
- kintone / GitHub 側で必要な作業が docs にまとまっている。
- 初回認可、deploy、2回目 deploy の合否を記録できるテンプレートがある。
- Secret を Git に含めない構成になっている。

