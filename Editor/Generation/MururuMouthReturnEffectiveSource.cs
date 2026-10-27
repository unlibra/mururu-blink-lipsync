using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MururuBlinkLipSync.Editor
{
    internal sealed partial class MururuMouthReturnFxRoutesPlan
    {
        // A synced layer executes its root graph, but owns its effective motions.
        // In Unity 2022.3 an absent Motion override is null, rather than a request
        // to replay state.motion. Shared inherited clips are explicit references.
        static int RootIndex(AnimatorControllerLayer[] layers, int index)
        {
            var seen = new HashSet<int>();
            while (index >= 0 && index < layers.Length && seen.Add(index))
            {
                int next = layers[index].syncedLayerIndex;
                if (next == -1) return index;
                index = next;
            }
            return -1;
        }

        static Motion EffectiveMotion(AnimatorControllerLayer[] layers, int index, AnimatorState state)
        {
            if (RootIndex(layers, index) < 0) return null;
            return layers[index].syncedLayerIndex == -1 ? state.motion : layers[index].GetOverrideMotion(state);
        }

        static StateMachineBehaviour[] EffectiveBehaviours(AnimatorController source, int index, AnimatorState state)
        {
            if (RootIndex(source.layers, index) < 0) return Array.Empty<StateMachineBehaviour>();
            return source.GetStateEffectiveBehaviours(state, index) ?? Array.Empty<StateMachineBehaviour>();
        }

        sealed class EffectiveSource
        {
            sealed class Layer
            {
                internal int Index;
                internal string Role;
                internal AnimatorStateMachine Machine;
                internal HashSet<string> Properties;
                internal bool VariableWeight;
            }

            readonly AnimatorController source;
            readonly AnimatorControllerLayer[] layers;
            readonly MururuBlinkLipSyncInstallRecord settings;
            readonly string body, mouth;
            readonly ISet<string> unknown;
            readonly List<Layer> writers = new List<Layer>();
            readonly Dictionary<string, Dictionary<string, float>> clipValues = new Dictionary<string, Dictionary<string, float>>();
            readonly string facial;
            readonly AdditionalFx additional;
            bool valid;

            internal EffectiveSource(AnimatorController source, MururuBlinkLipSyncInstallRecord settings,
                string body, string mouth, ISet<string> unknown, AdditionalFx additional = null)
            {
                this.source = source; this.settings = settings; this.body = body; this.mouth = mouth; this.unknown = unknown;
                this.additional = additional;
                layers = source ? source.layers : Array.Empty<AnimatorControllerLayer>();
                facial = FacialInput(source);
                valid = source && Layers.All(name => layers.Count(l => l.name == name) <= 1)
                    && Clip(source, "Facial Idle", null);
                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].name == MururuMouthReturnControllerGenerator.PhaseName) continue;
                    int root = RootIndex(layers, i);
                    if (root < 0 || !layers[root].stateMachine) { valid = false; continue; }
                    var machine = layers[root].stateMachine;
                    var properties = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var sm in Machines(machine))
                        foreach (var state in sm.states) properties.UnionWith(MouthProperties(EffectiveMotion(layers, i, state.state), body, unknown));
                    string role = Layers.Contains(layers[i].name) ? layers[i].name : layers[root].name;
                    writers.Add(new Layer { Index = i, Role = role, Machine = machine, Properties = properties });
                }
                // Only effective behaviours execute. An overridden source behaviour
                // must not disable an unrelated mouth, nor hide an inherited control.
                foreach (var layer in writers)
                    foreach (var sm in Machines(layer.Machine))
                        foreach (var behaviour in sm.behaviours.Concat(sm.states.SelectMany(s => EffectiveBehaviours(source, layer.Index, s.state))))
                            if (behaviour is VRCAnimatorLayerControl control && control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX)
                            {
                                if (control.layer < 0 || control.layer >= layers.Length) { valid = false; continue; }
                                var target = writers.SingleOrDefault(l => l.Index == control.layer);
                                if (target != null && target.Index != 0 && target.Properties.Count != 0
                                    && control.goalWeight != layers[target.Index].defaultWeight) target.VariableWeight = true;
                            }
            }

            internal MururuMouthReturnFxRoutesPlan Capture(IReadOnlyCollection<string> bridgeShapes)
            {
                var routes = new List<MururuMouthReturnFxRoutesRoute>();
                var warnings = new List<string>();
                var unsafeRight = Enumerable.Range(0, 8).Where(v => !SafeAuxiliary("Facial RightHand", "GestureRight", v)).ToArray();
                var unsafeAccessory = Enumerable.Range(0, 9).Where(v => v == 6 || !SafeAuxiliary("Accessory", "Accessory", v)).ToArray();
                foreach (int v in unsafeRight.Where(v => v == 0 || settings.rightLipSyncEnabled[v - 1]))
                    warnings.Add("右手 " + v + ": 口への影響またはFXの割り当てを確定できません。");
                foreach (int v in unsafeAccessory.Where(v => v != 6))
                    warnings.Add("Accessory=" + v + ": 口への影響またはFXの割り当てを確定できません。");
                var initial = new Dictionary<string, float>(StringComparer.Ordinal);
                if (settings.bodyRenderer && settings.bodyRenderer.sharedMesh)
                    foreach (string shape in MururuMouthReturnFxRoutesPose.Properties)
                    {
                        int index = settings.bodyRenderer.sharedMesh.GetBlendShapeIndex(shape);
                        if (index >= 0) initial[shape] = settings.bodyRenderer.GetBlendShapeWeight(index);
                    }
                foreach (string shape in unknown)
                {
                    var renderer = settings.bodyRenderer;
                    int index = renderer && renderer.sharedMesh ? renderer.sharedMesh.GetBlendShapeIndex(shape) : -1;
                    if (index < 0) valid = false;
                    else initial[shape] = renderer.GetBlendShapeWeight(index);
                }
                var contexts = additional == null ? new[] { new Dictionary<int, AdditionalFx.Group>() } : additional.Contexts().ToArray();
                int contextIndex = 0;
                foreach (var actual in contexts)
                {
                int enabledCombinations = 0;
                var contextFailures = new Dictionary<string, int>();
                for (int routeIndex = 0; routeIndex < 17; routeIndex++)
                {
                    bool afk = routeIndex == 16, left = routeIndex < 8;
                    int value = left ? routeIndex : routeIndex - 7;
                    string key = afk ? "AFK" : left ? "Left" + value : "Ex" + value;
                    var guards = new List<MururuMouthReturnFxRoutesCondition> {
                        C("AFK", afk ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot),
                        C("VRCEmote", AnimatorConditionMode.Equals), C("Seated", AnimatorConditionMode.IfNot),
                        C("Accessory", AnimatorConditionMode.NotEqual, 6), C("Accessory", AnimatorConditionMode.Greater, -1),
                        C("Accessory", AnimatorConditionMode.Less, 9), C("GestureRight", AnimatorConditionMode.Greater, -1),
                        C("GestureRight", AnimatorConditionMode.Less, 8)
                    };
                    if (!afk && facial != null) guards.Add(C(facial, AnimatorConditionMode.Equals, left ? 0 : value));
                    if (left) guards.Add(C("GestureLeft", AnimatorConditionMode.Equals, value));
                    foreach (int v in unsafeRight) guards.Add(C("GestureRight", AnimatorConditionMode.NotEqual, v));
                    foreach (int v in unsafeAccessory.Where(v => v != 6)) guards.Add(C("Accessory", AnimatorConditionMode.NotEqual, v));
                    if (additional != null) foreach (var writer in additional.Writers.Where(w => w.Parameter != null))
                        guards.Add(C(writer.Parameter, AnimatorConditionMode.Equals, actual[writer.Index].Id));
                    // The recognized domain remains finite. Additional contexts are
                    // emitted only when actual mouth priority/overrides distinguish them.
                    var candidates = new MururuMouthReturnFxRoutesPose[left ? 1 : 8, afk ? 9 : 1];
                    bool initialized = actual.Values.All(g => g.Id != 0);
                    bool context = initialized && valid && unsafeRight.Length != 8 && unsafeAccessory.Length != 9 && (afk || facial != null);
                    for (int l = 0; l < candidates.GetLength(0); l++)
                        for (int e = 0; e < candidates.GetLength(1); e++)
                        {
                            candidates[l, e] = context ? Evaluate(left ? value : l, afk ? e : left ? 0 : value, afk, initial, actual)
                                : MururuMouthReturnFxRoutesPose.Classify(new Dictionary<string, float>());
                            var diagnostic = bridgeShapes == null ? candidates[l, e] : candidates[l, e].WithGeometry(bridgeShapes);
                            if (initialized && MururuMouthReturnWarning.Enabled(settings, left ? value : l, afk ? e : left ? 0 : value))
                            {
                                enabledCombinations++;
                                if (!diagnostic.Supported)
                                {
                                    string description = MururuMouthReturnWarning.Describe(diagnostic);
                                    if (additional != null && additional.Writers.Count != 0)
                                        contextFailures[description] = contextFailures.TryGetValue(description, out int count) ? count + 1 : 1;
                                    else warnings.Add(MururuMouthReturnWarning.RouteName(key) + ": " + description);
                                }
                            }
                        }
                    AddContexts(routes, contextIndex == 0 ? key : key + "_Actual" + contextIndex, guards, candidates, !left, afk);
                }
                foreach (var failure in contextFailures)
                    warnings.Add(MururuMouthReturnWarning.AdditionalSubject(additional, actual) + ": "
                        + (failure.Value == enabledCombinations ? "この表情では" : "ほかの口の表情との一部の組み合わせで")
                        + "瞬時切り替えになります。" + failure.Key);
                contextIndex++;
                }
                return new MururuMouthReturnFxRoutesPlan(routes, body, mouth, warnings);
            }

            bool SafeAuxiliary(string role, string parameter, int value)
            {
                foreach (var layer in writers.Where(l => l.Role == role))
                {
                    if (layer.Index != 0 && layers[layer.Index].defaultWeight == 0 && !layer.VariableWeight) continue;
                    var state = AssignedStateMachine(layer.Machine, value + " ", parameter, value, false);
                    if (!state) return false;
                    var motion = EffectiveMotion(layers, layer.Index, state);
                    if ((!motion && layers[layer.Index].syncedLayerIndex == -1) || MouthProperties(motion, body, unknown).Count != 0) return false;
                }
                return true;
            }

            AnimatorState Selected(Layer layer, int left, int ex, bool afk)
            {
                AnimatorState state;
                switch (layer.Role)
                {
                    case "Facial Idle": state = AssignedStateMachine(layer.Machine, null, null, 0, false); break;
                    case "Facial LeftHand": state = AssignedStateMachine(layer.Machine, left + " ", "GestureLeft", left, false); break;
                    case "Facial Ex": state = AssignedStateMachine(layer.Machine, ex + " ", facial, ex, false); break;
                    case "Facial AFK":
                        state = AssignedStateMachine(layer.Machine, "AFK", "AFK", 1, true);
                        if (state && !afk) state = layer.Machine.defaultState;
                        break;
                    default: state = AssignedStateMachine(layer.Machine, null, null, 0, false); break;
                }
                return state;
            }

            Dictionary<string, float> Values(AnimationClip clip)
            {
                if (!clip) return null;
                string key = clip.GetInstanceID().ToString();
                if (!clipValues.TryGetValue(key, out var values))
                {
                    values = new Dictionary<string, float>(StringComparer.Ordinal);
                    if (!Read(clip, body, values, unknown)) values = null;
                    clipValues[key] = values;
                }
                return values;
            }

            MururuMouthReturnFxRoutesPose Evaluate(int left, int ex, bool afk, Dictionary<string, float> initial,
                Dictionary<int, AdditionalFx.Group> actual)
            {
                var values = new Dictionary<string, float>(initial, StringComparer.Ordinal);
                var covered = new HashSet<string>(StringComparer.Ordinal);
                foreach (var layer in writers)
                {
                    // These selectors are guarded to values with no mouth writer.
                    if (layer.Role == "Facial RightHand" || layer.Role == "Accessory") continue;
                    var definition = layers[layer.Index];
                    if (layer.Index != 0 && definition.defaultWeight == 0 && !layer.VariableWeight) continue;
                    bool tracked = actual.TryGetValue(layer.Index, out var group);
                    if (!tracked && !Layers.Contains(layer.Role) && !AssignedStateMachine(layer.Machine, null, null, 0, false))
                    {
                        foreach (string p in layer.Properties) values[p] = float.NaN;
                        continue;
                    }
                    var state = Selected(layer, left, ex, afk);
                    var motion = state ? EffectiveMotion(layers, layer.Index, state) : null;
                    var next = tracked ? group.Values : state && ((!motion && definition.syncedLayerIndex != -1)
                        || (motion && MouthProperties(motion, body, unknown).Count == 0))
                        ? new Dictionary<string, float>() : Values(motion as AnimationClip);
                    if (next == null)
                    {
                        // A malformed known selector cannot establish its endpoint.
                        // Subsequent explicit writes can still cover its uncertainty.
                        var affected = Layers.Contains(layer.Role) ? layer.Properties.Concat(MururuMouthReturnFxRoutesPose.Properties) : layer.Properties;
                        foreach (string p in affected) values[p] = float.NaN;
                        continue;
                    }
                    if (tracked && group.Omitted.Count != 0)
                    {
                        next = new Dictionary<string, float>(next);
                        foreach (string p in group.Omitted.Where(p => !covered.Contains(p))) next[p] = float.NaN;
                    }
                    bool additive = definition.blendingMode != AnimatorLayerBlendingMode.Override;
                    float weight = layer.Index == 0 ? 1 : definition.defaultWeight;
                    // AvatarMask filters Transform/humanoid channels, not these
                    // SkinnedMeshRenderer curves (verified with native Animator).
                    bool uncertain = layer.VariableWeight || float.IsNaN(weight)
                        || float.IsInfinity(weight) || weight < 0 || weight > 1;
                    foreach (var pair in next)
                    {
                        if (additive || (uncertain && (!values.TryGetValue(pair.Key, out float previous) || previous != pair.Value))) values[pair.Key] = float.NaN;
                        else if (weight != 1)
                            values[pair.Key] = values.TryGetValue(pair.Key, out float prior) ? prior + weight * (pair.Value - prior) : float.NaN;
                        else values[pair.Key] = pair.Value;
                        if (!additive && !uncertain && weight == 1 && (!tracked || !group.Omitted.Contains(pair.Key))) covered.Add(pair.Key);
                    }
                }
                // Keep active unknown properties for diagnostics; they remain unsupported.
                foreach (string p in values.Keys.Where(p => !MururuMouthReturnFxRoutesPose.Properties.Contains(p) && values[p] == 0).ToArray()) values.Remove(p);
                return MururuMouthReturnFxRoutesPose.Classify(values);
            }

            static bool Same(MururuMouthReturnFxRoutesPose a, MururuMouthReturnFxRoutesPose b)
                => a.Supported && b.Supported && a.Raw.Count == b.Raw.Count && a.Raw.All(p => b.Raw.TryGetValue(p.Key, out float v) && v == p.Value);

            void AddContexts(List<MururuMouthReturnFxRoutesRoute> routes, string key,
                List<MururuMouthReturnFxRoutesCondition> guards, MururuMouthReturnFxRoutesPose[,] candidates, bool variableLeft, bool variableEx)
            {
                int rows = candidates.GetLength(0), columns = candidates.GetLength(1), number = 0;
                var used = new bool[rows, columns];
                for (int row = 0; row < rows; row++)
                    for (int column = 0; column < columns; column++)
                    {
                        var pose = candidates[row, column];
                        if (used[row, column] || !pose.Supported) continue;
                        var selectedColumns = Enumerable.Range(0, columns).Where(c => !used[row, c] && Same(pose, candidates[row, c])).ToArray();
                        var selectedRows = Enumerable.Range(0, rows).Where(r => selectedColumns.All(c => !used[r, c] && Same(pose, candidates[r, c]))).ToArray();
                        var conditions = new List<MururuMouthReturnFxRoutesCondition>(guards);
                        if (variableLeft)
                        {
                            conditions.Add(C("GestureLeft", AnimatorConditionMode.Greater, -1));
                            conditions.Add(C("GestureLeft", AnimatorConditionMode.Less, 8));
                            foreach (int r in Enumerable.Range(0, rows).Except(selectedRows)) conditions.Add(C("GestureLeft", AnimatorConditionMode.NotEqual, r));
                        }
                        if (variableEx && facial != null)
                        {
                            conditions.Add(C(facial, AnimatorConditionMode.Greater, -1));
                            conditions.Add(C(facial, AnimatorConditionMode.Less, 9));
                            foreach (int c in Enumerable.Range(0, columns).Except(selectedColumns)) conditions.Add(C(facial, AnimatorConditionMode.NotEqual, c));
                        }
                        foreach (int r in selectedRows) foreach (int c in selectedColumns) used[r, c] = true;
                        routes.Add(new MururuMouthReturnFxRoutesRoute(number == 0 ? key : key + "_Context" + number, conditions, pose));
                        number++;
                    }
                if (number == 0) routes.Add(new MururuMouthReturnFxRoutesRoute(key, guards, MururuMouthReturnFxRoutesPose.Classify(new Dictionary<string, float>())));
            }
        }
    }
}
