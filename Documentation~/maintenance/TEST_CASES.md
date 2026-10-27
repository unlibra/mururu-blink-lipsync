# テストケース

標準アセット・追加fixtureの要件と実行手順は [TEST_ENVIRONMENT.md](TEST_ENVIRONMENT.md) を参照してください。v1.1.1では289件のEditModeテストを検証しています。

Unity CLI（`unity test`）のEditModeテストで自動化できる項目の一覧。
「修正確認」はコードレビューで修正した箇所の確認項目。

GUI操作、ダイアログでの選択、Playモード、再コンパイル、VRChat上での動作を必要とする項目は含めない。

テストは `Tests/Editor` にある。標準Mururuのprefabがあるプロジェクトで次のように実行する（エディターでそのプロジェクトを開いていると実行できない）。

滑らかな口復帰の仕様・制約・検証結果は [MOUTH_RETURN.md](MOUTH_RETURN.md) を参照。
`MururuMouthReturnRevisedLookupTests`が実効Motion/Behaviour・レイヤー順・ウェイト・マスク・静的追加口の実際の復帰先と条件別除外、`MururuMouthReturnStandardVariantsTests`が同期構成の実NDMFビルドと入力不変を確認する。

```
unity test --mode EditMode --filter MururuBlinkLipSync <プロジェクトのパス>
```

## 未保存の設定と終了確認

未保存の変更は、保存済み設定との比較で判定する。`ApplySettingsTests`と`SpeechNotesTests`で、変更・元の値への復帰・適用成功/失敗・対象切替・適用のUndoに伴う判定を確認する。画面内の黄色い未保存案内は終了確認と重複するため削除した。

終了確認はUnity標準の`EditorWindow.hasUnsavedChanges`を使用し、保存・破棄・キャンセルを選べる。保存は既存の設定適用処理を実行し、失敗時は例外で終了を中止する。破棄は保存済み設定を読み直す。`ApplySettingsTests.E7/E8`で保存成功・失敗時の編集保持・破棄時の元設定とController不変を確認する。実際のダイアログ操作は手動確認対象。


## 音符の非表示設定

`SpeechNotesTests`で、設定の保存・対象切替・Scene再読込・修復・初期化・Undo、ビルド用Controllerだけの変更、チェックOFFでの既存ウェイト維持、対象レイヤーの欠損・重複・先頭レイヤー時の変更抑止を確認する。実NDMFビルドでもON/OFFと元FX不変を確認する。


## 前提

座標軸が異なる口の導入エラーは`InstallTests.C3/C4`で、回転した5方向の案内と、口の動きが欠損した場合との区別を確認する。Blenderによる補正手順の実データ検証は[MOUTH_RETURN.md](MOUTH_RETURN.md)を参照。


標準モデルの既知データ差は `MururuMouthReturnStandardVariantsTests` で確認する。既知2種類の口生成と実NDMFビルド、発音形状・骨情報の保持、口が使う骨の不整合の拒否と使わない骨の差の許容を含む。背景は [MOUTH_RETURN.md](MOUTH_RETURN.md) を参照。

未リリース版では、`MururuMouthReturnMeshTests`の口形状の欠損・複数フレーム・輪郭欠損、`TargetTests`の口以外の改変と未知シェイプの影響判定、`RevisedLookupTests`の表情ごとの除外、`RevisedTimingTests`の対応外だけ80msへ戻る実Animator評価を追加した。補正済み改変FBXの実NDMFビルドも隔離環境で確認する。

同日、製品200件＋隔離環境の実データ等6件の206/206成功（失敗・スキップ0、Unity CLI終了0）。補正済み改変FBXで35シェイプの実NDMFビルドと入力不変を確認した。今回の変更後のVRChat実機確認は未実施。

### 1.0.0以降のレビュー指摘の修正

`RevisedLookupTests`で、Facial EXのパラメータ名変更、割当不明時の左手側の除外、クリップ欠損時に該当表情だけ除外されることを確認する。右手の口指定は0/100・未知の口シェイプを含め該当ジェスチャーだけ除外する。`RevisedTimingTests`で、その右手が有効な間の80ms瞬間切替、ほかの右手での滑らか復帰、復帰途中の変更による同一評価での解除を確認する。`StandardVariantsTests.ReadWriteOff_BuildsSmoothMouthWithoutChangingImporterOrInputs`で、Read/Write無効のまま実NDMFビルドが35シェイプを生成し、元のBody・導入口・インポート設定が変わらないことを確認する。




- Unity 2022.3、VRChat SDK Avatars 3.10.x、Modular Avatar 1.18.x 以降
- 標準体型のMururuのprefabがあるテスト用プロジェクト。ない場合、アバターを使うテストはスキップされる
- 各テストは空のシーンから始め、作った生成ファイル・一時ファイルの削除と、BodyのFBXのRead/Write設定の復元をして終わる

## A. 対象Avatarの選択

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| A-1 | 基本 | 未導入のMururuが1体 | 何も選択せずにウィンドウを開く | 対象アバターに自動で入る | `TargetSelectionTests.A1_SingleMururu_IsDetectedWhenWindowOpens` |
| A-2 | 基本 | 未導入のMururuが2体 | 何も選択せずにウィンドウを開く | 対象アバターは空 | `TargetSelectionTests.A2_TwoMururu_LeaveTargetEmpty` |
| A-3 | 修正確認 | 未導入のMururuが2体。ウィンドウは開いたまま | Mururu A、次にMururu Bを選択する | 選ぶたびに対象アバターがA、Bと切り替わる | `TargetSelectionTests.A3_SelectingAnotherMururu_SwitchesTarget` |
| A-4 | 修正確認 | 導入済みA、未導入の複製B | ウィンドウを開いてからBを選択する | 対象アバターがBになり、未導入と判定される | `TargetSelectionTests.A4_SelectingUninstalledDuplicate_TargetsItAsNotInstalled` |
| A-5 | 基本 | 対象アバターが設定済み | Avatar以外のオブジェクト、たとえばDirectional Lightを選択する | 対象アバターは変わらない | `TargetSelectionTests.A5_SelectingNonAvatar_KeepsTarget` |
| A-6 | 修正確認 | 導入済みA、導入済みB | Aを選択したまま、対象アバターをBにして修復する | Bだけが修復される。AのDescriptorとEye Lookは変わらない | `TargetSelectionTests.A6_RepairUsesTargetField_NotSelection` |

## B. インストール

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| B-1 | 基本 | 標準Mururu | インストールする | Avatar直下にMururuBlinkLipSync、その下にLipSync_Mouthができる | `InstallTests.B1_Install_CreatesRootAndLipSyncMouth` |
| B-2 | 基本 | B-1の後 | パッケージ直下のGeneratedを見る | LipSync_Mouth_Generated.asset、ON/OFFのanim、FX.controllerがある | `InstallTests.B2_Install_CreatesGeneratedAssets` |
| B-3 | 基本 | B-1の後 | Avatar DescriptorのLipSyncとEye Lookを見る | VisemeがLipSync_Mouthを指す。Eyelidsは Blendshapes で Body の eye_blink | `InstallTests.B3_Install_ConfiguresVisemeAndEyelids` |
| B-4 | 基本 | B-1の後 | Body Meshを見る | Body Meshは差し替わっていない | `InstallTests.B4_Install_KeepsBodyMesh` |
| B-5 | 基本 | FBXのRead/WriteがOFF | インストールする | 成功し、終了後FBXのRead/WriteがOFFに戻っている | `InstallTests.B5_InstallWithReadWriteOff_RestoresReadWriteOff` |
| B-6 | 基本 | B-1の後 | Undoを1回実行する | インストールの変更が1回でまとめて戻る | `InstallTests.B6_UndoOnce_RevertsWholeInstall` |
| B-7 | 異常系 | Avatar直下に MururuBlinkLipSync という名前の空オブジェクトがある | インストールする | 重複導入を防ぐエラーになり、何も変更されない | `InstallTests.B7_ExistingRootObject_IsRejected` |
| B-8 | 異常系 | BodyをAvatar直下から別の階層に移す | インストールする | Bodyの親についてのエラーになり、何も変更されない | `InstallTests.B8_BodyNotUnderAvatarRoot_IsRejected` |

## C. 口形状の検証

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| C-1 | 基本 | 標準Mururu | 口形状を検証する | 確認が必要な差は見つからない | `InstallTests.C1_StandardMururu_HasNoMouthShapeWarnings` |
| C-2 | 異常系 | 口周辺の頂点を改変したBody | インストールする | 口頂点を特定できないエラーになり、何も変更されない | `InstallTests.C2_EditedMouthTopology_IsRejected` |

