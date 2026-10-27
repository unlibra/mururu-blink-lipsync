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
    internal void AutoDetectTarget()
    {
        // An avatar selected in the Hierarchy always becomes the target, as the
        // README instructs. Selecting anything else keeps the current target.
        VRCAvatarDescriptor selectedDescriptor =
            null;

        if (Selection.activeGameObject != null)
        {
            selectedDescriptor =
                Selection.activeGameObject
                    .GetComponentInParent<VRCAvatarDescriptor>();
        }

        if (selectedDescriptor != null &&
            selectedDescriptor.gameObject.scene.IsValid() &&
            selectedDescriptor.gameObject.scene.isLoaded)
        {
            avatarDescriptor =
                selectedDescriptor;

            RefreshTargetFields();

            Repaint();
            return;
        }

        if (avatarDescriptor != null &&
            avatarDescriptor.gameObject.scene.IsValid() &&
            avatarDescriptor.gameObject.scene.isLoaded)
        {
            RefreshTargetFields();

            Repaint();
            return;
        }

        avatarDescriptor = null;

        List<MururuBlinkLipSyncInstallRecord> installedRecords =
            FindSceneInstallRecords();

        if (installedRecords.Count == 1)
        {
            MururuBlinkLipSyncInstallRecord record =
                installedRecords[0];

            avatarDescriptor =
                record.avatarDescriptor != null
                    ? record.avatarDescriptor
                    : record.GetComponentInParent<VRCAvatarDescriptor>();

            RefreshTargetFields();
            Repaint();
            return;
        }

        List<VRCAvatarDescriptor> installable =
            FindSceneAvatarDescriptors()
                .Where(descriptor =>
                    descriptor != null &&
                    descriptor
                        .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
                        .Length == 0 &&
                    FindCompatibleBodyRenderer(descriptor) != null)
                .ToList();

        if (installable.Count == 1)
        {
            avatarDescriptor =
                installable[0];

            RefreshTargetFields();
            Repaint();
            return;
        }

        Repaint();
    }

    internal void RefreshTargetFields()
    {
        bodyRenderer = null;

        if (avatarDescriptor == null)
        {
            settingsTargetAvatarId = int.MinValue;
            return;
        }

        int targetId = avatarDescriptor.GetInstanceID();

        MururuBlinkLipSyncInstallRecord record =
            avatarDescriptor
                .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
                .FirstOrDefault();

        if (record != null)
        {
            bodyRenderer =
                record.bodyRenderer != null
                    ? record.bodyRenderer
                    : FindCompatibleBodyRenderer(avatarDescriptor);

            if (settingsTargetAvatarId != targetId)
            {
                LoadExpressionSettings(record);
                settingsTargetAvatarId = targetId;
            }

            return;
        }

        bodyRenderer =
            FindCompatibleBodyRenderer(
                avatarDescriptor);

        if (settingsTargetAvatarId != targetId)
        {
            ResetExpressionSettingsToMururuDefaults();
            TryAutoDetectFacialExParameterName();
            settingsTargetAvatarId = targetId;
        }
    }

    internal bool IsCurrentTargetInstalled()
    {
        if (avatarDescriptor == null)
            return false;

        return avatarDescriptor
            .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
            .Any();
    }

    internal static SkinnedMeshRenderer FindCompatibleBodyRenderer(
        VRCAvatarDescriptor descriptor)
    {
        if (descriptor == null)
            return null;

        SkinnedMeshRenderer[] renderers =
            descriptor
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // Prefer the canonical Mururu object name.
        SkinnedMeshRenderer exactBody =
            renderers.FirstOrDefault(
                renderer =>
                    renderer != null &&
                    renderer.gameObject.name == "Body" &&
                    IsCompatibleMururuBody(renderer.sharedMesh));

        if (exactBody != null)
            return exactBody;

        List<SkinnedMeshRenderer> compatible =
            renderers
                .Where(renderer =>
                    renderer != null &&
                    IsCompatibleMururuBody(renderer.sharedMesh))
                .ToList();

        return compatible.Count == 1
            ? compatible[0]
            : null;
    }

    private static bool IsCompatibleMururuBody(
        Mesh mesh)
    {
        if (mesh == null)
            return false;

        foreach (string shape in RequiredBodyShapeNames)
        {
            if (mesh.GetBlendShapeIndex(shape) < 0)
                return false;
        }

        return true;
    }

    private static List<VRCAvatarDescriptor> FindSceneAvatarDescriptors()
    {
        return Resources
            .FindObjectsOfTypeAll<VRCAvatarDescriptor>()
            .Where(descriptor =>
                descriptor != null &&
                !EditorUtility.IsPersistent(descriptor) &&
                descriptor.gameObject.scene.IsValid() &&
                descriptor.gameObject.scene.isLoaded)
            .ToList();
    }

    // Only objects in a loaded scene can be operated on. The target field also
    // accepts a Prefab asset from the Project window, whose contents must not
    // be edited in place.
    private static bool IsEditableSceneObject(
        Component component)
    {
        return component != null &&
               !EditorUtility.IsPersistent(component) &&
               component.gameObject.scene.IsValid() &&
               component.gameObject.scene.isLoaded;
    }

    private List<string> GetInstallMissingRequirements()
    {
        List<string> missing =
            new List<string>();

        if (avatarDescriptor == null)
        {
            missing.Add("対象アバター");
        }

        if (bodyRenderer == null)
        {
            missing.Add("対応Body");
        }

        return missing;
    }

    private void EnsureUninstallTargetSelected()
    {
        if (avatarDescriptor != null)
        {
            MururuBlinkLipSyncInstallRecord record =
                avatarDescriptor
                    .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
                    .FirstOrDefault();

            if (record != null)
                return;
        }

        AutoDetectTarget();

        if (avatarDescriptor != null &&
            avatarDescriptor
                .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
                .Any())
        {
            return;
        }

        throw new Exception(
            "対象のアバターを自動特定できません。\n" +
            "導入済みMururuが複数ある場合は、Hierarchyで対象のアバターを選択してください。");
    }

    private static List<MururuBlinkLipSyncInstallRecord> FindSceneInstallRecords()
    {
        return Resources
            .FindObjectsOfTypeAll<MururuBlinkLipSyncInstallRecord>()
            .Where(record =>
                record != null &&
                !EditorUtility.IsPersistent(record) &&
                record.gameObject.scene.IsValid() &&
                record.gameObject.scene.isLoaded)
            .ToList();
    }

    internal MururuBlinkLipSyncInstallRecord FindInstallRecord()
    {
        if (avatarDescriptor == null)
            return null;

        return avatarDescriptor
            .GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
            .FirstOrDefault();
    }
}
}
