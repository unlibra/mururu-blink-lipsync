using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Object = UnityEngine.Object;
using nadena.dev.ndmf;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuMouthReturnRevisedLookupTests
    {
        const string SourcePath = "Assets/Lethal Cats/Mururu/Animation/FX/Mururu_FX.controller";
        readonly List<Object> owned = new List<Object>();
        AnimatorController source, input;
        AnimatorStateMachine left;
        AnimationClip originalClip;
        string originalClipJson;
        byte[] sourceBytes;
        MururuBlinkLipSyncInstallRecord settings;
        T Keep<T>(T value) where T : Object { owned.Add(value); return value; }
        AnimatorState Left(int value) => left.states.Single(s => s.state.name.StartsWith(value + " ", StringComparison.Ordinal)).state;
        MururuMouthReturnFxRoutesPlan Capture() => MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string>());

        [SetUp] public void StandardBaselineAndIndependentInputs()
        {
            source = AssetDatabase.LoadAssetAtPath<AnimatorController>(SourcePath);
            Assert.That(source, Is.Not.Null);
            sourceBytes = File.ReadAllBytes(SourcePath);
            input = Keep(Object.Instantiate(source));
            settings = Keep(new GameObject("Lookup settings")).AddComponent<MururuBlinkLipSyncInstallRecord>();
            settings.facialExParameterName = "Facial EX";
            settings.leftLipSyncEnabled = Enumerable.Repeat(true, 7).ToArray();
            settings.rightLipSyncEnabled = Enumerable.Repeat(true, 7).ToArray();
            settings.facialLipSyncEnabled = Enumerable.Repeat(true, 8).ToArray();
            settings.bodyRenderer = settings.gameObject.AddComponent<SkinnedMeshRenderer>();
            var defaults = Keep(new Mesh()); defaults.vertices = new[] { Vector3.zero };
            defaults.AddBlendShapeFrame("CustomSmile", 100, new Vector3[1], new Vector3[1], new Vector3[1]);
            settings.bodyRenderer.sharedMesh = defaults;
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17), "Every mutation starts from the accepted standard");
            left = CopyLayer("Facial LeftHand");
            originalClip = (AnimationClip)Left(4).motion;
            AnimationUtility.GetCurveBindings(originalClip);
            originalClipJson = EditorJsonUtility.ToJson(originalClip);
        }

        [Test]
        public void StandardBaseline_DoesNotReportFallback()
        {
            var plan = Capture();
            Assert.That(plan.Warnings, Is.Empty);
            Assert.That(ErrorReport.CaptureErrors(() => MururuMouthReturnWarning.Report(settings,
                MururuMouthReturnUnsupportedReason.None, plan)), Is.Empty);
        }

        [Test]
        public void ActiveRendererShape_ReportsItsNameWithoutEnablingAnyRoute()
        {
            settings.bodyRenderer.SetBlendShapeWeight(0, 100);
            var plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string> { "CustomSmile" });
            Assert.That(plan.Routes.Any(r => r.Pose.Supported), Is.False);
            Assert.That(plan.Warnings.Count, Is.EqualTo(17), "Repeated combinations are grouped by primary expression and cause");
            Assert.That(plan.Warnings.All(w => w.Contains("CustomSmile")), Is.True);
            using (var bundle = MururuMouthReturnControllerGenerator.Generate(input, settings, "Body", "Mouth",
                new[] { MururuMouthReturnMeshAssembler.DefaultShapeName }, new HashSet<string> { "CustomSmile" }))
                CollectionAssert.AreEqual(plan.Warnings, bundle.Plan.Warnings);
            var errors = ErrorReport.CaptureErrors(() => MururuMouthReturnWarning.Report(settings,
                MururuMouthReturnUnsupportedReason.None, plan));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].TheError.Severity, Is.EqualTo(ErrorSeverity.NonFatal));
            Assert.That(errors[0].TheError.ToMessage(), Does.Contain("CustomSmile").And.Contain("瞬時切り替え"));
        }

        [Test]
        public void MeshFailure_ReportsNonBlockingCause()
        {
            var errors = ErrorReport.CaptureErrors(() => MururuMouthReturnWarning.Report(settings,
                MururuMouthReturnUnsupportedReason.BindposeMismatch, Capture()));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0].TheError.Severity, Is.EqualTo(ErrorSeverity.NonFatal));
            Assert.That(errors[0].TheError.ToMessage(), Does.Contain("骨の基準姿勢が一致しません").And.Not.Contain("BindposeMismatch"));
        }
        [Test]
        public void WdOffOmission_RequiresAnimatedCoverageRatherThanRendererDefault()
        {
            var idle = CopyLayer("Facial Idle").defaultState;
            var lower = Keep(Object.Instantiate((AnimationClip)idle.motion)); idle.motion = lower;
            lower.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_excited", null);
            settings.bodyRenderer.sharedMesh.AddBlendShapeFrame("mouth_excited", 100, new Vector3[1], null, null);
            var machine = Keep(new AnimatorStateMachine());
            var previous = Keep(machine.AddState("Excited")); var eyes = Keep(machine.AddState("Eyes")); machine.defaultState = previous;
            previous.writeDefaultValues = eyes.writeDefaultValues = false;
            var full = Keep(new AnimationClip());
            foreach (string p in MururuMouthReturnFxRoutesPose.Properties)
                full.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + p, AnimationCurve.Constant(0, 1, p == "mouth_excited" ? 100 : 0));
            previous.motion = full;
            var eye = Keep(new AnimationClip()); eye.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_close", AnimationCurve.Constant(0, 1, 100)); eyes.motion = eye;
            input.AddLayer(new AnimatorControllerLayer { name = "Omission", stateMachine = machine, defaultWeight = 1, syncedLayerIndex = -1 });
            Func<MururuMouthReturnFxRoutesPose> endpoint = () => {
                var extra = MururuMouthReturnFxRoutesPlan.AdditionalFx.Capture(input, "Body", new HashSet<string>());
                var writer = extra.Writers.Single(); int id = writer.Groups.Single(g => g.States.Contains(eyes)).Id;
                return MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string>(), null, extra).Routes
                    .Single(r => r.Key.StartsWith("Left0") && r.Conditions.Any(c => c.Parameter == writer.Parameter && c.Threshold == id)).Pose;
            };
            Assert.That(endpoint().Supported, Is.False,
                "A renderer's initial zero does not erase a mouth retained from the preceding state");
            lower.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_excited", AnimationCurve.Constant(0, 1, 0));
            Assert.That(endpoint().Supported, Is.True, "An actual lower animation supplies the omitted channel");
        }

        [Test]
        public void MissingGeometry_IsReportedOnlyWhenLipSyncCanBeEnabled()
        {
            var shapes = new[] { MururuMouthReturnMeshAssembler.DefaultShapeName };
            using (var bundle = MururuMouthReturnControllerGenerator.Generate(input, settings, "Body", "Mouth", shapes, new HashSet<string>()))
                Assert.That(bundle.Plan.Warnings.Any(w => w.StartsWith("左手 4:") && w.Contains("生成できません")), Is.True);
            settings.leftLipSyncEnabled[3] = false;
            using (var bundle = MururuMouthReturnControllerGenerator.Generate(input, settings, "Body", "Mouth", shapes, new HashSet<string>()))
                Assert.That(bundle.Plan.Warnings.Any(w => w.StartsWith("左手 4:")), Is.False);
        }

        [Test]
        public void UnknownActiveMouthShape_OnlyDisablesItsOwnRoute()
        {
            var clip = Keep(Object.Instantiate(originalClip)); Left(4).motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.CustomSmile", AnimationCurve.Constant(0, .1f, 100));
            var blocked = new HashSet<string> { "CustomSmile" };
            var plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", blocked);
            Assert.IsFalse(plan.Routes.Single(r => r.Key == "Left4").Pose.Supported);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(16));
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.CustomSmile", AnimationCurve.Constant(0, .1f, 0));
            plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", blocked);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
        }

        [TestCase("CustomFacial")] [TestCase("Facial EX")]
        public void RenamedFacialInput_IsUsedForEveryExpressionGuard(string parameter)
        {
            var ex = CopyLayer("Facial Ex");
            var parameters = input.parameters;
            foreach (var p in parameters) if (p.name == "Facial Ex") p.name = parameter;
            input.parameters = parameters;
            foreach (var transition in ex.entryTransitions)
            {
                var conditions = transition.conditions;
                for (int i = 0; i < conditions.Length; i++) conditions[i].parameter = parameter;
                transition.conditions = conditions;
            }
            foreach (var state in ex.states)
                foreach (var transition in state.state.transitions)
                {
                    var conditions = transition.conditions;
                    for (int i = 0; i < conditions.Length; i++) if (conditions[i].parameter == "Facial Ex") conditions[i].parameter = parameter;
                    transition.conditions = conditions;
                }
            settings.facialExParameterName = parameter;
            var plan = Capture();
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            foreach (var route in plan.Routes.Where(r => r.Key != "AFK"))
            {
                var guard = route.Conditions.Single(c => c.Parameter == parameter);
                Assert.That(guard.Mode, Is.EqualTo(AnimatorConditionMode.Equals));
                Assert.That(guard.Threshold, Is.EqualTo(route.Key.StartsWith("Left") ? 0 : int.Parse(route.Key.Substring(2))));
                Assert.IsFalse(route.Conditions.Any(c => c.Parameter == "Facial Ex"));
            }
        }

        [Test]
        public void UnknownFacialAssignment_DoesNotLeaveLeftRoutesEnabled()
        {
            var ex = CopyLayer("Facial Ex");
            ex.entryTransitions[0].AddCondition(AnimatorConditionMode.Equals, 1, "OtherFacial");
            Assert.IsFalse(Capture().Routes.Where(r => r.Key != "AFK").Any(r => r.Pose.Supported));
        }

        [Test]
        public void MissingFacialClip_OnlyDisablesItsOwnRoute()
        {
            var ex = CopyLayer("Facial Ex");
            ex.states.Single(s => s.state.name.StartsWith("1 ", StringComparison.Ordinal)).state.motion = null;
            var plan = Capture();
            Assert.IsFalse(plan.Routes.Single(r => r.Key == "Ex1").Pose.Supported);
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(16));
        }

        [TestCase("mouth_triangle", 100)] [TestCase("mouth_triangle", 0)] [TestCase("CustomSmile", 100)]
        public void RightMouthWriter_ExcludesOnlyItsGesture(string shape, float weight)
        {
            var right = CopyLayer("Facial RightHand");
            var state = right.states.Single(s => s.state.name.StartsWith("1 ", StringComparison.Ordinal)).state;
            var clip = Keep(Object.Instantiate((AnimationClip)state.motion)); state.motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape, AnimationCurve.Constant(0, .1f, weight));
            var plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string> { "CustomSmile" });
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            foreach (var route in plan.Routes)
            {
                var excluded = route.Conditions.Where(c => c.Parameter == "GestureRight" && c.Mode == AnimatorConditionMode.NotEqual).ToArray();
                Assert.That(excluded.Select(c => c.Threshold), Is.EqualTo(new[] { 1f }));
            }
            Assert.That(plan.Warnings.Any(w => w.StartsWith("右手 1:")), Is.True);
            settings.rightLipSyncEnabled[0] = false;
            plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string> { "CustomSmile" });
            Assert.That(plan.Warnings.Any(w => w.StartsWith("右手 1:")), Is.False);
        }

        [Test]
        public void UnknownRightAssignments_DoNotEnableAnySmoothRoute()
        {
            var right = CopyLayer("Facial RightHand");
            right.entryTransitions = Array.Empty<AnimatorTransition>();
            Assert.IsFalse(Capture().Routes.Any(r => r.Pose.Supported));
        }

        [TestCase("Facial LeftHand", 9)] [TestCase("Facial Ex", 1)]
        [TestCase("Facial AFK", 0)] [TestCase("Facial Idle", 0)] [TestCase("Facial RightHand", 0)]
        public void AnyStateOverride_DisablesOnlyRoutesDependingOnItsLayer(string layerName, int remaining)
        {
            var machine = CopyLayer(layerName);
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            input.AddParameter("OverrideFace", AnimatorControllerParameterType.Bool);
            var transition = Keep(machine.AddAnyStateTransition(machine.states[0].state));
            transition.AddCondition(AnimatorConditionMode.If, 0, "OverrideFace");
            var plan = Capture();
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(remaining));
            if (layerName == "Facial LeftHand") Assert.IsFalse(plan.Routes.Where(r => r.Key.StartsWith("Left")).Any(r => r.Pose.Supported));
            if (layerName == "Facial Ex") Assert.IsTrue(plan.Routes.Single(r => r.Pose.Supported).Key == "AFK");
            if (layerName == "Facial AFK") Assert.IsFalse(plan.Routes.Single(r => r.Key == "AFK").Pose.Supported);
        }

        [TestCase("Direct")] [TestCase("MissingExit")] [TestCase("WrongExit")]
        [TestCase("DelayedExit")] [TestCase("MutedExit")]
        public void NonstandardStateTransition_DisablesLeftRoutes(string mutation)
        {
            if (mutation == "Direct") Keep(Left(4).AddTransition(Left(1))).AddCondition(AnimatorConditionMode.If, 0, "OverrideFace");
            if (mutation == "MissingExit") Left(4).transitions = Array.Empty<AnimatorStateTransition>();
            if (mutation == "DelayedExit") Left(4).transitions[0].hasExitTime = true;
            if (mutation == "MutedExit") Left(4).transitions[0].mute = true;
            if (mutation == "WrongExit") Left(4).transitions[0].conditions = new[] {
                new AnimatorCondition { mode = AnimatorConditionMode.NotEqual, parameter = "OtherGesture", threshold = 4 } };
            var plan = Capture();
            Assert.IsFalse(plan.Routes.Where(r => r.Key.StartsWith("Left")).Any(r => r.Pose.Supported));
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(9));
        }

        [Test]
        public void UnknownRendererWeight_OnlyAllowsExpressionsExplicitlyClearingIt()
        {
            settings.bodyRenderer.SetBlendShapeWeight(0, 100);
            var clip = Keep(Object.Instantiate(originalClip)); Left(4).motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.CustomSmile", AnimationCurve.Constant(0, .1f, 0));
            var plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string> { "CustomSmile" });
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "Left4").Pose.Supported);
            Assert.IsFalse(plan.Routes.Where(r => r.Key.StartsWith("Left") && r.Key != "Left4").Any(r => r.Pose.Supported));
            foreach (var route in plan.Routes.Where(r => r.Pose.Supported && !r.Key.StartsWith("Left")))
                foreach (int value in Enumerable.Range(0, 8).Where(v => v != 4))
                    Assert.IsTrue(route.Conditions.Any(c => c.Parameter == "GestureLeft" && c.Mode == AnimatorConditionMode.NotEqual && c.Threshold == value),
                        "Higher routes are safe only where the lower explicit zero clears the initial unknown shape");
            var allowed = Matching(plan, 4, 1);
            Assert.That(allowed, Is.Not.Null);
            var actual = NativeMouth(4, 1);
            Assert.That(actual["CustomSmile"], Is.EqualTo(0).Within(.02f));
            foreach (var p in allowed.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
            Assert.That(Matching(plan, 3, 1), Is.Null);
            Assert.That(NativeMouth(3, 1)["CustomSmile"], Is.EqualTo(100).Within(.02f));
        }

        [TestCase("mouth_A")] [TestCase("eye_smile")] [TestCase("@mesh")]
        public void ExtraStaticLayer_UsesKnownEndpointAndRejectsUnsupportedMixtures(string property)
        {
            var machine = Keep(new AnimatorStateMachine());
            var state = Keep(machine.AddState("Custom")); machine.defaultState = state;
            var clip = Keep(new AnimationClip()); state.motion = clip;
            if (property == "@mesh") AnimationUtility.SetObjectReferenceCurve(clip,
                EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Mesh"),
                new[] { new ObjectReferenceKeyframe { time = 0, value = settings.bodyRenderer.sharedMesh } });
            else clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + property, AnimationCurve.Constant(0, .1f, 100));
            input.AddLayer(new AnimatorControllerLayer { name = "Custom expressions", stateMachine = machine, defaultWeight = 1 });
            var plan = Capture();
            if (property == "@mesh") Assert.IsFalse(plan.Routes.Any(r => r.Pose.Supported));
            else if (property == "eye_smile") Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            else
            {
                var route = Matching(plan, 0); Assert.That(route, Is.Not.Null);
                var actual = NativeMouth(0);
                foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
                Assert.That(actual["mouth_A"], Is.EqualTo(100).Within(.02f));
                Assert.That(Matching(plan, 0, 1), Is.Null, "A special mouth mixed with A remains unsupported");
                var mixture = NativeMouth(0, 1);
                Assert.That(mixture["mouth_A"], Is.EqualTo(100).Within(.02f));
                Assert.That(mixture["mouth_grin"], Is.EqualTo(100).Within(.02f));
            }
        }

        [Test]
        public void ExtraStaticCompleteMouth_PreservesAllKnownRoutes()
        {
            var machine = Keep(new AnimatorStateMachine()); var state = Keep(machine.AddState("Constant mouth")); machine.defaultState = state;
            state.motion = Endpoint("mouth_triangle");
            input.AddLayer(new AnimatorControllerLayer { name = "Static mouth", stateMachine = machine, defaultWeight = 1 });
            var plan = Capture();
            Assert.That(plan.Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            var actual = NativeMouth(4, 1);
            foreach (var p in Matching(plan, 4, 1).Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
            Assert.That(actual["mouth_triangle"], Is.EqualTo(100).Within(.02f));
        }

        [Test]
        public void ExtraUnknownMouthGraph_IsSafeOnlyWhereLaterWritesCoverIt()
        {
            var machine = Keep(new AnimatorStateMachine()); var state = Keep(machine.AddState("Unknown first")); machine.defaultState = state;
            var clip = Keep(new AnimationClip()); state.motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_A", AnimationCurve.Constant(0, .1f, 100));
            Keep(machine.AddState("Unknown second")).motion = clip;
            var layers = input.layers.ToList(); int ex = layers.FindIndex(l => l.name == "Facial Ex");
            layers.Insert(ex, new AnimatorControllerLayer { name = "Unknown graph", stateMachine = machine, defaultWeight = 1 }); input.layers = layers.ToArray();
            var plan = Capture();
            Assert.That(Matching(plan, 0), Is.Null);
            var route = Matching(plan, 0, 1); Assert.That(route, Is.Not.Null);
            var actual = NativeMouth(0, 1);
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
        }

        [Test]
        public void EyeOnlySyncedLayer_PreservesEveryMouthRoute()
        {
            int root = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand");
            input.AddLayer(new AnimatorControllerLayer { name = "Synced eyes", stateMachine = Keep(new AnimatorStateMachine()),
                syncedLayerIndex = root, defaultWeight = 1 });
            var eye = Keep(new AnimationClip());
            eye.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_smile", AnimationCurve.Constant(0, .1f, 100));
            foreach (var state in left.states) input.SetStateEffectiveMotion(state.state, eye, input.layers.Length - 1);
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            var route = Matching(Capture(), 4);
            var actual = NativeMouth(4);
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
        }

        AnimationClip Endpoint(string shape)
        {
            var clip = Keep(new AnimationClip());
            foreach (string p in MururuMouthReturnFxRoutesPose.Properties)
                clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + p, AnimationCurve.Constant(0, .1f, p == shape ? 100 : 0));
            return clip;
        }

        int Sync(int root, string name)
        {
            input.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = Keep(new AnimatorStateMachine()),
                syncedLayerIndex = root, defaultWeight = 1 });
            return input.layers.Length - 1;
        }

        MururuMouthReturnFxRoutesRoute Matching(MururuMouthReturnFxRoutesPlan plan, int gesture, int facial = 0, bool afk = false)
        {
            var values = new Dictionary<string, float> { ["GestureLeft"] = gesture, ["GestureRight"] = 0,
                ["Facial Ex"] = facial, ["AFK"] = afk ? 1 : 0, ["VRCEmote"] = 0, ["Seated"] = 0, ["Accessory"] = 0 };
            return plan.Routes.SingleOrDefault(r => r.Pose.Supported && r.Conditions.All(c => {
                float v = values[c.Parameter];
                switch (c.Mode) {
                    case AnimatorConditionMode.Equals: return v == c.Threshold;
                    case AnimatorConditionMode.NotEqual: return v != c.Threshold;
                    case AnimatorConditionMode.If: return v != 0;
                    case AnimatorConditionMode.IfNot: return v == 0;
                    case AnimatorConditionMode.Greater: return v > c.Threshold;
                    case AnimatorConditionMode.Less: return v < c.Threshold;
                    default: throw new InvalidOperationException();
                }
            }));
        }

        Dictionary<string, float> NativeMouth(int gesture, int facial = 0, bool afk = false)
        {
            var root = Keep(new GameObject("Effective source evaluation"));
            var child = new GameObject("Body"); child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            var mesh = Keep(new Mesh()); mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (string p in MururuMouthReturnFxRoutesPose.Properties.Concat(new[] { "CustomSmile" })) mesh.AddBlendShapeFrame(p, 100, new Vector3[3], new Vector3[3], new Vector3[3]);
            renderer.sharedMesh = mesh;
            renderer.SetBlendShapeWeight(mesh.GetBlendShapeIndex("CustomSmile"), settings.bodyRenderer.GetBlendShapeWeight(0));
            var animator = root.AddComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = input; animator.Rebind();
            animator.SetInteger("GestureLeft", gesture); animator.SetInteger("Facial Ex", facial); animator.SetBool("AFK", afk);
            for (int i = 0; i < 100; i++) animator.Update(.02f);
            var result = MururuMouthReturnFxRoutesPose.Properties.Concat(new[] { "CustomSmile" }).ToDictionary(p => p, p => renderer.GetBlendShapeWeight(mesh.GetBlendShapeIndex(p)));
            animator.runtimeAnimatorController = null;
            return result;
        }

        [TestCase("Absent")] [TestCase("Shared")] [TestCase("Override")]
        public void SyncedMouthMotion_MatchesNativeEndpoint(string assignment)
        {
            int root = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand");
            int synced = Sync(root, "Effective mouth");
            if (assignment != "Absent") input.SetStateEffectiveMotion(Left(4), assignment == "Shared" ? originalClip : Endpoint("mouth_triangle"), synced);
            Assert.That(input.GetStateEffectiveMotion(Left(4), synced) != null, Is.EqualTo(assignment != "Absent"));
            var route = Matching(Capture(), 4, 1);
            Assert.That(route, Is.Not.Null, "A known synced mouth at higher priority must retain a smooth context");
            var actual = NativeMouth(4, 1);
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f),
                p.Key + " expected=" + string.Join(",", route.Pose.Raw.Where(v => v.Value != 0)) + " actual=" + string.Join(",", actual.Where(v => v.Value != 0))
                + " effectiveMotion=" + (input.GetStateEffectiveMotion(Left(4), synced) ? input.GetStateEffectiveMotion(Left(4), synced).name : "null"));
        }

        [TestCase(false)] [TestCase(true)]
        public void SyncedChain_UsesEachLayersEffectiveMotion(bool nearerOverride)
        {
            int root = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand");
            int first = Sync(root, "First sync");
            input.SetStateEffectiveMotion(Left(4), Endpoint("mouth_triangle"), first);
            int last = Sync(first, "Second sync");
            if (nearerOverride) input.SetStateEffectiveMotion(Left(4), Endpoint("mouth_circle"), last);
            Assert.That(input.GetStateEffectiveMotion(Left(4), last) != null, Is.EqualTo(nearerOverride));
            var route = Matching(Capture(), 4, 1);
            Assert.That(route, Is.Not.Null);
            var actual = NativeMouth(4, 1);
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
            Assert.That(route.Pose.Raw[nearerOverride ? "mouth_circle" : "mouth_triangle"], Is.EqualTo(100));
        }

        [Test]
        public void SyncedBehaviourOverride_ControlsOnlyEffectiveMouthTarget()
        {
            var machine = Keep(new AnimatorStateMachine()); var state = Keep(machine.AddState("Eyes")); machine.defaultState = state;
            state.motion = Keep(new AnimationClip());
            input.AddLayer(new AnimatorControllerLayer { name = "Eyes", stateMachine = machine, defaultWeight = 1 });
            int eyes = input.layers.Length - 1;
            var inherited = Keep(state.AddStateMachineBehaviour<VRCAnimatorLayerControl>());
            inherited.playable = VRC_AnimatorLayerControl.BlendableLayer.FX; inherited.layer = eyes; inherited.goalWeight = 0;
            int synced = Sync(eyes, "Synced eyes");
            Assert.That(input.GetStateEffectiveBehaviours(state, synced), Is.Empty,
                "Unity 2022.3 reports no effective callbacks for an absent synced Behaviour override");
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17), "Inherited eye weight is unrelated");
            input.SetStateEffectiveBehaviours(state, synced, Array.Empty<StateMachineBehaviour>());
            Assert.That(input.GetStateEffectiveBehaviours(state, synced), Is.Empty);
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            input.SetStateEffectiveBehaviours(state, synced, state.behaviours);
            CollectionAssert.AreEqual(state.behaviours, input.GetStateEffectiveBehaviours(state, synced), "Explicit shared Behaviour references are preserved");
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
            var effective = Keep(ScriptableObject.CreateInstance<VRCAnimatorLayerControl>());
            effective.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            effective.layer = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand"); effective.goalWeight = 0;
            input.SetStateEffectiveBehaviours(state, synced, new StateMachineBehaviour[] { effective });
            Assert.That(input.GetStateEffectiveBehaviours(state, synced).Single(), Is.SameAs(effective));
            Assert.That(Matching(Capture(), 4), Is.Null, "Effective mouth weight is uncertain");
            Assert.That(Matching(Capture(), 4, 1), Is.Not.Null, "An upper complete endpoint remains known");
            input.SetStateEffectiveBehaviours(state, synced, Array.Empty<StateMachineBehaviour>());
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17), "An explicit empty override removes the effective mouth control");
        }

        [Test]
        public void HarmlessBehaviourOverride_DoesNotCopySourceMouthControl()
        {
            var machine = Keep(new AnimatorStateMachine()); var state = Keep(machine.AddState("Source")); machine.defaultState = state;
            state.motion = Keep(new AnimationClip());
            input.AddLayer(new AnimatorControllerLayer { name = "Behaviour source", stateMachine = machine, defaultWeight = 1 });
            int root = input.layers.Length - 1;
            var mouthControl = Keep(state.AddStateMachineBehaviour<VRCAnimatorLayerControl>());
            mouthControl.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            mouthControl.layer = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand"); mouthControl.goalWeight = 0;
            int synced = Sync(root, "Behaviour override");
            var eyeControl = Keep(ScriptableObject.CreateInstance<VRCAnimatorLayerControl>());
            eyeControl.playable = VRC_AnimatorLayerControl.BlendableLayer.FX; eyeControl.layer = root; eyeControl.goalWeight = 0;
            input.SetStateEffectiveBehaviours(state, synced, new StateMachineBehaviour[] { eyeControl });
            var resolve = typeof(MururuMouthReturnFxRoutesPlan).GetMethod("EffectiveBehaviours", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var actual = (StateMachineBehaviour[])resolve.Invoke(null, new object[] { input, synced, state });
            CollectionAssert.AreEqual(input.GetStateEffectiveBehaviours(state, synced), actual);
            CollectionAssert.AreEqual(new[] { eyeControl }, actual, "The harmless override must not replay source mouth callbacks");
        }

        [TestCase("Cycle")] [TestCase("OutOfRange")]
        public void InvalidSyncedReferences_AreNotGuessed(string mutation)
        {
            int root = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand");
            int synced = Sync(root, "Invalid sync"); var layers = input.layers;
            layers[synced].syncedLayerIndex = mutation == "Cycle" ? synced : layers.Length + 10;
            // The native layers setter normalizes invalid references. Validate the
            // resolver directly so malformed arrays cannot loop or invent a root.
            var resolve = typeof(MururuMouthReturnFxRoutesPlan).GetMethod("RootIndex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(resolve.Invoke(null, new object[] { layers, synced }), Is.EqualTo(-1));
        }

        [Test]
        public void UnrelatedOrderAndWeightControl_PreserveEveryRoute()
        {
            var layers = input.layers.ToList(); var accessory = layers.Single(l => l.name == "Accessory");
            layers.Remove(accessory); layers.Insert(0, accessory); input.layers = layers.ToArray();
            var control = Keep(Left(4).AddStateMachineBehaviour<VRCAnimatorLayerControl>());
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = 0; control.goalWeight = 0;
            Assert.That(Capture().Routes.Count(r => r.Pose.Supported), Is.EqualTo(17));
        }

        [Test]
        public void ConstantLayerWeight_KeepsKnownClosedMouthCombination()
        {
            Left(4).motion = Endpoint("mouth_good");
            var layers = input.layers; int index = Array.FindIndex(layers, l => l.name == "Facial LeftHand");
            layers[index].defaultWeight = .5f; input.layers = layers;
            var route = Matching(Capture(), 4);
            Assert.That(route, Is.Not.Null);
            Assert.That(route.Pose.Raw["mouth_good"], Is.EqualTo(50));
            var actual = NativeMouth(4);
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
        }

        [Test]
        public void WeightControlCanActivateInitiallyZeroMouthLayer()
        {
            var layers = input.layers; int index = Array.FindIndex(layers, l => l.name == "Facial LeftHand");
            layers[index].defaultWeight = 0; input.layers = layers;
            Assert.That(Matching(Capture(), 4), Is.Not.Null, "A permanently zero layer leaves a known baseline");
            var control = Keep(Left(4).AddStateMachineBehaviour<VRCAnimatorLayerControl>());
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX; control.layer = index; control.goalWeight = 1;
            Assert.That(Matching(Capture(), 4), Is.Null, "Goal one is dynamic when the initial weight was zero");
            Assert.That(Matching(Capture(), 4, 1), Is.Not.Null, "Upper complete coverage remains safe");
        }

        [Test]
        public void AdditiveMouthLayer_DoesNotMistakeEqualValuesForAnOverride()
        {
            int root = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand");
            int synced = Sync(root, "Additive mouth");
            input.SetStateEffectiveMotion(Left(4), Endpoint("mouth_A"), synced);
            var layers = input.layers; layers[synced].blendingMode = AnimatorLayerBlendingMode.Additive; input.layers = layers;
            Assert.That(Matching(Capture(), 4), Is.Null);
        }

        [TestCase(false)] [TestCase(true)]
        public void ExplicitBodyMask_UsesActualMouthInfluence(bool included)
        {
            var mask = Keep(new AvatarMask()); mask.transformCount = 2;
            mask.SetTransformPath(0, ""); mask.SetTransformActive(0, true);
            mask.SetTransformPath(1, "Body"); mask.SetTransformActive(1, included);
            var layers = input.layers; int index = Array.FindIndex(layers, l => l.name == "Facial LeftHand");
            layers[index].avatarMask = mask; input.layers = layers;
            var actual = NativeMouth(4);
            var route = Matching(Capture(), 4);
            Assert.That(route, Is.Not.Null, "An explicit Body mask establishes whether this mouth writer applies");
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
            Assert.That(actual["mouth_A"], Is.EqualTo(100).Within(.02f),
                "AvatarMask does not filter this renderer blend-shape channel even when Body is explicitly excluded");
        }

        [Test]
        public void SyncedExtraLayer_CannotHideEffectiveMouthOverride()
        {
            int baselineIndex = Array.FindIndex(input.layers, l => l.name == "Facial Idle");
            var baseline = input.layers[baselineIndex].stateMachine;
            var original = baseline.defaultState.motion;
            var empty = Keep(new AnimatorStateMachine());
            var clip = Keep(new AnimationClip());
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_triangle", AnimationCurve.Constant(0, .1f, 100));
            input.AddLayer(new AnimatorControllerLayer { name = "Synced custom", stateMachine = empty,
                syncedLayerIndex = baselineIndex, defaultWeight = 1 });
            input.SetStateEffectiveMotion(baseline.defaultState, clip, input.layers.Length - 1);
            Assert.That(input.GetStateEffectiveMotion(baseline.defaultState, input.layers.Length - 1), Is.SameAs(clip));
            Assert.That(baseline.defaultState.motion, Is.SameAs(original));
            var plan = Capture();
            var native = Keep(new AnimatorController());
            native.layers = new[] {
                new AnimatorControllerLayer { name = "Baseline", stateMachine = baseline, defaultWeight = 1 },
                new AnimatorControllerLayer { name = "Override", stateMachine = empty, syncedLayerIndex = 0, defaultWeight = 1 } };
            native.SetStateEffectiveMotion(baseline.defaultState, clip, 1);
            var root = Keep(new GameObject("Synced evaluation")); var child = new GameObject("Body"); child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>(); var mesh = Keep(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("mouth_triangle", 100, new Vector3[3], new Vector3[3], new Vector3[3]); renderer.sharedMesh = mesh;
            var animator = root.AddComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = native; animator.Rebind(); for (int i = 0; i < 5; i++) animator.Update(.02f);
            float actual = renderer.GetBlendShapeWeight(0); animator.runtimeAnimatorController = null;
            Assert.That(actual, Is.EqualTo(100).Within(.01f));
            Assert.That(plan.Routes.Single(r => r.Key == "Left0").Pose.Raw["mouth_triangle"], Is.EqualTo(actual));
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "Left0").Pose.Supported,
                "A known effective override preserves its determinable endpoint");
        }

        [Test]
        public void ReorderedMouthLayers_AreNotTreatedAsStandardPriority()
        {
            var layers = input.layers; int a = Array.FindIndex(layers, l => l.name == "Facial Ex");
            int b = Array.FindIndex(layers, l => l.name == "Facial LeftHand");
            var swap = layers[a]; layers[a] = layers[b]; layers[b] = swap; input.layers = layers;
            var plan = Capture();
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "Left4").Pose.Supported);
            var actual = NativeMouth(4);
            foreach (var p in plan.Routes.Single(r => r.Key == "Left4").Pose.Raw)
                Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
            Assert.IsTrue(plan.Routes.Any(r => r.Key.StartsWith("Ex1_Context") && r.Pose.Supported),
                "Known lower-input contexts need their actual new priority, not global rejection");
        }

        [Test]
        public void DynamicSourceLayerWeight_IsNotTreatedAsConstantOne()
        {
            var control = Keep(Left(4).AddStateMachineBehaviour<VRCAnimatorLayerControl>());
            control.playable = VRC_AnimatorLayerControl.BlendableLayer.FX;
            control.layer = Array.FindIndex(input.layers, l => l.name == "Facial LeftHand"); control.goalWeight = 0;
            var plan = Capture();
            Assert.IsFalse(plan.Routes.Single(r => r.Key == "Left4").Pose.Supported);
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "Ex1").Pose.Supported,
                "A later complete writer covers the controlled lower layer");
        }

        [Test]
        public void InactiveFacialMouthWriter_UsesActualKnownEndpoint()
        {
            var ex = CopyLayer("Facial Ex"); var clip = Keep(new AnimationClip()); ex.defaultState.motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_A", AnimationCurve.Constant(0, .1f, 100));
            var plan = Capture();
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "Left0").Pose.Supported);
            Assert.That(plan.Routes.Single(r => r.Key == "Left0").Pose.Raw["mouth_A"], Is.EqualTo(100));
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "Ex1").Pose.Supported);
            Assert.IsTrue(plan.Routes.Single(r => r.Key == "AFK").Pose.Supported);
        }

        [Test]
        public void SparseFacialOverride_DoesNotAssumeMissingValuesComeFromBaseline()
        {
            var lower = Keep(Object.Instantiate(originalClip)); Left(4).motion = lower;
            lower.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_A", AnimationCurve.Constant(0, .1f, 100));
            var ex = CopyLayer("Facial Ex"); var state = ex.states.Single(s => s.state.name.StartsWith("1 ", StringComparison.Ordinal)).state;
            var clip = Keep(Object.Instantiate((AnimationClip)state.motion)); state.motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_A", null);
            var plan = Capture();
            Assert.That(plan.Routes.Single(r => r.Key == "Ex1").Conditions.Any(c => c.Parameter == "GestureLeft"
                && c.Mode == AnimatorConditionMode.NotEqual && c.Threshold == 4), Is.True);
            Assert.That(plan.Routes.Single(r => r.Key == "Ex2").Pose.Supported, Is.True);
        }

        [Test]
        public void SparseWriteDefaultsEndpoint_MatchesNativeMouth()
        {
            var defaults = Keep(new Mesh()); defaults.vertices = new[] { Vector3.zero };
            foreach (string p in MururuMouthReturnFxRoutesPose.Properties.Concat(new[] { "CustomSmile" }))
                defaults.AddBlendShapeFrame(p, 100, new Vector3[1], new Vector3[1], new Vector3[1]);
            settings.bodyRenderer.sharedMesh = defaults;
            var lower = Endpoint("mouth_good"); Left(4).motion = lower;
            lower.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_good", AnimationCurve.Constant(0, .1f, 50));
            var ex = CopyLayer("Facial Ex");
            var upper = Endpoint("mouth_good2");
            upper.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_good", null);
            upper.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_good2", AnimationCurve.Constant(0, .1f, 50));
            ex.states.Single(s => s.state.name.StartsWith("1 ", StringComparison.Ordinal)).state.motion = upper;
            var route = Matching(Capture(), 4, 1);
            Assert.That(route, Is.Not.Null);
            var actual = NativeMouth(4, 1);
            foreach (var p in route.Pose.Raw) Assert.That(actual[p.Key], Is.EqualTo(p.Value).Within(.02f), p.Key);
        }

        [TestCase(false)] [TestCase(true)]
        public void MutedOrSoloEntry_DoesNotEnableAssumedAssignments(bool solo)
        {
            if (solo) left.entryTransitions[0].solo = true; else left.entryTransitions[0].mute = true;
            Assert.IsFalse(Capture().Routes.Where(r => r.Key.StartsWith("Left")).Any(r => r.Pose.Supported));
        }

        [Test]
        public void UnknownLowerWriter_AlsoGuardsHigherExpressionsThatDoNotOverwriteIt()
        {
            var clip = Keep(Object.Instantiate(originalClip)); Left(4).motion = clip;
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.CustomSmile", AnimationCurve.Constant(0, .1f, 100));
            var plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string> { "CustomSmile" });
            Assert.IsFalse(plan.Routes.Single(r => r.Key == "Left4").Pose.Supported);
            foreach (var route in plan.Routes.Where(r => r.Key.StartsWith("Ex") || r.Key == "AFK"))
                Assert.That(route.Conditions.Any(c => c.Parameter == "GestureLeft" && c.Mode == AnimatorConditionMode.NotEqual && c.Threshold == 4), Is.True);
            Assert.That(plan.Warnings.Any(w => w.StartsWith("Facial EX 1:")), Is.True,
                "Report unsupported combinations even when this expression retains other smooth routes");
            settings.leftLipSyncEnabled[3] = false;
            plan = MururuMouthReturnFxRoutesPlan.Capture(input, settings, "Body", "Mouth", new HashSet<string> { "CustomSmile" });
            Assert.That(plan.Warnings, Is.Empty, "Intentionally disabled LipSync is not a fallback warning");
        }

        AnimatorStateMachine CopyLayer(string name)
        {
            var layers = input.layers; int index = Array.FindIndex(layers, l => l.name == name);
            var original = layers[index].stateMachine;
            var copy = Keep(new AnimatorStateMachine { name = original.name });
            // Build native owners through their APIs; do not instantiate serialized graph nodes.
            var map = original.states.ToDictionary(s => s.state, s => {
                var state = Keep(copy.AddState(s.state.name, s.position));
                state.motion = s.state.motion;
                state.writeDefaultValues = s.state.writeDefaultValues;
                return state;
            });
            copy.defaultState = map[original.defaultState];
            foreach (var pair in map)
                foreach (var transition in pair.Key.transitions)
                {
                    var clone = Keep(transition.isExit ? pair.Value.AddExitTransition()
                        : pair.Value.AddTransition(map[transition.destinationState]));
                    clone.conditions = transition.conditions;
                    clone.hasExitTime = transition.hasExitTime; clone.exitTime = transition.exitTime;
                    clone.hasFixedDuration = transition.hasFixedDuration; clone.duration = transition.duration;
                }
            foreach (var transition in original.entryTransitions)
            {
                Assert.That(transition.destinationState, Is.Not.Null, "The standard fixture has direct Entry assignments");
                var clone = Keep(copy.AddEntryTransition(map[transition.destinationState]));
                foreach (var condition in transition.conditions) clone.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }
            layers[index].stateMachine = copy; input.layers = layers; return copy;
        }

        [TearDown] public void SourceAndSharedClipStayUnchanged()
        {
            try
            {
                if (sourceBytes != null) Assert.That(File.ReadAllBytes(SourcePath), Is.EqualTo(sourceBytes));
                if (originalClip) Assert.That(EditorJsonUtility.ToJson(originalClip), Is.EqualTo(originalClipJson));
            }
            finally
            {
                foreach (var value in owned.AsEnumerable().Reverse()) if (value) Object.DestroyImmediate(value);
                owned.Clear();
            }
        }

        [TestCase("Unconditional")] [TestCase("Greater")] [TestCase("OtherParameter")] [TestCase("DuplicateEquals")]
        public void CompetingEntryAssignment_IsUnsupported(string mutation)
        {
            Assert.That(Capture().Routes.Single(r => r.Key == "Left4").Pose.Supported, Is.True);
            var transition = Keep(left.AddEntryTransition(Left(1)));
            if (mutation == "Greater") transition.AddCondition(AnimatorConditionMode.Greater, 0, "GestureLeft");
            if (mutation == "OtherParameter") transition.AddCondition(AnimatorConditionMode.Equals, 1, "Custom");
            if (mutation == "DuplicateEquals") transition.AddCondition(AnimatorConditionMode.Equals, 4, "GestureLeft");
            left.entryTransitions = new[] { transition }.Concat(left.entryTransitions.Where(t => t != transition)).ToArray();
            Assert.That(Capture().Routes.Single(r => r.Key == "Left4").Pose.Supported, Is.False);
        }

        [TestCase("MissingClip")] [TestCase("UnknownMouthShape")] [TestCase("NonConstant")]
        [TestCase("MissingCoverage")]
        public void InvalidLocalWriter_IsUnsupported(string mutation)
        {
            Assert.That(Capture().Routes.Single(r => r.Key == "Left4").Pose.Supported, Is.True);
            var clip = Keep(Object.Instantiate(originalClip)); Left(4).motion = clip;
            if (mutation == "MissingClip") Left(4).motion = null;
            if (mutation == "UnknownMouthShape") clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_custom", AnimationCurve.Constant(0, .1f, 100));
            if (mutation == "NonConstant") clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_good", AnimationCurve.Linear(0, 0, .1f, 100));
            if (mutation == "MissingCoverage")
            {
                var idle = CopyLayer("Facial Idle").defaultState;
                var baseline = Keep(Object.Instantiate((AnimationClip)idle.motion)); idle.motion = baseline;
                foreach (var writer in new[] { baseline, clip }) writer.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_good", null);
            }
            var plan = Capture();
            Assert.That(plan.Routes.Single(r => r.Key == "Left4").Pose.Supported, Is.False);
            if (mutation != "MissingCoverage") Assert.That(plan.Routes.Where(r => r.Key != "Left4").All(r => r.Pose.Supported), Is.True);
        }
    }
}
