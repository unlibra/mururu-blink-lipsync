# ドキュメントサイトの運用

公開先は https://docs.unlibra.com/mururu-blink-lipsync/ です。このリポジトリのGitHub PagesへGitHub Actionsから直接配信します。

## GitHub設定

- PagesのBuild and deploymentはGitHub Actionsを選択します。
- このプロジェクト専用のカスタムドメインとCNAMEは設定しません。ユーザーサイトのカスタムドメイン `docs.unlibra.com` を継承し、リポジトリ名をパスに使います。
- Actionsの既定権限は読み取り専用です。Websiteのdeployジョブだけに `pages: write` と `id-token: write` を付けます。他リポジトリへの書き込み用デプロイ鍵は不要です。
- 実行を許可するActionはGitHub公式と、ワークフローに固定したpnpm/action-setupのコミットに限定します。Dependabotでpnpmの固定コミットを更新するときは、この許可リストも確認します。
- `github-pages` 環境のデプロイ元はmainに限定します。

## 配信と検索エンジンへの通知

[Website](../../.github/workflows/website.yml) はPRでは公開対象の検査・自動化テスト・VitePressビルドを行います。mainでは次も実行します。

1. ビルド成果物に、コミットと各ページの本文指紋を `publication.json` として記録します。
2. 配信前の公開サイトと比較し、追加・変更・削除されたページの通知計画を作成します。初回移行では旧サイトのHTMLとサイトマップを読みます。
3. このリポジトリのGitHub Pagesへ配信します。
4. サイトマップ内のページが応答することを確認します。
5. 公開されたコミットが成果物と一致することを確認してIndexNowへ通知します。失敗した配信では送信しません。

比較対象は本文・タイトル・説明・リンク・画像です。テーマのスクリプトやスタイルだけの変更では通知しません。同一URLの画像だけを差し替えた場合など、本文指紋に出ない変更を網羅するものではありません。

IndexNowの所有確認ファイルはこのサイト自身のパスに置きます。公開される確認用の文字列で、認証用の秘密情報ではありません。他プロジェクトのURLは通知対象にできません。

## 第三者の権利表示

Viteの `build.license` で、配信するJavaScriptへ含まれる依存ソフトウェアのライセンス一覧を `licenses/dependencies.md` に生成します。Geist MonoとNoto Sans JPのフォントは、それぞれのパッケージに付属するライセンス全文を `docs/public/licenses/` へ保存します。フォントを更新した場合はこの原本も確認・更新してください。公開サイトの「第三者ライセンス」から各表示を参照できます。

[Production check](../../.github/workflows/production-check.yml) は毎日と手動実行で、このマニュアルのページ、所有確認ファイル、公開状態とサイトマップの一致を検査します。

## 共有サイトからの切り替え

新しいPages配信の成功を確認してから、共有サイトに残るこのマニュアルの旧ビルド成果物を削除します。共有サイトのトップページ・カスタムドメイン・ブランド画像・他プロジェクトは維持します。ルートのサイトマップからこのマニュアルへの参照も維持します。

共有サイトに残る旧規約の公開ファイルも切り替え時に除去します。旧配布物や非公開の保管用Git履歴を削除する手順ではありません。

## ローカル検証

```sh
node --test Tools~/privacy.test.mjs Tools~/website.test.mjs
```

VitePressをビルドした後、`node Tools~/website.mjs stamp Documentation~/website/docs/.vitepress/dist` でページとサイトマップの対応を検証できます。`plan DIST OUTPUT` は公開中のページとの比較のみを行い、IndexNowへの送信や配信は行いません。
