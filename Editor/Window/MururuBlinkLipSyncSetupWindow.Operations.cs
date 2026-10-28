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
public partial class MururuBlinkLipSyncSetupWindow
{
    internal void ApplyExpressionSettings()
    {
        ValidateUninstallInputs();
        ValidateExpressionBehaviorSettings();

        MururuBlinkLipSyncInstallRecord record = FindInstallRecord();

        if (record == null)
            throw new Exception("MururuBlinkLipSyncInstallRecordが見つかりません。");

        // Snapshot the InstallRecord together with the Merge Animator so Undo
        // reverts both references consistently. A complete snapshot does not
        // depend on when pending RecordObject diffs are flushed.
        Undo.RegisterCompleteObjectUndo(
            record,
            "Mururu Blink & LipSync - Apply Expression Settings");

        RefreshTrackedAssetPaths(record);

        if (record.installSchemaVersion != InstallSchemaVersion)
        {
            throw new Exception(
                $"導入形式が古いため、「{ApplyButtonLabel}」では更新できません。\n" +
                $"「{UpgradeButtonLabel}」を実行してください。");
        }

        AnimationClip onClip = record.installedOnClip;
        AnimationClip offClip = record.installedOffClip;

        if (onClip == null || offClip == null)
        {
            throw new Exception(
                "現在の導入形式で使うAnimation Clipの参照がありません。\n" +
                $"「{RebuildButtonLabel}」を実行してください。");
        }

        ModularAvatarMergeAnimator merge =
            record.GetComponent<ModularAvatarMergeAnimator>();

        if (merge == null)
            throw new Exception("Modular Avatar Merge Animatorが見つかりません。");

        AnimatorController oldController =
            record.installedController as AnimatorController;

        if (oldController == null)
            oldController = merge.animator as AnimatorController;

        string oldPath =
            oldController != null
                ? AssetDatabase.GetAssetPath(oldController)
                : null;

        // Created only after every check above, so a refused Apply leaves no
        // empty Generated folder behind.
        PrepareStorageForWrite(record);

        string controllerPath =
            AssetDatabase.GenerateUniqueAssetPath(
                $"{OutputFolder}/MururuBlinkLipSync_FX.controller");

        List<FeatureTrigger> blinkDisabledTriggers =
            BuildDisabledFeatureTriggers(forBlink: true);

        List<FeatureTrigger> lipSyncDisabledTriggers =
            BuildDisabledFeatureTriggers(forBlink: false);

        AnimatorController newController =
            CreateFreshController(
                controllerPath,
                onClip,
                offClip,
                blinkDisabledTriggers,
                lipSyncDisabledTriggers);

        TrackAsset(record, controllerPath);

        Undo.RecordObject(
            merge,
            "Mururu Blink & LipSync - Apply Expression Settings");

        merge.animator = newController;
        record.installedController = newController;
        record.installedVersion = ToolVersion;
        record.installSchemaVersion = InstallSchemaVersion;

        SaveExpressionSettings(record);

        EditorUtility.SetDirty(merge);
        EditorUtility.SetDirty(record);

        if (!string.IsNullOrEmpty(oldPath) &&
            oldPath != controllerPath)
        {
            DetachTrackedAssetAndDeleteIfUnshared(
                record,
                oldPath);
        }

        // The record is edited directly after a complete Undo snapshot (its
        // tracked paths change in the call above as well); on a Prefab
        // instance its changes must be kept as overrides explicitly.
        PrefabUtility.RecordPrefabInstancePropertyModifications(record);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    internal void RepairInstallationAuto()
    {
        EnsureUninstallTargetSelected();
        RepairInstallation();
    }

    internal void UninstallAuto()
    {
        EnsureUninstallTargetSelected();
        Uninstall();
    }

    // Each operation becomes a single Undo step. Undo history outside the tool
    // is never cleared. Undoing an operation that deleted generated assets can
    // leave Missing references; the window detects that state and asks for a
    // Rebuild (see FindMissingGeneratedReferences).
    private void RunSafely(Action action, string successMessage)
    {
        // Scene edits made in Play mode are discarded when it ends, but the
        // generated files created or deleted on disk are not, which would
        // leave the scene pointing at deleted files.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog(
                "Mururu Blink & LipSync",
                PlayModeMessage,
                "OK");
            return;
        }

        Exception error =
            RunOperation(action);

