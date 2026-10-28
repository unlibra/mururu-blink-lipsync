# 専用Unity検証環境

## 場所と保持対象

テスト用Unityプロジェクトは製品リポジトリの外に用意します。`Run-Tests.ps1` は、既定で隣の `../mururu-blink-lipsync-tests/` を参照します。継続利用する検証環境はGit worktreeや一時ファイルとして削除しないでください。

有料アバターと検証用アセットは各自で用意し、Git・公開サイト・配布パッケージへ入れません。

| 対象 | 管理方法 |
| --- | --- |
| 製品と全テストのコード | このリポジトリの `Editor/`・`Runtime/`・`Tests/` が原本 |
| `Assets/Addon` | 原本から同期する実行用コピー。直接編集しない |
| `Assets/Lethal Cats` | 標準MururuのPrefab・FBX・標準FX・参照アセット |
| `Assets/AxisRepairProbe`・`CustomMouthProbe`・`PackageMouthProbe` | 不具合の再現に使った改変・座標補正・unitypackage導入モデル等の実データ |
| `Assets/MBLSFaceEmoDiagnostic` | FaceEmoの実アセットを使う検証用fixture |
| `Packages/` | 検証済み56パッケージのローカルコピー。別プロジェクトのキャッシュや絶対パスを参照しない |
| `ProjectSettings/` | 継続利用するUnityプロジェクトの設定 |
| `Library/`・`Temp/`・`Logs/`・NDMFの `__Generated/` | 再生成可能なキャッシュ・ビルド成果物 |

Unityは2022.3.22f1。SDK Avatars/Base 3.10.5、MA 1.18.7、NDMF 1.14.8、lilToon 2.3.4、Test Framework 1.1.29。全パッケージの固定版は [test-environment-packages.json](test-environment-packages.json) を参照してください。

スクリプトは指定版のパッケージがプロジェクト内のローカルコピーとして存在することを確認します。バージョン記録は検証条件であり、有料アセットや検証プロジェクトを配布するものではありません。

## 実行

Unity CLIと指定版のUnity Editorを用意し、専用プロジェクトをEditorで開いていない状態で、リポジトリ直下からPowerShell 7で実行します。

```powershell
./Documentation~/Run-Tests.ps1
```

別の配置へ移動した場合は `-ProjectPath <Unityプロジェクトのパス>` を明示します。プロジェクトの識別ファイル `.mbls-test-project` が必要です。

スクリプトはUnity・パッケージの版と必要な実データを確認し、製品・全テストを同期してUnity CLIでEditModeテストを実行します。実行前後に原本とコピーのhashを比較します。古い余剰ファイルは自動削除せず停止します。

結果XMLとEditorログはリポジトリの `Library/MBLS-Tests/<実行日時>/` に保存します。失敗・スキップ・実行0件を全件成功として扱いません。

全300件のうち、`Tests/Editor/PrivateAssets/` の4ファイル・7件は `PrivateAssets` カテゴリです。改変・座標補正・実FaceEmoの非公開実データを必要とします。ほかの293件にも標準Mururu等が必要なテストが含まれます。アバターを入れていない環境で一部がスキップされても、全件検証を完了したことにはなりません。

## 再構築・更新

製品・全テストのコードとパッケージのバージョンはGitで管理します。一方、非公開の実データとUnityプロジェクト設定は専用環境に保持しており、このリポジトリだけでは全件テスト環境を再構築できません。環境を作り直す場合は、上表の実データと指定版のパッケージを別途用意する必要があります。

実データ・パッケージ・ProjectSettingsを意図して変更した場合は、バージョン記録とこの手順を更新し、全件検証します。製品・全テストのコードは `Run-Tests.ps1` が原本から同期し、キャッシュはUnityが再生成します。

## 配布配置の検証

通常のAssets配置に加え、VPMのPackages配置でも全件実行します。

```powershell
./Documentation~/Run-Tests.ps1 -Placement Packages
```

Packages配置では、Assets/Addonを一時退避し、製品とテストを専用のPackages配置へ同期します。パッケージの登録とtestablesは実行中だけ追加し、終了時に元のmanifestとAssets/Addonを復元します。既存の専用Packages配置がある場合は上書きせず停止します。製品ZIPにはテストを含めません。

特定の回帰だけを調べる場合は -TestFilter を指定できます。限定実行を全件成功として扱いません。VCC本体の操作・公開一覧からの導入は、Packages配置でのUnity実行とは別に確認します。
