# Mururu Blink & LipSync

> **使い方はマニュアルを参照してください。**
> https://docs.unlibra.com/mururu-blink-lipsync/
>
> このREADMEは開発者向けの技術資料です。

ムルルに、まばたき機能と声に合わせて口が動くリップシンク機能を追加するUnityツールです。Modular Avatarで非破壊に組み込みます。

## 対応環境

| 項目 | バージョン |
|---|---|
| Unity | 2022.3 |
| VRChat SDK Avatars | 3.10.x |
| Modular Avatar | 1.18.x 以降（NDMFを含む） |
| 対応アバター | Mururu（ver2.2.2で確認） |

## リポジトリ構成

| パス | 内容 |
|---|---|
| `Editor/Window/` | 設定ウィンドウ（`Tools > Mururu Blink & LipSync`）。インストール・適用・修復・アンインストールの処理を partial class で分割 |
| `Editor/Build/` | NDMFプラグイン。ビルド時の口の滑らかな復帰と、Bodyのマテリアルの反映 |
| `Editor/Generation/` | 滑らかな復帰に使う口形状と専用Animatorの生成 |
| `Editor/Data/` | MururuのNeutral口の内蔵データと、口形状の検証に使う許容値 |
| `Editor/Inspector/` | `MururuBlinkLipSyncInstallRecord` のInspector |
| `Runtime/` | `MururuBlinkLipSyncInstallRecord`。導入状態と導入前の設定をシーンに保存するComponent |
| `Tests/Editor/` | EditModeテスト。unitypackageには含めない |
| `Documentation~/` | Unityに取り込まれない公開マニュアル・保守資料。[資料の案内](Documentation~/README.md)を参照 |
| `Tools~/` | 公開対象と個人情報の検査・画像メタデータの除去 |
| `Generated/` | インストール時に生成されるアセット。リポジトリには含めない |

## 仕組み

インストールすると、アバター直下に `MururuBlinkLipSync` を作り、次を設定します。Body Meshと既存のFX・メニュー・パラメータは変更しません。

- **LipSync_Mouth**: BodyからAIUEOの口部分だけを切り出したSkinnedMeshRenderer。Avatar DescriptorのVisemeをこのメッシュに向ける
- **Eye Look**: EyelidsをBlendshapes（Bodyの `eye_blink`）にする
- **FX（MA Merge Animator）**
  - LipSyncレイヤー: 発話中は `LipSync_Mouth` を表示し、Bodyの口を `mouth_off` で隠す。ビルド時に、無音から80ms待機し、80msで元の表情の口に近づけてから切り替える制御を組み込む。対応外の口は無音から80ms後に瞬間切替。LipSyncを無効にしたジェスチャー・Facial EXでは常に元の表情の口
  - Blinkレイヤー: Blinkを無効にしたジェスチャー・Facial EXの間、Eyes Trackingを Animation にする。すべて有効なときはレイヤーを作らない
- **MururuBlinkLipSyncInstallRecord**: 導入時のバージョン、表情ごとの設定、導入前のVisemeとEye Look、生成アセットへの参照を保存する。アンインストール時はこれをもとに、導入後に利用者が変えていない項目だけを戻す
- **発話時の音符を非表示**: 初期設定はOFF。設定を導入状態に保存し、ビルド用コピーのFXで標準ムルルの `Facial LipSync` レイヤーのウェイトだけを0にする。元の共有FXは変更しない。OFFでは既存のウェイトを維持し、対象が欠損・重複する場合は警告して変更しない

生成アセット（`LipSync_Mouth_Generated.asset`、ON/OFFのAnimation Clip、FX Controller）はパッケージ直下の `Generated/` に作ります。修復・アンインストールのときは、読み込まれていないシーンやPrefabからの参照も調べ、使われているファイルは残します。

## 開発

### テスト

テスト用Unityプロジェクトはリポジトリの外に用意します。必要なアセットと実行方法は [検証環境の手順](Documentation~/maintenance/TEST_ENVIRONMENT.md) を参照してください。製品と全テストの原本はこのリポジトリで管理します。

```
./Documentation~/Run-Tests.ps1
```

