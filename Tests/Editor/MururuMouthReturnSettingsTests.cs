using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuMouthReturnSettingsTests : MururuAvatarTestBase
    {
        readonly List<Object> temporary = new List<Object>();

        T Keep<T>(T value) where T : Object { temporary.Add(value); return value; }

        [TearDown]
        public void DestroyBuildOutputs()
        {
            foreach (var value in temporary.AsEnumerable().Reverse()) if (value) Object.DestroyImmediate(value);
            temporary.Clear();
        }

        [Test]
        public void Install_UsesFixed80msSceneWaitWithoutBridgeShapes()
        {
            var record = Install(SpawnMururu("Fixed scene timing"));
            AssertSceneWait(record);
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh.blendShapeCount, Is.EqualTo(7));
        }

        [Test]
        public void ApplyAndRepair_PreserveSettingsAcrossTargetAndSceneReload()
        {
            var avatar = SpawnMururu("Saved settings");
            var record = Install(avatar);
            var body = record.bodyRenderer.sharedMesh;
            PrefabUtility.SaveAsPrefabAssetAndConnect(avatar.gameObject,
                EnsureTempFolder() + "/MouthReturn.prefab", InteractionMode.AutomatedAction);
            window.leftLipSyncEnabled[1] = false;
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            SetTarget(SpawnMururu("Other target"));
            Assert.That(window.leftLipSyncEnabled[1], Is.True);
            SetTarget(avatar);
            Assert.That(window.leftLipSyncEnabled[1], Is.False);
            Assert.That(Run(window.RepairInstallationAuto), Is.Null);
            Assert.That(record.leftLipSyncEnabled[1], Is.False);
            Assert.That(record.bodyRenderer.sharedMesh, Is.SameAs(body));
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh.blendShapeCount, Is.EqualTo(7));
            string path = EnsureTempFolder() + "/MouthReturn.unity";
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), path), Is.True);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var reloaded = SceneManager.GetActiveScene().GetRootGameObjects()
                .Select(go => go.GetComponent<VRCAvatarDescriptor>()).Single(d => d && d.name == "Saved settings");
            SetTarget(reloaded);
            Assert.That(RecordOf(reloaded).leftLipSyncEnabled[1], Is.False);
            Assert.That(window.leftLipSyncEnabled[1], Is.False);
            AssertSceneWait(RecordOf(reloaded));
        }

        [Test]
        public void ApplyUndo_RestoresExpressionSettingAndControllerReference()
        {
            var record = Install(SpawnMururu("Undo"));
            var previous = record.installedController;
            // A duplicate keeps the previous controller alive through the Apply.
            Object.Instantiate(record.avatarDescriptor.gameObject).name = "Undo asset owner";
            window.leftLipSyncEnabled[1] = false;
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            Undo.PerformUndo();
            Assert.That(record.leftLipSyncEnabled[1], Is.True);
            Assert.That(record.installedController, Is.SameAs(previous));
            Assert.That(record.GetComponent<ModularAvatarMergeAnimator>().animator, Is.SameAs(previous));
        }

        [Test]
        public void ApplyRepairAndRemove_KeepDuplicateAssetsAndDeleteOnlyUnsharedAssets()
        {
            var avatar = SpawnMururu("Owned installation");
            var record = Install(avatar);
            var duplicate = Object.Instantiate(avatar.gameObject).GetComponent<VRCAvatarDescriptor>();
            duplicate.name = "Shared owner";
            var other = RecordOf(duplicate);
            string[] shared = other.generatedAssetPaths.ToArray();
            SetTarget(avatar); window.leftLipSyncEnabled[1] = false;
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            Assert.That(Run(window.RepairInstallationAuto), Is.Null);
            Assert.That(record.leftLipSyncEnabled[1], Is.False);
            Assert.That(other.leftLipSyncEnabled[1], Is.True);
            string[] unshared = record.generatedAssetPaths.Except(shared).ToArray();
            Assert.That(unshared, Is.Not.Empty);
            Assert.That(Run(window.UninstallAuto), Is.Null);
            Assert.That(RecordOf(avatar), Is.Null);
            foreach (string path in shared) Assert.That(AssetExists(path), Is.True, path);
            foreach (string path in unshared) Assert.That(AssetExists(path), Is.False, path);
            SetTarget(duplicate);
            Assert.That(Run(window.UninstallAuto), Is.Null);
            foreach (string path in shared) Assert.That(AssetExists(path), Is.False, path);
        }

        [Test]
        public void Build_UsesFixed80msWaitAndMorphAndDoesNotAccumulate()
        {
            SetBodyFbxReadable(true);
            var record = Install(SpawnMururu("Build settings"));
            window.leftLipSyncEnabled[0] = false;
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            var originalMesh = record.installedLipSyncMouthRenderer.sharedMesh;
            var originalController = record.installedController;
            string bodyHash = Fingerprint(record.bodyRenderer.sharedMesh), mouthHash = Fingerprint(originalMesh);
            for (int build = 0; build < 2; build++)
            {
                var clone = Keep(Object.Instantiate(record.avatarDescriptor.gameObject));
                MururuMouthReturnPlugin.Prepare(clone);
                var built = clone.GetComponentInChildren<MururuBlinkLipSyncInstallRecord>(true);
                var controller = Keep((AnimatorController)built.installedController);
                var phase = controller.layers.Single(l => l.name == MururuMouthReturnControllerGenerator.PhaseName);
                TrackPhase(phase.stateMachine);
                var mesh = Keep(built.installedLipSyncMouthRenderer.sharedMesh);
                Assert.That(mesh, Is.Not.SameAs(originalMesh));
                Assert.That(mesh.blendShapeCount, Is.EqualTo(originalMesh.blendShapeCount + 28));
                Assert.That(mesh.vertexCount, Is.EqualTo(originalMesh.vertexCount));
                Assert.That(controller.layers.Length, Is.EqualTo(((AnimatorController)originalController).layers.Length));
                Assert.That(clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length,
                    Is.EqualTo(record.avatarDescriptor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length));
                Assert.That(controller.parameters.Any(p => p.name.StartsWith("MBLS_Return_")), Is.False);
                var states = phase.stateMachine.states.Select(s => s.state).ToArray();
                var bridges = states.Where(s => s.name.StartsWith("Bridge_")).ToArray();
                Assert.That(bridges.Length, Is.EqualTo(17));
                var waitState = states.Single(s => s.name == "Wait");
                Assert.That(((AnimationClip)waitState.motion).length, Is.EqualTo(.08f).Within(.00001f));
                var bridge = (AnimationClip)bridges.Single(s => s.name == "Bridge_Left4").motion;
                var binding = AnimationUtility.GetCurveBindings(bridge).Single(b => b.propertyName == "blendShape." + MururuMouthReturnMeshAssembler.DefaultShapeName);
                var keys = AnimationUtility.GetEditorCurve(bridge, binding).keys;
                Assert.That(keys.Last().time - keys.First().time, Is.EqualTo(.08f).Within(.00001f));
                Assert.That(keys.First().time, Is.Zero);
                var speaking = states.Single(s => s.name == "Speaking");
                Assert.That(speaking.transitions.Any(t => t.destinationState.name == "Original" && t.conditions.Any(c =>
                    c.parameter == "GestureLeft" && c.mode == AnimatorConditionMode.Equals && c.threshold == 1)), Is.True);
                if (build == 0) Debug.Log("MouthReturnBuildLocal wait=0.08 morph=0.08" +
                    " renderers=" + clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length +
                    " ownedLayers=" + ((AnimatorController)originalController).layers.Length + "->" + controller.layers.Length +
                    " ownedParameters=" + ((AnimatorController)originalController).parameters.Length + "->" + controller.parameters.Length +
                    " ownedStates=" + states.Length + " vertices=" + originalMesh.vertexCount + "->" + mesh.vertexCount +
                    " shapes=" + originalMesh.blendShapeCount + "->" + mesh.blendShapeCount +
                    " meshMemoryBytes=" + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(originalMesh) + "->" +
                    UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh));
            }
            Assert.That(record.installedController, Is.SameAs(originalController));
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh, Is.SameAs(originalMesh));
            Assert.That(Fingerprint(record.bodyRenderer.sharedMesh), Is.EqualTo(bodyHash));
            Assert.That(Fingerprint(originalMesh), Is.EqualTo(mouthHash));
        }

        [Test]
        public void Build_UnknownMouthUsesFixed80msInstantWithoutChangingMesh()
        {
            SetBodyFbxReadable(true);
            var record = Install(SpawnMururu("Unknown mouth"));
            Assert.That(Run(window.ApplyExpressionSettings), Is.Null);
            var clone = Keep(Object.Instantiate(record.avatarDescriptor.gameObject));
            var built = clone.GetComponentInChildren<MururuBlinkLipSyncInstallRecord>(true);
            var unknown = Keep(Object.Instantiate(built.installedLipSyncMouthRenderer.sharedMesh));
            var vertices = unknown.vertices; vertices[0] += Vector3.right; unknown.vertices = vertices;
            built.installedLipSyncMouthRenderer.sharedMesh = unknown;
            string before = Fingerprint(unknown);
            MururuMouthReturnPlugin.Prepare(clone);
            var controller = Keep((AnimatorController)built.installedController);
            var phase = controller.layers.Single(l => l.name == MururuMouthReturnControllerGenerator.PhaseName);
            TrackPhase(phase.stateMachine);
            Assert.That(phase.stateMachine.states.Any(s => s.state.name.StartsWith("Bridge_")), Is.False);
            Assert.That(((AnimationClip)phase.stateMachine.states.Single(s => s.state.name == "Wait").state.motion).length,
                Is.EqualTo(.08f).Within(.00001f));
            Assert.That(built.installedLipSyncMouthRenderer.sharedMesh, Is.SameAs(unknown));
            Assert.That(Fingerprint(unknown), Is.EqualTo(before));
        }

        void TrackPhase(AnimatorStateMachine machine)
        {
            Keep(machine);
            foreach (var child in machine.states)
            {
                Keep(child.state.motion); Keep(child.state);
                foreach (var transition in child.state.transitions) Keep(transition);
                foreach (var behaviour in child.state.behaviours) Keep(behaviour);
            }
        }

        static void AssertSceneWait(MururuBlinkLipSyncInstallRecord record)
        {
            var states = ((AnimatorController)record.installedController).layers[0].stateMachine.states;
            var speaking = states.Single(s => s.state.name == "LipSync").state;
            var grace = states.Single(s => s.state.name == "LipSync Silence Grace").state;
            var silent = speaking.transitions.Single(t => t.conditions.Any(c => c.parameter == "Voice" && c.mode == AnimatorConditionMode.Less));
            Assert.That(silent.destinationState.name, Is.EqualTo(grace.name));
            Assert.That(((AnimationClip)grace.motion).length / grace.speed, Is.EqualTo(.08f).Within(.00001f));
        }

        static string Fingerprint(Mesh mesh)
        {
            Assert.That(MururuMouthReturnFingerprint.TryCompute(mesh, out string hash, out _), Is.True);
            return hash;
        }
    }
}
