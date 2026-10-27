using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MururuBlinkLipSync.Editor
{
    internal sealed class MururuMouthReturnFxRoutesCondition
    {
        internal string Parameter { get; }
        internal AnimatorConditionMode Mode { get; }
        internal float Threshold { get; }
        internal MururuMouthReturnFxRoutesCondition(string parameter, AnimatorConditionMode mode, float threshold)
        { Parameter = parameter; Mode = mode; Threshold = threshold; }
    }

    internal sealed class MururuMouthReturnFxRoutesRoute
    {
        internal string Key { get; }
        internal IReadOnlyList<MururuMouthReturnFxRoutesCondition> Conditions { get; }
        internal MururuMouthReturnFxRoutesPose Pose { get; }
        internal MururuMouthReturnFxRoutesRoute(string key, List<MururuMouthReturnFxRoutesCondition> conditions,
            MururuMouthReturnFxRoutesPose pose)
        { Key = key; Conditions = conditions.AsReadOnly(); Pose = pose; }
    }

    // Local lookup for the standard Mururu assignments. This is not a graph certificate.
    internal sealed partial class MururuMouthReturnFxRoutesPlan
    {
        internal IReadOnlyList<MururuMouthReturnFxRoutesRoute> Routes { get; }
        internal string BodyPath { get; }
        internal string MouthPath { get; }
        internal IReadOnlyList<string> Warnings { get; }
        internal MururuMouthReturnFxRoutesPlan(List<MururuMouthReturnFxRoutesRoute> routes, string body, string mouth,
            IEnumerable<string> warnings = null)
        { Routes = routes.AsReadOnly(); BodyPath = body; MouthPath = mouth; Warnings = (warnings ?? Array.Empty<string>()).Distinct().ToArray(); }

        internal static MururuMouthReturnFxRoutesPlan Capture(AnimatorController source,
            MururuBlinkLipSyncInstallRecord settings, string body, string mouth, ISet<string> unknownMouthShapes,
            IReadOnlyCollection<string> bridgeShapes = null, AdditionalFx additional = null)
        {
            return new EffectiveSource(source, settings, body, mouth, unknownMouthShapes, additional).Capture(bridgeShapes);
        }

        static MururuMouthReturnFxRoutesCondition C(string p, AnimatorConditionMode m, float v = 0)
            => new MururuMouthReturnFxRoutesCondition(p, m, v);

        static readonly string[] Layers = { "Facial Idle", "Facial LeftHand", "Facial RightHand", "Facial Ex", "Facial AFK", "Accessory" };
        static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine root)
        {
            var pending = new Stack<AnimatorStateMachine>(); var seen = new HashSet<AnimatorStateMachine>();
            if (root) pending.Push(root);
            while (pending.Count != 0)
            {
                var sm = pending.Pop(); if (!seen.Add(sm)) continue; yield return sm;
                foreach (var child in sm.stateMachines) if (child.stateMachine) pending.Push(child.stateMachine);
            }
        }
        static HashSet<string> MouthProperties(Motion root, string body, ISet<string> unknown)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<Motion>(); var seen = new HashSet<Motion>(); if (root) pending.Push(root);
            while (pending.Count != 0)
            {
                var motion = pending.Pop(); if (!seen.Add(motion)) continue;
                if (motion is BlendTree tree) { foreach (var child in tree.children) if (child.motion) pending.Push(child.motion); continue; }
                if (!(motion is AnimationClip clip)) { result.Add("@unknown-motion"); continue; }
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    if (b.path == body && b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        string name = b.propertyName.Substring("blendShape.".Length);
                        if (name.StartsWith("mouth_", StringComparison.Ordinal) || unknown.Contains(name)) result.Add(name);
                    }
                if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Any(b => b.path == body
                    && b.type == typeof(SkinnedMeshRenderer) && b.propertyName == "m_Mesh")) result.Add("@mesh");
            }
            return result;
        }
        static string FacialInput(AnimatorController source)
        {
            if (!source) return null;
            var layers = source.layers.Where(l => l.name == "Facial Ex").ToArray();
            if (layers.Length != 1 || !layers[0].stateMachine) return null;
            int index = Array.FindIndex(source.layers, l => l.name == "Facial Ex");
            int root = RootIndex(source.layers, index);
            if (root < 0 || !source.layers[root].stateMachine) return null;
            var entries = source.layers[root].stateMachine.entryTransitions;
            if (entries.Length != 8 || entries.Any(t => !t || t.conditions.Length != 1)) return null;
            var names = entries.Select(t => t.conditions[0].parameter).Distinct().ToArray();
            if (names.Length != 1 || string.IsNullOrWhiteSpace(names[0])
                || new[] { "Voice", "GestureLeft", "GestureRight", "AFK", "VRCEmote", "Seated", "Accessory" }.Contains(names[0])) return null;
            var parameters = source.parameters.Where(p => p.name == names[0]).ToArray();
            return parameters.Length == 1 && parameters[0].type == AnimatorControllerParameterType.Int
                && AssignedState(source, "Facial Ex", "1 ", names[0], 1) ? names[0] : null;
        }

        static AnimationClip Clip(AnimatorController source, string layerName, string label,
            string parameter = null, int value = 0, bool afk = false)
        {
            var state = AssignedState(source, layerName, label, parameter, value, afk);
            int index = source ? Array.FindIndex(source.layers, l => l.name == layerName) : -1;
            return state && index >= 0 ? EffectiveMotion(source.layers, index, state) as AnimationClip : null;
        }

        static AnimatorState AssignedState(AnimatorController source, string layerName, string label,
            string parameter = null, int value = 0, bool afk = false)
        {
            if (!source) return null;
            var layers = source.layers.Where(l => l.name == layerName).ToArray();
            if (layers.Length != 1) return null;
            int index = Array.FindIndex(source.layers, l => l.name == layerName);
            int root = RootIndex(source.layers, index);
            if (root < 0) return null;
            return AssignedStateMachine(source.layers[root].stateMachine, label, parameter, value, afk);
        }

        static AnimatorState AssignedStateMachine(AnimatorStateMachine sm, string label, string parameter, int value, bool afk)
        {
            if (!sm || sm.stateMachines.Length != 0
                || sm.anyStateTransitions.Length != 0) return null;
            var states = sm.states.Where(s => label == null || (afk ? s.state.name == label : s.state.name.StartsWith(label, StringComparison.Ordinal))).ToArray();
            if (states.Length != 1) return null;
            var state = states[0].state;
            if (parameter == null && (sm.defaultState != state || sm.entryTransitions.Length != 0 || state.transitions.Length != 0)) return null;
            // Read the direct standard assignment, not names alone. Ambiguous assignments are unsupported.
            if (afk)
            {
                var init = sm.states.Where(s => s.state.name == "Afk Init").Select(s => s.state).ToArray();
                var end = sm.states.Where(s => s.state.name == "BlendOut").Select(s => s.state).ToArray();
                if (sm.states.Length != 4 || init.Length != 1 || end.Length != 1 || !sm.defaultState
                    || sm.defaultState.name != "WaitForActionOrAFK" || sm.entryTransitions.Length != 0) return null;
                var begin = sm.defaultState.transitions;
                var arrive = init[0].transitions;
                var leave = state.transitions; var finish = end[0].transitions;
                if (begin.Length != 1 || begin[0].mute || begin[0].destinationState != init[0] || begin[0].conditions.Length != 1
                    || begin[0].conditions[0].parameter != "AFK" || begin[0].conditions[0].mode != AnimatorConditionMode.If
                    || arrive.Length != 1 || arrive[0].mute || arrive[0].destinationState != state || arrive[0].conditions.Length != 0
                    || leave.Length != 1 || leave[0].mute || leave[0].destinationState != end[0] || leave[0].conditions.Length != 1
                    || leave[0].conditions[0].parameter != "AFK" || leave[0].conditions[0].mode != AnimatorConditionMode.IfNot
                    || finish.Length != 1 || finish[0].mute || !finish[0].isExit || finish[0].conditions.Length != 0) return null;
            }
            else if (parameter != null)
            {
                // Recognize the small standard table as a whole. Unknown conditions cannot safely be
                // ignored: an earlier unconditional or non-Equals Entry may intercept this route.
                int count = parameter == "GestureLeft" || parameter == "GestureRight" ? 7 : 8;
                var entries = sm.entryTransitions;
                if (entries.Length != count || sm.states.Length != count + 1 || !sm.defaultState
                    || !sm.defaultState.name.StartsWith("0 ", StringComparison.Ordinal)) return null;
                for (int i = 0; i <= count; i++)
                {
                    var matches = sm.states.Where(s => s.state.name.StartsWith(i + " ", StringComparison.Ordinal)).ToArray();
                    if (matches.Length != 1) return null;
                    var exits = matches[0].state.transitions;
                    if (exits.Length != 1 || exits[0].mute || !exits[0].isExit || exits[0].hasExitTime || exits[0].conditions.Length != 1) return null;
                    var c = exits[0].conditions[0];
                    if (c.parameter != parameter || c.mode != AnimatorConditionMode.NotEqual || c.threshold != i) return null;
                }
                var seen = new HashSet<float>();
                foreach (var entry in entries)
                {
                    if (!entry || entry.mute || entry.solo || entry.isExit || entry.conditions.Length != 1) return null;
                    var c = entry.conditions[0];
                    if (c.parameter != parameter || c.mode != AnimatorConditionMode.Equals || !seen.Add(c.threshold)
                        || !(c.threshold >= 1 && c.threshold <= count) || c.threshold != Mathf.Floor(c.threshold)) return null;
                    var destination = sm.states.Where(s => s.state.name.StartsWith(((int)c.threshold) + " ", StringComparison.Ordinal)).ToArray();
                    if (destination.Length != 1 || entry.destinationState != destination[0].state) return null;
                }
                var assignments = entries.Where(t => t.conditions[0].threshold == value).ToArray();
                if (value == 0)
                {
                    if (sm.defaultState != state || assignments.Length != 0) return null;
                }
                else if (assignments.Length != 1 || assignments[0].destinationState != state || assignments[0].isExit
                    || assignments[0].conditions.Length != 1) return null;
            }
            return state;
        }

        static bool Read(AnimationClip clip, string path, Dictionary<string, float> values, ISet<string> unknownMouthShapes)
        {
            if (!clip) return false;
            if (MouthProperties(clip, path, unknownMouthShapes).Contains("@mesh")) return false;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path || binding.type != typeof(SkinnedMeshRenderer)
                    || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) continue;
                string key = binding.propertyName.Substring("blendShape.".Length);
                if (!key.StartsWith("mouth_", StringComparison.Ordinal) && !unknownMouthShapes.Contains(key)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) return false;
                if (!MururuMouthReturnFxRoutesPose.Properties.Contains(key))
                {
                    if (curve.keys.All(k => k.value == 0 && k.inTangent == 0 && k.outTangent == 0 &&
                        !float.IsNaN(k.time) && !float.IsInfinity(k.time))) { values[key] = 0; continue; }
                    return false;
                }
                float value = curve.keys[0].value;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 100
                    || curve.keys.Any(k => k.value != value || float.IsNaN(k.time) || float.IsInfinity(k.time)
                        || k.inTangent != 0 || k.outTangent != 0)) return false;
                values[key] = value;
            }
            return true;
        }
    }
}
