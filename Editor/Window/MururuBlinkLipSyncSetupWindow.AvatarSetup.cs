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
    private static VRCAvatarDescriptor.CustomEyeLookSettings CloneEyeLookSettings(
        VRCAvatarDescriptor.CustomEyeLookSettings source)
    {
        VRCAvatarDescriptor.CustomEyeLookSettings clone =
            source;

        if (source.eyelidsBlendshapes != null)
        {
            clone.eyelidsBlendshapes =
                (int[])source.eyelidsBlendshapes.Clone();
        }

        return clone;
    }

    private static void RestoreEyeLookIfOwned(
        MururuBlinkLipSyncInstallRecord record,
        VRCAvatarDescriptor descriptor)
    {
        if (record == null ||
            descriptor == null ||
            record.bodyRenderer == null)
        {
            return;
        }

        Mesh blinkSourceMesh =
            record.originalBodyMesh != null
                ? record.originalBodyMesh
                : record.bodyRenderer.sharedMesh;

        if (blinkSourceMesh == null)
            return;

        int blinkIndex =
            blinkSourceMesh.GetBlendShapeIndex(
                "eye_blink");

        if (blinkIndex < 0)
            return;

        VRCAvatarDescriptor.CustomEyeLookSettings current =
            descriptor.customEyeLookSettings;

        bool toolStillOwnsBlinkSettings =
            descriptor.enableEyeLook &&
            current.eyelidType ==
                VRCAvatarDescriptor.EyelidType.Blendshapes &&
            current.eyelidsSkinnedMesh ==
                record.bodyRenderer &&
            current.eyelidsBlendshapes != null &&
            current.eyelidsBlendshapes.Length == 3 &&
            current.eyelidsBlendshapes[0] == blinkIndex &&
            current.eyelidsBlendshapes[1] == -1 &&
            current.eyelidsBlendshapes[2] == -1;

        if (!toolStillOwnsBlinkSettings)
            return;

        Undo.RecordObject(
            descriptor,
            "Mururu Blink & LipSync - Restore Blink / Eye Look");

        // Preserve unrelated Eye Look fields that may have been edited after
        // installation. Restore only the fields Mururu Blink & LipSync owns.
        VRCAvatarDescriptor.CustomEyeLookSettings restored =
            CloneEyeLookSettings(
                descriptor.customEyeLookSettings);

        VRCAvatarDescriptor.CustomEyeLookSettings original =
            record.originalEyeLookSettings;

        restored.eyelidType =
            original.eyelidType;

        restored.eyelidsSkinnedMesh =
            original.eyelidsSkinnedMesh;

        restored.eyelidsBlendshapes =
            original.eyelidsBlendshapes != null
                ? (int[])original.eyelidsBlendshapes.Clone()
                : null;

        descriptor.enableEyeLook =
            record.originalEnableEyeLook;

        descriptor.customEyeLookSettings =
            restored;

        EditorUtility.SetDirty(descriptor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
    }

    private SkinnedMeshRenderer CreateLipSyncMouthRenderer(
        GameObject root,
        Mesh generatedSe,
        int mouthMaterialIndex)
    {
        GameObject go =
            new GameObject(SeObjectName);

        Undo.RegisterCreatedObjectUndo(
            go,
            "Mururu Blink & LipSync - Create LipSync_Mouth");

        go.transform.SetParent(
            root.transform,
            false);

        SkinnedMeshRenderer renderer =
            Undo.AddComponent<SkinnedMeshRenderer>(go);

        ApplyBodyRendererSettings(
            renderer,
            generatedSe,
            mouthMaterialIndex);

        return renderer;
    }

    private void RefreshLipSyncMouthRenderer(
        SkinnedMeshRenderer renderer,
        Mesh generatedSe,
        int mouthMaterialIndex)
    {
        if (renderer == null)
            throw new ArgumentNullException(nameof(renderer));

        if (generatedSe == null)
            throw new ArgumentNullException(nameof(generatedSe));

        Undo.RecordObject(
            renderer,
            "Mururu Blink & LipSync - Refresh LipSync_Mouth");

        Undo.RecordObject(
            renderer.transform,
            "Mururu Blink & LipSync - Refresh LipSync_Mouth Transform");

        ApplyBodyRendererSettings(
            renderer,
            generatedSe,
            mouthMaterialIndex);

        EditorUtility.SetDirty(renderer);
    }

    // LipSync_Mouth replaces part of Body's face while speaking, so it must be
    // skinned, lit and culled exactly like Body. Otherwise an Anchor Override,
    // probe or shadow setting on Body makes the mouth look different from the face.
    private void ApplyBodyRendererSettings(
        SkinnedMeshRenderer renderer,
        Mesh generatedSe,
        int mouthMaterialIndex)
    {
        // Body is validated as a direct child of Avatar Root and MururuBlinkLipSync root is identity,
        // so copying Body local transform produces the same avatar-space transform.
        renderer.transform.localPosition =
            bodyRenderer.transform.localPosition;

        renderer.transform.localRotation =
            bodyRenderer.transform.localRotation;

        renderer.transform.localScale =
            bodyRenderer.transform.localScale;

        renderer.sharedMesh =
            generatedSe;

        renderer.bones =
            bodyRenderer.bones;

        renderer.rootBone =
            bodyRenderer.rootBone;

        renderer.sharedMaterials =
            BuildLipSyncMaterialSlots(
                generatedSe,
                mouthMaterialIndex);

        renderer.localBounds =
            bodyRenderer.localBounds;

        renderer.updateWhenOffscreen =
            bodyRenderer.updateWhenOffscreen;

        renderer.quality =
            bodyRenderer.quality;

        renderer.skinnedMotionVectors =
            bodyRenderer.skinnedMotionVectors;

        renderer.probeAnchor =
            bodyRenderer.probeAnchor;

        renderer.lightProbeUsage =
            bodyRenderer.lightProbeUsage;

        renderer.reflectionProbeUsage =
            bodyRenderer.reflectionProbeUsage;

        renderer.shadowCastingMode =
            bodyRenderer.shadowCastingMode;

        renderer.receiveShadows =
            bodyRenderer.receiveShadows;

        renderer.allowOcclusionWhenDynamic =
            bodyRenderer.allowOcclusionWhenDynamic;

        renderer.lightProbeProxyVolumeOverride =
            bodyRenderer.lightProbeProxyVolumeOverride;

        renderer.motionVectorGenerationMode =
            bodyRenderer.motionVectorGenerationMode;

        renderer.renderingLayerMask =
            bodyRenderer.renderingLayerMask;

        renderer.rendererPriority =
            bodyRenderer.rendererPriority;

        renderer.sortingLayerID =
            bodyRenderer.sortingLayerID;

        renderer.sortingOrder =
            bodyRenderer.sortingOrder;

        renderer.enabled =
            true;
    }

    private Material[] BuildLipSyncMaterialSlots(
        Mesh generatedMesh,
        int mouthMaterialIndex)
    {
        if (generatedMesh == null)
            throw new ArgumentNullException(nameof(generatedMesh));

        return MururuLipSyncMaterialSlots.Build(
            bodyRenderer,
            generatedMesh.subMeshCount,
            mouthMaterialIndex);
    }

    private void ConfigureInitialWeights(
        SkinnedMeshRenderer seRenderer)
    {
        int seHide =
            seRenderer.sharedMesh.GetBlendShapeIndex(
                HideSelfShapeName);

        if (seHide < 0)
        {
            throw new Exception(
                "LipSync_MouthにSE_hide_self BlendShapeがありません。");
        }

        // Edit-mode preview stays on the original Mururu mouth.
        // At runtime LipSync_Mouth is revealed only while Voice is active and the
        // selected expression permits LipSync. Body itself is never replaced.
        seRenderer.SetBlendShapeWeight(
            seHide,
            100f);
    }

    private void ConfigureDescriptor(
        SkinnedMeshRenderer seRenderer)
    {
        Undo.RecordObject(
            avatarDescriptor,
            "Mururu Blink & LipSync - Configure Viseme");

        avatarDescriptor.lipSync =
            VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;

        avatarDescriptor.VisemeSkinnedMesh =
            seRenderer;

        avatarDescriptor.VisemeBlendShapes =
            (string[])VisemeMapping.Clone();
    }

    private void ConfigureBlink()
    {
        int blinkIndex =
            bodyRenderer.sharedMesh.GetBlendShapeIndex(
                "eye_blink");

        if (blinkIndex < 0)
            throw new Exception("Bodyにeye_blinkがありません。");

        Undo.RecordObject(
            avatarDescriptor,
            "Mururu Blink & LipSync - Configure Blink");

        avatarDescriptor.enableEyeLook =
            true;

        VRCAvatarDescriptor.CustomEyeLookSettings settings =
            CloneEyeLookSettings(
                avatarDescriptor.customEyeLookSettings);

        settings.eyelidType =
            VRCAvatarDescriptor.EyelidType.Blendshapes;

        settings.eyelidsSkinnedMesh =
            bodyRenderer;

        if (settings.eyelidsBlendshapes == null ||
            settings.eyelidsBlendshapes.Length != 3)
        {
            settings.eyelidsBlendshapes =
                new int[3];
        }

        settings.eyelidsBlendshapes[0] =
            blinkIndex;

        settings.eyelidsBlendshapes[1] =
            -1;

        settings.eyelidsBlendshapes[2] =
            -1;

        avatarDescriptor.customEyeLookSettings =
            settings;
    }

    private static void ConfigureModularAvatarComponents(
        GameObject root,
        AnimatorController controller)
    {
        ModularAvatarMergeAnimator merge =
            Undo.AddComponent<ModularAvatarMergeAnimator>(
                root);

        ConfigureModularAvatarMergeAnimator(
            merge,
            controller);
    }

    private static void ConfigureModularAvatarMergeAnimator(
        ModularAvatarMergeAnimator merge,
        AnimatorController controller)
    {
        if (merge == null)
            throw new ArgumentNullException(nameof(merge));

        Undo.RecordObject(
            merge,
            "Mururu Blink & LipSync - Configure Merge Animator");

        merge.animator =
            controller;

        merge.layerType =
            VRCAvatarDescriptor.AnimLayerType.FX;

        merge.pathMode =
            MergeAnimatorPathMode.Absolute;

        merge.deleteAttachedAnimator =
            false;

        merge.matchAvatarWriteDefaults =
            true;

        merge.layerPriority =
            1;

        merge.mergeAnimatorMode =
            MergeAnimatorMode.Append;

        EditorUtility.SetDirty(merge);
    }
}
}
