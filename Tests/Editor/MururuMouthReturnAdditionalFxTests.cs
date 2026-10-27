using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(MururuBlinkLipSync.Editor.Tests.MouthReturnReactivationTestPlugin))]
namespace MururuBlinkLipSync.Editor.Tests
{
    // Re-enter animator services after the product pass. This catches stale virtual
    // caches that would otherwise silently discard native additions later in a build.
    internal sealed class MouthReturnReactivationTestPlugin : Plugin<MouthReturnReactivationTestPlugin>
    {
        public override string QualifiedName => "dev.unlibra.mururu-blink-lipsync.tests.reactivation";
        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).AfterPlugin("dev.unlibra.mururu-blink-lipsync.mouth-return")
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                    sequence.Run("Revirtualize mouth return regression", ctx => { }));
        }
    }
    internal sealed class MururuMouthReturnAdditionalFxTests : MururuAvatarTestBase
    {
        readonly List<Object> temporary = new List<Object>();
        T Keep<T>(T value) where T : Object { temporary.Add(value); return value; }
        [TearDown] public void CleanupAdditional()
        {
            foreach (var value in temporary.AsEnumerable().Reverse())
                if (value && !EditorUtility.IsPersistent(value)) Object.DestroyImmediate(value);
            temporary.Clear();
        }
        AnimatorController Additional(out AnimatorState first, out AnimatorState second, bool full = true)
        {
            var controller = Keep(new AnimatorController { name = "FaceEmo fixture" });
            var sm = Keep(new AnimatorStateMachine { name = "Actual mouth" });
            first = Keep(sm.AddState("Current")); second = Keep(sm.AddState("Requested"));
            first.writeDefaultValues = second.writeDefaultValues = false;
            sm.defaultState = first;
            first.motion = Mouth("mouth_excited", 100, full); second.motion = Mouth("mouth_pout", 100, full);
            controller.AddParameter("SYNC_EM_EMOTE", AnimatorControllerParameterType.Int);
            var transition = first.AddTransition(second); transition.hasExitTime = false;
            transition.AddCondition(AnimatorConditionMode.Equals, 2, "SYNC_EM_EMOTE");
            controller.layers = new[] { new AnimatorControllerLayer { name = "Additional mouth", stateMachine = sm,
                defaultWeight = 1, syncedLayerIndex = -1 } };
            return controller;
        }
        AnimationClip Mouth(string property, float value, bool full)
        {
            var clip = Keep(new AnimationClip { name = property });
            foreach (string shape in full ? MururuMouthReturnFxRoutesPose.Properties : new[] { property })
                clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape,
                    AnimationCurve.Constant(0, .1f, shape == property ? value : 0));
            return clip;
        }
        AnimatorController Combined(AnimatorController extra)
        {
            var standard = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Lethal Cats/Mururu/Animation/FX/Mururu_FX.controller");
            Assert.That(standard, Is.Not.Null);
            var copy = Keep(Object.Instantiate(standard)); copy.layers = copy.layers.Concat(extra.layers).ToArray();
            return copy;
        }
        MururuMouthReturnFxRoutesPlan Plan(AnimatorController source, MururuBlinkLipSyncInstallRecord record,
            out MururuMouthReturnFxRoutesPlan.AdditionalFx extra)
        {
            extra = MururuMouthReturnFxRoutesPlan.AdditionalFx.Capture(source, "Body", new HashSet<string>());
            return MururuMouthReturnFxRoutesPlan.Capture(source, record, "Body", "Mouth", new HashSet<string>(), null, extra);
        }
        [Test]
        public void ActualState_DistinguishesHeldExpressionFromSelectionAndUninitialized()
        {
            var record = Install(SpawnMururu("Actual state"));
            var additional = Additional(out var current, out var requested);
            var source = Combined(additional);
            var plan = Plan(source, record, out var captured);
            Assert.That(captured.Writers.Count, Is.EqualTo(1));
            var writer = captured.Writers.Single();
            var currentId = writer.Groups.Single(g => g.States.Contains(current)).Id;
            var requestedId = writer.Groups.Single(g => g.States.Contains(requested)).Id;
            var supported = plan.Routes.Where(r => r.Pose.Supported).ToArray();
            Assert.That(supported.Length, Is.EqualTo(34));
            Assert.That(plan.Warnings, Is.Empty, "The safe startup fallback is not a permanent content warning");
            Assert.That(supported.All(r => !r.Conditions.Any(c => c.Parameter == "SYNC_EM_EMOTE")), Is.True);
            Assert.That(supported.Where(r => r.Conditions.Any(c => c.Parameter == writer.Parameter && c.Threshold == currentId))
                .All(r => r.Pose.Raw["mouth_excited"] == 100), Is.True);
            Assert.That(supported.Where(r => r.Conditions.Any(c => c.Parameter == writer.Parameter && c.Threshold == requestedId))
                .All(r => r.Pose.Raw["mouth_pout"] == 100), Is.True);
            Assert.That(plan.Routes.Where(r => r.Conditions.Any(c => c.Parameter == writer.Parameter && c.Threshold == 0))
                .Any(r => r.Pose.Supported), Is.False);
            captured.Instrument(source, o => temporary.Contains(o));
            foreach (var state in new[] { current, requested })
            {
                var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().Single(); Keep(driver);
                Assert.That(driver.localOnly, Is.False);
                Assert.That(driver.parameters.Single().name, Is.EqualTo(writer.Parameter));
                Assert.That(driver.parameters.Single().type, Is.EqualTo(VRC_AvatarParameterDriver.ChangeType.Set));
            }
        }
        [Test]
        public void PartialWrites_UseLowerLayerComponentsAndRejectIncompatibleCombinations()
        {
            var record = Install(SpawnMururu("Partial writes"));
            var additional = Additional(out var current, out var requested, false);
            var source = Combined(additional);
            var plan = Plan(source, record, out var captured);
            Assert.That(plan.Routes.Where(r => r.Key.StartsWith("Left0") && r.Pose.Supported).Count(), Is.EqualTo(2),
                "Native WD-off endpoints use the lower mouth for omitted curves, regardless of prior state");
            Assert.That(plan.Routes.Any(r => !r.Pose.Supported), Is.True,
                "A partial special mouth combined with another lower mouth must still fall back");
            Assert.That(plan.Warnings, Is.Not.Empty);
            Assert.That(plan.Warnings.All(w => w.Contains("一部の組み合わせ")), Is.True);
        }
        [Test]
        public void EyeOnlyState_InMouthWritingLayer_UsesEveryLowerMouthEndpoint()
        {
            var record = Install(SpawnMururu("Eye endpoint"));
            var additional = Additional(out var current, out var requested);
            var eye = Keep(new AnimationClip { name = "Eye only" });
            eye.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_close", AnimationCurve.Constant(0, 1, 100));
            requested.motion = eye;
            var source = Combined(additional);
            var baseline = MururuMouthReturnFxRoutesPlan.Capture(Combined(Keep(new AnimatorController())), record,
                "Body", "Mouth", new HashSet<string>());
            var plan = Plan(source, record, out var captured);
            var writer = captured.Writers.Single();
            int id = writer.Groups.Single(g => g.States.Contains(requested)).Id;
            var eyes = plan.Routes.Where(r => r.Conditions.Any(c => c.Parameter == writer.Parameter && c.Threshold == id)).ToArray();
            Assert.That(eyes.Length, Is.EqualTo(17));
            foreach (var route in eyes)
            {
                Assert.That(route.Pose.Supported, Is.True);
                CollectionAssert.AreEquivalent(baseline.Routes.Single(r => r.Key == route.Key.Split(new[] { "_Actual" }, StringSplitOptions.None)[0]).Pose.Raw,
                    route.Pose.Raw, route.Key);
            }
            Assert.That(plan.Warnings, Is.Empty);
        }
        [TestCase("default", false)] [TestCase("default", true)]
        [TestCase("mouth_triangle", false)] [TestCase("mouth_triangle", true)]
        [TestCase("none", false)] [TestCase("none", true)]
        public void NativeWdOff_OmissionsInheritAnimatedLowerChannelsOtherwiseRetainHistory(string lower, bool partial)
        {
            var controller = Additional(out var current, out var target);
            var eye = Keep(new AnimationClip());
            eye.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_close", AnimationCurve.Constant(0, 1, 100));
            target.motion = partial ? Mouth("mouth_pout", 100, false) : eye;
            var lowerMachine = Keep(new AnimatorStateMachine());
            var lowerState = Keep(lowerMachine.AddState("Lower")); lowerMachine.defaultState = lowerState;
            lowerState.motion = lower == "none" ? eye : Mouth(lower, 100, true); lowerState.writeDefaultValues = false;
            controller.layers = new[] { new AnimatorControllerLayer { name = "Lower", stateMachine = lowerMachine,
                defaultWeight = 1, syncedLayerIndex = -1 }, controller.layers.Single() };
            var root = Keep(new GameObject("Native omission")); var child = new GameObject("Body"); child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>(); var mesh = Keep(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (string p in MururuMouthReturnFxRoutesPose.Properties.Concat(new[] { "eye_close" }))
                mesh.AddBlendShapeFrame(p, 100, new Vector3[3], null, null);
            renderer.sharedMesh = mesh;
            var animator = root.AddComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller; animator.Rebind();
            try
            {
                animator.Play(current.name, 1); for (int i = 0; i < 10; i++) animator.Update(.02f);
                animator.CrossFadeInFixedTime(target.name, .16f, 1); for (int i = 0; i < 4; i++) animator.Update(.02f);
                Assert.That(animator.IsInTransition(1), Is.True);
                Assert.That(renderer.GetBlendShapeWeight(mesh.GetBlendShapeIndex("mouth_excited")), Is.EqualTo(lower == "none" ? 100 : 50).Within(.02f));
                for (int i = 0; i < 20; i++) animator.Update(.02f);
                Assert.That(animator.IsInTransition(1), Is.False);
                foreach (var p in new[] { "mouth_excited", "mouth_pout", "mouth_triangle" })
                    Assert.That(renderer.GetBlendShapeWeight(mesh.GetBlendShapeIndex(p)),
                        Is.EqualTo(p == "mouth_excited" && lower == "none" || p == "mouth_pout" && partial || p == lower ? 100 : 0).Within(.02f), p);
            }
            finally { animator.runtimeAnimatorController = null; }
        }
        [Test]
        public void FaceEmoWarnings_GroupExpressionCauseWithoutRepeatingUnverifiedHandAssignments()
        {
            var record = Install(SpawnMururu("Readable warning"));
            var additional = Additional(out var current, out var requested);
            var definitions = additional.layers; definitions[0].name = "[ USER EDIT ] FACE EMOTE PLAYER"; additional.layers = definitions;
            current.name = "困り顔";
            ((AnimationClip)current.motion).SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_excited", AnimationCurve.Linear(0, 0, 1, 100));
            var plan = Plan(Combined(additional), record, out var captured);
            Assert.That(plan.Warnings.Count, Is.EqualTo(1));
            Assert.That(plan.Warnings.Single(), Does.Contain("FaceEmo 「困り顔」").And.Contain("瞬時切り替え").And.Contain("変化"));
            TestContext.WriteLine(plan.Warnings.Single());
            Assert.That(plan.Warnings.Single(), Does.Not.Contain("左手").And.Not.Contain("Facial EX").And.Not.Contain("NonFinite")
                .And.Not.Contain("NaN").And.Not.Contain("MBLS_Return").And.Not.Contain("FACE EMOTE PLAYER"));
            current.motion = null; current.writeDefaultValues = true;
            plan = Plan(Combined(additional), record, out captured);
            Assert.That(plan.Warnings.Count, Is.EqualTo(1));
            Assert.That(plan.Warnings.Single(), Does.Contain("困り顔").And.Not.Contain("内部制御状態"));
            current.name = "Facial_idle";
            plan = Plan(Combined(additional), record, out captured);
            Assert.That(plan.Warnings.Single(), Does.Contain("通常の表情").And.Not.Contain("内部制御状態"));
            current.name = "in OVERRIDE"; current.motion = Keep(Object.Instantiate((AnimationClip)requested.motion));
            // Force an actual invalid written curve even though this internal state has a clip.
            ((AnimationClip)current.motion).SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_pout", AnimationCurve.Linear(0, 0, 1, 100));
            plan = Plan(Combined(additional), record, out captured);
            Assert.That(plan.Warnings.Single(), Does.Contain("FaceEmoの内部制御状態").And.Not.Contain("in OVERRIDE"));
            TestContext.WriteLine(plan.Warnings.Single());
        }
        [Test]
        public void EqualMouthBlendTree_IgnoresChangingEyeCurves()
        {
            var record = Install(SpawnMururu("Tree"));
            var additional = Additional(out var current, out var requested);
            var tree = Keep(new BlendTree());
            var a = Mouth("mouth_excited", 100, true); var b = Mouth("mouth_excited", 100, true);
            b.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_close", AnimationCurve.Linear(0, 0, 1, 100));
            tree.children = new[] { new ChildMotion { motion = a, timeScale = 1 }, new ChildMotion { motion = b, timeScale = 1 } };
            current.motion = tree;
            var plan = Plan(Combined(additional), record, out var captured);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(34));
        }
        [TestCase(false)] [TestCase(true)]
        public void DirectBlendTree_OnlyFallsBackForItsStateAndAllowsLaterFullOverwrite(bool normalized)
        {
            var record = Install(SpawnMururu("Direct tree"));
            var additional = Additional(out var current, out var requested);
            var tree = Keep(new BlendTree { blendType = BlendTreeType.Direct });
            tree.children = new[] {
                new ChildMotion { motion = Mouth("mouth_excited", 100, true), timeScale = 1, directBlendParameter = "DirectA" },
                new ChildMotion { motion = Mouth("mouth_excited", 100, true), timeScale = 1, directBlendParameter = "DirectB" }
            };
            var serialized = new SerializedObject(tree);
            var normalize = serialized.FindProperty("m_NormalizedBlendValues");
            Assert.That(normalize, Is.Not.Null); normalize.boolValue = normalized; serialized.ApplyModifiedPropertiesWithoutUndo();
            current.motion = tree;
            var source = Combined(additional);
            var plan = Plan(source, record, out var captured);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17),
                "Direct weights can sum to zero, one, or more; equal children do not establish a constant mouth");
            Assert.That(plan.Routes.Where(r => r.Pose.Supported).All(r => r.Pose.Raw["mouth_pout"] == 100), Is.True);
            Assert.That(plan.Warnings, Is.Not.Empty);
            Assert.That(plan.Warnings.All(w => w.Contains("Additional mouth") && w.Contains("Current")), Is.True);
            var later = Additional(out var first, out var second);
            first.motion = second.motion = Mouth("mouth_triangle", 100, true);
            var definitions = later.layers; definitions[0].name = "Later full mouth"; later.layers = definitions;
            source.layers = source.layers.Concat(later.layers).ToArray();
            plan = Plan(source, record, out captured);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(34));
            Assert.That(plan.Routes.Where(r => r.Pose.Supported).All(r => r.Pose.Raw["mouth_triangle"] == 100), Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
        [Test]
        public void VariableMouth_OnlyFallsBackForItsOwnActualState()
        {
            var record = Install(SpawnMururu("Variable"));
            var additional = Additional(out var current, out var requested);
            ((AnimationClip)requested.motion).SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_pout", AnimationCurve.Linear(0, 0, 1, 100));
            var plan = Plan(Combined(additional), record, out var captured);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            Assert.That(plan.Warnings, Is.Not.Empty);
            Assert.That(plan.Warnings.All(w => w.Contains("Additional mouth") && w.Contains("Requested")), Is.True);
        }
        [Test]
        public void LaterFullWriter_RecoversEarlierUnknownMouthComponents()
        {
            var record = Install(SpawnMururu("Overwrite recovery"));
            var prior = Additional(out var current, out var requested);
            ((AnimationClip)requested.motion).SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_pout", AnimationCurve.Linear(0, 0, 1, 100));
            var later = Additional(out var first, out var second);
            first.motion = second.motion = Mouth("mouth_triangle", 100, true);
            var definitions = later.layers; definitions[0].name = "Later full mouth"; later.layers = definitions;
            var source = Combined(prior); source.layers = source.layers.Concat(later.layers).ToArray();
            var plan = Plan(source, record, out var captured);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(34));
            Assert.That(plan.Routes.Where(r => r.Pose.Supported).All(r => r.Pose.Raw["mouth_triangle"] == 100), Is.True);
            Assert.That(plan.Warnings, Is.Empty);
        }
        [Test]
        public void EyeOnlyGraph_DoesNotAddActualStateParameters()
        {
            var record = Install(SpawnMururu("Eyes"));
            var additional = Additional(out var current, out var requested);
            var eye = Keep(new AnimationClip()); eye.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_close", AnimationCurve.Linear(0, 0, 1, 100));
            current.motion = requested.motion = eye;
            var plan = Plan(Combined(additional), record, out var captured);
            Assert.That(captured.Writers, Is.Empty);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
        }
        [Test]
        public void DisabledMouthLayer_DoesNotRequireAnUninitializedStateId()
        {
            var record = Install(SpawnMururu("Disabled writer"));
            var source = Combined(Additional(out var first, out var second));
            var definitions = source.layers; definitions.Last().defaultWeight = 0; source.layers = definitions;
            var plan = Plan(source, record, out var captured);
            Assert.That(captured.Writers, Is.Empty);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            Assert.That(plan.Warnings, Is.Empty);
        }
        [Test]
        public void RealMaBuild_PreservesRenamedFacialLipSyncSuppression()
        {
            var avatar = SpawnMururu("Remapped stop"); var record = Install(avatar);
            record.facialLipSyncEnabled[1] = false;
            var config = record.gameObject.AddComponent<ModularAvatarParameters>();
            config.parameters.Add(new ParameterConfig { nameOrPrefix = record.facialExParameterName,
                remapTo = "ActualFacialStop", syncType = ParameterSyncType.NotSynced });
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            var errors = ErrorReport.CaptureErrors(() => AvatarProcessor.ProcessAvatar(clone));
            Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
            var final = (AnimatorController)clone.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var speaking = final.layers.Single(l => l.name == MururuMouthReturnControllerGenerator.PhaseName).stateMachine.states
                .Single(s => s.state.name == "Speaking").state;
            Assert.That(speaking.transitions.Any(t => t.conditions.Any(c => c.parameter == "ActualFacialStop"
                && c.mode == AnimatorConditionMode.Equals && c.threshold == 2)), Is.True);
            Assert.That(speaking.transitions.Any(t => t.conditions.Any(c => c.parameter == record.facialExParameterName)), Is.False);
            Assert.That(record.facialExParameterName, Is.Not.EqualTo("ActualFacialStop"));
        }
        [Test]
        public void RealMaBuild_ClonesDriversAndFinalClipsWithoutAccumulation()
        {
            var avatar = SpawnMururu("MA actual state"); var record = Install(avatar);
            var additional = Additional(out var current, out var requested);
            var holder = new GameObject("Additional"); holder.transform.SetParent(avatar.transform, false);
            var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
            merge.animator = additional; merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
            merge.pathMode = MergeAnimatorPathMode.Absolute; merge.layerPriority = 0;
            string input = EditorJsonUtility.ToJson(additional), stateInput = EditorJsonUtility.ToJson(current);
            var expressionParameters = avatar.expressionParameters;
            var originalMesh = record.installedLipSyncMouthRenderer.sharedMesh;
            for (int run = 0; run < 2; run++)
            {
                var clone = Keep(Object.Instantiate(avatar.gameObject));
                var errors = ErrorReport.CaptureErrors(() => AvatarProcessor.ProcessAvatar(clone));
                Assert.That(errors.Where(e => e.TheError.Severity >= ErrorSeverity.Error), Is.Empty);
                var final = (AnimatorController)clone.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                    .Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                Assert.That(final, Is.Not.SameAs(additional));
                var state = final.layers.Single(l => l.name == "Additional mouth").stateMachine.states.Single(s => s.state.name == "Current").state;
                Assert.That(state, Is.Not.SameAs(current));
                Assert.That(state.behaviours.OfType<VRCAvatarParameterDriver>().Count(), Is.EqualTo(1));
                var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().Single();
                Assert.That(driver.localOnly, Is.False);
                string parameter = driver.parameters.Single().name; float id = driver.parameters.Single().value;
                Assert.That(final.parameters.Count(p => p.name == parameter && p.type == AnimatorControllerParameterType.Int), Is.EqualTo(1));
                var phase = final.layers.Single(l => l.name == MururuMouthReturnControllerGenerator.PhaseName).stateMachine;
                var wait = phase.states.Single(s => s.state.name == "Wait").state;
                var transition = wait.transitions.First(t => t.conditions.Any(c => c.parameter == parameter && c.threshold == id));
                var clip = (AnimationClip)transition.destinationState.motion;
                var binding = AnimationUtility.GetCurveBindings(clip).Single(b => b.propertyName == "blendShape." + MururuMouthReturnMeshAssembler.TargetShapeName("mouth_excited"));
                Assert.That(AnimationUtility.GetEditorCurve(clip, binding).keys.Last().value, Is.EqualTo(100));
                Assert.That(clip.length, Is.EqualTo(.08f).Within(.00001f));
                Assert.That(((AnimationClip)wait.motion).length, Is.EqualTo(.08f).Within(.00001f));
                Assert.That(clone.GetComponent<VRCAvatarDescriptor>().expressionParameters.parameters.Any(p => p.name == parameter), Is.False);
                Assert.That(EditorJsonUtility.ToJson(additional), Is.EqualTo(input));
                Assert.That(EditorJsonUtility.ToJson(current), Is.EqualTo(stateInput));
                Assert.That(current.behaviours, Is.Empty);
                Assert.That(record.installedLipSyncMouthRenderer.sharedMesh, Is.SameAs(originalMesh));
                Assert.That(avatar.expressionParameters, Is.SameAs(expressionParameters));
            }
        }
    }
}