## D. Facial EXパラメータ

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| D-1 | 基本 | Expression ParametersにInt型の Facial EX がある | 自動検出を実行する | Facial EX が入る | `FacialExParameterTests.D1_IntExpressionParameter_IsAutoDetected` |
| D-2 | 基本 | Facial EXをMA Parametersで定義している | 自動検出を実行する | その名前が入る | `FacialExParameterTests.D2_MaParametersDefinition_IsAutoDetected` |
| D-3 | 異常系 | Float型のパラメータ名を入力 | インストールする | Intではないというエラーになる | `FacialExParameterTests.D3_FloatParameter_IsRejected` |
| D-4 | 修正確認 | 導入済み。Facial EXのチェックを外した行がある。Facial EXパラメータに Voice を入力 | 設定の変更を適用する | 組み込みパラメータのエラーになり、何も変更されない | `FacialExParameterTests.D4_BuiltInVoice_IsRejectedOnApply` |
| D-5 | 修正確認 | 祖先オブジェクトのMA Parametersに A→B と B→C の2つの設定がある | 子のMA ParametersでAを定義し、Facial EXパラメータに B を入力してインストールする | 見つからない警告の対象にならない | `FacialExParameterTests.D5_NameRemappedByNearestAncestor_IsFound` |
| D-6 | 修正確認 | Facial EX 3のBlinkを外し、Facial EXパラメータに GestureLeft を入力 | インストールする | 組み込みパラメータのエラーになり、何も変更されない | `FacialExParameterTests.D6_BuiltInGestureLeft_IsRejectedOnInstall` |

## E. 設定の変更を適用

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| E-1 | 基本 | 導入済み | チェックを変えて設定の変更を適用する | 新しいFX.controllerがMerge Animatorに入る。古いControllerは削除される | `ApplySettingsTests.E1_Apply_ReplacesControllerAndDeletesOldOne` |
| E-2 | 基本 | E-1の後 | 別のAvatarを対象にしてから戻す | 変更した設定がウィンドウの設定値に読み込まれる | `ApplySettingsTests.E2_AppliedSettings_AreLoadedWhenTargetReturns` |
| E-3 | 基本 | Blinkのチェックをすべて付けた状態 | 設定の変更を適用してControllerを見る | Blinkレイヤーがない | `ApplySettingsTests.E3_AllBlinkEnabled_HasNoBlinkLayer` |
| E-4 | 基本 | 導入済み。チェックを初期値から変えて適用済み | デフォルト設定に戻して適用する | チェックが初期値に戻り、そのまま適用される | `ApplySettingsTests.E4_ResetToDefaults_IsApplied` |
| E-5 | 修正確認 | 導入済みAvatarをPrefabとして保存し、シーンのインスタンスはUnpackしない | チェックを変えて設定の変更を適用し、シーンを保存して開き直す | 変えた設定がウィンドウの設定値に読み込まれ、Merge Animatorも新しいControllerのまま | `ApplySettingsTests.E5_ApplyOnPrefabInstance_SurvivesSceneReload` |

## F. 修復 / 新しい方式に更新

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| F-1 | 基本 | 導入済み | 修復する | 完了する。LipSync_MouthとClipは同じファイルのまま更新される | `RepairTests.F1_Repair_UpdatesSameFiles` |
| F-2 | 基本 | Generatedのファイルを1つ削除し、Missingの参照がある | 修復する | Missingの参照がなくなり、削除したファイルが作り直される | `RepairTests.F2_Repair_RecreatesDeletedGeneratedFile` |
| F-3 | 基本 | 旧バージョン（RC4以前など）で導入したシーン | 新しい方式に更新する | 現行方式に更新される。Body Meshは元に戻る | `RepairTests.F3_Upgrade_RestoresLegacyGeneratedBody` |
| F-4 | 修正確認 | 導入済み。Facial EXのチェックを外した行がある。Facial EXパラメータに Voice を入力 | 修復する | 組み込みパラメータのエラーになり、何も変更されない | `RepairTests.F4_BuiltInVoice_IsRejectedOnRepair` |
| F-5 | 修正確認 | 導入後にBodyのGameObjectを削除し、FBXから同じ位置に入れ直した | 修復する | 修復が完了し、LipSync_Mouthが新しいBodyに合わせて作り直される | `RepairTests.F5_Repair_FollowsReaddedBody` |

