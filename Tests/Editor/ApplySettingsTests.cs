using System.Linq;
using nadena.dev.modular_avatar.core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace MururuBlinkLipSync.Editor.Tests
{
    // TEST_CASES.md E. 設定の変更を適用
    internal sealed class ApplySettingsTests : MururuAvatarTestBase
    {
        [Test]
        public void E1_Apply_ReplacesControllerAndDeletesOldOne()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));
            ModularAvatarMergeAnimator merge = record.GetComponent<ModularAvatarMergeAnimator>();
            RuntimeAnimatorController oldController = merge.animator;
            string oldPath = AssetDatabase.GetAssetPath(oldController);

            Assert.IsFalse(window.HasUnappliedSettings(record));
            window.leftBlinkEnabled[0] = false;
            Assert.IsTrue(window.HasUnappliedSettings(record));

            Assert.IsNull(Run(window.ApplyExpressionSettings));
            Assert.IsFalse(window.HasUnappliedSettings(record));

            Assert.IsNotNull(merge.animator);
            Assert.AreNotEqual(oldController, merge.animator);
            Assert.AreEqual(record.installedController, merge.animator);
            Assert.IsFalse(AssetExists(oldPath), oldPath);
        }

        [Test]
        public void E2_AppliedSettings_AreLoadedWhenTargetReturns()
        {
            VRCAvatarDescriptor a = SpawnMururu("Mururu A");
            VRCAvatarDescriptor b = SpawnMururu("Mururu B");
            Install(a);

            window.leftBlinkEnabled[0] = false;
            Assert.IsNull(Run(window.ApplyExpressionSettings));

            SetTarget(b);
            Assert.IsTrue(window.leftBlinkEnabled[0]);
            Assert.IsFalse(window.HasUnappliedSettings(window.FindInstallRecord()));

            SetTarget(a);
            Assert.IsFalse(window.leftBlinkEnabled[0]);
            Assert.IsFalse(window.HasUnappliedSettings(window.FindInstallRecord()));
        }

        [Test]
        public void E3_AllBlinkEnabled_HasNoBlinkLayer()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));

            SetAll(window.leftBlinkEnabled, true);
            SetAll(window.rightBlinkEnabled, true);
            SetAll(window.facialBlinkEnabled, true);

            Assert.IsNull(Run(window.ApplyExpressionSettings));

            AnimatorController controller = (AnimatorController)record.installedController;

            Assert.IsFalse(
                controller.layers.Any(layer =>
                    layer.name == MururuBlinkLipSyncSetupWindow.BlinkLayerName));
        }

        [Test]
        public void E4_ResetToDefaults_IsApplied()
        {
            MururuBlinkLipSyncInstallRecord record = Install(SpawnMururu("Mururu"));

            window.leftBlinkEnabled[0] = false;
            window.facialLipSyncEnabled[3] = false;
            Assert.IsNull(Run(window.ApplyExpressionSettings));

            // What the reset button does after its confirmation.
            window.ResetExpressionSettingsToMururuDefaults();
            window.TryAutoDetectFacialExParameterName();
            Assert.IsNull(Run(window.ApplyExpressionSettings));

            CollectionAssert.AreEqual(MururuBlinkLipSyncSetupWindow.DefaultLeftBlinkEnabled, record.leftBlinkEnabled);
            CollectionAssert.AreEqual(MururuBlinkLipSyncSetupWindow.DefaultLeftLipSyncEnabled, record.leftLipSyncEnabled);
            CollectionAssert.AreEqual(MururuBlinkLipSyncSetupWindow.DefaultRightBlinkEnabled, record.rightBlinkEnabled);
            CollectionAssert.AreEqual(MururuBlinkLipSyncSetupWindow.DefaultRightLipSyncEnabled, record.rightLipSyncEnabled);
            CollectionAssert.AreEqual(MururuBlinkLipSyncSetupWindow.DefaultFacialBlinkEnabled, record.facialBlinkEnabled);
            CollectionAssert.AreEqual(MururuBlinkLipSyncSetupWindow.DefaultFacialLipSyncEnabled, record.facialLipSyncEnabled);
        }

        [Test]
        public void E5_ApplyOnPrefabInstance_SurvivesSceneReload()
        {
            VRCAvatarDescriptor mururu = SpawnMururu("Mururu");
            Install(mururu);

            PrefabUtility.SaveAsPrefabAssetAndConnect(
                mururu.gameObject,
                $"{EnsureTempFolder()}/E5_Mururu.prefab",
                InteractionMode.AutomatedAction);

            window.leftBlinkEnabled[0] = false;
            Assert.IsNull(Run(window.ApplyExpressionSettings));

            string controllerPath =
                AssetDatabase.GetAssetPath(RecordOf(mururu).installedController);

            string scenePath = $"{TempFolder}/E5.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath));
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            VRCAvatarDescriptor reloaded =
                SceneManager.GetActiveScene()
                    .GetRootGameObjects()
                    .Select(go => go.GetComponent<VRCAvatarDescriptor>())
                    .Single(d => d != null);

            SetTarget(reloaded);

            MururuBlinkLipSyncInstallRecord record = RecordOf(reloaded);
            Assert.IsFalse(window.leftBlinkEnabled[0]);
            Assert.AreEqual(
                controllerPath,
                AssetDatabase.GetAssetPath(record.GetComponent<ModularAvatarMergeAnimator>().animator));
        }

        [Test]
        public void E6_RevertingEdits_ClearsUnappliedStatus()
        {
            var record = Install(SpawnMururu("Reverted settings"));
            window.hideSpeechNotes = true;
            Assert.IsTrue(window.HasUnappliedSettings(record));
            window.hideSpeechNotes = false;
            Assert.IsFalse(window.HasUnappliedSettings(record));

            window.rightLipSyncEnabled[1] = false;
            Assert.IsTrue(window.HasUnappliedSettings(record));
            window.rightLipSyncEnabled[1] = true;
            Assert.IsFalse(window.HasUnappliedSettings(record));

            string savedName = window.facialExParameterName;
            window.facialExParameterName = "GestureLeft";
            Assert.IsTrue(window.HasUnappliedSettings(record));
            AssertFailedWith(Run(window.ApplyExpressionSettings), "組み込みパラメータ");
            Assert.IsTrue(window.HasUnappliedSettings(record));
            window.facialExParameterName = savedName;
            Assert.IsFalse(window.HasUnappliedSettings(record));
        }

        [Test]
        public void E7_CloseSave_AppliesOrKeepsFailedEdits()
        {
            var record = Install(SpawnMururu("Close save"));
            window.hideSpeechNotes = true;
            window.UpdateUnsavedChanges();
            Assert.IsTrue(window.hasUnsavedChanges);
            window.SaveChanges();
            Assert.IsTrue(record.hideSpeechNotes);
            Assert.IsFalse(window.hasUnsavedChanges);

            string savedName = record.facialExParameterName;
            window.facialExParameterName = "GestureLeft";
            window.UpdateUnsavedChanges();
            Assert.Catch<System.Exception>(() => window.SaveChanges());
            Assert.IsTrue(window.hasUnsavedChanges);
            Assert.AreEqual("GestureLeft", window.facialExParameterName);
            Assert.AreEqual(savedName, record.facialExParameterName);
        }

        [Test]
        public void E8_CloseDiscard_ReloadsWithoutApplying()
        {
            var record = Install(SpawnMururu("Close discard"));
            var controller = record.installedController;
            window.hideSpeechNotes = true;
            window.leftBlinkEnabled[0] = false;
            window.UpdateUnsavedChanges();
            Assert.IsTrue(window.hasUnsavedChanges);
            window.DiscardChanges();
            Assert.IsFalse(window.hasUnsavedChanges);
            Assert.IsFalse(window.HasUnappliedSettings(record));
            Assert.IsFalse(record.hideSpeechNotes);
            Assert.IsTrue(record.leftBlinkEnabled[0]);
            Assert.AreSame(controller, record.installedController);
        }

        private static void SetAll(
            bool[] values,
            bool value)
        {
            for (int i = 0; i < values.Length; i++)
                values[i] = value;
        }
    }
}
