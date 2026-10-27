using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MururuBlinkLipSync.Editor
{
    internal static class MururuMouthReturnControllerGenerator
    {
        internal const string PhaseName = "MBLS_Return_Phase";
        internal const float WaitDuration = 0.08f;
        internal const float MorphDuration = 0.08f;
        const float VoiceEpsilon = 0.000001f;
        sealed class Suppression { internal string Parameter; internal int Value; }

        internal static MururuMouthReturnControllerBundle Generate(AnimatorController source,
            MururuBlinkLipSyncInstallRecord settings, string body, string mouth,
            IReadOnlyCollection<string> bridgeShapes, ISet<string> unknownMouthShapes,
            MururuMouthReturnFxRoutesPlan.AdditionalFx additional = null, string facialSuppressionParameter = null)
        {
            if (!settings || settings.leftLipSyncEnabled == null || settings.leftLipSyncEnabled.Length != 7
                || settings.rightLipSyncEnabled == null || settings.rightLipSyncEnabled.Length != 7
                || settings.facialLipSyncEnabled == null || settings.facialLipSyncEnabled.Length != 8)
                throw new InvalidOperationException("Explicit LipSync settings arrays of lengths 7/7/8 are required");
            if (string.IsNullOrWhiteSpace(settings.facialExParameterName)
                || new[] { "Voice", "GestureLeft", "GestureRight", "AFK", "VRCEmote", "Seated", "Accessory" }.Contains(settings.facialExParameterName))
                throw new InvalidOperationException("A distinct facial parameter is required");
            if (!Path(body) || !Path(mouth) || body == mouth) throw new InvalidOperationException("Distinct renderer paths are required");
            bool smooth = bridgeShapes.Count != 0;
            var plan = smooth ? MururuMouthReturnFxRoutesPlan.Capture(source, settings, body, mouth, unknownMouthShapes, bridgeShapes, additional)
                : new MururuMouthReturnFxRoutesPlan(new List<MururuMouthReturnFxRoutesRoute>(), body, mouth);
            var b = new MururuMouthReturnControllerBundle { BridgeShapes = bridgeShapes,
                Plan = new MururuMouthReturnFxRoutesPlan(plan.Routes.Select(r => new MururuMouthReturnFxRoutesRoute(
                    r.Key, r.Conditions.ToList(), r.Pose.WithGeometry(bridgeShapes))).ToList(), body, mouth, plan.Warnings) };
            try { Phase(b, settings, smooth, facialSuppressionParameter); return b; }
            catch { b.Dispose(); throw; }
        }
        static bool Path(string p) => p != null && (p.Length == 0 || !string.IsNullOrWhiteSpace(p)
            && p.IndexOf('\\') < 0 && p.Split('/').All(s => s.Length > 0 && s != "." && s != ".."));
        static AnimatorStateTransition Go(MururuMouthReturnControllerBundle b, AnimatorState from, AnimatorState to)
        {
            var t = b.Own(from.AddTransition(to)); t.hasExitTime = false; t.hasFixedDuration = true;
            t.duration = 0; t.canTransitionToSelf = false; return t;
        }
        static void Guard(AnimatorStateTransition t, MururuMouthReturnFxRoutesRoute r)
        { foreach (var c in r.Conditions) t.AddCondition(c.Mode, c.Threshold, c.Parameter); }
        static void RawExits(MururuMouthReturnControllerBundle b, AnimatorState from, AnimatorState to, MururuMouthReturnFxRoutesRoute r)
        {
            foreach (var c in r.Conditions)
            {
                AnimatorConditionMode inverse;
                float threshold = c.Threshold;
                switch (c.Mode)
                {
                    case AnimatorConditionMode.If: inverse = AnimatorConditionMode.IfNot; break;
                    case AnimatorConditionMode.IfNot: inverse = AnimatorConditionMode.If; break;
                    case AnimatorConditionMode.Equals: inverse = AnimatorConditionMode.NotEqual; break;
                    case AnimatorConditionMode.NotEqual: inverse = AnimatorConditionMode.Equals; break;
                    case AnimatorConditionMode.Greater: inverse = AnimatorConditionMode.Less; threshold++; break;
                    case AnimatorConditionMode.Less: inverse = AnimatorConditionMode.Greater; threshold--; break;
                    default: throw new InvalidOperationException("Unknown route guard");
                }
                Go(b, from, to).AddCondition(inverse, threshold, c.Parameter);
            }
        }
        static List<Suppression> Suppressions(MururuBlinkLipSyncInstallRecord settings, string facialParameter)
        {
            var result = new List<Suppression>();
            for (int i = 0; i < 7; i++)
            {
                if (!settings.leftLipSyncEnabled[i]) result.Add(new Suppression { Parameter = "GestureLeft", Value = i + 1 });
                if (!settings.rightLipSyncEnabled[i]) result.Add(new Suppression { Parameter = "GestureRight", Value = i + 1 });
            }
            for (int i = 0; i < 8; i++) if (!settings.facialLipSyncEnabled[i])
                result.Add(new Suppression { Parameter = facialParameter ?? settings.facialExParameterName, Value = i + 1 });
            return result;
        }
        static void Resume(AnimatorStateTransition t, List<Suppression> disabled)
        { t.AddCondition(AnimatorConditionMode.Greater, 0, "Voice"); foreach (var c in disabled) t.AddCondition(AnimatorConditionMode.NotEqual, c.Value, c.Parameter); }
        static void Silent(AnimatorStateTransition t) => t.AddCondition(AnimatorConditionMode.Less, VoiceEpsilon, "Voice");
        static void Finish(AnimatorStateTransition t) { t.hasExitTime = true; t.exitTime = 1; Silent(t); }
        static void Suppress(MururuMouthReturnControllerBundle b, AnimatorState from, AnimatorState to, List<Suppression> disabled)
        { foreach (var c in disabled) Go(b, from, to).AddCondition(AnimatorConditionMode.Equals, c.Value, c.Parameter); }

        static void Phase(MururuMouthReturnControllerBundle b, MururuBlinkLipSyncInstallRecord settings, bool smooth, string facialParameter)
        {
            var sm = b.Own(new AnimatorStateMachine { name = PhaseName });
            b.PhaseLayer = new AnimatorControllerLayer { name = PhaseName, defaultWeight = 1,
                blendingMode = AnimatorLayerBlendingMode.Override, syncedLayerIndex = -1, stateMachine = sm };
            var speaking = State(b, sm, "Speaking", false, 0, smooth);
            var original = State(b, sm, "Original", true, 0, smooth); sm.defaultState = original;
            var wait = State(b, sm, "Wait", false, WaitDuration, smooth);
            var disabled = Suppressions(settings, facialParameter);
            Suppress(b, speaking, original, disabled);
            var tracking = b.Own(speaking.AddStateMachineBehaviour<VRCAnimatorTrackingControl>());
            tracking.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            Resume(Go(b, original, speaking), disabled);
            Suppress(b, wait, original, disabled); Resume(Go(b, wait, speaking), disabled);
            if (smooth) foreach (var route in b.Plan.Routes.Where(r => r.Pose.Supported))
            {
                var bridge = State(b, sm, "Bridge_" + route.Key, false, MorphDuration, smooth, route);
                var enter = Go(b, wait, bridge); Finish(enter); Guard(enter, route);
                Suppress(b, bridge, original, disabled); Resume(Go(b, bridge, speaking), disabled);
                RawExits(b, bridge, original, route); Finish(Go(b, bridge, original));
            }
            Silent(Go(b, speaking, wait)); Finish(Go(b, wait, original));
        }
        static void Shape(AnimationClip clip, string path, string name, AnimationCurve curve)
            => clip.SetCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + name, curve);
        static AnimatorState State(MururuMouthReturnControllerBundle b, AnimatorStateMachine sm, string name,
            bool original, float length, bool smooth, MururuMouthReturnFxRoutesRoute route = null)
        {
            var clip = b.Own(new AnimationClip { name = "MBLS_Return_" + name });
            Shape(clip, b.Plan.BodyPath, "mouth_off", new AnimationCurve(new Keyframe(0, original ? 0 : 100)));
            Shape(clip, b.Plan.MouthPath, "SE_hide_self", length > 0 ? AnimationCurve.Constant(0, length, original ? 100 : 0)
                : new AnimationCurve(new Keyframe(0, original ? 100 : 0)));
            if (smooth) foreach (string property in b.BridgeShapes)
            {
                var curve = route == null ? new AnimationCurve(new Keyframe(0, 0)) : AnimationCurve.Linear(0, 0, length, route.Pose.Bridge[property]);
                curve.preWrapMode = curve.postWrapMode = WrapMode.ClampForever;
                Shape(clip, b.Plan.MouthPath, property, curve);
            }
            var state = b.Own(sm.AddState(name)); state.writeDefaultValues = false; state.motion = clip; return state;
        }
    }
}
