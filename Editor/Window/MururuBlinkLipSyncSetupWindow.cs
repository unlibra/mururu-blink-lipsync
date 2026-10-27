// Mururu Blink & LipSync
// v1.1.1 / 2026-10-26
//
// Requires:
// - VRChat SDK
// - Modular Avatar
// - Mururu Body with the expected mouth / blink BlendShapes
//
// Install:
// 1. Backs up the avatar's original Body/Descriptor settings into MururuBlinkLipSyncInstallRecord.
// 2. Temporarily enables FBX Read/Write if necessary.
// 3. Reconstructs the separate LipSync_Mouth mesh from the installed Mururu Body.
// 4. Keeps the avatar Body Mesh untouched and uses the existing mouth_off BlendShape at runtime.
// 5. Configures Descriptor Viseme + Blink.
// 6. Creates a fresh addon-owned FX controller and merges it with Modular Avatar.
// 7. Uses built-in Voice + Gesture parameters. No Expression Menu or custom synced parameter is added.
//
// Remove:
// - Does not roll the avatar back to its install-time state.
// - Deletes only MururuBlinkLipSync-owned hierarchy/assets.
// - Restores Lip Sync/Viseme only when it still points at the LipSync_Mouth being removed.
// - Restores the pre-install Blink/Eye Look fields only while they are still tool-owned.
// - Leaves post-install user edits untouched.
// - Includes one-time cleanup for legacy RC4 generated Body meshes.
//
// This tool never copies or edits the avatar's existing FX/Menu/Parameters assets.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using nadena.dev.modular_avatar.core;

namespace MururuBlinkLipSync.Editor
{
// This file holds the window state and IMGUI. The rest of the class is split by
// responsibility into MururuBlinkLipSyncSetupWindow.*.cs in this folder:
// - Target:             finding the avatar, Body and InstallRecord
// - ExpressionSettings: Blink / LipSync settings and Facial EX parameter lookup
// - Operations:         Install / Apply / Rebuild / Remove flows and validation
// - AvatarSetup:        Descriptor, Eye Look, LipSync_Mouth renderer, MA components
// - Animator:           toggle clips and the generated FX controller
// - MouthMesh:          reconstructing the LipSync mouth mesh from Body
// - GeneratedAssets:    package paths and ownership / deletion of generated assets
public partial class MururuBlinkLipSyncSetupWindow : EditorWindow
{
    private const string ToolVersion = "1.1.1";

    private const string DocumentationUrl = "https://docs.unlibra.com/mururu-blink-lipsync/";

    internal const int InstallSchemaVersion = 4;

    // Button labels, also quoted by messages that tell the user what to press.
    private const string InstallButtonLabel = "インストール";

    private const string ApplyButtonLabel = "設定の変更を適用";

    private const string RebuildButtonLabel = "修復";

    private const string ResetButtonLabel = "デフォルト設定に戻す";

    private const string UpgradeButtonLabel = "新しい方式に更新";

    private const string RemoveButtonLabel = "アンインストール";

    private const string PlayModeMessage =
        "Playモード中は操作できません。Playモードを終了してから操作してください。";

    private const string PrefabAssetTargetMessage =
        "Prefabアセットは対象にできません。\nシーンに置いたアバターを対象アバターに指定してください。";

    // The target and the settings below are serialized so a script recompile
    // keeps the chosen target and any edits that are not applied yet.
    [SerializeField]
    internal VRCAvatarDescriptor avatarDescriptor;

    [SerializeField]
    internal SkinnedMeshRenderer bodyRenderer;

    internal const string RootObjectName = "MururuBlinkLipSync";

    internal const string SeObjectName = "LipSync_Mouth";

    private const string LegacyEnhancedLipSyncParameterName = "MururuBlinkLipSync_EnhancedLipSync";

    private const string LegacyMenuAssetName = "MururuBlinkLipSync_Menu";

    private const string LayerName = "MururuBlinkLipSync LipSync";

    internal const string BlinkLayerName = "MururuBlinkLipSync Blink";

    private const string GestureLeftParameterName = "GestureLeft";

    private const string GestureRightParameterName = "GestureRight";

    private const string VoiceParameterName = "Voice";

