---
description: Mururu Blink & LipSyncのインストール手順。Modular Avatarの導入、unitypackageのインポート、ツールのウィンドウからムルルにまばたきとリップシンクを追加するまで。
---

# インストール

## 1. 前提ツールを導入する

VRChat Creator Companion（VCC）で作ったアバター用のプロジェクトには、VRChat SDK が最初から入っています。そのプロジェクトに Modular Avatar（MA）を追加してください。<br>
本ツールとは直接関係ないですが、ムルルの改変が初めてな方はlilToonも必要です。<br>
MAとlilToonは無料です。

MAの追加方法は [公式ドキュメントの「インストール」](https://modular-avatar.nadena.dev/ja/docs/intro) にあります。
lilToonは[こちら](https://lilxyzw.github.io/lilToon/ja_JP/)です。

対応するバージョンは[対応環境](./requirements)を参照してください。

::: tip
MAとlilToonは、VCCのManage Packagesから使用するプロジェクトに追加してください。
:::

## 2. unitypackage をインポートする

ダウンロードした unitypackage をUnityのProjectウィンドウへドラッグ＆ドロップし、すべて選択したままImportします。

![Import Unity Package の画面](/images/1-2.webp)

## 3. ツールのウィンドウを開く

メニューの **Tools > Mururu Blink & LipSync** を選びます。

ウィンドウ右上の **ヘルプ > ドキュメントを開く** から、このサイトを開けます。ヘルプにはツールのバージョンも表示されます。エラーへの対処はFAQを確認してください。

![Tools メニューの Mururu Blink & LipSync](/images/1-3.webp)

## 4. ムルルを選択してインストールする

Hierarchyでシーンに置いたムルルを選択すると、「対象アバター」に自動で入ります。対象が正しいことを確認して、**インストール** を押します。

![未導入の状態のMururu Blink & LipSyncウィンドウ](/images/1-4.webp)

「インストールが完了しました。」と表示されれば完了です。アバター直下に `MururuBlinkLipSync` が追加されます。そのままVRChatへアップロードできます。

::: tip
シーンに置いたムルルが1体だけなら、ウィンドウを開いた時点で自動で対象になります。Prefabアセットは対象にできません。
:::

::: warning
インストールすると、ツールのフォルダ内の `Generated` に生成ファイルが作られます。手動で削除したり、自作のAnimator Controllerなどから使ったりしないでください。修復やアンインストールのときに、置き換えや削除をすることがあります。
:::