PowerShell 7とUnity CLIを使用します。スクリプトが原本と実行用コピーを同期・照合し、失敗・スキップを検出します。エディターで専用プロジェクトを開いていると実行できません。テスト項目は [Documentation~/maintenance/TEST_CASES.md](Documentation~/maintenance/TEST_CASES.md) にあります。

### 公開前の検査

Node.js 24以降を用意し、各クローンで一度 `node Tools~/privacy.mjs install` を実行します。画像をGitへ登録する際のメタデータ除去と、コミット対象の検査が有効になります。

公開ファイルの追加・変更、PDFの更新、新しいリポジトリへの移行手順は [公開ファイルの管理](Documentation~/maintenance/PUBLICATION.md) を参照してください。

### ドキュメント

- ドキュメントサイトは `Documentation~/website/` の VitePress で作る。原稿は `docs/` のMarkdown。サイトには最新版だけを置く
- Node.js と pnpm で動かす。プレビューとビルドは `Documentation~/website/` で次を実行する

  ```
  pnpm install
  pnpm dev
  pnpm build
  ```

- サイト原稿をmainへ反映すると、[GitHub Actions](.github/workflows/website.yml)が検査・ビルドし、このリポジトリのGitHub Pagesへ直接配信する。PRでは検査とビルドだけを行う
- 公開用の商品画像は `Documentation~/booth/`。サイトのリンクカードの画像は `Documentation~/website/docs/public/images/link-card.webp`
- 編集原稿、販売方針、研究ログなどの非公開資料はリポジトリの外で管理します。

### リリース

配布物は[Releaseワークフロー](.github/workflows/release.yml)で生成する。mainとPRでは検査・ビルドを行い、`v<バージョン>` タグのpushでは検査成功後にGitHub Releasesへunitypackageを公開する。

BOOTHには、そのReleaseからダウンロードした同じunitypackageをアップロードする。手元で配布物を生成し直す必要はない。VCC対応は公開とサイト切り替えの完了後に進める。

生成処理はNode.js 24以降の `Tools~/release.mjs` に共通化し、WindowsとLinuxの生成結果を照合する。版番号はソースとCHANGELOGから取得し、タグとの一致を確認する。[リリース手順と検証内容](Documentation~/maintenance/RELEASE.md)を参照。

| 種類 | ファイル | 元 |
|---|---|---|
| 配布用unitypackage | `MururuBlinkLipSync_v<ツールのバージョン>.unitypackage` | `Editor/`・`Runtime/`・`README.md`・`CHANGELOG.md`・`LICENSE.txt`・`NOTICE.txt`（それぞれの `.meta` を含む） |
| CIの内部確認用 | `release-manifest.json` | 版番号、元コミット、ファイルサイズとSHA-256。BOOTHへの同梱不要 |

unitypackageには `Tests/` と `Generated/` を含めない。`Tests/` は利用者には不要で、Test Frameworkが入ったプロジェクトではコンパイルされ、Test Runnerにも表示される。`Generated/` は開発環境でインストールしたときに作られたもの。

### バージョン

`Editor/Window/MururuBlinkLipSyncSetupWindow.cs` の `ToolVersion`、[CHANGELOG.md](CHANGELOG.md)、マニュアルの表記を揃えます。

## ライセンス

本プロジェクトの独自作成部分には **PolyForm Perimeter License 1.0.1** を適用します。[ライセンス全文](LICENSE.txt)と[権利表示](NOTICE.txt)を参照してください。第三者のソフトウェア・フォント・アバター等には、それぞれのライセンス・利用条件が適用されます。

以下は利用条件の要約です。正式な条件はライセンス本文に従います。

- 個人・企業とも無料で利用でき、ツールを使った作品の制作・販売、動画・配信の収益化も可能です。
- このソフトウェアを利用して、本ツールと競合する商品・サービスを他者に提供することはできません。公式版の代替となるコピー品・改変版の提供は、有償・無償を問わず制限の対象です。
- ソフトウェアの全部または一部を配布する場合は、ライセンス本文またはそのURLと、`Required Notice:` の権利表示を引き継ぐ必要があります。

競合する提供を制限するソース公開ライセンスであり、OSIの定義によるオープンソースライセンスではありません。