    // VRChat applies the user's microphone activation/sensitivity upstream of the
    // built-in Voice parameter. Treat any positive Voice value as speaking; the
    // tiny epsilon below represents exact zero in Animator float conditions, not
    // an independent microphone-volume threshold.
    private const float VoiceZeroEpsilon = 0.000001f;

    private const int GestureCount = 7;

    private const int FacialExCount = 8;

    private static readonly string[] GestureLabels =
    {
        "Fist",
        "Open",
        "Point",
        "Victory",
        "Rock n Roll",
        "Hand Gun",
        "Thumbs Up"
    };

    // Defaults for standard Mururu:
    // - Left gestures: mouth expressions -> Blink stays enabled. LipSync is allowed
    //   during speech, while the original gesture mouth remains visible when silent.
    // - Right gestures: eye expressions -> Blink is disabled for every gesture.
    //   LipSync remains enabled because the mouth is still driven independently.
    // - Facial EX 1-8: Blink stays disabled to preserve explicit eye expressions.
    //   LipSync is enabled: it only appears while speaking, so it does not
    //   compete with the expression's own mouth when silent.
    // Always Clone() these; the arrays are shared.
    internal static readonly bool[] DefaultLeftBlinkEnabled =
        Enumerable.Repeat(true, GestureCount).ToArray();

    internal static readonly bool[] DefaultLeftLipSyncEnabled =
        Enumerable.Repeat(true, GestureCount).ToArray();

    internal static readonly bool[] DefaultRightBlinkEnabled =
        Enumerable.Repeat(false, GestureCount).ToArray();

    internal static readonly bool[] DefaultRightLipSyncEnabled =
        Enumerable.Repeat(true, GestureCount).ToArray();

    internal static readonly bool[] DefaultFacialBlinkEnabled =
        Enumerable.Repeat(false, FacialExCount).ToArray();

    internal static readonly bool[] DefaultFacialLipSyncEnabled =
        Enumerable.Repeat(true, FacialExCount).ToArray();

    // UI state.
    [SerializeField]
    internal bool[] leftBlinkEnabled =
        (bool[])DefaultLeftBlinkEnabled.Clone();

    [SerializeField]
    internal bool[] leftLipSyncEnabled =
        (bool[])DefaultLeftLipSyncEnabled.Clone();

    [SerializeField]
    internal bool[] rightBlinkEnabled =
        (bool[])DefaultRightBlinkEnabled.Clone();

    [SerializeField]
    internal bool[] rightLipSyncEnabled =
        (bool[])DefaultRightLipSyncEnabled.Clone();

    [SerializeField]
    internal bool[] facialBlinkEnabled =
        (bool[])DefaultFacialBlinkEnabled.Clone();

    [SerializeField]
    internal bool[] facialLipSyncEnabled =
        (bool[])DefaultFacialLipSyncEnabled.Clone();

    [SerializeField]
    internal string facialExParameterName = "";

    [SerializeField]
    internal bool hideSpeechNotes;

    // Instance ID of the avatar the settings above belong to.
    [SerializeField]
    private int settingsTargetAvatarId = int.MinValue;

    // Setup window UI state. Keep the entire window scrollable. The per-gesture
    // rows are collapsed by default: turning a setting off for only some
    // gestures is uncommon, so the per-group toggles cover the usual case.
    private Vector2 scrollPosition;

    private bool individualSettingsFoldout = false;

    private bool advancedSettingsFoldout = false;

    // See OnGUI. Not serialized: it is re-sampled on every Layout event.
    private bool playModeThisFrame;

    private const string HideSelfShapeName = "SE_hide_self";

    private const string NoneShapeName = "SE_mouth_none";

    private const float MouthDeltaThreshold = 0.005f;

    // Generated assets live beside the installed package. The package root is
    // resolved from the Editor asmdef at operation time so moving/renaming the
    // whole MururuBlinkLipSync folder under Assets does not split generated files.
    internal static string OutputFolder =>
        (operationPackageRoot ?? GetPackageRoot()) + "/Generated";

    // Per-operation caches. Every operation entry point must go through
    // RunSafely, which fills operationPackageRoot before any scene or asset is
    // modified and clears both afterwards so the next operation starts fresh.
    private static string operationPackageRoot;

