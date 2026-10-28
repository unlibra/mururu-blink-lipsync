# リリース

## 運用

1. `ToolVersion`、CHANGELOGの版番号・日付・変更内容を揃え、mainへ反映します。
2. mainのCI成功を確認し、リリースするコミットへ `v<バージョン>` タグを付けてpushします。タグのpushはRelease公開を開始する操作です。
3. [Releaseワークフロー](../../.github/workflows/release.yml)がWindowsとLinuxで検査・生成します。両方の成功と成果物の一致を確認してからGitHub Releasesへ公開します。
4. ReleaseのAssetsから `.unitypackage` をダウンロードし、そのままBOOTHへアップロードします。自動表示されるSource codeのZIP・tar.gzは配布用unitypackageではありません。

Release本文には元コミットとunitypackageのSHA-256を記載します。BOOTHにはこの同じファイルを登録し、手元で再生成したものへ差し替えません。

mainとPRでも検査・生成しますが、Release公開は行いません。既に存在するReleaseは上書きせず停止します。公開済みの修正は版番号を上げて新しいReleaseにします。

## 生成と検証

- 実装は `Tools~/release.mjs`。Node.js 24以降とGitを使い、PowerShell・外部のtarコマンド・追加のnpmパッケージには依存しません。
- 作業ファイルではなく、対象コミットのGit blobから生成します。改行変換やファイル時刻、ローカルの未追跡ファイルが配布物へ入ることを防ぎます。作業中変更があるcheckoutからの生成は停止します。
- `Editor/`、`Runtime/`、README、CHANGELOG、LICENSE、NOTICEとそれぞれのmetaを含めます。従来の配置とフォルダGUID、各アセットのGUIDを維持します。
- GUIDの欠損・重複、参照先のないmeta、シンボリックリンク、版番号・日付の不整合を検出します。
- アーカイブの所有者名と元パスを保存せず、所有者IDと時刻を0に固定します。USTARは本ツールのGUIDディレクトリと既知のファイル名だけを扱います。
- 解凍した全データ・メタデータを元コミットと照合し、WindowsとLinuxの成果物がバイト単位で一致すること、タグが版番号に一致しmainの履歴に含まれることを公開前に確認します。
- テストは別実装での展開（unitypackageはtar、VPM ZIPはWindows標準tar/Linuxのunzip）、LF/CRLFのcheckoutでの同一性、混入・不完全な入力・改ざん・タグ不一致の拒否を含みます。
- このCIはUnityの実行テストを代替しません。製品コードの変更に必要なUnity検証は [TEST_ENVIRONMENT.md](TEST_ENVIRONMENT.md) に従います。

CIの生成先は `dist~/` です。manifestと生成したRelease本文はActionsの内部成果物として保存し、公開Releaseにはunitypackage、VPM ZIP、package.jsonを添付します。VPM ZIPはルートにpackage.json・Editor・Runtime・文書とmetaを含み、テスト・生成済みアセットを含みません。必要な調査時は、変更のないコミット済みcheckoutで同じ処理を実行できます。

```sh
node Tools~/release.mjs build
node --test Tools~/release.test.mjs Tools~/vpm.test.mjs
```

## VCC

Package IDは com.unlibra.mururu-blink-lipsync。package.json・ToolVersion・CHANGELOG・タグの版番号を揃えます。VPM ZIPの内容を元コミットと照合し、Windows/Linuxで一致させてから公開します。

共通一覧は [unlibra/vpm](https://github.com/unlibra/vpm) で管理します。Release公開後、notify-vpmジョブが共通一覧の更新を起動し、配信の完了を待ちます。vpm側は公開済みZIPとpackage.jsonを検証して新しい版を記録し、Pagesへ配信します。通知の再試行はnotify-vpmジョブだけを再実行します。既存Releaseを作り直しません。

初回の設定は [vpmリポジトリの手順](https://github.com/unlibra/vpm#自動更新の初期設定) に従います。製品リポジトリのActions Secret VPM_DISPATCH_TOKENに、unlibra/vpmだけを対象とするActions read/write権限の認証を登録します。認証にはvpmへのContents write権限は不要です。メタデータのコミットと配信はvpm自身のGITHUB_TOKENで行います。

Release公開前に通知先のworkflowと配信URLを確認します。vpmのmain・Pages・初期一覧を先に公開してください。VPM版の公開後は旧版ZIP・一覧エントリを削除しません。

旧unitypackageのフォルダには利用者のGeneratedがあるため、legacyFolders/legacyFilesによる自動削除は有効にしていません。配布形式の切り替え手順は利用者向けインストールページに記載します。
