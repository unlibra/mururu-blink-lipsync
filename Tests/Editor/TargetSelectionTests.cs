using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace MururuBlinkLipSync.Editor.Tests
{
    // TEST_CASES.md A. 対象Avatarの選択
    internal sealed class TargetSelectionTests : MururuAvatarTestBase
    {
        [Test]
        public void A1_SingleMururu_IsDetectedWhenWindowOpens()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");

            CreateWindow();

            Assert.AreEqual(mururu, window.avatarDescriptor);
        }

        [Test]
        public void A2_TwoMururu_LeaveTargetEmpty()
        {
            SpawnMururu("Mururu A");
            SpawnMururu("Mururu B");

            CreateWindow();

            Assert.IsNull(window.avatarDescriptor);
        }

        [Test]
        public void A3_SelectingAnotherMururu_SwitchesTarget()
        {
            VRCAvatarDescriptor a = SpawnMururu("Mururu A");
            VRCAvatarDescriptor b = SpawnMururu("Mururu B");
            CreateWindow();

            Selection.activeGameObject = a.gameObject;
            window.AutoDetectTarget();
            Assert.AreEqual(a, window.avatarDescriptor);

            Selection.activeGameObject = b.gameObject;
            window.AutoDetectTarget();
            Assert.AreEqual(b, window.avatarDescriptor);
        }

        [Test]
        public void A4_SelectingUninstalledDuplicate_TargetsItAsNotInstalled()
        {
            VRCAvatarDescriptor a = SpawnMururu("Mururu A");
            VRCAvatarDescriptor b = SpawnMururu("Mururu B");
            Install(a);

            Selection.activeGameObject = b.gameObject;
            window.AutoDetectTarget();

            Assert.AreEqual(b, window.avatarDescriptor);
            Assert.IsFalse(window.IsCurrentTargetInstalled());
        }

        [Test]
        public void A5_SelectingNonAvatar_KeepsTarget()
        {
            VRCAvatarDescriptor a = SpawnMururu("Mururu A");
            SpawnMururu("Mururu B");
            SetTarget(a);

            GameObject light = new GameObject("Directional Light", typeof(Light));
            Selection.activeGameObject = light;
            window.AutoDetectTarget();

            Assert.AreEqual(a, window.avatarDescriptor);
        }

        [Test]
        public void A6_RepairUsesTargetField_NotSelection()
        {
            VRCAvatarDescriptor a = SpawnMururu("Mururu A");
            VRCAvatarDescriptor b = SpawnMururu("Mururu B");
            MururuBlinkLipSyncInstallRecord recordA = Install(a);
            MururuBlinkLipSyncInstallRecord recordB = Install(b);

            Object controllerA = recordA.installedController;
            Object controllerB = recordB.installedController;
            SkinnedMeshRenderer visemeA = a.VisemeSkinnedMesh;
            SkinnedMeshRenderer eyelidsA = a.customEyeLookSettings.eyelidsSkinnedMesh;

            Selection.activeGameObject = a.gameObject;
            SetTarget(b);

            Assert.IsNull(Run(window.RepairInstallationAuto));

            Assert.AreNotEqual(controllerB, recordB.installedController);
            Assert.AreEqual(controllerA, recordA.installedController);
            Assert.AreEqual(visemeA, a.VisemeSkinnedMesh);
            Assert.AreEqual(eyelidsA, a.customEyeLookSettings.eyelidsSkinnedMesh);
        }
    }
}
