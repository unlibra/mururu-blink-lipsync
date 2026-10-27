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
    private sealed class FeatureTrigger
    {
        public string parameterName;
        public int intValue;
    }

    private AnimationClip CreateOrUpdateToggleClip(
        AnimationClip existingClip,
        string desiredPath,
        string clipName,
        SkinnedMeshRenderer seRenderer,
        float bodyMouthOffValue,
        float seHideValue,
        MururuBlinkLipSyncInstallRecord record)
    {
        string existingPath =
            existingClip != null
                ? AssetDatabase.GetAssetPath(existingClip)
                : null;

        bool canUpdateInPlace =
            existingClip != null &&
            !string.IsNullOrEmpty(existingPath) &&
            IsInsideGeneratedFolder(existingPath) &&
            !IsAssetReferencedElsewhere(record, existingPath);

        if (!canUpdateInPlace)
        {
            string path =
                AssetDatabase.GenerateUniqueAssetPath(desiredPath);

            AnimationClip created =
                CreateToggleClip(
                    path,
                    clipName,
                    seRenderer,
                    bodyMouthOffValue,
                    seHideValue);

            TrackAsset(record, path);

            if (!string.IsNullOrEmpty(existingPath) &&
                existingPath != path)
            {
                DetachTrackedAssetAndDeleteIfUnshared(
                    record,
                    existingPath);
            }

            return created;
        }

        Undo.RegisterCompleteObjectUndo(
            existingClip,
            "Mururu Blink & LipSync - Update LipSync Clip");

        existingClip.ClearCurves();
        existingClip.name = clipName;
        existingClip.frameRate = 60f;

        PopulateToggleClip(
            existingClip,
            seRenderer,
            bodyMouthOffValue,
            seHideValue);

        EditorUtility.SetDirty(existingClip);
        TrackAsset(record, existingPath);
        return existingClip;
    }

    private AnimationClip CreateToggleClip(
        string assetPath,
        string clipName,
        SkinnedMeshRenderer seRenderer,
        float bodyMouthOffValue,
        float seHideValue)
    {
        AnimationClip clip =
            new AnimationClip
            {
                name = clipName,
                frameRate = 60f
            };

        PopulateToggleClip(
            clip,
            seRenderer,
            bodyMouthOffValue,
            seHideValue);

        AssetDatabase.CreateAsset(
            clip,
            assetPath);

        EditorUtility.SetDirty(clip);

        return clip;
    }

    private void PopulateToggleClip(
        AnimationClip clip,
        SkinnedMeshRenderer seRenderer,
        float bodyMouthOffValue,
        float seHideValue)
    {
        string bodyPath =
            AnimationUtility.CalculateTransformPath(
                bodyRenderer.transform,
                avatarDescriptor.transform);

        string sePath =
            AnimationUtility.CalculateTransformPath(
                seRenderer.transform,
                avatarDescriptor.transform);

        if (bodyRenderer.sharedMesh.GetBlendShapeIndex("mouth_off") < 0)
            throw new Exception("Bodyにmouth_offがありません。");

        clip.SetCurve(
            bodyPath,
            typeof(SkinnedMeshRenderer),
            "blendShape.mouth_off",
            MakeConstantCurve(bodyMouthOffValue));

        clip.SetCurve(
            sePath,
            typeof(SkinnedMeshRenderer),
            $"blendShape.{HideSelfShapeName}",
            MakeConstantCurve(seHideValue));
    }

    private static AnimationCurve MakeConstantCurve(
        float value)
    {
        Keyframe k0 =
            new Keyframe(0f, value);

        Keyframe k1 =
            new Keyframe(1f / 60f, value);

        AnimationCurve curve =
            new AnimationCurve(k0, k1);

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(
                curve,
                i,
                AnimationUtility.TangentMode.Constant);

            AnimationUtility.SetKeyRightTangentMode(
                curve,
                i,
                AnimationUtility.TangentMode.Constant);
        }

        return curve;
    }

    private AnimatorController CreateFreshController(
        string assetPath,
        AnimationClip onClip,
        AnimationClip offClip,
        List<FeatureTrigger> blinkDisabledTriggers,
        List<FeatureTrigger> lipSyncDisabledTriggers)
    {
        AnimatorController controller =
            AnimatorController.CreateAnimatorControllerAtPath(
                assetPath);

        if (controller == null)
            throw new Exception("Mururu Blink & LipSync用AnimatorControllerの作成に失敗しました。");

        try
        {
            ConfigureOwnedController(
                controller,
                onClip,
                offClip,
                blinkDisabledTriggers,
                lipSyncDisabledTriggers);
        }
        catch
        {
            // The caller tracks the asset only after this returns, so a half
            // configured controller would otherwise be orphaned in Generated.
            AssetDatabase.DeleteAsset(assetPath);
            throw;
        }

        AssetDatabase.SaveAssets();
        return controller;
    }

    private void ConfigureOwnedController(
        AnimatorController controller,
        AnimationClip onClip,
        AnimationClip offClip,
        List<FeatureTrigger> blinkDisabledTriggers,
        List<FeatureTrigger> lipSyncDisabledTriggers)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));

        if (onClip == null || offClip == null)
            throw new Exception("LipSync表示切替Animation Clipがありません。");

        // This controller is fully owned by Mururu Blink & LipSync and is
        // freshly created for Install / Apply.
        AnimatorControllerLayer[] layers = controller.layers;

        if (layers == null ||
            layers.Length != 1 ||
            layers[0].stateMachine == null)
        {
            throw new Exception("Mururu Blink & LipSync AnimatorのBase Layerを取得できません。");
        }

        AnimatorControllerLayer lipLayer = layers[0];
        lipLayer.name = LayerName;
        lipLayer.defaultWeight = 1f;
        lipLayer.blendingMode = AnimatorLayerBlendingMode.Override;
        layers[0] = lipLayer;
        controller.layers = layers;

        EnsureTriggerParameters(controller, blinkDisabledTriggers);
        EnsureTriggerParameters(controller, lipSyncDisabledTriggers);
        EnsureAnimatorParameter(
            controller,
            VoiceParameterName,
            AnimatorControllerParameterType.Float);

        ConfigureLipSyncLayer(
            controller.layers[0].stateMachine,
            onClip,
            offClip,
            lipSyncDisabledTriggers);

        // With nothing to suppress, a Blink layer would only force Eyes back to
        // Tracking on load, overriding other gimmicks. The controller is rebuilt
        // from scratch on every Apply / Rebuild, so its layer set may change freely.
        if (blinkDisabledTriggers.Count > 0)
        {
            AddBlinkLayer(
                controller,
                blinkDisabledTriggers);
        }

        EditorUtility.SetDirty(controller);
    }

    private void ConfigureLipSyncLayer(
        AnimatorStateMachine sm,
        AnimationClip onClip,
        AnimationClip offClip,
        List<FeatureTrigger> disabledTriggers)
    {
        foreach (ChildAnimatorState child in sm.states.ToArray())
        {
            if (child.state != null)
                sm.RemoveState(child.state);
        }

        AnimatorState activeState = sm.AddState("LipSync");
        AnimatorState silenceGraceState = sm.AddState("LipSync Silence Grace");
        AnimatorState originalState = sm.AddState("Original Expression");

        activeState.motion = onClip;
        silenceGraceState.motion = onClip;
        originalState.motion = offClip;

        activeState.writeDefaultValues = false;
        silenceGraceState.writeDefaultValues = false;
        originalState.writeDefaultValues = false;

        // Silent is the normal state: keep Mururu's gesture/expression mouth visible.
        sm.defaultState = originalState;

        // Only the speaking state needs Mouth/Jaw tracking, because LipSync_Mouth
        // is driven by Visemes. The grace state is only entered from it (this
        // layer has no AnyState / Entry transitions), and
        // Original Expression hides LipSync_Mouth, so neither re-applies the
        // setting. This avoids overriding other gimmicks that set Mouth to
        // Animation while the avatar is silent.
        VRCAnimatorTrackingControl activeTracking =
            activeState.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();

        activeTracking.trackingMouth =
            VRC_AnimatorTrackingControl.TrackingType.Tracking;

        // Start LipSync for any positive Voice value. VRChat's own microphone
        // sensitivity/activation setting is expected to gate Voice upstream.
        // An unchecked LipSync setting is an absolute block.
        AnimatorStateTransition startLipSync =
            originalState.AddTransition(activeState);

        startLipSync.hasExitTime = false;
        startLipSync.duration = 0f;
        startLipSync.AddCondition(
            AnimatorConditionMode.Greater,
            0f,
            VoiceParameterName);

        foreach (FeatureTrigger trigger in disabledTriggers)
        {
            startLipSync.AddCondition(
                AnimatorConditionMode.NotEqual,
                trigger.intValue,
                trigger.parameterName);
        }

        // When Voice reaches zero, keep the LipSync mouth visible briefly. This
        // prevents natural pauses between syllables/words from flashing back to
        // the gesture mouth. The grace state uses the same ON clip.
        AnimatorStateTransition beginSilenceGrace =
            activeState.AddTransition(silenceGraceState);

        beginSilenceGrace.hasExitTime = false;
        beginSilenceGrace.duration = 0f;
        beginSilenceGrace.AddCondition(
            AnimatorConditionMode.Less,
            VoiceZeroEpsilon,
            VoiceParameterName);

        // Stretch the constant one-frame ON clip so normalized exit time 1.0
        // corresponds to the fixed 80ms grace period.
        float onClipLength =
            Mathf.Max(onClip.length, 1f / 60f);

        silenceGraceState.speed =
            onClipLength / MururuMouthReturnControllerGenerator.WaitDuration;

        // If speech resumes during the grace period, immediately continue LipSync.
        AnimatorStateTransition resumeSpeech =
            silenceGraceState.AddTransition(activeState);

        resumeSpeech.hasExitTime = false;
        resumeSpeech.duration = 0f;
        resumeSpeech.AddCondition(
            AnimatorConditionMode.Greater,
            0f,
            VoiceParameterName);

        foreach (FeatureTrigger trigger in disabledTriggers)
        {
            resumeSpeech.AddCondition(
                AnimatorConditionMode.NotEqual,
                trigger.intValue,
                trigger.parameterName);
        }

        // Only return to the original expression after Voice has remained at zero
        // for the complete grace period.
        AnimatorStateTransition finishSilenceGrace =
            silenceGraceState.AddTransition(originalState);

        finishSilenceGrace.hasExitTime = true;
        finishSilenceGrace.exitTime = 1f;
        finishSilenceGrace.duration = 0f;
        finishSilenceGrace.AddCondition(
            AnimatorConditionMode.Less,
            VoiceZeroEpsilon,
            VoiceParameterName);

        // Any expression that explicitly disables LipSync takes priority even
        // in the middle of speech or during the silence grace period.
        foreach (FeatureTrigger trigger in disabledTriggers)
        {
            AnimatorStateTransition suppressActive =
                activeState.AddTransition(originalState);

            suppressActive.hasExitTime = false;
            suppressActive.duration = 0f;
            suppressActive.AddCondition(
                AnimatorConditionMode.Equals,
                trigger.intValue,
                trigger.parameterName);

            AnimatorStateTransition suppressGrace =
                silenceGraceState.AddTransition(originalState);

            suppressGrace.hasExitTime = false;
            suppressGrace.duration = 0f;
            suppressGrace.AddCondition(
                AnimatorConditionMode.Equals,
                trigger.intValue,
                trigger.parameterName);
        }

        EditorUtility.SetDirty(activeTracking);
        EditorUtility.SetDirty(activeState);
        EditorUtility.SetDirty(silenceGraceState);
        EditorUtility.SetDirty(originalState);
        EditorUtility.SetDirty(sm);
    }

    private void AddBlinkLayer(
        AnimatorController controller,
        List<FeatureTrigger> disabledTriggers)
    {
        controller.AddLayer(BlinkLayerName);

        AnimatorControllerLayer[] layers = controller.layers;
        int layerIndex = layers.Length - 1;

        AnimatorControllerLayer layer = layers[layerIndex];
        layer.defaultWeight = 1f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layers[layerIndex] = layer;
        controller.layers = layers;

        AnimatorStateMachine sm =
            controller.layers[layerIndex].stateMachine;

        if (sm == null)
            throw new Exception("Blink StateMachineを取得できません。");

        // Idle writes nothing, so loading the avatar does not force Eyes back to
        // Tracking over other gimmicks. Tracking is restored only after this
        // layer itself switched Eyes to Animation.
        AnimatorState idleState = sm.AddState("Blink Idle");
        AnimatorState suppressedState = sm.AddState("Blink Disabled");
        AnimatorState restoreState = sm.AddState("Blink Restore");

        idleState.writeDefaultValues = false;
        suppressedState.writeDefaultValues = false;
        restoreState.writeDefaultValues = false;
        sm.defaultState = idleState;

        VRCAnimatorTrackingControl suppressedBehaviour =
            suppressedState.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();

        suppressedBehaviour.trackingEyes =
            VRC_AnimatorTrackingControl.TrackingType.Animation;

        VRCAnimatorTrackingControl restoreBehaviour =
            restoreState.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();

        restoreBehaviour.trackingEyes =
            VRC_AnimatorTrackingControl.TrackingType.Tracking;

        foreach (FeatureTrigger trigger in disabledTriggers)
        {
            foreach (AnimatorState source in new[] { idleState, restoreState })
            {
                AnimatorStateTransition suppress =
                    source.AddTransition(suppressedState);

                suppress.hasExitTime = false;
                suppress.duration = 0f;
                suppress.AddCondition(
                    AnimatorConditionMode.Equals,
                    trigger.intValue,
                    trigger.parameterName);
            }
        }

        AnimatorStateTransition resume =
            suppressedState.AddTransition(restoreState);

        resume.hasExitTime = false;
        resume.duration = 0f;

        foreach (FeatureTrigger trigger in disabledTriggers)
        {
            resume.AddCondition(
                AnimatorConditionMode.NotEqual,
                trigger.intValue,
                trigger.parameterName);
        }

        EditorUtility.SetDirty(suppressedBehaviour);
        EditorUtility.SetDirty(restoreBehaviour);
        EditorUtility.SetDirty(idleState);
        EditorUtility.SetDirty(suppressedState);
        EditorUtility.SetDirty(restoreState);
        EditorUtility.SetDirty(sm);
    }

    private static void EnsureTriggerParameters(
        AnimatorController controller,
        IEnumerable<FeatureTrigger> triggers)
    {
        if (triggers == null)
            return;

        foreach (FeatureTrigger trigger in triggers)
        {
            EnsureAnimatorParameter(
                controller,
                trigger.parameterName,
                AnimatorControllerParameterType.Int);
        }
    }

    private static void EnsureAnimatorParameter(
        AnimatorController controller,
        string parameterName,
        AnimatorControllerParameterType type)
    {
        AnimatorControllerParameter existing =
            controller.parameters
                .FirstOrDefault(parameter =>
                    parameter.name == parameterName);

        if (existing != null)
        {
            if (existing.type != type)
            {
                throw new Exception(
                    $"Animatorパラメータ '{parameterName}' の型が一致しません。\n" +
                    $"必要な型: {type} / 実際の型: {existing.type}");
            }

            return;
        }

        controller.AddParameter(parameterName, type);
    }

    private List<FeatureTrigger> BuildDisabledFeatureTriggers(
        bool forBlink)
    {
        List<FeatureTrigger> result =
            new List<FeatureTrigger>();

        bool[] left =
            forBlink ? leftBlinkEnabled : leftLipSyncEnabled;

        bool[] right =
            forBlink ? rightBlinkEnabled : rightLipSyncEnabled;

        bool[] facial =
            forBlink ? facialBlinkEnabled : facialLipSyncEnabled;

        for (int i = 0; i < GestureCount; i++)
        {
            if (!left[i])
            {
                result.Add(
                    new FeatureTrigger
                    {
                        parameterName = GestureLeftParameterName,
                        intValue = i + 1
                    });
            }

            if (!right[i])
            {
                result.Add(
                    new FeatureTrigger
                    {
                        parameterName = GestureRightParameterName,
                        intValue = i + 1
                    });
            }
        }

        for (int i = 0; i < FacialExCount; i++)
        {
            if (!facial[i])
            {
                result.Add(
                    new FeatureTrigger
                    {
                        parameterName = facialExParameterName,
                        intValue = i + 1
                    });
            }
        }

        return result;
    }
}
}