    // Generated paths referenced from scene files that are not loaded, or from
    // Prefab assets. Duplicated avatars in backup scenes / prefabs still use
    // these assets even though no loaded InstallRecord points at them.
    // Built lazily once per operation.
    private static HashSet<string> externallyReferencedGeneratedPaths;

    private static readonly string[] SourceNames =
    {
        "mouth_A", "mouth_I", "mouth_U", "mouth_E", "mouth_O"
    };

    private static readonly string[] TargetNames =
    {
        "SE_mouth_A", "SE_mouth_I", "SE_mouth_U", "SE_mouth_E", "SE_mouth_O"
    };

    // BlendShapes a compatible Mururu Body must have.
    private static readonly string[] RequiredBodyShapeNames =
        SourceNames
            .Concat(new[] { "mouth_off", "eye_blink" })
            .ToArray();

    // VRChat order:
    // sil, pp, ff, th, dd, kk, ch, ss, nn, rr, aa, E, ih, oh, ou
    private static readonly string[] VisemeMapping =
    {
        NoneShapeName, NoneShapeName, NoneShapeName, NoneShapeName, NoneShapeName,
        NoneShapeName, NoneShapeName, NoneShapeName, NoneShapeName, NoneShapeName,
        "SE_mouth_A", "SE_mouth_E", "SE_mouth_I", "SE_mouth_O", "SE_mouth_U"
    };

    // A single & marks a menu access key and is not shown; && shows one &.
    [MenuItem("Tools/Mururu Blink && LipSync")]
    public static void OpenWindow()
    {
        MururuBlinkLipSyncSetupWindow window =
            GetWindow<MururuBlinkLipSyncSetupWindow>(
                "Mururu Blink & LipSync");

        window.AutoDetectTarget();
        window.Show();
        window.Repaint();
    }

    public static void OpenForRecord(
        MururuBlinkLipSyncInstallRecord record)
    {
        MururuBlinkLipSyncSetupWindow window =
            GetWindow<MururuBlinkLipSyncSetupWindow>(
                "Mururu Blink & LipSync");

        // The Inspector also shows this button for a record inside a Prefab
        // asset selected in the Project window.
        if (IsEditableSceneObject(record))
        {
            window.avatarDescriptor =
                record.avatarDescriptor != null
                    ? record.avatarDescriptor
                    : record.GetComponentInParent<VRCAvatarDescriptor>();

            window.RefreshTargetFields();
        }
        else
        {
            window.AutoDetectTarget();
        }

        window.Show();
        window.Repaint();
    }