## G. 削除

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| G-1 | 基本 | 導入済み | 削除する | MururuBlinkLipSyncとGeneratedのファイルが消える。LipSyncとEye Lookが導入前に戻る | `RemoveTests.G1_Remove_DeletesFilesAndRestoresDescriptor` |
| G-2 | 基本 | 導入後、DescriptorのVisemeを別のMeshに変えた | 削除する | 変えたVisemeはそのまま残る | `RemoveTests.G2_Remove_KeepsVisemeChangedAfterInstall` |
| G-3 | 基本 | 導入後、Eye Lookを変えた | 削除する | 変えたEye Lookはそのまま残る | `RemoveTests.G3_Remove_KeepsEyeLookChangedAfterInstall` |
| G-4 | 基本 | 導入済みAvatarを複製 | 片方で削除する | もう片方が使うファイルは残る | `RemoveTests.G4_Remove_KeepsFilesUsedByDuplicate` |
| G-5 | 基本 | 導入済みAvatarを別シーンにコピーして保存し、そのシーンを閉じる | 元のシーンで削除する | 閉じたシーンが使うファイルは残る | `RemoveTests.G5_Remove_KeepsFilesUsedByClosedScene` |
| G-6 | 基本 | 導入済みAvatarをPrefabとして保存 | 元のシーンで削除する | Prefabが使うファイルは残る | `RemoveTests.G6_Remove_KeepsFilesUsedByPrefab` |
| G-7 | 修正確認 | 導入後にLipSync_Mouthを削除した | 削除する | Visemeが導入前に戻り、Missingが残らない | `RemoveTests.G7_RemoveAfterLipSyncMouthDeleted_RestoresViseme` |
| G-8 | 要確認 | 導入済みAvatarをPrefabとして保存し、シーンのインスタンスはUnpackしない | 削除する | 結果を記録する。エラーになった場合は何も変更されていないことを確認する | `RemoveTests.G8_RemoveOnPrefabInstance_SucceedsOrChangesNothing` |

## H. ビルド

| ID | 区分 | 前提 | 手順 | 期待結果 | テスト |
|---|---|---|---|---|---|
| H-1 | 基本 | 導入後にBodyのマテリアルを差し替えた | NDMFでビルドしてLipSync_Mouthを見る | Bodyと同じマテリアルになっている | `BuildTests.H1_Build_CopiesBodyMaterialToLipSyncMouth` |
| H-2 | 基本 | MA Material Setterで初期マテリアルを変えている | NDMFでビルドする | 変更後のマテリアルがLipSync_Mouthにも反映される | `BuildTests.H2_Build_FollowsMaterialSetterInitialState` |
| H-3 | 修正確認 | 口を動かす独自シェイプの値が0または100 | ビルド前処理を実行する | 100のときだけシェイプ名を含むNDMF警告を1件表示し、処理は完了。Bodyのメッシュと値は維持 | `BuildTests.H3_MouthReturn_ReportsActiveCustomShapeAndKeepsInputs` |

滑らか復帰の警告の回帰は `MururuMouthReturnRevisedLookupTests` で確認する。標準構成で警告なし、メッシュ生成失敗の理由とNonFatal通知、一部の組み合わせだけが対応外の場合の通知、復帰先形状の不足、設定でリップシンクを止めた表情の通知除外を含む。警告追加前の対応判定・タイミング・入力不変のテストも維持する。


FaceEmo追加FXの回帰は `MururuMouthReturnAdditionalFxTests` で確認する。要求値と実状態の区別、未初期化の安全切替と警告除外、部分書込、同口BlendTree、可変口のみのフォールバック、後段完全上書きでの回復、目のみ・無効レイヤーの無関係性を含む。実MA/NDMFビルドを2回行い、最終復帰Clip、FX内IntとDriver（`localOnly=false`）、固定80+80ms、入力Controller/状態/メッシュ/Expression Parameters不変と非累積を確認する。MAで名称変更した停止条件と、後段AnimatorServicesContext再活性化→commit後のdriver・最終口曲線保持も確認する。

Direct BlendTreeは正規化OFF/ONの両方で、同じ口の子だけでも確定口として扱わない。その実状態だけが80ms切替＋警告となること、別の定数口状態は滑らか復帰を維持すること、後段の完全書込で回復できることを2ケースで確認する。




目のみ表情の追試: `MururuMouthReturnAdditionalFxTests` に、口を書く同一レイヤー内の目のみ状態が下位の全17復帰先を使う回帰、部分書込みの組合せ判定、native Animatorの下位default/triangle/書込みなし×目のみ/poutのみ6件を追加。下位が書く場合の80ms時点の補間と完了後の口、書かない場合の直前値の保持を区別する。`MururuMouthReturnRevisedLookupTests.WdOffOmission_RequiresAnimatedCoverageRatherThanRendererDefault` はRenderer初期値だけで口を確定しないことを確認する。

FaceEmo警告は可変口1原因が左手・Facial EXへ反復されないこと、利用者の表情名を保持すること、通常の表情と既知の内部制御状態を区別すること、日本語で理由を示し内部識別子を出さないことを回帰する。既存の未対応口・欠損形状・非FaceEmo警告と未初期化除外も維持する。実入力は隔離プロジェクトで両目のみ表情の最終17経路と下位のdefault/mouth_A、警告除外、2回ビルドの入力不変を確認する。
