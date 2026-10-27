using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class MururuMouthReturnStandardVariantsTests : MururuAvatarTestBase
    {
        readonly List<Object> temporary = new List<Object>();
        T Keep<T>(T value) where T : Object { temporary.Add(value); return value; }
        [TearDown] public void Cleanup()
        {
            foreach (var value in temporary.AsEnumerable().Reverse()) if (value) Object.DestroyImmediate(value);
            temporary.Clear();
        }

        // Recreate the measured difference without storing the avatar's FBX in the repository.
        static void SetVariant(Mesh mesh, bool package)
        {
            var poses = mesh.bindposes;
            poses[75][1] = package ? -8.853965e-16f : -8.85396552e-16f;
            poses[79][1] = package ? 8.853964e-16f : 8.85396446e-16f;
            mesh.bindposes = poses;
        }

        [TestCase(false)] [TestCase(true)]
        public void KnownVariants_KeepBindingsAndVowelEndpoints(bool package)
        {
            var record = Install(SpawnMururu("Standard variant"));
            var body = Keep(Object.Instantiate(record.bodyRenderer.sharedMesh));
            var sdk = Keep(Object.Instantiate(record.installedLipSyncMouthRenderer.sharedMesh));
            SetVariant(body, package); SetVariant(sdk, package);
            Assert.That(MururuMouthReturnFingerprint.TryCompute(body, out var bf, out _), Is.True);
            Assert.That(bf, Is.EqualTo(package ? MururuMouthReturnFingerprint.StandardPackage : MururuMouthReturnFingerprint.Standard));
            Assert.That(MururuMouthReturnFingerprint.TryCompute(sdk, out var sf, out _), Is.True);
            Assert.That(sf, Is.EqualTo(package ? MururuMouthReturnMeshAssembler.StandardPackageSdkFingerprint : MururuMouthReturnMeshAssembler.StandardSdkFingerprint));
            Assert.That(MururuMouthReturnMeshAssembler.TryAssemble(body, sdk, out var output, out var reason), Is.True, reason.ToString());
            Keep(output);
            Assert.That(output.blendShapeCount, Is.EqualTo(35));
            CollectionAssert.AreEqual(sdk.bindposes, output.bindposes);
            CollectionAssert.AreEqual(sdk.boneWeights, output.boneWeights);
            foreach (var vowel in new[] { "A", "I", "U", "E", "O" })
            {
                var before = new Vector3[sdk.vertexCount]; var after = new Vector3[output.vertexCount];
                sdk.GetBlendShapeFrameVertices(sdk.GetBlendShapeIndex("SE_mouth_" + vowel), 0, before, null, null);
                output.GetBlendShapeFrameVertices(output.GetBlendShapeIndex("SE_mouth_" + vowel), 0, after, null, null);
                var vertices = sdk.vertices; var corrected = output.vertices;
                for (int i = 0; i < vertices.Length; i++)
                    Assert.That(Vector3.Distance(vertices[i] + before[i], corrected[i] + after[i]), Is.LessThan(1e-7f), vowel + "/" + i);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void SyncedEffectiveMotion_NdmfBuildKeepsSmoothRoutesAndInputs(bool mouthOverride)
        {
            var avatar = SpawnMururu("Synced mouth build"); Unpack(avatar);
            var record = Install(avatar);
            var body = record.bodyRenderer.sharedMesh;
            var sdk = record.installedLipSyncMouthRenderer.sharedMesh;
            var slots = avatar.baseAnimationLayers;
            int fx = System.Array.FindIndex(slots, l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            var original = (AnimatorController)slots[fx].animatorController;
            string originalJson = EditorJsonUtility.ToJson(original);
            var input = Keep(Object.Instantiate(original));
            int baseline = System.Array.FindIndex(input.layers, l => l.name == "Facial Idle");
            input.AddLayer(new AnimatorControllerLayer { name = "Synced endpoint", syncedLayerIndex = baseline,
                stateMachine = Keep(new AnimatorStateMachine()), defaultWeight = 1 });
            var clip = Keep(new AnimationClip());
            if (mouthOverride)
                foreach (string p in MururuMouthReturnFxRoutesPose.Properties)
                    clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + p,
                        AnimationCurve.Constant(0, .1f, p == "mouth_triangle" ? 100 : 0));
            else clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.eye_smile", AnimationCurve.Constant(0, .1f, 100));
            input.SetStateEffectiveMotion(input.layers[baseline].stateMachine.defaultState, clip, input.layers.Length - 1);
            slots[fx].animatorController = input; avatar.baseAnimationLayers = slots;
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            AvatarProcessor.ProcessAvatar(clone);
            var descriptor = clone.GetComponent<VRCAvatarDescriptor>();
            var built = (AnimatorController)descriptor.baseAnimationLayers.Single(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            var bridge = built.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state).Where(s => s.name.StartsWith("Bridge_")).ToArray();
            Assert.That(bridge.Length, Is.EqualTo(17));
            if (mouthOverride)
                foreach (var state in bridge)
                {
                    var binding = AnimationUtility.GetCurveBindings((AnimationClip)state.motion).Single(b => b.propertyName ==
                        "blendShape." + MururuMouthReturnMeshAssembler.TargetShapeName("mouth_triangle"));
                    Assert.That(AnimationUtility.GetEditorCurve((AnimationClip)state.motion, binding).Evaluate(.08f), Is.EqualTo(100).Within(.01f));
                }
            Assert.That(clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "LipSync_Mouth").sharedMesh.blendShapeCount, Is.EqualTo(35));
            Assert.That(record.bodyRenderer.sharedMesh, Is.SameAs(body));
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh, Is.SameAs(sdk));
            Assert.That(sdk.blendShapeCount, Is.EqualTo(7));
            Assert.That(EditorJsonUtility.ToJson(original), Is.EqualTo(originalJson));
        }

        [Test]
        public void ReadWriteOff_BuildsSmoothMouthWithoutChangingImporterOrInputs()
        {
            SetBodyFbxReadable(false);
            var avatar = SpawnMururu("ReadWrite off build"); Unpack(avatar);
            var record = Install(avatar);
            var body = record.bodyRenderer.sharedMesh;
            var sdk = record.installedLipSyncMouthRenderer.sharedMesh;
            Assert.IsFalse(body.isReadable);
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            AvatarProcessor.ProcessAvatar(clone);
            var mouth = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "LipSync_Mouth");
            Assert.That(mouth.sharedMesh.blendShapeCount, Is.EqualTo(35));
            Assert.IsFalse(BodyImporter.isReadable);
            Assert.That(record.bodyRenderer.sharedMesh, Is.SameAs(body));
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh, Is.SameAs(sdk));
            Assert.That(sdk.blendShapeCount, Is.EqualTo(7));
        }

        [Test]
        public void ActiveUnknownRendererShape_BuildUsesInstantWithoutChangingTheSetting()
        {
            var avatar = SpawnMururu("Unknown initial mouth"); Unpack(avatar);
            var record = Install(avatar);
            var body = Keep(Object.Instantiate(record.bodyRenderer.sharedMesh));
            var delta = new Vector3[body.vertexCount];
            foreach (int i in MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(body)) delta[i] = Vector3.right * .02f;
            body.AddBlendShapeFrame("CustomSmile", 100, delta, new Vector3[delta.Length], new Vector3[delta.Length]);
            record.bodyRenderer.sharedMesh = body;
            record.bodyRenderer.SetBlendShapeWeight(body.GetBlendShapeIndex("CustomSmile"), 100);
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            var built = clone.GetComponentInChildren<MururuBlinkLipSyncInstallRecord>(true);
            MururuMouthReturnPlugin.Prepare(clone);
            var controller = (UnityEditor.Animations.AnimatorController)built.installedController;
            var phase = controller.layers.Single(l => l.name == MururuMouthReturnControllerGenerator.PhaseName);
            Assert.IsFalse(phase.stateMachine.states.Any(s => s.state.name.StartsWith("Bridge_")));
            Assert.That(built.bodyRenderer.GetBlendShapeWeight(body.GetBlendShapeIndex("CustomSmile")), Is.EqualTo(100));
            Assert.That(record.bodyRenderer.GetBlendShapeWeight(body.GetBlendShapeIndex("CustomSmile")), Is.EqualTo(100));
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh.blendShapeCount, Is.EqualTo(7));
        }

        [Test]
        public void UnusedBindposeDifference_DoesNotDisableMouth()
        {
            var record = Install(SpawnMururu("Mixed variants"));
            var body = Keep(Object.Instantiate(record.bodyRenderer.sharedMesh));
            var sdk = Keep(Object.Instantiate(record.installedLipSyncMouthRenderer.sharedMesh));
            var w = sdk.boneWeights;
            var used = new HashSet<int>(w.Where(p => p.weight0 > 0).Select(p => p.boneIndex0)
                .Concat(w.Where(p => p.weight1 > 0).Select(p => p.boneIndex1))
                .Concat(w.Where(p => p.weight2 > 0).Select(p => p.boneIndex2))
                .Concat(w.Where(p => p.weight3 > 0).Select(p => p.boneIndex3)));
            int unused = Enumerable.Range(0, body.bindposes.Length).First(i => !used.Contains(i));
            var poses = body.bindposes; poses[unused][0] += .001f; body.bindposes = poses;
            Assert.That(MururuMouthReturnMeshAssembler.TryAssemble(body, sdk, out var output, out var reason), Is.True, reason.ToString());
            Keep(output);
            Assert.That(output.blendShapeCount, Is.EqualTo(35));
        }

        [TestCase(false)] [TestCase(true)]
        public void ArbitraryBindposeEdits_RemainUnsupported(bool changeSdk)
        {
            var record = Install(SpawnMururu("Unknown rig edit"));
            var body = Keep(Object.Instantiate(record.bodyRenderer.sharedMesh));
            var sdk = Keep(Object.Instantiate(record.installedLipSyncMouthRenderer.sharedMesh));
            SetVariant(body, true); SetVariant(sdk, true);
            var target = changeSdk ? sdk : body;
            int used = sdk.boneWeights.First(w => w.weight0 > 0).boneIndex0;
            var poses = target.bindposes; poses[used][0] += .001f; target.bindposes = poses;
            Assert.That(MururuMouthReturnMeshAssembler.TryAssemble(body, sdk, out var output, out var reason), Is.False);
            Assert.That(output, Is.Null);
            Assert.That(reason, Is.EqualTo(MururuMouthReturnUnsupportedReason.BindposeMismatch));
        }

        [TestCase(false)] [TestCase(true)]
        public void RealBuild_KnownVariantsGenerateBridgeShapes(bool package)
        {
            var avatar = SpawnMururu("Package build"); Unpack(avatar);
            var renderer = MururuBlinkLipSyncSetupWindow.FindCompatibleBodyRenderer(avatar);
            var body = Object.Instantiate(renderer.sharedMesh); SetVariant(body, package);
            AssetDatabase.CreateAsset(body, EnsureTempFolder() + "/StandardVariantBody.asset");
            renderer.sharedMesh = body;
            var record = Install(avatar);
            var originalMouth = record.installedLipSyncMouthRenderer.sharedMesh;
            var clone = Keep(Object.Instantiate(avatar.gameObject));
            AvatarProcessor.ProcessAvatar(clone);
            var mouth = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "LipSync_Mouth");
            Assert.That(mouth.sharedMesh.blendShapeCount, Is.EqualTo(35));
            CollectionAssert.AreEqual(body.bindposes, mouth.sharedMesh.bindposes);
            Assert.That(renderer.sharedMesh, Is.SameAs(body));
            Assert.That(record.installedLipSyncMouthRenderer.sharedMesh, Is.SameAs(originalMouth));
            Assert.That(originalMouth.blendShapeCount, Is.EqualTo(7));
        }
    }
}
