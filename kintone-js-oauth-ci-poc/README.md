# kintone JavaScript OAuth CI PoC

このディレクトリは、kintone の JavaScript カスタマイズを
管理者 OAuth の refresh token を使って CI/CD から自動反映する
最小実験を行うための構成です。

`get-token` と `deploy` は C# CLI で実行します。
kintone への初回登録対象 JS (`src/desktop.js`) 以外は
すべて .NET 10 で実装しています。

## 目的

- 初回のみ管理者 OAuth 認可して refresh token を取得する
- 2回目以降は refresh token から access token を再発行して自動反映する
- `GET/POST /k/v1/file.json`、`PUT /k/v1/preview/app/customize.json`、`POST /k/v1/preview/app/deploy.json`、`GET /k/v1/preview/app/deploy.json` を一連で実行する
- `workflow_dispatch` で GitHub Actions からも動作確認できる状態にする

## ディレクトリ構成

```text
kintone-js-oauth-ci-poc/
  .env.example
  .gitignore
  customize.json
  README.md
  src/
    desktop.js
    KintoneJsDeploy.Cli/
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

## 前提

- .NET SDK 10.0
- kintone 管理者アカウントとテストアプリ
- `k:app_settings:read`,`k:app_settings:write`,`k:file:write` スコープを持つ OAuth クライアント

## ローカル実行手順

### 1. 環境変数の準備

`.env.example` をコピーして `.env` を作成し、必須値を入力する。  
`.env` は Git 管理しない（このリポジトリの `.gitignore` に含まれる）。
`KINTONE_DESKTOP_JS_PATH` を設定すれば、対象 JS のパスを上書きできる。

```powershell
copy .env.example .env
notepad .env
```

### 2. 初回 refresh token 取得

```powershell
dotnet run --project src/KintoneJsDeploy.Cli -- get-token
```

表示された URL をブラウザで開く（自動で開くことも想定）。

認可完了後、`refresh token` が端末へ表示される。  
**この値は端末で保護して次回以降のデプロイ時に `KINTONE_REFRESH_TOKEN` として使う。**

### 3. deploy の実行

```powershell
dotnet run --project src/KintoneJsDeploy.Cli -- deploy
```

実行内容:

1. `KINTONE_REFRESH_TOKEN` から access token を再発行
2. `src/desktop.js` をアップロードして `fileKey` 取得
3. pre-live の customize を上書き更新（desktop js のみ）
4. deploy API を実行して状態をポーリング

### 4. 2回目以降の確認

`src/desktop.js` の文言を変更して再度 `deploy` を実行し、認可なしで反映できるか確認する。

## コマンド

- `get-token`
  - 初回 OAuth 認可
  - `KINTONE_REFRESH_TOKEN` を表示
  - `scope` と `expires_in` を表示
- `deploy`
  - refresh token から access token を再発行
  - JS upload / customize update / deploy / deploy status check を実行
- `check`
  - ローカルの最小構成チェック

```powershell
dotnet run --project src/KintoneJsDeploy.Cli -- check
```

## セキュリティ

- access token / refresh token / client secret は標準出力に出さない
- 失敗時も `Exception.ToString()` を trace 出力する
- `.env` はコミット対象外
- GitHub Actions では Secrets のみを使う

## 既知の制約

- 最小実験のため、既存 customize 設定を上書きします
- mobile JS / CSS / 複数アプリ / 複数環境は未対応
- rollback や前バージョン差分保存は未対応
- ログには endpoint / status / fileKey / revision / appId 程度のみを表示
*** End Patch
