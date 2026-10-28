---
description: Mururu Blink & LipSyncのインストール手順。Modular Avatarの導入、VCCまたはunitypackageからの追加、ツールのウィンドウからムルルにまばたきとリップシンクを追加するまで。
---

<script setup>
import InstallationMethods from './.vitepress/theme/InstallationMethods.vue'
</script>

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

## 2. ツールをプロジェクトへ追加する

使用する配布形式のタブを選び、どちらか一方の方法で追加してください。

<InstallationMethods>
<template #vcc>

1. 下の「VCCに追加」を押し、VCCの確認画面でリポジトリを登録します。

   <a class="vcc-add-button" href="vcc://vpm/addRepo?url=https%3A%2F%2Fdocs.unlibra.com%2Fvpm%2Findex.json">VCCに追加</a>

2. VCCで使用するプロジェクトの **Manage Packages** を開き、**Mururu Blink & LipSync** を追加します。
3. Unityでプロジェクトを開きます。

リポジトリの登録だけでは、プロジェクトへツールは追加されません。

ボタンが動かない場合は、VCCの **Settings > Packages > Add Repository** から、次のURLを手動で登録してください。

`https://docs.unlibra.com/vpm/index.json`

既にunitypackage版を入れている場合は、先に[配布形式の切り替え](#配布形式の切り替え)を確認してください。

</template>
<template #unitypackage>

[BOOTH](https://unlibra.booth.pm/items/9056145)または[GitHub Releases](https://github.com/unlibra/mururu-blink-lipsync/releases)からダウンロードしたunitypackageをUnityのProjectウィンドウへドラッグ＆ドロップし、すべて選択したままImportします。

![Import Unity Package の画面](/images/1-2.webp)

</template>
</InstallationMethods>

追加できたら、以下の共通手順へ進みます。

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
インストールすると、`Assets/ZZZ_MururuBlinkLipSyncAssets/` にアバターが使用するメッシュやアニメーション等が自動で作られます。保存先の選択は不要です。このフォルダはビルド時だけのキャッシュではありません。導入したアバターがある間は削除しないでください。
:::

## 配布形式の切り替え

VCC版とunitypackage版のコードを同時に置くと、重複してコンパイルできなくなります。切り替える前にUnityプロジェクトをバックアップしてください。

unitypackage版からVCC版へ切り替える場合は、Sceneを保存してUnityを閉じ、旧版に同梱されたEditor/Runtimeのコードを.metaと一緒にAssets外へ退避してから、VCCのManage Packagesで本ツールを追加します。旧版のGeneratedや本ツールの保存先、Scene、Prefabは残してください。旧コードを退避した状態でSceneを開いて保存しないでください。

新しいコードで開いた後、導入済みのアバターをツールの更新操作で更新します。旧Generatedのファイルは自動削除しません。ほかのSceneやPrefabが使っている可能性があるためです。製品コードの退避時は、利用者が追加した別のコード・ファイルまで削除しないでください。

VCC版からunitypackage版へ切り替える場合も、先にVCCで本ツールのパッケージを削除し、その後unitypackageをインポートします。本ツールのパッケージをVCCから削除する操作と、アバターから機能を削除する操作は別です。アバターから機能を外す場合は、コードを削除する前にツールの削除操作を行ってください。
