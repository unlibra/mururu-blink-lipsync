using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MururuBlinkLipSync.Editor
{
    internal sealed partial class MururuMouthReturnFxRoutesPlan
    {
        // Captured only from the MA/NDMF committed build copy. No graph is simulated:
        // each executing state reports its mouth-write group on entry.
        internal sealed class AdditionalFx
        {
            internal sealed class Group
            {
                internal int Id;
                internal Dictionary<string, float> Values;
                internal readonly HashSet<string> Omitted = new HashSet<string>();
                internal readonly List<AnimatorState> States = new List<AnimatorState>();
            }
            internal sealed class Writer
            {
                internal int Index;
                internal string Name;
                internal string Parameter;
                internal readonly List<Group> Groups = new List<Group>();
            }
            internal readonly List<Writer> Writers = new List<Writer>();

            internal static AdditionalFx Capture(AnimatorController controller, string body, ISet<string> unknown)
            {
                var result = new AdditionalFx();
                var layers = controller.layers;
                var names = new HashSet<string>(controller.parameters.Select(p => p.name));
                var controllerProperties = new HashSet<string>();
                var controlled = new HashSet<int>();
                for (int layer = 0; layer < layers.Length; layer++)
                {
                    int graph = RootIndex(layers, layer);
                    if (graph < 0) continue;
                    foreach (var sm in Machines(layers[graph].stateMachine))
                    {
                        foreach (var state in sm.states)
                            controllerProperties.UnionWith(MouthProperties(EffectiveMotion(layers, layer, state.state), body, unknown));
                        foreach (var control in sm.behaviours.Concat(sm.states.SelectMany(s => EffectiveBehaviours(controller, layer, s.state)))
                            .OfType<VRCAnimatorLayerControl>())
                            if (control.playable == VRC_AnimatorLayerControl.BlendableLayer.FX && control.goalWeight != 0) controlled.Add(control.layer);
                    }
                }
                for (int i = 0; i < layers.Length; i++)
                {
                    if (Layers.Contains(layers[i].name) || layers[i].name == MururuMouthReturnControllerGenerator.PhaseName) continue;
                    if (i != 0 && layers[i].defaultWeight == 0 && !controlled.Contains(i)) continue;
                    int root = RootIndex(layers, i);
                    if (root < 0 || !layers[root].stateMachine) continue;
                    var states = Machines(layers[root].stateMachine).SelectMany(sm => sm.states).Select(s => s.state).ToArray();
                    var properties = new HashSet<string>(states.SelectMany(s => MouthProperties(EffectiveMotion(layers, i, s), body, unknown)));
                    if (properties.Count == 0) continue;
                    var writer = new Writer { Index = i, Name = layers[i].name };
                    foreach (var state in states)
                    {
                        var values = ConstantMouth(EffectiveMotion(layers, i, state), body, unknown);
                        // WD-off omissions inherit an animated lower channel. Without
                        // that coverage they can retain history; composition decides.
                        if (state.writeDefaultValues)
                            foreach (string property in controllerProperties)
                                if (!values.ContainsKey(property)) values[property] = float.NaN;
                        var group = writer.Groups.FirstOrDefault(g => Equal(g.Values, values));
                        if (group == null) { group = new Group { Id = writer.Groups.Count + 1, Values = values }; writer.Groups.Add(group); }
                        if (!state.writeDefaultValues) group.Omitted.UnionWith(properties.Except(values.Keys));
                        group.States.Add(state);
                    }
                    if (writer.Groups.Count == 0) continue;
                    if (writer.Groups.Count > 1)
                    {
                        string prefix = "MBLS_Return_Actual" + i, name = prefix;
                        for (int suffix = 1; names.Contains(name); suffix++) name = prefix + "_" + suffix;
                        names.Add(name); writer.Parameter = name;
                        writer.Groups.Insert(0, new Group { Id = 0, Values = properties.ToDictionary(p => p, p => float.NaN) });
                    }
                    result.Writers.Add(writer);
                }
                return result;
            }

            internal void Instrument(AnimatorController controller, Func<UnityEngine.Object, bool> isBuildAsset)
            {
                foreach (var writer in Writers.Where(w => w.Parameter != null))
                {
                    controller.AddParameter(writer.Parameter, AnimatorControllerParameterType.Int);
                    foreach (var group in writer.Groups)
                        foreach (var state in group.States)
                        {
                            if (!isBuildAsset(state)) throw new InvalidOperationException("Actual-state drivers require NDMF build copies");
                            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                            driver.localOnly = false;
                            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter {
                                name = writer.Parameter, type = VRC_AvatarParameterDriver.ChangeType.Set, value = group.Id });
                        }
                }
            }

            internal IEnumerable<Dictionary<int, Group>> Contexts()
            {
                IEnumerable<Dictionary<int, Group>> contexts = new[] { new Dictionary<int, Group>() };
                foreach (var writer in Writers)
                    contexts = contexts.SelectMany(context => writer.Groups.Select(group => {
                        var copy = new Dictionary<int, Group>(context); copy.Add(writer.Index, group); return copy;
                    })).ToArray();
                return contexts;
            }
            static bool Equal(Dictionary<string, float> a, Dictionary<string, float> b)
                => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out float value) && p.Value.Equals(value));
        }

        static Dictionary<string, float> ConstantMouth(Motion motion, string body, ISet<string> unknown, HashSet<Motion> ancestors = null)
        {
            ancestors = ancestors == null ? new HashSet<Motion>() : new HashSet<Motion>(ancestors);
            if (motion && !ancestors.Add(motion)) return MouthProperties(motion, body, unknown).ToDictionary(p => p, p => float.NaN);
            if (motion is BlendTree tree)
            {
                // Direct weights need not sum to one, even with normalization on.
                // Equal child curves therefore do not establish a constant mouth.
                if (tree.blendType == BlendTreeType.Direct)
                    return MouthProperties(tree, body, unknown).ToDictionary(p => p, p => float.NaN);
                var children = tree.children.Select(c => ConstantMouth(c.motion, body, unknown, ancestors)).ToArray();
                var properties = children.SelectMany(c => c.Keys).Distinct();
                return properties.ToDictionary(p => p, p => children.Length != 0 && children.All(c => c.ContainsKey(p))
                    && children.All(c => c[p].Equals(children[0][p])) ? children[0][p] : float.NaN);
            }
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            if (!(motion is AnimationClip clip)) return result;
            foreach (string property in MouthProperties(clip, body, unknown))
            {
                var binding = EditorCurveBinding.FloatCurve(body, typeof(SkinnedMeshRenderer), "blendShape." + property);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                float value = curve != null && curve.length != 0 ? curve.keys[0].value : float.NaN;
                result[property] = curve != null && curve.length != 0 && !float.IsNaN(value) && !float.IsInfinity(value)
                    && curve.keys.All(k => k.value == value && k.inTangent == 0 && k.outTangent == 0
                        && !float.IsNaN(k.time) && !float.IsInfinity(k.time)) ? value : float.NaN;
            }
            return result;
        }
    }
}
