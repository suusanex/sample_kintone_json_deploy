# 実験結果テンプレート

## 実験日時

YYYY-MM-DD

## 対象

- kintone サブドメイン: 
- app id: 
- OAuth scope: `k:app_settings:read k:app_settings:write k:file:write`
- 実行環境: local / GitHub Actions

## 合否

- 初回refresh token取得: PASS / FAIL
- refresh tokenでaccess token再発行: PASS / FAIL
- JS upload: PASS / FAIL
- customize update: PASS / FAIL
- deploy: PASS / FAIL
- 画面確認: PASS / FAIL
- 2回目deployでブラウザ認証不要: PASS / FAIL

## 詳細

- 使用 endpoint:
  - `/oauth2/token`
  - `/k/v1/file.json`
  - `/k/v1/preview/app/customize.json`
  - `/k/v1/preview/app/deploy.json`
  - `/k/v1/preview/app/deploy.json?apps[0]=...`
- fileKey:
- revision:
- 最終 deploy status:
- 取得ログ抜粋:

## エラー

- HTTP status:
- response body:
- 推定原因:

## 結論

- 実現可否: `実現可能 / 追加調査必要 / 不可`
- 次ステップ:
