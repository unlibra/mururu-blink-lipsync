using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(MururuBlinkLipSync.Editor.MururuMouthReturnPlugin))]
namespace MururuBlinkLipSync.Editor
{
    internal sealed class MururuMouthReturnPlugin : Plugin<MururuMouthReturnPlugin>
    {
        public override string QualifiedName => "dev.unlibra.mururu-blink-lipsync.mouth-return";
        public override string DisplayName => "Mururu Mouth Return";
        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).BeforePlugin("nadena.dev.modular-avatar")
                .Run("Prepare owned mouth return", ctx => Prepare(ctx.AvatarRootObject, false));
            InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar")
                .Run("Capture merged mouth return", ctx => {
                    if (ctx.AvatarRootObject.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true).Length == 0) return;
                    // Force native commit before touching the controller. No original
                    // native objects may be edited while virtualization is active.
                    ctx.DeactivateExtensionContext<AnimatorServicesContext>();
                    ctx.DeactivateExtensionContext<VirtualControllerContext>();
                    Complete(ctx.AvatarRootObject, ctx.IsTemporaryAsset);
                });
        }

        // Called only for the NDMF build clone. The scene installation and input assets stay untouched.
        internal static void Prepare(GameObject root, bool reportSmooth = true)
        {
            if (!root || EditorUtility.IsPersistent(root)) throw new InvalidOperationException("A build-local avatar is required");
            PrepareSpeechNotes(root);
            foreach (var record in root.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)) Prepare(root, record, reportSmooth);
        }

        internal const string SpeechNotesLayerName = "Facial LipSync";
        internal const string SpeechNotesUnavailable = "Mururu Blink & LipSync: 音符を非表示にできません。標準のFacial LipSyncレイヤーが特定できないため、元のFXを維持します。";

        internal static void PrepareSpeechNotes(GameObject root)
        {
            if (!root || EditorUtility.IsPersistent(root)) throw new InvalidOperationException("A build-local avatar is required");
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (!descriptor || !root.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true)
                .Any(record => record.avatarDescriptor == descriptor && record.hideSpeechNotes)) return;
            var slots = descriptor.baseAnimationLayers;
            var fxIndices = Enumerable.Range(0, slots.Length)
                .Where(i => slots[i].type == VRCAvatarDescriptor.AnimLayerType.FX && !slots[i].isDefault).ToArray();
            if (fxIndices.Length != 1 || !(slots[fxIndices[0]].animatorController is AnimatorController source))
            {
                Debug.LogWarning(SpeechNotesUnavailable, descriptor);
                return;
            }
            var layers = source.layers;
            var indices = Enumerable.Range(0, layers.Length).Where(i => layers[i].name == SpeechNotesLayerName).ToArray();
            // The first Animator layer always has weight 1, regardless of defaultWeight.
            if (indices.Length != 1 || indices[0] == 0)
            {
                Debug.LogWarning(SpeechNotesUnavailable, descriptor);
                return;
            }
            if (layers[indices[0]].defaultWeight == 0f) return;
            var controller = UnityEngine.Object.Instantiate(source);
            controller.name = source.name + " (speech notes hidden)";
            layers = controller.layers;
            layers[indices[0]].defaultWeight = 0f;
            controller.layers = layers;
            slots[fxIndices[0]].animatorController = controller;
            descriptor.baseAnimationLayers = slots;
        }
        static void Prepare(GameObject root, MururuBlinkLipSyncInstallRecord record, bool reportSmooth)
        {
            var merge = record.GetComponent<ModularAvatarMergeAnimator>();
            var owned = record.installedController as AnimatorController;
            var body = record.bodyRenderer; var mouth = record.installedLipSyncMouthRenderer;
            if (!merge || !owned || merge.animator != owned || !body || !mouth || !body.sharedMesh || !mouth.sharedMesh
                || !body.transform.IsChildOf(root.transform) || !mouth.transform.IsChildOf(root.transform)
                || merge.pathMode != MergeAnimatorPathMode.Absolute || merge.layerType != VRCAvatarDescriptor.AnimLayerType.FX) return;
            var layers = owned.layers;
            var indices = Enumerable.Range(0, layers.Length).Where(i => layers[i].name == "MururuBlinkLipSync LipSync"
                || layers[i].name == MururuMouthReturnControllerGenerator.PhaseName).ToArray();
            if (indices.Length != 1) return;
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            var source = descriptor ? descriptor.baseAnimationLayers.Where(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX
                && !l.isDefault).Select(l => l.animatorController).SingleOrDefault() as AnimatorController : null;
            string bodyPath = AnimationUtility.CalculateTransformPath(body.transform, root.transform);
            string mouthPath = AnimationUtility.CalculateTransformPath(mouth.transform, root.transform);
            Mesh mesh = null;
            MururuMouthReturnControllerBundle bundle = null;
            AnimatorController controller = null;
            try
            {
                bool smooth = MururuMouthReturnMeshAssembler.TryAssemble(body.sharedMesh, mouth.sharedMesh, out mesh, out var reason);
                string[] shapes = smooth ? Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName)
                    .Where(n => n.StartsWith("MBLS_Bridge_", StringComparison.Ordinal) && n != MururuMouthReturnMeshAssembler.ClockShapeName).ToArray()
                    : Array.Empty<string>();
                bundle = MururuMouthReturnControllerGenerator.Generate(source, record, bodyPath, mouthPath, shapes,
                    smooth ? MururuMouthReturnTargetGenerator.UnknownMouthShapes(body.sharedMesh) : new HashSet<string>());
                controller = UnityEngine.Object.Instantiate(owned); controller.name = "MBLS Mouth Return (build)";
                layers[indices[0]] = bundle.PhaseLayer; controller.layers = layers;
                Add(controller, "Voice", AnimatorControllerParameterType.Float);
                Add(controller, "GestureLeft", AnimatorControllerParameterType.Int);
                Add(controller, "GestureRight", AnimatorControllerParameterType.Int);
                Add(controller, record.facialExParameterName, AnimatorControllerParameterType.Int);
                if (smooth)
                {
                    foreach (var condition in bundle.Plan.Routes.SelectMany(r => r.Conditions))
                        Add(controller, condition.Parameter,
                            condition.Mode == AnimatorConditionMode.If || condition.Mode == AnimatorConditionMode.IfNot
                                ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Int);
                }
                // All preparation succeeded before publishing any reference.
                if (mesh) { mouth.sharedMesh = mesh; mouth.localBounds = mesh.bounds; }
                merge.animator = controller; record.installedController = controller;
                if (reportSmooth || !smooth) MururuMouthReturnWarning.Report(record, reason, bundle.Plan);
            }
            catch
            {
                bundle?.Dispose();
                if (controller) UnityEngine.Object.DestroyImmediate(controller);
                if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                // Fail the build instead of adopting a partially prepared smooth controller.
                throw;
            }
        }
        internal static void Complete(GameObject root, Func<UnityEngine.Object, bool> isBuildAsset)
        {
            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            var source = descriptor ? descriptor.baseAnimationLayers.Where(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX
                && !l.isDefault).Select(l => l.animatorController).SingleOrDefault() as AnimatorController : null;
            if (!source) return;
            bool copied = false;
            foreach (var record in root.GetComponentsInChildren<MururuBlinkLipSyncInstallRecord>(true))
            {
                var mouth = record.installedLipSyncMouthRenderer;
                var body = record.bodyRenderer;
                if (!mouth || !body || !mouth.sharedMesh || !body.sharedMesh) continue;
                var shapes = Enumerable.Range(0, mouth.sharedMesh.blendShapeCount).Select(mouth.sharedMesh.GetBlendShapeName)
                    .Where(n => n.StartsWith("MBLS_Bridge_", StringComparison.Ordinal) && n != MururuMouthReturnMeshAssembler.ClockShapeName).ToArray();
                if (shapes.Length == 0) continue;
                var layers = source.layers;
                var indices = Enumerable.Range(0, layers.Length).Where(i => layers[i].name == MururuMouthReturnControllerGenerator.PhaseName).ToArray();
                if (indices.Length != 1) throw new InvalidOperationException("Merged mouth-return layer is ambiguous");
                if (!copied)
                {
                    if (!isBuildAsset(source)) throw new InvalidOperationException("Merged mouth return requires an NDMF build controller");
                    // NDMF reuses its virtual cache when the native Controller identity
                    // equals LastCommit. A new wrapper makes later activation re-clone
                    // our changes. The shared states here are already NDMF build copies.
                    var replacement = new AnimatorController { name = source.name + " (mouth return)",
                        parameters = source.parameters, layers = source.layers };
                    var slots = descriptor.baseAnimationLayers;
                    for (int i = 0; i < slots.Length; i++)
                        if (slots[i].type == VRCAvatarDescriptor.AnimLayerType.FX && !slots[i].isDefault) slots[i].animatorController = replacement;
                    descriptor.baseAnimationLayers = slots; source = replacement; copied = true;
                }
                string bodyPath = AnimationUtility.CalculateTransformPath(body.transform, root.transform);
                string mouthPath = AnimationUtility.CalculateTransformPath(mouth.transform, root.transform);
                var unknown = MururuMouthReturnTargetGenerator.UnknownMouthShapes(body.sharedMesh);
                var additional = MururuMouthReturnFxRoutesPlan.AdditionalFx.Capture(source, bodyPath, unknown);
                string suppression = FacialSuppression(record, layers[indices[0]].stateMachine);
                var bundle = MururuMouthReturnControllerGenerator.Generate(source, record, bodyPath, mouthPath, shapes, unknown, additional, suppression);
                additional.Instrument(source, isBuildAsset);
                layers[indices[0]] = bundle.PhaseLayer; source.layers = layers;
                foreach (var condition in bundle.Plan.Routes.SelectMany(r => r.Conditions))
                    Add(source, condition.Parameter, condition.Mode == AnimatorConditionMode.If || condition.Mode == AnimatorConditionMode.IfNot
                        ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Int);
                MururuMouthReturnWarning.Report(record, MururuMouthReturnUnsupportedReason.None, bundle.Plan);
            }
        }
        static string FacialSuppression(MururuBlinkLipSyncInstallRecord record, AnimatorStateMachine phase)
        {
            int facial = record.facialLipSyncEnabled.Count(enabled => !enabled);
            if (facial == 0) return null;
            int hands = record.leftLipSyncEnabled.Count(enabled => !enabled) + record.rightLipSyncEnabled.Count(enabled => !enabled);
            var speaking = phase.states.Where(s => s.state.name == "Speaking").Select(s => s.state).Single();
            // Our generated transition order is left, right, facial. MA has already
            // remapped these conditions, including a distinct user stop parameter.
            var conditions = speaking.transitions.Where(t => t.conditions.Length == 1
                && t.conditions[0].mode == AnimatorConditionMode.Equals).Select(t => t.conditions[0]).ToArray();
            if (conditions.Length != hands + facial) throw new InvalidOperationException("Ambiguous built LipSync suppressions");
            var names = conditions.Skip(hands).Select(c => c.parameter).Distinct().ToArray();
            if (names.Length != 1 || string.IsNullOrWhiteSpace(names[0])) throw new InvalidOperationException("Ambiguous built facial suppression");
            return names[0];
        }
        static void Add(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            var matches = controller.parameters.Where(p => p.name == name).ToArray();
            if (matches.Length == 0) controller.AddParameter(name, type);
            else if (matches.Length != 1 || matches[0].type != type) throw new InvalidOperationException("Ambiguous mouth return parameter: " + name);
        }
    }
}