        if (error == null)
        {
            EditorUtility.DisplayDialog(
                "Mururu Blink & LipSync",
                successMessage,
                "OK");
        }
        else if (!(error is OperationCanceledException))
        {
            Debug.LogException(error);

            EditorUtility.DisplayDialog(
                "Mururu Blink & LipSync - エラー",
                error.Message,
                "OK");
        }
    }

    // Runs one operation as a single Undo group and reverts it on failure.
    // Returns null on success, or the exception that stopped it: an
    // OperationCanceledException when the user declined a confirmation.
    // Shows no result dialog, so the EditMode tests drive the same path as
    // the buttons.
    internal Exception RunOperation(Action action)
    {
        // Seal the user's own pending edits first so they stay a separate step.
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Mururu Blink & LipSync");

        try
        {
            // Resolve the package layout up front so a duplicated or misplaced
            // package aborts before anything is modified.
            operationCreatedAssetGuids.Clear();
            operationOutputFolder = null;
            operationAssetSet = null;
            operationPackageRoot = GetPackageRoot();

            action();

            return null;
        }
        catch (OperationCanceledException ex)
        {
            // The user declined a confirmation dialog. Revert whatever this
            // operation already changed. Only groups from undoGroup onward are
            // reverted, and the user's own edits were flushed before it.
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(undoGroup);
            RollbackCreatedAssets();

            return ex;
        }
        catch (ExitGUIException)
        {
            // Unity uses ExitGUIException internally to abort the current IMGUI
            // event safely (for example after dialogs / asset refreshes).
            // Swallowing it corrupts the GUILayout stack and causes errors such as
            // "EndLayoutGroup: BeginLayoutGroup must be called first."
            throw;
        }
        catch (Exception ex)
        {
            // Revert the scene and undo-tracked asset changes so a failure part
            // way through an operation does not leave a half-applied install.
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(undoGroup);
            RollbackCreatedAssets();

            return ex;
        }
        finally
        {
            operationCreatedAssetGuids.Clear();
            operationOutputFolder = null;
            operationAssetSet = null;
            operationRecord = null;
            operationPackageRoot = null;
            externallyReferencedGeneratedPaths = null;

            // RecordObject diffs are otherwise registered at the end of the
            // frame, after the group has been collapsed.
            Undo.FlushUndoRecordObjects();
            Undo.SetCurrentGroupName("Mururu Blink & LipSync");
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    private static void RollbackCreatedAssets()
    {
        foreach (string guid in operationCreatedAssetGuids.AsEnumerable().Reverse())
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);
        }
    }

    private void ValidateInstallInputs()
    {
        ValidateInstallRecordScriptPlacement();
        ValidateTargetBody("インストール対象のアバターを特定できません。");

        if (avatarDescriptor.transform.Find(RootObjectName) != null)
        {
            throw new Exception(
                $"アバター直下に '{RootObjectName}' が既にあります。\n" +
                "重複導入を防ぐため中止します。");
        }

        ValidateExpressionBehaviorSettings();
    }

    private void ValidateRepairInputs()
    {
        ValidateTargetBody("更新対象のアバターを特定できません。");
        ValidateExpressionBehaviorSettings();
    }

    // Validates the target the operation was started with. The Hierarchy
    // selection is not consulted here: re-detecting the target part way
    // through an operation could swap in another avatar's descriptor or Body.
    private void ValidateTargetBody(
        string missingAvatarMessage)
    {
        if (avatarDescriptor == null)
            throw new Exception(missingAvatarMessage);

        if (!IsEditableSceneObject(avatarDescriptor))
            throw new Exception(PrefabAssetTargetMessage);

        if (bodyRenderer == null || bodyRenderer.sharedMesh == null)
            throw new Exception("対応Body Rendererを特定できません。");
        if (bodyRenderer.transform.parent != avatarDescriptor.transform)
        {
            throw new Exception(
                "このバージョンは、ムルルのBodyがアバター直下にある構成を対象にしています。\n" +
                "Bodyの親がAvatar Rootか確認してください。");
        }

        foreach (string name in RequiredBodyShapeNames)
            RequireBlendShape(bodyRenderer.sharedMesh, name, "Body");
    }

    private void ValidateUninstallInputs()
    {
        if (avatarDescriptor == null)
            throw new Exception("対象アバターを指定してください。");

        if (!IsEditableSceneObject(avatarDescriptor))
            throw new Exception(PrefabAssetTargetMessage);

        MururuBlinkLipSyncInstallRecord record =
            FindInstallRecord();

        if (record == null)
        {
            throw new Exception(
                "MururuBlinkLipSyncInstallRecordが見つかりません。\n" +
                "このアバターにはMururu Blink & LipSyncが導入されていません。");
        }
    }

    internal void Install()
    {
        ValidateInstallInputs();

        ModelImporter sourceImporter = null;
        bool restoreImporterReadable = false;
        bool originalImporterReadable = false;
        string sourceMeshPath = AssetDatabase.GetAssetPath(bodyRenderer.sharedMesh);
        string sourceMeshName = bodyRenderer.sharedMesh.name;

        GameObject root = null;
        MururuBlinkLipSyncInstallRecord record = null;

        try
        {
            Mesh sourceBody = EnsureBodyReadable(
                ref sourceImporter,
                ref restoreImporterReadable,
                ref originalImporterReadable,
                sourceMeshPath,
                sourceMeshName);

            int[] mapping =
                ValidateNeutralMouthSource(
                    sourceBody,
                    out int[] neutralTriangles);

            // Created only after every confirmation dialog, so cancelling
            // leaves no empty Generated folder behind.
            PrepareStorageForWrite(null);

            root = CreateRoot();

            record = Undo.AddComponent<MururuBlinkLipSyncInstallRecord>(root);

            if (record == null)
            {
                throw new Exception(
                    "MururuBlinkLipSyncInstallRecord Componentを追加できませんでした。\n" +
                    "MururuBlinkLipSyncInstallRecord.cs がRuntimeフォルダにあり、MururuBlinkLipSync.Runtime Assemblyに含まれているか確認してください。\n" +
                    $"推奨配置: {GetPackageRoot()}/Runtime/MururuBlinkLipSyncInstallRecord.cs");
            }

            record.generatedAssetSet = operationAssetSet;
            CaptureOriginalState(record, sourceBody);
            record.installedVersion = ToolVersion;
            record.installSchemaVersion = InstallSchemaVersion;
            SaveExpressionSettings(record);

            Mesh generatedSe =
                GenerateLipSyncMouth(
                    sourceBody,
                    mapping,
                    neutralTriangles);

            string seMeshPath =
                CreateTrackedMeshAsset(
                    generatedSe,
                    $"{OutputFolder}/LipSync_Mouth_Generated.asset",
                    record);

            record.mouthMaterialIndex =
                FindMouthSubMeshIndex(
                    sourceBody,
                    mapping);

            SkinnedMeshRenderer seRenderer =
                CreateLipSyncMouthRenderer(
                    root,
                    generatedSe,
                    record.mouthMaterialIndex);

            record.installedLipSyncMouthRenderer = seRenderer;

            ConfigureInitialWeights(seRenderer);
            ConfigureDescriptor(seRenderer);
            ConfigureBlink();

            string onClipPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{OutputFolder}/MururuBlinkLipSync_LipSync_ON.anim");

            string offClipPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{OutputFolder}/MururuBlinkLipSync_LipSync_OFF.anim");

            string controllerPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{OutputFolder}/MururuBlinkLipSync_FX.controller");

            // ON owns mouth_off and forces the complete original mouth set hidden.
            AnimationClip onClip =
                CreateToggleClip(
                    onClipPath,
                    "MururuBlinkLipSync_LipSync_ON",
                    seRenderer,
                    bodyMouthOffValue: 100f,
                    seHideValue: 0f);

            TrackAsset(record, onClipPath);

            // OFF / suppressed expression explicitly restores Body.mouth_off = 0
            // and hides the generated LipSync mouth so the original Mururu
            // expression mouth is visible.
            AnimationClip offClip =
                CreateToggleClip(
                    offClipPath,
                    "MururuBlinkLipSync_LipSync_OFF",
                    seRenderer,
                    bodyMouthOffValue: 0f,
                    seHideValue: 100f);

            TrackAsset(record, offClipPath);

            List<FeatureTrigger> blinkDisabledTriggers =
                BuildDisabledFeatureTriggers(forBlink: true);

            List<FeatureTrigger> lipSyncDisabledTriggers =
                BuildDisabledFeatureTriggers(forBlink: false);

            AnimatorController controller =
                CreateFreshController(
                    controllerPath,
                    onClip,
                    offClip,
                    blinkDisabledTriggers,
                    lipSyncDisabledTriggers);

            TrackAsset(record, controllerPath);
            record.installedController = controller;
            record.installedOnClip = onClip;
            record.installedOffClip = offClip;

            ConfigureModularAvatarComponents(
                root,
                controller);

            EditorUtility.SetDirty(record);
            EditorUtility.SetDirty(seRenderer);
            EditorUtility.SetDirty(avatarDescriptor);

            PrefabUtility.RecordPrefabInstancePropertyModifications(avatarDescriptor);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log(
                "[MururuBlinkLipSync] " + ToolVersion + " インストールが完了しました。",
                avatarDescriptor);
        }
        catch
        {
            if (record != null)
            {
                try
                {
                    // Immediate install failure: no user edits can exist between
                    // capture and rollback, so a full descriptor rollback is safe.
                    RestoreOriginalState(record, deleteGeneratedAssets: true);
                }
                catch (Exception rollbackEx)
                {
                    Debug.LogError(
                        "[MururuBlinkLipSync] インストール失敗後の自動復元に失敗しました。Sceneと生成ファイルの状態を確認してください:\n" +
                        rollbackEx);
                }
            }
            else if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
            }

            throw;
        }
        finally
        {
            RestoreImporterReadableIfNeeded(
                sourceImporter,
                restoreImporterReadable,
                originalImporterReadable);
        }
    }

    private void RepairInstallation()
    {
        ValidateUninstallInputs();
        ValidateInstallRecordScriptPlacement();

        MururuBlinkLipSyncInstallRecord record = FindInstallRecord();

        if (record == null)
            throw new Exception("MururuBlinkLipSyncInstallRecordが見つかりません。");

        Undo.RegisterCompleteObjectUndo(
            record,
            "Mururu Blink & LipSync - Rebuild Installation");

        RefreshTrackedAssetPaths(record);

        // RC4 compatibility: restore the original Body in place before rebuilding
        // the current separate LipSync_Mouth representation.
        RestoreLegacyGeneratedBodyIfOwned(record);

        // A Body re-added from the FBX loses the record's reference; the
        // compatible Body under the same avatar takes its place.
        bodyRenderer =
            record.bodyRenderer != null
                ? record.bodyRenderer
                : FindCompatibleBodyRenderer(avatarDescriptor);

        ValidateRepairInputs();

        record.bodyRenderer = bodyRenderer;

        GameObject root = record.gameObject;
        ModelImporter sourceImporter = null;
        bool restoreImporterReadable = false;
        bool originalImporterReadable = false;
        string sourceMeshPath = AssetDatabase.GetAssetPath(bodyRenderer.sharedMesh);
        string sourceMeshName = bodyRenderer.sharedMesh.name;

        try
        {
            Mesh sourceBody = EnsureBodyReadable(
                ref sourceImporter,
                ref restoreImporterReadable,
                ref originalImporterReadable,
                sourceMeshPath,
                sourceMeshName);

            int[] mapping =
                ValidateNeutralMouthSource(
                    sourceBody,
                    out int[] neutralTriangles);

            PrepareStorageForWrite(record);

            Mesh generatedSe =
                GenerateLipSyncMouth(
                    sourceBody,
                    mapping,
                    neutralTriangles);

            record.mouthMaterialIndex =
                FindMouthSubMeshIndex(
                    sourceBody,
                    mapping);

            SkinnedMeshRenderer seRenderer =
                record.installedLipSyncMouthRenderer != null
                    ? record.installedLipSyncMouthRenderer
                    : root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .FirstOrDefault(r => r != null && r.gameObject.name == SeObjectName);

            Mesh installedMesh =
                UpdateOrCreateTrackedMeshAsset(
                    generatedSe,
                    seRenderer,
                    record);

            if (seRenderer == null)
            {
                seRenderer =
                    CreateLipSyncMouthRenderer(
                        root,
                        installedMesh,
                        record.mouthMaterialIndex);
            }
            else
            {
                RefreshLipSyncMouthRenderer(
                    seRenderer,
                    installedMesh,
                    record.mouthMaterialIndex);
            }

            record.installedLipSyncMouthRenderer = seRenderer;

            ConfigureInitialWeights(seRenderer);
            ConfigureDescriptor(seRenderer);
            ConfigureBlink();

            AnimationClip onClip =
                CreateOrUpdateToggleClip(
                    record.installedOnClip,
                    $"{OutputFolder}/MururuBlinkLipSync_LipSync_ON.anim",
                    "MururuBlinkLipSync_LipSync_ON",
                    seRenderer,
                    bodyMouthOffValue: 100f,
                    seHideValue: 0f,
                    record: record);

            AnimationClip offClip =
                CreateOrUpdateToggleClip(
                    record.installedOffClip,
                    $"{OutputFolder}/MururuBlinkLipSync_LipSync_OFF.anim",
                    "MururuBlinkLipSync_LipSync_OFF",
                    seRenderer,
                    bodyMouthOffValue: 0f,
                    seHideValue: 100f,
                    record: record);

            List<FeatureTrigger> blinkDisabledTriggers =
                BuildDisabledFeatureTriggers(forBlink: true);

            List<FeatureTrigger> lipSyncDisabledTriggers =
                BuildDisabledFeatureTriggers(forBlink: false);

            ModularAvatarMergeAnimator merge =
                record.GetComponent<ModularAvatarMergeAnimator>();

            if (merge == null)
            {
                merge =
                    Undo.AddComponent<ModularAvatarMergeAnimator>(root);
            }

            AnimatorController oldController =
                record.installedController as AnimatorController;

            if (oldController == null)
                oldController = merge.animator as AnimatorController;

            string oldControllerPath =
                oldController != null
                    ? AssetDatabase.GetAssetPath(oldController)
                    : null;

            string controllerPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{OutputFolder}/MururuBlinkLipSync_FX.controller");

            AnimatorController controller =
                CreateFreshController(
                    controllerPath,
                    onClip,
                    offClip,
                    blinkDisabledTriggers,
                    lipSyncDisabledTriggers);

            TrackAsset(record, controllerPath);
            ConfigureModularAvatarMergeAnimator(merge, controller);

            record.installedController = controller;
            record.installedOnClip = onClip;
            record.installedOffClip = offClip;

            CleanupLegacyMenuAndParameterResources(record);

            if (!string.IsNullOrEmpty(oldControllerPath) &&
                oldControllerPath != controllerPath)
            {
                DetachTrackedAssetAndDeleteIfUnshared(
                    record,
                    oldControllerPath);
            }

            record.installedVersion = ToolVersion;
            record.installSchemaVersion = InstallSchemaVersion;
            SaveExpressionSettings(record);

            EditorUtility.SetDirty(record);
            EditorUtility.SetDirty(seRenderer);
            EditorUtility.SetDirty(avatarDescriptor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(record);
            PrefabUtility.RecordPrefabInstancePropertyModifications(avatarDescriptor);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }
        finally
        {
            RestoreImporterReadableIfNeeded(
                sourceImporter,
                restoreImporterReadable,
                originalImporterReadable);
        }
    }

    private void Uninstall()
    {
        ValidateUninstallInputs();
        RemoveInstallation();
    }

    private GameObject CreateRoot()
    {
        GameObject root =
            new GameObject(RootObjectName);

        Undo.RegisterCreatedObjectUndo(
            root,
            "Mururu Blink & LipSync - Create root");

        root.transform.SetParent(
            avatarDescriptor.transform,
            false);

        root.transform.localPosition =
            Vector3.zero;

        root.transform.localRotation =
            Quaternion.identity;

        root.transform.localScale =
            Vector3.one;

        return root;
    }

    private void CaptureOriginalState(
        MururuBlinkLipSyncInstallRecord record,
        Mesh originalBody)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        if (originalBody == null)
            throw new ArgumentNullException(nameof(originalBody));

        record.avatarDescriptor = avatarDescriptor;
        record.bodyRenderer = bodyRenderer;

        // Retained for RC4 compatibility / immediate install rollback.
        // Normal Remove does not roll Body back to this snapshot.
        record.originalBodyMesh = originalBody;
        record.originalBodyBlendShapeNames =
            new string[originalBody.blendShapeCount];
        record.originalBodyBlendShapeWeights =
            new float[originalBody.blendShapeCount];

        for (int i = 0; i < originalBody.blendShapeCount; i++)
        {
            record.originalBodyBlendShapeNames[i] =
                originalBody.GetBlendShapeName(i);
            record.originalBodyBlendShapeWeights[i] =
                bodyRenderer.GetBlendShapeWeight(i);
        }

        record.originalLipSync = avatarDescriptor.lipSync;
        record.originalVisemeSkinnedMesh = avatarDescriptor.VisemeSkinnedMesh;
        record.originalVisemeBlendShapes =
            avatarDescriptor.VisemeBlendShapes != null
                ? (string[])avatarDescriptor.VisemeBlendShapes.Clone()
                : null;

        record.originalEnableEyeLook = avatarDescriptor.enableEyeLook;
        record.originalEyeLookSettings =
            CloneEyeLookSettings(avatarDescriptor.customEyeLookSettings);

        record.generatedAssetPaths = new List<string>();
        record.installedVersion = ToolVersion;
        record.installSchemaVersion = InstallSchemaVersion;
        record.installedLipSyncMouthRenderer = null;
        record.installedController = null;
        record.installedOnClip = null;
        record.installedOffClip = null;

        EditorUtility.SetDirty(record);
    }

    private void RestoreOriginalState(
        MururuBlinkLipSyncInstallRecord record,
        bool deleteGeneratedAssets)
    {
        if (record == null)
            throw new Exception("Install Recordがありません。");

        VRCAvatarDescriptor descriptor =
            record.avatarDescriptor != null
                ? record.avatarDescriptor
                : avatarDescriptor;

        SkinnedMeshRenderer body =
            record.bodyRenderer;

        if (descriptor == null)
            throw new Exception("復元対象Avatar Descriptorがありません。");

        if (body == null)
            throw new Exception("復元対象Body Rendererがありません。");

        if (record.originalBodyMesh == null)
            throw new Exception("Original Body Meshバックアップがありません。");

        List<string> assetPaths =
            record.generatedAssetPaths != null
                ? new List<string>(record.generatedAssetPaths)
                : new List<string>();

        Undo.RecordObject(
            body,
            "Mururu Blink & LipSync - Restore Body");

        body.sharedMesh =
            record.originalBodyMesh;

        RestoreBodyBlendShapeWeights(
            body,
            record.originalBodyBlendShapeNames,
            record.originalBodyBlendShapeWeights);

        Undo.RecordObject(
            descriptor,
            "Mururu Blink & LipSync - Restore Descriptor");

        descriptor.lipSync =
            record.originalLipSync;

        descriptor.VisemeSkinnedMesh =
            record.originalVisemeSkinnedMesh;

        descriptor.VisemeBlendShapes =
            record.originalVisemeBlendShapes != null
                ? (string[])record.originalVisemeBlendShapes.Clone()
                : null;

        descriptor.enableEyeLook =
            record.originalEnableEyeLook;

        descriptor.customEyeLookSettings =
            CloneEyeLookSettings(
                record.originalEyeLookSettings);

        EditorUtility.SetDirty(body);
        EditorUtility.SetDirty(descriptor);

        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);

        GameObject root =
            record.gameObject;

        Undo.DestroyObjectImmediate(root);

        if (deleteGeneratedAssets)
        {
            foreach (string path in assetPaths
                     .Where(IsInsideGeneratedFolder)
                     .Distinct()
                     .OrderByDescending(p => p.Length))
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    if (!AssetDatabase.DeleteAsset(path))
                    {
                        Debug.LogWarning(
                            "[MururuBlinkLipSync] 生成ファイルを削除できませんでした。ファイルの使用状態や書き込み権限を確認してください: " +
                            path);
                    }
                }
            }
        }
    }

    private void RemoveInstallation()
    {
        MururuBlinkLipSyncInstallRecord record = FindInstallRecord();

        if (record == null)
            throw new Exception("MururuBlinkLipSyncInstallRecordが見つかりません。");

        RefreshTrackedAssetPaths(record);

        VRCAvatarDescriptor descriptor =
            record.avatarDescriptor != null
                ? record.avatarDescriptor
                : avatarDescriptor;

        GameObject root = record.gameObject;

        SkinnedMeshRenderer seRenderer =
            record.installedLipSyncMouthRenderer != null
                ? record.installedLipSyncMouthRenderer
                : root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .FirstOrDefault(r => r != null && r.gameObject.name == SeObjectName);

        List<string> assetPaths =
            record.generatedAssetPaths != null
                ? new List<string>(record.generatedAssetPaths)
                : new List<string>();

        // RC4 compatibility: only restore the old Body Mesh when the current
        // Body is demonstrably the MururuBlinkLipSync-generated RC4 mesh. If the user has
        // already replaced Body with another mesh, do not overwrite it.
        RestoreLegacyGeneratedBodyIfOwned(record);

        // LipSync_Mouth is about to be deleted. A Descriptor must not retain a
        // reference to that renderer. Restore the pre-install LipSync block only
        // when it still points at the MururuBlinkLipSync-owned renderer.
        // When LipSync_Mouth was deleted by hand, the Viseme reference is
        // Missing; the tool's exact Viseme names still identify the setting
        // as the tool's, and it must not be left pointing at nothing.
        bool visemeIsTools =
            descriptor != null &&
            (seRenderer != null
                ? descriptor.VisemeSkinnedMesh == seRenderer
                : descriptor.VisemeSkinnedMesh == null &&
                  descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape &&
                  descriptor.VisemeBlendShapes != null &&
                  descriptor.VisemeBlendShapes.SequenceEqual(VisemeMapping));

        if (visemeIsTools)
        {
            Undo.RecordObject(
                descriptor,
                "Mururu Blink & LipSync - Detach LipSync Mouth Viseme");

            descriptor.lipSync = record.originalLipSync;
            descriptor.VisemeSkinnedMesh = record.originalVisemeSkinnedMesh;
            descriptor.VisemeBlendShapes =
                record.originalVisemeBlendShapes != null
                    ? (string[])record.originalVisemeBlendShapes.Clone()
                    : null;

            EditorUtility.SetDirty(descriptor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
        }

        // Restore only the Blink/Eye Look fields still owned by this tool.
        // If the user changed any of those fields after installation, leave the
        // current configuration untouched.
        RestoreEyeLookIfOwned(
            record,
            descriptor);

        // Body BlendShape weights are intentionally not restored during normal
        // Remove because they may have been edited after installation.

        List<string> ownedAssetPaths =
            assetPaths
                .Where(IsInsideGeneratedFolder)
                .Distinct()
                .ToList();

        List<string> outsideAssetPaths =
            assetPaths
                .Where(path =>
                    !string.IsNullOrEmpty(path) &&
                    !IsInsideGeneratedFolder(path))
                .Distinct()
                .ToList();

        if (outsideAssetPaths.Count > 0)
        {
            Debug.Log(
                "[MururuBlinkLipSync] 管理対象外のため、次のファイルは削除しませんでした:\n" +
                string.Join("\n", outsideAssetPaths));
        }

        List<string> keptAssetPaths =
            ownedAssetPaths
                .Where(path => IsAssetReferencedElsewhere(record, path))
                .ToList();

        List<string> deletableAssetPaths =
            ownedAssetPaths
                .Except(keptAssetPaths)
                .OrderByDescending(path => path.Length)
                .ToList();

        if (keptAssetPaths.Count > 0)
        {
            Debug.Log(
                "[MururuBlinkLipSync] ほかのアバター・Scene・Prefabから参照されているため、次の生成ファイルを保持しました:\n" +
                string.Join("\n", keptAssetPaths));
        }

        Undo.DestroyObjectImmediate(root);

        foreach (string path in deletableAssetPaths)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null &&
                !AssetDatabase.DeleteAsset(path))
            {
                Debug.LogWarning(
                    "[MururuBlinkLipSync] Generated asset could not be deleted: " + path);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        bodyRenderer =
            descriptor != null
                ? FindCompatibleBodyRenderer(descriptor)
                : null;
    }

    private static void RestoreLegacyGeneratedBodyIfOwned(
        MururuBlinkLipSyncInstallRecord record)
    {
        if (record == null ||
            record.bodyRenderer == null ||
            record.originalBodyMesh == null ||
            record.bodyRenderer.sharedMesh == null)
        {
            return;
        }

        Mesh current = record.bodyRenderer.sharedMesh;

        if (current == record.originalBodyMesh)
            return;

        string currentPath =
            AssetDatabase.GetAssetPath(current);

        bool trackedByMururuSe =
            record.generatedAssetPaths != null &&
            record.generatedAssetPaths.Any(p =>
                !string.IsNullOrEmpty(p) &&
                string.Equals(p, currentPath, StringComparison.Ordinal));

        bool looksLikeLegacyBody =
            string.Equals(
                current.name,
                "Body_MururuSE_Generated",
                StringComparison.Ordinal) ||
            (!string.IsNullOrEmpty(currentPath) &&
             currentPath.IndexOf(
                 "Body_MururuSE_Generated",
                 StringComparison.OrdinalIgnoreCase) >= 0);

        if (!trackedByMururuSe || !looksLikeLegacyBody)
            return;

        Dictionary<string, float> currentWeights =
            CaptureBlendShapeWeights(record.bodyRenderer);

        Undo.RecordObject(
            record.bodyRenderer,
            "Mururu Blink & LipSync - Remove legacy generated Body");

        record.bodyRenderer.sharedMesh = record.originalBodyMesh;
        ApplyBlendShapeWeightsByName(record.bodyRenderer, currentWeights);

        EditorUtility.SetDirty(record.bodyRenderer);
        PrefabUtility.RecordPrefabInstancePropertyModifications(record.bodyRenderer);
    }

    private static Dictionary<string, float> CaptureBlendShapeWeights(
        SkinnedMeshRenderer renderer)
    {
        Dictionary<string, float> result =
            new Dictionary<string, float>();

        if (renderer == null || renderer.sharedMesh == null)
            return result;

        Mesh mesh = renderer.sharedMesh;

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string name = mesh.GetBlendShapeName(i);

            if (!string.IsNullOrEmpty(name))
                result[name] = renderer.GetBlendShapeWeight(i);
        }

        return result;
    }

    private static void ApplyBlendShapeWeightsByName(
        SkinnedMeshRenderer renderer,
        Dictionary<string, float> weights)
    {
        if (renderer == null ||
            renderer.sharedMesh == null ||
            weights == null)
        {
            return;
        }

        foreach (KeyValuePair<string, float> pair in weights)
        {
            int index = renderer.sharedMesh.GetBlendShapeIndex(pair.Key);

            if (index >= 0)
                renderer.SetBlendShapeWeight(index, pair.Value);
        }
    }

    private static void RestoreBodyBlendShapeWeights(
        SkinnedMeshRenderer body,
        string[] names,
        float[] weights)
    {
        if (body.sharedMesh == null ||
            names == null ||
            weights == null)
        {
            return;
        }

        int count =
            Mathf.Min(names.Length, weights.Length);

        for (int i = 0; i < count; i++)
        {
            int shapeIndex =
                body.sharedMesh.GetBlendShapeIndex(
                    names[i]);

            if (shapeIndex >= 0)
            {
                body.SetBlendShapeWeight(
                    shapeIndex,
                    weights[i]);
            }
        }
    }

    private Mesh EnsureBodyReadable(
        ref ModelImporter importer,
        ref bool restoreReadable,
        ref bool originalReadable,
        string meshAssetPath,
        string meshName)
    {
        Mesh current =
            bodyRenderer.sharedMesh;

        if (current.isReadable)
            return current;

        importer =
            AssetImporter.GetAtPath(meshAssetPath)
            as ModelImporter;

        if (importer == null)
        {
            throw new Exception(
                "Body MeshがRead/Write OFFですが、ModelImporterを取得できません。\n" +
                "FBX由来Meshか確認してください。");
        }

        // The Mesh is found again by its local file id, not its name: an FBX
        // can hold several meshes with the same name.
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                current,
                out string _,
                out long localId))
        {
            throw new Exception(
                $"Body Mesh '{meshName}' のアセット内IDを取得できません。");
        }

        originalReadable =
            importer.isReadable;

        if (!originalReadable)
        {
            importer.isReadable =
                true;

            importer.SaveAndReimport();

            restoreReadable =
                true;
        }

        Mesh reacquired =
            AssetDatabase.LoadAllAssetsAtPath(meshAssetPath)
                .OfType<Mesh>()
                .FirstOrDefault(m =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        m,
                        out string _,
                        out long id) &&
                    id == localId);

        if (reacquired == null)
        {
            throw new Exception(
                $"Read/Write変更後にBody Mesh '{meshName}' を再取得できません。");
        }

        if (!reacquired.isReadable)
        {
            throw new Exception(
                "Body MeshのRead/Writeを自動有効化できませんでした。");
        }

        if (bodyRenderer.sharedMesh != reacquired)
        {
            Undo.RecordObject(
                bodyRenderer,
                "Mururu Blink & LipSync - Reacquire Body Mesh");

            bodyRenderer.sharedMesh =
                reacquired;

            PrefabUtility.RecordPrefabInstancePropertyModifications(bodyRenderer);
        }

        return reacquired;
    }

    private static void RestoreImporterReadableIfNeeded(
        ModelImporter importer,
        bool restoreReadable,
        bool originalReadable)
    {
        if (!restoreReadable ||
            importer == null)
        {
            return;
        }

        try
        {
            importer.isReadable =
                originalReadable;

            importer.SaveAndReimport();
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                "[MururuBlinkLipSync] FBXのRead/Write設定を元に戻せませんでした。対象FBXのImport Settingsを確認してください:\n" +
                ex);
        }
    }

    private static void CleanupLegacyMenuAndParameterResources(
        MururuBlinkLipSyncInstallRecord record)
    {
        if (record == null)
            return;

        foreach (ModularAvatarParameters parameters in
                 record.GetComponents<ModularAvatarParameters>())
        {
            if (parameters == null || parameters.parameters == null)
                continue;

            Undo.RecordObject(
                parameters,
                "Mururu Blink & LipSync - Remove Legacy Parameter");

            int removed =
                parameters.parameters.RemoveAll(config =>
                    config.nameOrPrefix == LegacyEnhancedLipSyncParameterName);

            if (removed <= 0)
                continue;

            if (parameters.parameters.Count == 0)
            {
                Undo.DestroyObjectImmediate(parameters);
            }
            else
            {
                EditorUtility.SetDirty(parameters);
            }
        }

        foreach (ModularAvatarMenuInstaller installer in
                 record.GetComponents<ModularAvatarMenuInstaller>())
        {
            if (installer == null)
                continue;

            // A Missing menu (reference set, asset deleted) is left behind when a
            // previous legacy cleanup is undone. The current schema never adds a
            // Menu Installer to this root, so it is safe to remove.
            bool menuMissing =
                installer.menuToAppend == null &&
                new SerializedObject(installer)
                    .FindProperty("menuToAppend")
                    .objectReferenceInstanceIDValue != 0;

            if (menuMissing ||
                (installer.menuToAppend != null &&
                 installer.menuToAppend.name == LegacyMenuAssetName))
            {
                Undo.DestroyObjectImmediate(installer);
            }
        }

        if (record.generatedAssetPaths == null)
            return;

        foreach (string path in record.generatedAssetPaths.ToArray())
        {
            if (string.IsNullOrEmpty(path))
                continue;

            VRCExpressionsMenu menu =
                AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);

            if (menu == null || menu.name != LegacyMenuAssetName)
                continue;

            DetachTrackedAssetAndDeleteIfUnshared(
                record,
                path);
        }

        EditorUtility.SetDirty(record);
    }

    private static void ValidateInstallRecordScriptPlacement()
    {
        string[] guids =
            AssetDatabase.FindAssets(
                "MururuBlinkLipSyncInstallRecord t:MonoScript");

        List<string> matchingPaths =
            new List<string>();

        foreach (string guid in guids)
        {
            string path =
                AssetDatabase.GUIDToAssetPath(guid);

            MonoScript script =
                AssetDatabase.LoadAssetAtPath<MonoScript>(path);

            if (script == null)
                continue;

            Type scriptClass =
                script.GetClass();

            if (scriptClass == typeof(MururuBlinkLipSyncInstallRecord))
            {
                matchingPaths.Add(
                    path.Replace('\\', '/'));
            }
        }

        if (matchingPaths.Count == 0)
        {
            throw new Exception(
                "MururuBlinkLipSyncInstallRecord.cs をProject内で確認できません。\n" +
                $"推奨配置: {GetPackageRoot()}/Runtime/MururuBlinkLipSyncInstallRecord.cs");
        }

        if (matchingPaths.Count > 1)
        {
            throw new Exception(
                "MururuBlinkLipSyncInstallRecord.cs が複数存在します。\n" +
                "重複を削除してください:\n" +
                string.Join("\n", matchingPaths));
        }

        string scriptPath =
            matchingPaths[0];

        // The Runtime asmdef decides the assembly, not folder names. If the file
        // was moved out of the Runtime folder it may end up in an Editor-only
        // assembly, which cannot be attached to scene objects.
        string assemblyName =
            typeof(MururuBlinkLipSyncInstallRecord)
                .Assembly
                .GetName()
                .Name;

        if (!string.Equals(
                assemblyName,
                "MururuBlinkLipSync.Runtime",
                StringComparison.Ordinal))
        {
            throw new Exception(
                "MururuBlinkLipSyncInstallRecord はSceneに付与するRuntime Componentなので、" +
                "MururuBlinkLipSync.Runtime Assemblyに置く必要があります。\n\n" +
                $"現在のScript: {scriptPath}\n" +
                $"Assembly: {assemblyName}\n\n" +
                "MururuBlinkLipSyncInstallRecord.cs を次へ移動してください:\n" +
                $"{GetPackageRoot()}/Runtime/MururuBlinkLipSyncInstallRecord.cs");
        }
    }

    private static void RequireBlendShape(
        Mesh mesh,
        string shapeName,
        string ownerLabel)
    {
        if (mesh.GetBlendShapeIndex(shapeName) < 0)
        {
            throw new Exception(
                $"{ownerLabel}にBlendShape '{shapeName}' がありません。");
        }
    }
}
}