    private void OnEnable()
    {
        saveChangesMessage = "未保存の設定変更があります。保存してから閉じますか？\n保存すると「設定の変更を適用」を実行します。";
        Undo.undoRedoPerformed += OnUndoRedo;

        // A target that survived the reload is kept, so the Hierarchy
        // selection cannot replace it and reload the record over unapplied
        // edits. Without a target the saved settings cannot be tied to an
        // avatar (after an editor restart, for example), so they are not kept.
        if (avatarDescriptor != null &&
            IsEditableSceneObject(avatarDescriptor))
        {
            RefreshTargetFields();
        }
        else
        {
            settingsTargetAvatarId = int.MinValue;
            AutoDetectTarget();
        }

        UpdateUnsavedChanges();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    private void OnUndoRedo()
    {
        UpdateUnsavedChanges();
        Repaint();
    }

    private void OnSelectionChange()
    {
        AutoDetectTarget();
        UpdateUnsavedChanges();
        Repaint();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("ヘルプ", EditorStyles.toolbarDropDown, GUILayout.Width(70)))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("ドキュメントを開く"), false,
                    () => Application.OpenURL(DocumentationUrl));
                menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent($"バージョン {ToolVersion}"));
                menu.DropDown(GUILayoutUtility.GetLastRect());
            }
        }

        scrollPosition =
            EditorGUILayout.BeginScrollView(
                scrollPosition);

        bool installed =
            IsCurrentTargetInstalled();

        DrawInstallStatus(installed);

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        VRCAvatarDescriptor previousTarget =
            avatarDescriptor;

        avatarDescriptor =
            (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                "対象アバター",
                avatarDescriptor,
                typeof(VRCAvatarDescriptor),
                true);

        if (EditorGUI.EndChangeCheck())
        {
            if (avatarDescriptor != null &&
                !IsEditableSceneObject(avatarDescriptor))
            {
                // Keep the previous target and its unapplied edits.
                avatarDescriptor = previousTarget;

                ShowNotification(
                    new GUIContent(PrefabAssetTargetMessage));
            }
            else
            {
                RefreshTargetFields();

                // A new target can switch between the installed and
                // not-installed layouts in the middle of a drag-and-drop or
                // object-picker event, after Layout already counted the
                // controls. End this event and draw the new layout from the
                // next Layout pass.
                Repaint();
                GUIUtility.ExitGUI();
            }
        }

        DrawAdvancedSettings();

        EditorGUILayout.Space();

        DrawExpressionBehaviorSettings();

        EditorGUILayout.Space();

        hideSpeechNotes = EditorGUILayout.ToggleLeft(
            "発話時の音符を非表示", hideSpeechNotes);
        if (hideSpeechNotes)
        {
            EditorGUILayout.HelpBox(
                "標準ムルルの音符が対象です。アップロード時に反映されます。\n" +
                "チェックを外すと、元のFXの設定を使います。",
                MessageType.Info);
        }

        EditorGUILayout.Space();

        // Sampled on the Layout event only, so the Layout and Repaint passes of
        // one frame draw the same controls even if Play mode starts between
        // them. The buttons stay drawn but disabled; a disabled button never
        // fires, so no confirmation dialog or settings reset runs in Play mode.
        if (Event.current.type == EventType.Layout)
            playModeThisFrame = EditorApplication.isPlayingOrWillChangePlaymode;

        bool playMode =
            playModeThisFrame;

        if (playMode)
        {
            EditorGUILayout.HelpBox(
                PlayModeMessage,
                MessageType.Info);
        }

        EditorGUI.BeginDisabledGroup(playMode);

        if (installed)
        {
            if (GUILayout.Button(ApplyButtonLabel))
            {
                RunSafely(
                    ApplyExpressionSettings,
                    "設定の変更を適用しました。");
            }

            if (GUILayout.Button(ResetButtonLabel) &&
                EditorUtility.DisplayDialog(
                    "Mururu Blink & LipSync - デフォルト設定に戻す",
                    "表情ごとの設定と音符の非表示設定をデフォルトに戻して、そのまま適用しますか？\n\n" +
                    "今の設定は上書きされます。Ctrl+Zで戻すと生成ファイルがMissingになるため、" +
                    "元の設定に戻したい場合はチェックを付け直して適用してください。",
                    "戻して適用",
                    "キャンセル"))
            {
                ResetExpressionSettingsToMururuDefaults();
                TryAutoDetectFacialExParameterName();

                RunSafely(
                    ApplyExpressionSettings,
                    "デフォルト設定に戻して適用しました。");

                // Show what the record actually holds: the defaults on success,
                // the previous settings if the Apply failed or was cancelled.
                LoadExpressionSettings(FindInstallRecord());
            }

            // Maintenance operations, kept apart from the settings buttons above.
            DrawDivider();

            MururuBlinkLipSyncInstallRecord repairRecord =
                FindInstallRecord();

            bool upgradeNeeded =
                repairRecord != null &&
                repairRecord.installSchemaVersion != InstallSchemaVersion;

            if (GUILayout.Button(
                    upgradeNeeded
                        ? UpgradeButtonLabel
                        : RebuildButtonLabel))
            {
                RunSafely(
                    RepairInstallationAuto,
                    upgradeNeeded
                        ? "新しい方式に更新しました。"
                        : "修復しました。");
            }

            if (GUILayout.Button(RemoveButtonLabel))
            {
                bool confirmed =
                    EditorUtility.DisplayDialog(
                        "Mururu Blink & LipSync - アンインストール",
                        "Mururu Blink & LipSyncをこのアバターからアンインストールしますか？\n\n" +
                        "生成ファイルも削除されます。Ctrl+Zで戻すと生成ファイルがMissingになるため、" +
                        "元に戻したい場合は再度インストールしてください。",
                        RemoveButtonLabel,
                        "キャンセル");

                if (confirmed)
                {
                    RunSafely(
                        UninstallAuto,
                        "アンインストールしました。");
                }
            }
        }
        else
        {
            List<string> missingRequirements =
                GetInstallMissingRequirements();

            if (missingRequirements.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "インストールできません。以下を確認してください:\n" +
                    "・" +
                    string.Join(
                        "\n・",
                        missingRequirements) +
                    "\n\n" +
                    "通常は対象ムルルをHierarchyで選択すると自動設定されます。",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(
                missingRequirements.Count > 0))
            {
                if (GUILayout.Button(InstallButtonLabel))
                {
                    RunSafely(
                        Install,
                        "インストールが完了しました。");
                }
            }
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
        UpdateUnsavedChanges();
    }

    // Show guidance only when the user needs to choose a target, update, or repair.
    private void DrawInstallStatus(
        bool installed)
    {
        if (avatarDescriptor == null)
        {
            EditorGUILayout.HelpBox(
                "Hierarchyで対象ムルルを選択するか、対象アバターに指定してください。",
                MessageType.Info);
        }
        else if (installed)
        {
            MururuBlinkLipSyncInstallRecord installedRecord = FindInstallRecord();
            string installedVersion =
                installedRecord != null && !string.IsNullOrEmpty(installedRecord.installedVersion)
                    ? installedRecord.installedVersion
                    : "旧形式（RC4以前）";

            bool rebuildRecommended =
                installedRecord == null ||
                installedRecord.installSchemaVersion != InstallSchemaVersion;

            bool toolVersionDiffers =
                !string.Equals(installedVersion, ToolVersion, StringComparison.Ordinal);

            if (rebuildRecommended)
            {
                EditorGUILayout.HelpBox(
                    $"「{UpgradeButtonLabel}」で現在の方式へ更新してください。\n" +
                    "既存のLipSync_Mouth参照は維持されます。",
                    MessageType.Warning);
            }
            else if (toolVersionDiffers)
            {
                EditorGUILayout.HelpBox(
                    $"導入時のバージョン: {installedVersion}\n" +
                    $"「{ApplyButtonLabel}」で現在の制御ロジックへ更新できます。",
                    MessageType.Info);
            }

            List<string> missingReferences =
                FindMissingGeneratedReferences(installedRecord);

            if (missingReferences.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "生成ファイルへの参照が失われています:\n" +
                    "・" +
                    string.Join(
                        "\n・",
                        missingReferences) +
                    "\n\n" +
                    "Ctrl+Zで削除済みファイルを参照する状態に戻った可能性があります。\n" +
                    $"「{RebuildButtonLabel}」を実行してください。",
                    MessageType.Error);
            }
        }
    }

    // Detects Missing generated references, typically left by undoing an
    // operation whose replaced assets were already deleted. Legacy schemas are
    // excluded because they are reported as needing an upgrade instead.
    internal static List<string> FindMissingGeneratedReferences(
        MururuBlinkLipSyncInstallRecord record)
    {
        List<string> missing =
            new List<string>();

        if (record == null ||
            record.installSchemaVersion != InstallSchemaVersion)
        {
            return missing;
        }

        ModularAvatarMergeAnimator merge =
            record.GetComponent<ModularAvatarMergeAnimator>();

        if (merge == null || merge.animator == null)
            missing.Add("FX Controller（Merge Animator）");

        SkinnedMeshRenderer seRenderer =
            record.installedLipSyncMouthRenderer;

        if (seRenderer == null)
            missing.Add(SeObjectName);
        else if (seRenderer.sharedMesh == null)
            missing.Add($"{SeObjectName}のMesh");

        if (record.installedOnClip == null)
            missing.Add("LipSync ONのClip");

        if (record.installedOffClip == null)
            missing.Add("LipSync OFFのClip");

        return missing;
    }

    private void DrawAdvancedSettings()
    {
        advancedSettingsFoldout =
            EditorGUILayout.Foldout(
                advancedSettingsFoldout,
                "詳細設定",
                true);

        if (!advancedSettingsFoldout)
            return;

        EditorGUI.indentLevel++;

        using (new EditorGUILayout.HorizontalScope())
        {
            facialExParameterName =
                EditorGUILayout.TextField(
                    "Facial EXパラメータ",
                    facialExParameterName);

            if (GUILayout.Button(
                    "自動検出",
                    GUILayout.Width(90f)))
            {
                if (!TryAutoDetectFacialExParameterName())
                {
                    EditorUtility.DisplayDialog(
                        "Mururu Blink & LipSync",
                        "Facial EX用のInt型パラメータを自動検出できませんでした。",
                        "OK");
                }
            }
        }

        EditorGUI.indentLevel--;
    }

    private static void DrawDivider()
    {
        EditorGUILayout.Space(10f);

        Rect rect =
            EditorGUILayout.GetControlRect(
                false,
                1f);

        EditorGUI.DrawRect(
            rect,
            EditorGUIUtility.isProSkin
                ? new Color(0.32f, 0.32f, 0.32f)
                : new Color(0.68f, 0.68f, 0.68f));

        EditorGUILayout.Space(10f);
    }

    private void DrawExpressionBehaviorSettings()
    {
        EnsureSettingArrays();

        EditorGUILayout.LabelField(
            "表情ごとの設定",
            EditorStyles.boldLabel);

        DrawSettingsHeader();

        DrawGroupSettingsRow(
            "Left Hand (GestureLeft)",
            leftBlinkEnabled,
            leftLipSyncEnabled);

        DrawGroupSettingsRow(
            "Right Hand (GestureRight)",
            rightBlinkEnabled,
            rightLipSyncEnabled);

        DrawGroupSettingsRow(
            "Facial EX",
            facialBlinkEnabled,
            facialLipSyncEnabled);

        individualSettingsFoldout =
            EditorGUILayout.Foldout(
                individualSettingsFoldout,
                "個別設定",
                true);

        if (!individualSettingsFoldout)
            return;

        EditorGUI.indentLevel++;

        EditorGUILayout.LabelField(
            "Left Hand (GestureLeft)",
            EditorStyles.miniBoldLabel);

        for (int i = 0; i < GestureCount; i++)
        {
            DrawSettingsRow(
                $"Left {GestureLabels[i]}",
                ref leftBlinkEnabled[i],
                ref leftLipSyncEnabled[i]);
        }

        EditorGUILayout.LabelField(
            "Right Hand (GestureRight)",
            EditorStyles.miniBoldLabel);

        for (int i = 0; i < GestureCount; i++)
        {
            DrawSettingsRow(
                $"Right {GestureLabels[i]}",
                ref rightBlinkEnabled[i],
                ref rightLipSyncEnabled[i]);
        }

        EditorGUILayout.LabelField(
            "Facial EX",
            EditorStyles.miniBoldLabel);

        for (int i = 0; i < FacialExCount; i++)
        {
            DrawSettingsRow(
                $"Facial EX {i + 1}",
                ref facialBlinkEnabled[i],
                ref facialLipSyncEnabled[i]);
        }

        EditorGUI.indentLevel--;
    }

    // One row that sets every entry of a group at once. A group whose entries
    // differ shows the mixed value; changing it sets the whole group.
    private static void DrawGroupSettingsRow(
        string label,
        bool[] blink,
        bool[] lipSync)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                label,
                GUILayout.MinWidth(180f));

            DrawGroupToggle(blink);
            DrawGroupToggle(lipSync);
        }
    }

    private static void DrawGroupToggle(
        bool[] values)
    {
        bool allEnabled =
            values.All(v => v);

        bool mixed =
            !allEnabled && values.Any(v => v);

        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();

        bool enabled =
            EditorGUILayout.Toggle(
                allEnabled,
                GUILayout.Width(70f));

        EditorGUI.showMixedValue = false;

        if (EditorGUI.EndChangeCheck())
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = enabled;
        }
    }

    private static void DrawSettingsHeader()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                "入力",
                EditorStyles.miniBoldLabel,
                GUILayout.MinWidth(180f));

            EditorGUILayout.LabelField(
                "Blink",
                EditorStyles.miniBoldLabel,
                GUILayout.Width(70f));

            EditorGUILayout.LabelField(
                "LipSync",
                EditorStyles.miniBoldLabel,
                GUILayout.Width(70f));
        }
    }

    private static void DrawSettingsRow(
        string label,
        ref bool blink,
        ref bool lipSync)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                label,
                GUILayout.MinWidth(180f));

            blink =
                EditorGUILayout.Toggle(
                    blink,
                    GUILayout.Width(70f));

            lipSync =
                EditorGUILayout.Toggle(
                    lipSync,
                    GUILayout.Width(70f));
        }
    }
}
}
