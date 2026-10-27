using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MururuBlinkLipSync.Editor.Tests
{
    // Native Animator evaluation of our one layer only; SDK microphone processing is outside this fixture.
    internal sealed class MururuMouthReturnRevisedTimingTests
    {
        const string Source = "Assets/Lethal Cats/Mururu/Animation/FX/Mururu_FX.controller";
        const float Dt = 1f / 240;
        readonly List<Object> owned = new List<Object>();
        MururuMouthReturnControllerBundle bundle;
        Animator animator;
        SkinnedMeshRenderer body, mouth;
        string[] bank;
        T Keep<T>(T value) where T : Object { owned.Add(value); return value; }

        [TearDown] public void Cleanup()
        {
            if (animator) animator.runtimeAnimatorController = null;
            bundle?.Dispose(); bundle = null;
            foreach (var value in owned.AsEnumerable().Reverse()) if (value) Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test]
        public void Fixed80msWaitAndMorph_ReturnAndReset()
        {
            Setup();
            Voice(false);
            bool bridge = false;
            float elapsed = 0;
            for (int i = 0; i < 200; i++)
            {
                Step(); elapsed += Dt;
                float alpha = Weight(mouth, MururuMouthReturnMeshAssembler.DefaultShapeName);
                bridge |= alpha > .01f;
                if (elapsed < .08f - Dt) Assert.That(alpha, Is.EqualTo(0).Within(.02f), "Morph started before wait");
                if (Original()) break;
            }
            Assert.That(Original(), Is.True, Phase());
            Assert.That(elapsed, Is.InRange(.16f, .16f + 2 * Dt + .0001f));
            Assert.That(bridge, Is.True);
            AssertReset();
            Voice(true); Step();
            Assert.That(Original(), Is.False); AssertReset();
        }

        [TestCase(false)] [TestCase(true)]
        public void UnsupportedRoute_Uses80msFromSilence(bool anyStateOverride)
        {
            Setup(unsupported: !anyStateOverride, anyStateOverride: anyStateOverride); Voice(false);
            float elapsed = 0;
            for (int i = 0; i < 100 && !Original(); i++) { Step(); elapsed += Dt; AssertReset(); }
            Assert.That(Original(), Is.True, Phase());
            Assert.That(elapsed, Is.InRange(.08f, .08f + 2 * Dt + .0001f));
        }

        [Test]
        public void RightMouthWriter_Uses80msOnlyWhileItsGestureIsActive()
        {
            Setup(unsafeRight: true);
            animator.SetInteger("GestureRight", 1); Step(); Voice(false);
            float elapsed = 0;
            for (int i = 0; i < 100 && !Original(); i++) { Step(); elapsed += Dt; AssertReset(); }
            Assert.That(Original(), Is.True, Phase());
            Assert.That(elapsed, Is.InRange(.08f, .08f + 2 * Dt + .0001f));
            animator.SetInteger("GestureRight", 0); Voice(true); Step(); Voice(false);
            bool bridge = false;
            for (int i = 0; i < 100 && !Original(); i++) { Step(); bridge |= Weight(mouth, MururuMouthReturnMeshAssembler.DefaultShapeName) > 1; }
            Assert.That(bridge, Is.True, "Unaffected right gestures keep smooth return");
        }

        [TestCase("Voice")] [TestCase("GestureLeft")] [TestCase("Facial Ex")]
        [TestCase("AFK")] [TestCase("Accessory")] [TestCase("Seated")] [TestCase("VRCEmote")]
        [TestCase("DisableLeft")] [TestCase("DisableRight")] [TestCase("DisableFacial")]
        [TestCase("UnsafeRight")]
        public void BridgeInterruption_ClearsAllDedicatedShapesInSameEvaluation(string input)
        {
            Setup(unsafeRight: input == "UnsafeRight"); Voice(false);
            for (int i = 0; i < 100 && Weight(mouth, MururuMouthReturnMeshAssembler.DefaultShapeName) < 25; i++) Step();
            Assert.That(Weight(mouth, MururuMouthReturnMeshAssembler.DefaultShapeName), Is.InRange(25, 90), Phase());
            switch (input)
            {
                case "Voice": Voice(true); break;
                case "AFK": case "Seated": animator.SetBool(input, true); break;
                case "DisableLeft": animator.SetInteger("GestureLeft", 2); break;
                case "DisableRight": animator.SetInteger("GestureRight", 2); break;
                case "UnsafeRight": animator.SetInteger("GestureRight", 1); break;
                case "DisableFacial": animator.SetInteger("Facial EX", 2); break;
                default: animator.SetInteger(input, input == "Accessory" ? 6 : 1); break;
            }
            Step();
            AssertReset();
            Assert.That(Original(), Is.EqualTo(input != "Voice"), Phase());
            for (int i = 0; i < 10; i++) { Step(); AssertReset(); }
        }

        [Test]
        public void MissingActiveGeometry_UsesInstantWithoutWritingAbsentShapes()
        {
            Setup(missingGeometry: true); Voice(false);
            float elapsed = 0;
            for (int i = 0; i < 100 && !Original(); i++) { Step(); elapsed += Dt; AssertReset(); }
            Assert.That(Original(), Is.True);
            Assert.That(elapsed, Is.InRange(.08f, .08f + 2 * Dt + .0001f));
            Assert.That(bundle.Plan.Routes.Any(r => r.Pose.Supported), Is.True);
        }

        void Setup(bool unsupported = false, bool missingGeometry = false, bool unsafeRight = false, bool anyStateOverride = false)
        {
            var settingsObject = Keep(new GameObject("Explicit settings"));
            var settings = settingsObject.AddComponent<MururuBlinkLipSyncInstallRecord>();
            settings.leftLipSyncEnabled = Enumerable.Repeat(true, 7).ToArray();
            settings.rightLipSyncEnabled = Enumerable.Repeat(true, 7).ToArray();
            settings.facialLipSyncEnabled = Enumerable.Repeat(true, 8).ToArray();
            // Deliberately separate the configured suppression input from the source expression input.
            settings.facialExParameterName = "Facial EX";
            settings.leftLipSyncEnabled[1] = settings.rightLipSyncEnabled[1] = settings.facialLipSyncEnabled[1] = false;
            var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(Source);
            Assert.That(source, Is.Not.Null, "Standard source fixture required");
            if (unsupported)
            {
                source = Keep(Object.Instantiate(source));
                var layers = source.layers; layers.Single(l => l.name == "Facial LeftHand").name = "Unsupported"; source.layers = layers;
            }
            if (unsafeRight || anyStateOverride)
            {
                source = Keep(Object.Instantiate(source));
                var layers = source.layers; var layer = layers.Single(l => l.name == (unsafeRight ? "Facial RightHand" : "Facial LeftHand"));
                var original = layer.stateMachine; var copy = Keep(new AnimatorStateMachine());
                var map = original.states.ToDictionary(s => s.state, s => {
                    var state = Keep(copy.AddState(s.state.name)); state.motion = s.state.motion;
                    state.writeDefaultValues = s.state.writeDefaultValues; return state;
                });
                copy.defaultState = map[original.defaultState];
                foreach (var pair in map)
                    foreach (var t in pair.Key.transitions)
                    {
                        var transition = Keep(t.isExit ? pair.Value.AddExitTransition() : pair.Value.AddTransition(map[t.destinationState]));
                        transition.conditions = t.conditions; transition.hasExitTime = t.hasExitTime;
                        transition.exitTime = t.exitTime; transition.duration = t.duration;
                    }
                foreach (var t in original.entryTransitions)
                {
                    var transition = Keep(copy.AddEntryTransition(map[t.destinationState])); transition.conditions = t.conditions;
                }
                var target = map.Values.Single(s => s.name.StartsWith("1 ", System.StringComparison.Ordinal));
                if (anyStateOverride)
                {
                    source.AddParameter("OverrideFace", AnimatorControllerParameterType.Bool);
                    var transition = Keep(copy.AddAnyStateTransition(target));
                    transition.AddCondition(AnimatorConditionMode.If, 0, "OverrideFace");
                }
                else
                {
                    var clip = Keep(Object.Instantiate((AnimationClip)target.motion)); target.motion = clip;
                    clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.mouth_triangle", AnimationCurve.Constant(0, .1f, 100));
                }
                layer.stateMachine = copy; source.layers = layers;
            }
            var available = new[] { MururuMouthReturnMeshAssembler.DefaultShapeName }.Concat(MururuMouthReturnFxRoutesPose.Properties
                .Where(p => p != "mouth_off").Select(MururuMouthReturnMeshAssembler.TargetShapeName)).ToArray();
            if (missingGeometry)
            {
                var pose = MururuMouthReturnFxRoutesPlan.Capture(source, settings, "Body", "Mouth", new HashSet<string>()).Routes.Single(r => r.Key == "Left4").Pose;
                string absent = pose.Bridge.First(p => p.Value != 0 && p.Key != MururuMouthReturnMeshAssembler.DefaultShapeName).Key;
                available = available.Where(n => n != absent).ToArray();
            }
            bundle = MururuMouthReturnControllerGenerator.Generate(source, settings, "Body", "Mouth", available, new HashSet<string>());
            Assert.That(bundle.Plan.Routes.Single(r => r.Key == "Left4").Pose.Supported, Is.EqualTo(!unsupported && !missingGeometry && !anyStateOverride));
            var controller = Keep(new AnimatorController());
            controller.layers = new[] { bundle.PhaseLayer };
            controller.AddParameter("Voice", AnimatorControllerParameterType.Float);
            foreach (string name in new[] { "GestureLeft", "GestureRight", "Facial Ex", "Facial EX", "Accessory", "VRCEmote" })
                controller.AddParameter(name, AnimatorControllerParameterType.Int);
            foreach (string name in new[] { "AFK", "Seated" }) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
            Assert.That(controller.parameters.Any(p => p.name.StartsWith("MBLS_Return_")), Is.False);
            bank = new[] { MururuMouthReturnMeshAssembler.DefaultShapeName }.Concat(MururuMouthReturnFxRoutesPose.Properties
                .Where(p => p != "mouth_off").Select(MururuMouthReturnMeshAssembler.TargetShapeName)).ToArray();
            var root = Keep(new GameObject("Native return rig"));
            animator = root.AddComponent<Animator>();
            body = Renderer(root, "Body", new[] { "mouth_off" });
            mouth = Renderer(root, "Mouth", bank.Concat(new[] { "SE_hide_self" }).ToArray());
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.runtimeAnimatorController = controller; animator.Rebind(); animator.Update(0);
            animator.SetInteger("GestureLeft", 4); Voice(true);
            for (int i = 0; i < 10; i++) Step();
            Assert.That(Original(), Is.False, Phase()); AssertReset();
        }
        SkinnedMeshRenderer Renderer(GameObject root, string name, string[] shapes)
        {
            var child = new GameObject(name); child.transform.SetParent(root.transform, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            var mesh = Keep(new Mesh()); mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (string shape in shapes) mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.forward, Vector3.zero, Vector3.zero }, null, null);
            renderer.sharedMesh = mesh; renderer.updateWhenOffscreen = true; return renderer;
        }
        void Voice(bool speaking) => animator.SetFloat("Voice", speaking ? 1 : 0);
        string Phase() => bundle.PhaseLayer.stateMachine.states.Single(s => Animator.StringToHash(s.state.name)
            == animator.GetCurrentAnimatorStateInfo(0).shortNameHash).state.name;
        bool Original() => Weight(mouth, "SE_hide_self") > 99.99f;
        static float Weight(SkinnedMeshRenderer renderer, string shape) => renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(shape));
        void AssertReset()
        { foreach (string property in bank) Assert.That(Weight(mouth, property), Is.EqualTo(0).Within(.02f), property + " in " + Phase()); }
        void Step()
        {
            animator.Update(Dt);
            Assert.That(Weight(mouth, "SE_hide_self"), Is.EqualTo(0).Or.EqualTo(100), "Visibility must not crossfade");
            Assert.That(Weight(body, "mouth_off"), Is.EqualTo(Original() ? 0 : 100).Within(.02f));
        }
    }
}
