using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MururuBlinkLipSync.Editor.Tests
{
    // TEST_CASES.md F. 修復 / 新しい方式に更新
    internal sealed class RepairTests : MururuAvatarTestBase
    {
        [Test]
        public void F1_Repair_UpdatesSameFiles()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));
            string meshPath = AssetDatabase.GetAssetPath(record.installedLipSyncMouthRenderer.sharedMesh);
            AnimationClip onClip = record.installedOnClip;
            AnimationClip offClip = record.installedOffClip;

            Assert.IsNull(Run(window.RepairInstallationAuto));

            Assert.AreEqual(meshPath, AssetDatabase.GetAssetPath(record.installedLipSyncMouthRenderer.sharedMesh));
            Assert.AreEqual(onClip, record.installedOnClip);
            Assert.AreEqual(offClip, record.installedOffClip);
        }

        [Test]
        public void F2_Repair_RecreatesDeletedGeneratedFile()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(record.installedOnClip));

            CollectionAssert.IsNotEmpty(
                MururuBlinkLipSyncSetupWindow.FindMissingGeneratedReferences(record));

            Assert.IsNull(Run(window.RepairInstallationAuto));

            CollectionAssert.IsEmpty(
                MururuBlinkLipSyncSetupWindow.FindMissingGeneratedReferences(record));
            Assert.IsTrue(AssetExists(AssetDatabase.GetAssetPath(record.installedOnClip)));
        }

        [Test]
        public void F3_Upgrade_RestoresLegacyGeneratedBody()
        {
            SetBodyFbxReadable(true);
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            SkinnedMeshRenderer body = Body(mururu);
            Mesh originalMesh = body.sharedMesh;
            MururuBlinkLipSyncInstallRecord record = Install(mururu);

            // What an RC4 install left: an older schema and a generated Body mesh.
            Mesh legacyBody = Object.Instantiate(originalMesh);
            legacyBody.name = "Body_MururuSE_Generated";
            string legacyPath =
                AssetDatabase.GenerateUniqueAssetPath(
                    $"{MururuBlinkLipSyncSetupWindow.OutputFolder}/Body_MururuSE_Generated.asset");
            AssetDatabase.CreateAsset(legacyBody, legacyPath);
            record.generatedAssetPaths.Add(legacyPath);
            record.installSchemaVersion = MururuBlinkLipSyncSetupWindow.InstallSchemaVersion - 1;
            body.sharedMesh = legacyBody;
            TrackGeneratedPaths(record);

            Assert.IsNull(Run(window.RepairInstallationAuto));

            Assert.AreEqual(originalMesh, body.sharedMesh);
            Assert.AreEqual(MururuBlinkLipSyncSetupWindow.InstallSchemaVersion, record.installSchemaVersion);
        }

        [Test]
        public void F4_BuiltInVoice_IsRejectedOnRepair()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));
            Object controller = record.GetComponent<ModularAvatarMergeAnimator>().animator;

            window.facialLipSyncEnabled[0] = false;
            window.facialExParameterName = "Voice";

            AssertFailedWith(
                Run(window.RepairInstallationAuto),
                "組み込みパラメータ");

            Assert.AreEqual(controller, record.GetComponent<ModularAvatarMergeAnimator>().animator);
            Assert.AreEqual(controller, record.installedController);
        }

        [Test]
        public void F5_Repair_FollowsReaddedBody()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            Unpack(mururu);
            MururuBlinkLipSyncInstallRecord record = Install(mururu);

            GameObject oldBody = Body(mururu).gameObject;
            GameObject newBody = Object.Instantiate(oldBody, mururu.transform);
            Object.DestroyImmediate(oldBody);
            newBody.name = "Body";
            SkinnedMeshRenderer newRenderer = newBody.GetComponent<SkinnedMeshRenderer>();

            Assert.IsNull(Run(window.RepairInstallationAuto));

            Assert.AreEqual(newRenderer, record.bodyRenderer);
            Assert.AreEqual(newRenderer, mururu.customEyeLookSettings.eyelidsSkinnedMesh);
            CollectionAssert.IsEmpty(
                MururuBlinkLipSyncSetupWindow.FindMissingGeneratedReferences(record));
        }
    }
}
