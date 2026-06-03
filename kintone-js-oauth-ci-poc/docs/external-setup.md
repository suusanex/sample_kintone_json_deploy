# 外部作業手順書（kintone 側）

以下はこのリポジトリ外で実施する作業をまとめた手順です。
CLI の対象外内容が混じるので、ここに分離して管理します。

## 1. kintone テストアプリ作成

- 実験用のアプリを新規作成する
- アプリ管理権限を付与した管理者ユーザーを想定

## 2. OAuth クライアント作成

1. kintone 管理画面の OAuth クライアント管理へ移動
2. 新規クライアント作成
3. 必要 Scope を設定
   - `k:app_settings:read`
   - `k:app_settings:write`
   - `k:file:write`
4. Redirect URI を `http://localhost:3000/callback` など固定で登録
5. OAuth 利用対象ユーザーを実験管理者へ追加

## 3. CLI 実行時に必要な情報

- Client ID
- Client Secret
- Redirect URI
- App ID
- テナントサブドメイン（`example.kintone.com` の `example`）

## 4. 初回 refresh token の取得

ローカルで `get-token` を実行し、表示された URL で許可を行う。  
表示される `refresh token` は安全な場所へ保存。

## 5. GitHub Secrets 登録

- `KINTONE_SUBDOMAIN`
- `KINTONE_APP_ID`
- `KINTONE_OAUTH_CLIENT_ID`
- `KINTONE_OAUTH_CLIENT_SECRET`
- `KINTONE_REFRESH_TOKEN`

## 6. kintone 画面の確認

deploy 成功後、対象アプリのレコード一覧画面を開き、ヘッダ付近に
`kintone JS OAuth CI PoC loaded` 文言が表示されることを確認する。

表示が更新されない場合はハードリロードまたはシークレットモードで再確認する。

## 7. 実験終了後の片付け

- GitHub Secret の削除
- OAuth クライアント削除または秘密鍵ローテーション
- 使い終わった refresh token の廃棄
- 実験アプリの不要な customize 設定の整理
