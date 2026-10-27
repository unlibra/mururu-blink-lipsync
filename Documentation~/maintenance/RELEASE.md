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
- テストは別実装のtarでの展開、LF/CRLFのcheckoutでの同一性、混入・不完全な入力・改ざん・タグ不一致の拒否を含みます。
- このCIはUnityの実行テストを代替しません。製品コードの変更に必要なUnity検証は [TEST_ENVIRONMENT.md](TEST_ENVIRONMENT.md) に従います。

CIの生成先は `dist~/` です。manifestと生成したRelease本文はActionsの内部成果物として保存し、公開Releaseに添付するファイルはunitypackageだけです。必要な調査時は、変更のないコミット済みcheckoutで同じ処理を実行できます。

```sh
node Tools~/release.mjs build
node --test Tools~/release.test.mjs
```

## VCC

VCC対応は初回公開とサイト切り替えの完了後に進めます。現在のunitypackage配布と混同せず、生成先・既存アセットの移行と実Unity環境での検証を行います。
