using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace MururuBlinkLipSync.Editor.Tests
{
    internal sealed class InstallTests : MururuAvatarTestBase
    {
        [Test]
        public void B1_Install_CreatesRootAndLipSyncMouth()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");

            Install(mururu);

            Transform root = mururu.transform.Find(MururuBlinkLipSyncSetupWindow.RootObjectName);
            Assert.IsNotNull(root);
            Assert.IsNotNull(root.Find(MururuBlinkLipSyncSetupWindow.SeObjectName));
        }

        [Test]
        public void B2_Install_CreatesGeneratedAssets()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));

            string prefix = MururuBlinkLipSyncSetupWindow.OutputFolder + "/";

            AssertGenerated(
                AssetDatabase.GetAssetPath(record.installedLipSyncMouthRenderer.sharedMesh),
                prefix + "LipSync_Mouth_Generated",
                ".asset");

            AssertGenerated(
                AssetDatabase.GetAssetPath(record.installedOnClip),
                prefix + "MururuBlinkLipSync_LipSync_ON",
                ".anim");

            AssertGenerated(
                AssetDatabase.GetAssetPath(record.installedOffClip),
                prefix + "MururuBlinkLipSync_LipSync_OFF",
                ".anim");

            AssertGenerated(
                AssetDatabase.GetAssetPath(record.installedController),
                prefix + "MururuBlinkLipSync_FX",
                ".controller");
        }

        [Test]
        public void B3_Install_ConfiguresVisemeAndEyelids()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);

            MururuBlinkLipSyncInstallRecord record = Install(mururu);

            Assert.AreEqual(VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape, mururu.lipSync);
            Assert.AreEqual(record.installedLipSyncMouthRenderer, mururu.VisemeSkinnedMesh);

            VRCAvatarDescriptor.CustomEyeLookSettings eyeLook = mururu.customEyeLookSettings;
            Assert.IsTrue(mururu.enableEyeLook);
            Assert.AreEqual(VRCAvatarDescriptor.EyelidType.Blendshapes, eyeLook.eyelidType);
            Assert.AreEqual(body, eyeLook.eyelidsSkinnedMesh);
            Assert.AreEqual(body.sharedMesh.GetBlendShapeIndex("eye_blink"), eyeLook.eyelidsBlendshapes[0]);
        }

        [Test]
        public void B4_Install_KeepsBodyMesh()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);
            Mesh bodyMesh = body.sharedMesh;

            Install(mururu);

            Assert.AreEqual(bodyMesh, body.sharedMesh);
        }

        [Test]
        public void B5_InstallWithReadWriteOff_RestoresReadWriteOff()
        {
            SetBodyFbxReadable(false);
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");

            Install(mururu);

            Assert.IsFalse(BodyImporter.isReadable);
        }

        [Test]
        public void B6_UndoOnce_RevertsWholeInstall()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer visemeBefore = mururu.VisemeSkinnedMesh;
            VRC_AvatarDescriptor.LipSyncStyle lipSyncBefore = mururu.lipSync;
            VRCAvatarDescriptor.EyelidType eyelidTypeBefore = mururu.customEyeLookSettings.eyelidType;

            Install(mururu);
            Undo.PerformUndo();

            Assert.IsNull(RecordOf(mururu));
            Assert.IsNull(mururu.transform.Find(MururuBlinkLipSyncSetupWindow.RootObjectName));
            Assert.AreEqual(visemeBefore, mururu.VisemeSkinnedMesh);
            Assert.AreEqual(lipSyncBefore, mururu.lipSync);
            Assert.AreEqual(eyelidTypeBefore, mururu.customEyeLookSettings.eyelidType);
        }

        [Test]
        public void B7_ExistingRootObject_IsRejected()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer visemeBefore = mururu.VisemeSkinnedMesh;

            new GameObject(MururuBlinkLipSyncSetupWindow.RootObjectName)
                .transform.SetParent(mururu.transform, false);

            SetTarget(mururu);

            AssertFailedWith(
                Run(window.Install),
                $"'{MururuBlinkLipSyncSetupWindow.RootObjectName}' が既にあります");

            Assert.IsNull(RecordOf(mururu));
            Assert.AreEqual(visemeBefore, mururu.VisemeSkinnedMesh);
        }

        [Test]
        public void B8_BodyNotUnderAvatarRoot_IsRejected()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            Unpack(mururu);
            SkinnedMeshRenderer visemeBefore = mururu.VisemeSkinnedMesh;

            GameObject holder = new GameObject("Holder");
            holder.transform.SetParent(mururu.transform, false);
            Body(mururu).transform.SetParent(holder.transform, true);

            SetTarget(mururu);

            AssertFailedWith(
                Run(window.Install),
                "Bodyの親がAvatar Rootか確認してください");

            Assert.IsNull(RecordOf(mururu));
            Assert.AreEqual(visemeBefore, mururu.VisemeSkinnedMesh);
        }

        [Test]
        public void C1_StandardMururu_HasNoMouthShapeWarnings()
        {
            SetBodyFbxReadable(true);
            Mesh body = Body(SpawnMururu("Mururu")).sharedMesh;

            int[] mapping = MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(body);

            CollectionAssert.IsEmpty(
                MururuBlinkLipSyncSetupWindow.CollectNeutralMouthWarnings(body, mapping));
        }

        [Test]
        public void C2_EditedMouthTopology_IsRejected()
        {
            SetBodyFbxReadable(true);
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);

            body.sharedMesh = WithoutOneMouthTriangle(body.sharedMesh);
            SetTarget(mururu);

            AssertFailedWith(
                Run(window.Install),
                "Bodyの口周辺メッシュ");

            Assert.IsNull(RecordOf(mururu));
        }

        [TestCase(0, "+Y")]
        [TestCase(1, "-Y")]
        [TestCase(2, "+X")]
        [TestCase(3, "-X")]
        [TestCase(4, "-Z")]
        public void C3_RotatedMouth_ExplainsCoordinateAxes(int axisIndex, string axisName)
        {
            SetBodyFbxReadable(true);
            Mesh source = Body(SpawnMururu("Rotated mouth")).sharedMesh;
            Mesh rotated = Object.Instantiate(source);
            try
            {
                Vector3[] axes = { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.back };
                Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, axes[axisIndex]);
                var delta = new Vector3[source.vertexCount];
                int shape = source.GetBlendShapeIndex("mouth_A");
                source.GetBlendShapeFrameVertices(shape, source.GetBlendShapeFrameCount(shape) - 1,
                    delta, null, null);
                rotated.vertices = source.vertices.Select(v => rotation * v).ToArray();
                rotated.ClearBlendShapes();
                rotated.AddBlendShapeFrame("mouth_A", 100,
                    delta.Select(v => rotation * v).ToArray(), null, null);

                var error = Assert.Throws<System.Exception>(() =>
                    MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(rotated));
                StringAssert.Contains("座標軸", error.Message);
                StringAssert.Contains("口の前方向: " + axisName, error.Message);
                StringAssert.Contains("FAQ", error.Message);
            }
            finally { Object.DestroyImmediate(rotated); }
        }

        [Test]
        public void C4_MissingMouthMotion_DoesNotReportRotation()
        {
            SetBodyFbxReadable(true);
            Mesh changed = Object.Instantiate(Body(SpawnMururu("Missing motion")).sharedMesh);
            try
            {
                changed.ClearBlendShapes();
                changed.AddBlendShapeFrame("mouth_A", 100, new Vector3[changed.vertexCount], null, null);
                var error = Assert.Throws<System.Exception>(() =>
                    MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(changed));
                StringAssert.Contains("想定: 84 / 検出: 0", error.Message);
                StringAssert.DoesNotContain("座標軸", error.Message);
            }
            finally { Object.DestroyImmediate(changed); }
        }

        private static Mesh WithoutOneMouthTriangle(
            Mesh source)
        {
            HashSet<int> mouth =
                new HashSet<int>(MururuBlinkLipSyncSetupWindow.BuildNeutralSourceMapping(source));

            Mesh edited = Object.Instantiate(source);

            for (int subMesh = 0; subMesh < edited.subMeshCount; subMesh++)
            {
                List<int> triangles = edited.GetTriangles(subMesh).ToList();

                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    if (mouth.Contains(triangles[i]) &&
                        mouth.Contains(triangles[i + 1]) &&
                        mouth.Contains(triangles[i + 2]))
                    {
                        triangles.RemoveRange(i, 3);
                        edited.SetTriangles(triangles, subMesh);
                        return edited;
                    }
                }
            }

            Assert.Fail("口の三角形が見つからない");
            return null;
        }

        private static void AssertGenerated(
            string path,
            string expectedPrefix,
            string expectedExtension)
        {
            StringAssert.StartsWith(expectedPrefix, path);
            StringAssert.EndsWith(expectedExtension, path);
            Assert.IsTrue(AssetExists(path), path);
        }
    }
}
